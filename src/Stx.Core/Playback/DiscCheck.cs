using System.Text;

namespace Stx.Core.Playback;

public enum DiscKind { None, Dvd, BluRay }

/// <summary>What a disc, disc folder or disc image is, and whether it's encrypted.</summary>
public sealed record DiscVerdict(DiscKind Kind, bool Encrypted, string Why)
{
    public static readonly DiscVerdict NotADisc = new(DiscKind.None, false, "");
}

/// <summary>
/// STX's own check for encrypted discs, made before LibVLC is given anything. VLC's DVD plugins carry libdvdcss,
/// which would decrypt CSS; STX never lets them see an encrypted disc. The check reads the disc's own markings:
/// <list type="bullet">
/// <item>DVD: the PES scrambling bits in the MPEG program stream sectors of its VOB files (or of an image). CSS sets
/// them on every scrambled sector; unencrypted discs leave them clear.</item>
/// <item>Blu-ray: an AACS (or BD+ BDSVM) folder, which every protected disc carries, and the copy permission bits at
/// the start of each m2ts aligned unit, which AACS sets on encrypted units.</item>
/// </list>
/// A sector that can't be read counts as encrypted: a drive refuses to read CSS sectors it hasn't unlocked, and when
/// in doubt STX doesn't play. Nothing here decrypts anything.
/// </summary>
public static class DiscCheck
{
    public const string Refused = "THIS DISC IS ENCRYPTED, SO IT CAN'T BE PLAYED.";

    const int Sector = 2048;
    const int SectorsPerVob = 96;
    // Spread through a whole image; most of a film's sectors are video, which CSS scrambles.
    const int SectorsPerImage = 4096;
    const int AlignedUnit = 6144;
    const int UnitsPerStream = 32;
    // Disc images keep their directories near the start; the scan for names reads this much.
    const int ImageHead = 64 * 1024 * 1024;

