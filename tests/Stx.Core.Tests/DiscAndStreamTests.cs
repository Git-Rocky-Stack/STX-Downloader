using System.Text;
using Stx.Core.Playback;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>
/// Phase 4: STX refuses encrypted discs itself, before LibVLC sees them. The refusal is tested on the detection logic
/// with sectors built here, so no encrypted media goes in the repo (spec, Phase 4).
/// </summary>
public sealed class DiscCheckTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-disc").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>A DVD sector: an MPEG-2 pack header, then a video PES packet, scrambled as CSS does or not.</summary>
    static byte[] DvdSector(bool scrambled, byte stream = 0xE0)
    {
        var sector = new byte[2048];
        byte[] pack = [0, 0, 1, 0xBA, 0x44, 0, 4, 0, 4, 1, 1, 0x89, 0xC3, 0xF8];
        pack.CopyTo(sector, 0);
        byte[] pes = [0, 0, 1, stream, 0x07, 0xEC, (byte)(0x80 | (scrambled ? 0x10 : 0)), 0x80, 0x05];
        pes.CopyTo(sector, 14);
        return sector;
    }

    static byte[] Sectors(int count, Func<int, bool> scrambled) => [.. Enumerable.Range(0, count).SelectMany(i => DvdSector(scrambled(i)))];

    string Write(string relative, byte[] bytes)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    string DvdFolder(bool scrambled)
    {
        Write(Path.Combine("DISC", "VIDEO_TS", "VIDEO_TS.IFO"), Encoding.ASCII.GetBytes("DVDVIDEO-VMG"));
        Write(Path.Combine("DISC", "VIDEO_TS", "VIDEO_TS.VOB"), Sectors(8, _ => false));
        Write(Path.Combine("DISC", "VIDEO_TS", "VTS_01_1.VOB"), Sectors(300, i => scrambled && i % 3 != 0));
        return Path.Combine(_root, "DISC");
    }

    [Fact]
    public void A_scrambled_sector_is_recognised()
    {
        Assert.True(DiscCheck.IsScrambledSector(DvdSector(true)));
        Assert.False(DiscCheck.IsScrambledSector(DvdSector(false)));
        Assert.True(DiscCheck.IsScrambledSector(DvdSector(true, 0xBD)));
        // A navigation pack (private stream 2) carries no scrambling bits to read.
        Assert.False(DiscCheck.IsScrambledSector(DvdSector(true, 0xBF)));
        Assert.False(DiscCheck.IsScrambledSector(new byte[2048]));
    }

    [Fact]
    public void An_unencrypted_dvd_folder_is_a_dvd_and_plays()
    {
        var verdict = DiscCheck.Inspect(DvdFolder(scrambled: false));
        Assert.Equal(DiscKind.Dvd, verdict.Kind);
        Assert.False(verdict.Encrypted);
    }

    [Fact]
    public void An_encrypted_dvd_folder_is_refused_even_when_its_menu_is_clear()
    {
        var verdict = DiscCheck.Inspect(DvdFolder(scrambled: true));
        Assert.True(verdict.Encrypted);
        Assert.Contains("VTS_01_1.VOB", verdict.Why);
    }

    [Fact]
    public void Picking_the_video_ts_folder_or_an_ifo_reads_the_whole_disc()
    {
        string disc = DvdFolder(scrambled: true);
        Assert.True(DiscCheck.Inspect(Path.Combine(disc, "VIDEO_TS")).Encrypted);
        Assert.True(DiscCheck.Inspect(Path.Combine(disc, "VIDEO_TS", "VIDEO_TS.IFO")).Encrypted);
    }

    [Fact]
    public void A_scrambled_vob_on_its_own_is_refused()
    {
        string vob = Write("loose.vob", Sectors(50, i => i == 49));
        Assert.True(DiscCheck.Inspect(vob).Encrypted);
    }

    [Fact]
    public void A_blu_ray_folder_with_an_aacs_folder_is_refused()
    {
        Write(Path.Combine("BD", "BDMV", "index.bdmv"), Encoding.ASCII.GetBytes("INDX0200"));
        Write(Path.Combine("BD", "AACS", "Unit_Key_RO.inf"), new byte[16]);
        var verdict = DiscCheck.Inspect(Path.Combine(_root, "BD"));
        Assert.Equal(DiscKind.BluRay, verdict.Kind);
        Assert.True(verdict.Encrypted);
    }

    static byte[] M2ts(bool copyProtected)
    {
        var bytes = new byte[6144 * 4];
        for (int unit = 0; unit < 4; unit++)
            for (int packet = 0; packet < 32; packet++)
            {
                int at = unit * 6144 + packet * 192;
                bytes[at] = (byte)(copyProtected && packet == 0 ? 0xC0 : 0x00);
                bytes[at + 4] = 0x47;
            }
        return bytes;
    }

    [Fact]
    public void Blu_ray_copy_permission_bits_are_refused_without_an_aacs_folder()
    {
        Write(Path.Combine("BD2", "BDMV", "STREAM", "00000.m2ts"), M2ts(copyProtected: true));
        Assert.True(DiscCheck.Inspect(Path.Combine(_root, "BD2")).Encrypted);
    }

    [Fact]
    public void An_unencrypted_blu_ray_folder_plays()
    {
        Write(Path.Combine("BD3", "BDMV", "STREAM", "00000.m2ts"), M2ts(copyProtected: false));
        var verdict = DiscCheck.Inspect(Path.Combine(_root, "BD3", "BDMV"));
        Assert.Equal(DiscKind.BluRay, verdict.Kind);
        Assert.False(verdict.Encrypted);
    }

    static byte[] Image(string names, byte[] body)
    {
        var head = new byte[64 * 2048];
        Encoding.ASCII.GetBytes("CD001").CopyTo(head, 16 * 2048 + 1);
        Encoding.ASCII.GetBytes(names).CopyTo(head, 20 * 2048);
        return [.. head, .. body];
    }

    [Fact]
    public void Dvd_images_are_judged_by_their_sectors()
    {
        Assert.True(DiscCheck.Inspect(Write("scrambled.iso", Image("VIDEO_TS", Sectors(400, i => i > 200)))).Encrypted);
        var clear = DiscCheck.Inspect(Write("clear.iso", Image("VIDEO_TS", Sectors(400, _ => false))));
        Assert.Equal(DiscKind.Dvd, clear.Kind);
        Assert.False(clear.Encrypted);
    }

    [Fact]
    public void Blu_ray_images_with_aacs_are_refused_in_either_name_encoding()
    {
        Assert.True(DiscCheck.Inspect(Write("bd.iso", Image("BDMV AACS", []))).Encrypted);
        var wide = Image("BDMV", []);
        Encoding.BigEndianUnicode.GetBytes("AACS").CopyTo(wide, 30 * 2048);
        Assert.True(DiscCheck.Inspect(Write("bd16.iso", wide)).Encrypted);
        Assert.False(DiscCheck.Inspect(Write("bdclear.iso", Image("BDMV", []))).Encrypted);
    }

    [Fact]
    public void An_image_that_isnt_a_video_disc_is_just_a_file()
    {
        Assert.Equal(DiscKind.None, DiscCheck.Inspect(Write("data.iso", Image("README", []))).Kind);
        Assert.Equal(DiscKind.None, DiscCheck.Inspect(Write("song.mp3", new byte[100])).Kind);
        Assert.Equal(DiscKind.None, DiscCheck.Inspect(Path.Combine(_root, "nothing here")).Kind);
    }
}