    /// <summary>Whether a path is a disc, a disc folder or a disc image, and if so whether it's encrypted.</summary>
    public static DiscVerdict Inspect(string path)
    {
        try
        {
            if (Directory.Exists(path)) return Folder(path);
            if (!File.Exists(path)) return DiscVerdict.NotADisc;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".iso" or ".img" or ".udf") return Image(path);
            if (extension == ".vob") return Scrambled(path) ? new(DiscKind.Dvd, true, "a scrambled sector in " + Path.GetFileName(path)) : new(DiscKind.Dvd, false, "");
            if (extension is ".ifo" or ".bup") return Folder(Path.GetDirectoryName(path)!);
            return DiscVerdict.NotADisc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable: fail closed.
            return new(DiscKind.Dvd, true, "it couldn't be read: " + e.Message);
        }
    }

    static DiscVerdict Folder(string path)
    {
        string root = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)).ToUpperInvariant() switch
        {
            "VIDEO_TS" or "BDMV" => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!,
            _ => path,
        };
        string videoTs = Path.Combine(root, "VIDEO_TS"), bdmv = Path.Combine(root, "BDMV");
        if (Directory.Exists(bdmv)) return BluRayFolder(root, bdmv);
        if (Directory.Exists(videoTs)) return DvdFolder(videoTs);
        return DiscVerdict.NotADisc;
    }

    static DiscVerdict DvdFolder(string videoTs)
    {
        foreach (string vob in Directory.EnumerateFiles(videoTs, "*.VOB", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Order(StringComparer.OrdinalIgnoreCase))
            if (Scrambled(vob)) return new(DiscKind.Dvd, true, "a scrambled sector in " + Path.GetFileName(vob));
        return new(DiscKind.Dvd, false, "");
    }

    static DiscVerdict BluRayFolder(string root, string bdmv)
    {
        foreach (string marker in new[] { "AACS", "BDSVM" })
            if (Directory.Exists(Path.Combine(root, marker)) || Directory.Exists(Path.Combine(bdmv, marker)))
                return new(DiscKind.BluRay, true, $"an {marker} folder");
        string streams = Path.Combine(bdmv, "STREAM");
        if (Directory.Exists(streams))
            foreach (string m2ts in Directory.EnumerateFiles(streams, "*.m2ts", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }))
                if (CopyProtected(m2ts)) return new(DiscKind.BluRay, true, "copy permission bits set in " + Path.GetFileName(m2ts));
        return new(DiscKind.BluRay, false, "");
    }

    // An image is read as bytes: its directory names say what it is, and its sectors say whether they're scrambled.
    static DiscVerdict Image(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Sector * 16, FileOptions.RandomAccess);
        var head = new byte[(int)Math.Min(file.Length, ImageHead)];
        file.ReadExactly(head);
        bool bluRay = Contains(head, "BDMV"), dvd = Contains(head, "VIDEO_TS");
        if (bluRay)
        {
            foreach (string marker in new[] { "AACS", "BDSVM", "MKB_RO", "Unit_Key_RO" })
                if (Contains(head, marker)) return new(DiscKind.BluRay, true, "an " + marker + " entry in the image");
            return new(DiscKind.BluRay, false, "");
        }
        if (dvd) return ScrambledSectors(file, SectorsPerImage) ? new(DiscKind.Dvd, true, "a scrambled sector in the image") : new(DiscKind.Dvd, false, "");
        return DiscVerdict.NotADisc;
    }

    /// <summary>Whether any of a sample of the VOB's sectors, spread evenly through it, carries CSS's scrambling bits.</summary>
    public static bool Scrambled(string vob)
    {
        using var file = new FileStream(vob, FileMode.Open, FileAccess.Read, FileShare.Read, Sector, FileOptions.RandomAccess);
        return ScrambledSectors(file, SectorsPerVob);
    }

    static bool ScrambledSectors(FileStream file, long samples)
    {
        long sectors = file.Length / Sector;
        if (sectors == 0) return false;
        long count = Math.Min(sectors, Math.Max(1, samples));
        var buffer = new byte[Sector];
        for (long i = 0; i < count; i++)
        {
            long at = sectors <= count ? i : i * sectors / count;
            file.Position = at * Sector;
            file.ReadExactly(buffer);
            if (IsScrambledSector(buffer)) return true;
        }
        return false;
    }

    /// <summary>
    /// One 2048-byte DVD sector: an MPEG-2 pack header (00 00 01 BA), then a PES packet whose header's
    /// PES_scrambling_control bits (byte 6, mask 0x30) are non-zero when CSS scrambled it.
    /// </summary>
    public static bool IsScrambledSector(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < 24 || sector[0] != 0 || sector[1] != 0 || sector[2] != 1 || sector[3] != 0xBA) return false;
        // MPEG-2 pack headers are 14 bytes plus stuffing; MPEG-1's (marker '0010') are 12 and have no PES flags to read.
        if ((sector[4] & 0xC0) != 0x40) return false;
        int pes = 14 + (sector[13] & 0x07);
        if (pes + 7 > sector.Length || sector[pes] != 0 || sector[pes + 1] != 0 || sector[pes + 2] != 1) return false;
        byte stream = sector[pes + 3];
        bool carriesFlags = stream is 0xBD or (>= 0xC0 and <= 0xEF);
        return carriesFlags && (sector[pes + 6] & 0xC0) == 0x80 && (sector[pes + 6] & 0x30) != 0;
    }

    /// <summary>Whether the first aligned units of an m2ts carry AACS's copy permission bits (the top two bits of each unit's first TP_extra_header).</summary>
    public static bool CopyProtected(string m2ts)
    {
        using var file = new FileStream(m2ts, FileMode.Open, FileAccess.Read, FileShare.Read, AlignedUnit, FileOptions.SequentialScan);
        var unit = new byte[AlignedUnit];
        for (int n = 0; n < UnitsPerStream; n++)
        {
            if (file.Read(unit) < 5) break;
            if (unit[4] == 0x47 && (unit[0] & 0xC0) != 0) return true;
        }
        return false;
    }

    // UDF stores a name in 8 bits a character, or 16 (big-endian) when it has to; ISO 9660 in 8.
    static bool Contains(byte[] haystack, string name) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(name)) >= 0 || haystack.AsSpan().IndexOf(Encoding.BigEndianUnicode.GetBytes(name)) >= 0;
}