public sealed class PlaybackTargetTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-target").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void A_file_is_opened_by_its_path()
    {
        var target = PlaybackTarget.For(@"D:\Films\a.mkv", _ => DiscVerdict.NotADisc);
        Assert.Equal(TargetKind.File, target.Kind);
        Assert.Equal(@"D:\Films\a.mkv", target.Location);
    }

    // Done when: an encrypted sample is refused with no playback attempted.
    [Fact]
    public void An_encrypted_disc_is_refused_with_the_plain_line()
    {
        var target = PlaybackTarget.For(@"E:\", _ => new DiscVerdict(DiscKind.Dvd, true, "a scrambled sector"));
        Assert.True(target.IsRefused);
        Assert.Equal("THIS DISC IS ENCRYPTED, SO IT CAN'T BE PLAYED.", target.Refusal);
    }

    [Fact]
    public void An_unencrypted_disc_goes_to_the_disc_reader()
    {
        Directory.CreateDirectory(Path.Combine(_root, "My Film", "VIDEO_TS"));
        var dvd = PlaybackTarget.For(Path.Combine(_root, "My Film", "VIDEO_TS"), _ => new DiscVerdict(DiscKind.Dvd, false, ""));
        Assert.Equal(TargetKind.Disc, dvd.Kind);
        Assert.StartsWith("dvd:///", dvd.Location);
        Assert.EndsWith("My%20Film/", dvd.Location);
        Assert.DoesNotContain("VIDEO_TS", dvd.Location);
    }

    [Theory]
    [InlineData(DiscKind.Dvd, @"E:\", "dvd:///E:/")]
    [InlineData(DiscKind.BluRay, @"C:\Films\Big Night.iso", "bluray:///C:/Films/Big%20Night.iso")]
    [InlineData(DiscKind.Dvd, @"C:\Фильмы\disc.iso", "dvd:///C:/%D0%A4%D0%B8%D0%BB%D1%8C%D0%BC%D1%8B/disc.iso")]
    public void Disc_addresses_are_written_the_way_libvlc_reads_them(DiscKind kind, string path, string mrl)
    {
        if (!OperatingSystem.IsWindows() && path.EndsWith('\\')) return;
        Assert.Equal(mrl, PlaybackTarget.DiscMrl(kind, path));
    }

    [Fact]
    public void A_clear_vob_plays_as_a_file()
    {
        var target = PlaybackTarget.For(@"D:\rip\VTS_01_1.VOB", _ => new DiscVerdict(DiscKind.Dvd, false, ""));
        Assert.Equal(TargetKind.File, target.Kind);
    }

    [Fact]
    public void Streams_skip_the_disc_check_and_ports_are_refused()
    {
        var stream = PlaybackTarget.For("https://radio.example.com/live.mp3", _ => throw new InvalidOperationException("no disc check for a link"));
        Assert.Equal(TargetKind.Stream, stream.Kind);
        Assert.Equal(StreamLink.PortRefused, PlaybackTarget.For("udp://@239.0.0.1:1234").Refusal);
        Assert.Equal("mmsh://media.example.com/live", PlaybackTarget.For("mms://media.example.com/live").Location);
    }

    // Independent review, finding 4: a scheme STX doesn't know went to LibVLC as a stream address. dshow:// and
    // screen:// open the camera and the desktop. Only the streams STX knows reach the engine.
    [Theory]
    [InlineData("dshow://")]
    [InlineData("screen://")]
    [InlineData("smb://nas/films/a.mkv")]
    [InlineData("tcp://example.com:1234")]
    [InlineData("rist://example.com:1234")]
    public void An_address_stx_doesnt_know_never_reaches_the_engine(string address) =>
        Assert.True(PlaybackTarget.For(address, _ => throw new InvalidOperationException("no disc check for a link")).IsRefused, address);

    // The review's own example, tcp://@:1234, isn't an address .NET can read, so it's taken as a file name, and the
    // engine opens files by path (LibVlcEngine: FromType.FromPath). VLC never sees it as a stream, so nothing listens.
    [Fact]
    public void An_unreadable_address_is_only_ever_a_file_name()
    {
        var target = PlaybackTarget.For("tcp://@:1234", _ => new DiscVerdict(DiscKind.None, false, ""));
        Assert.Equal(TargetKind.File, target.Kind);
    }

    // A file: address is a file, and plays as one.
    [Fact]
    public void A_file_address_plays_as_the_file()
    {
        var target = PlaybackTarget.For("file:///C:/Music/a%20song.mp3", _ => new DiscVerdict(DiscKind.None, false, ""));
        Assert.Equal(TargetKind.File, target.Kind);
        Assert.Equal(@"C:\Music\a song.mp3", target.Location);
    }
}

public class StreamLinkTests
{
    [Theory]
    [InlineData("https://cdn.example.com/live/master.m3u8", LinkKind.Stream)]
    [InlineData("https://cdn.example.com/vod/manifest.mpd", LinkKind.Stream)]
    [InlineData("http://ice.example.com:8000/jazz.mp3", LinkKind.Stream)]
    [InlineData("http://ice.example.com:8000/jazz.ogg", LinkKind.Stream)]
    [InlineData("rtsp://camera.local/stream1", LinkKind.Stream)]
    [InlineData("https://radio.example.com/listen.pls", LinkKind.Playlist)]
    [InlineData("https://radio.example.com/listen.m3u", LinkKind.Playlist)]
    [InlineData("https://www.example.com/watch.php", LinkKind.WebPage)]
    [InlineData("http://ice.example.com:8000/live", LinkKind.Unknown)]
    [InlineData("udp://@239.0.0.1:1234", LinkKind.Refused)]
    [InlineData("rtp://239.0.0.1:5004", LinkKind.Refused)]
    [InlineData("not a link", LinkKind.NotALink)]
    public void Addresses_are_sorted(string text, LinkKind kind) => Assert.Equal(kind, StreamLink.Classify(text).Kind);

    [Theory]
    [InlineData("audio/mpeg", LinkKind.Stream)]
    [InlineData("audio/aacp", LinkKind.Stream)]
    [InlineData("application/vnd.apple.mpegurl", LinkKind.Stream)]
    [InlineData("audio/x-scpls", LinkKind.Playlist)]
    [InlineData("text/html; charset=utf-8", LinkKind.WebPage)]
    [InlineData("application/json", null)]
    public void The_servers_content_type_settles_it(string type, LinkKind? kind) => Assert.Equal(kind, StreamLink.FromContentType(type));

    [Fact]
    public void A_pls_playlist_lists_its_streams_in_order()
    {
        string pls = "[playlist]\nNumberOfEntries=2\nFile1=http://ice1.example.com:8000/jazz\nTitle1=Jazz\nFile2=http://ice2.example.com/jazz\nVersion=2\n";
        Assert.Equal(["http://ice1.example.com:8000/jazz", "http://ice2.example.com/jazz"], StreamLink.ReadPlaylist(pls, new Uri("https://radio.example.com/listen.pls")));
    }

    [Fact]
    public void An_m3u_playlist_skips_comments_and_resolves_relative_entries()
    {
        string m3u = "#EXTM3U\n#EXTINF:-1,Jazz\nlive/jazz.mp3\r\nhttps://b.example.com/x.aac\n";
        Assert.Equal(["https://radio.example.com/live/jazz.mp3", "https://b.example.com/x.aac"], StreamLink.ReadPlaylist(m3u, new Uri("https://radio.example.com/listen.m3u")));
    }

    [Fact]
    public void An_xspf_playlist_is_read_too() =>
        Assert.Equal(["http://a.example.com/s.mp3"], StreamLink.ReadPlaylist("<playlist><trackList><track><location>http://a.example.com/s.mp3</location></track></trackList></playlist>", new Uri("http://a.example.com/p.xspf")));
}
