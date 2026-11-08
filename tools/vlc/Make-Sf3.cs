// Converts a SoundFont 2 (.sf2) to SoundFont 3 (.sf3): every sample compressed as its own Ogg Vorbis stream, the
// rest of the font unchanged. FluidSynth, inside VLC's engine, reads SF3 (its plugin is built with libsndfile and
// libVorbis). The rules are FluidSynth 2.x's own (src/sfloader/fluid_sffile.c, fluid_defsfont.c):
//   - the font's version (ifil) is 3;
//   - a compressed sample's type has 0x10 (FLUID_SAMPLETYPE_OGG_VORBIS) set, and its start and end are byte offsets
//     of its Ogg stream inside the smpl chunk;
//   - its loop points count frames from the start of the decoded sample, not of the smpl chunk;
//   - SF2's 46 silent words after each sample aren't needed.
// FFmpeg's libvorbis does the encoding; with bitexact flags, the same FFmpeg build and the same font give the same
// file byte for byte, which is what lets tools/vlc/engine.json pin the result's sha256.
//
// dotnet run tools/vlc/Make-Sf3.cs -- <in.sf2> <out.sf3> <ffmpeg.exe> [vorbis quality, default 6]
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

if (args.Length < 3) { Console.Error.WriteLine("Make-Sf3 <in.sf2> <out.sf3> <ffmpeg.exe> [quality]"); return 2; }
string input = args[0], output = args[1], ffmpeg = args[2];
string quality = args.Length > 3 ? args[3] : "6";

byte[] font = File.ReadAllBytes(input);
if (Ascii(font, 0) != "RIFF" || Ascii(font, 8) != "sfbk") throw new InvalidDataException($"{input} isn't a SoundFont");

// The three lists of a SoundFont: INFO (with ifil), sdta (with smpl) and pdta (with shdr, the sample headers).
var lists = new Dictionary<string, (int Start, int End)>();
foreach (var (id, start, size) in Chunks(font, 12, font.Length))
    if (id == "LIST") lists[Ascii(font, start)] = (start + 4, start + size);
foreach (string needed in new[] { "INFO", "sdta", "pdta" })
    if (!lists.ContainsKey(needed)) throw new InvalidDataException($"{input} has no {needed} list");

var smpl = Chunks(font, lists["sdta"].Start, lists["sdta"].End).Single(c => c.Id == "smpl");
if (Chunks(font, lists["sdta"].Start, lists["sdta"].End).Any(c => c.Id == "sm24"))
    throw new InvalidDataException("24-bit samples (sm24) aren't converted");
var shdr = Chunks(font, lists["pdta"].Start, lists["pdta"].End).Single(c => c.Id == "shdr");
const int HeaderSize = 46;
int count = shdr.Size / HeaderSize - 1;   // the last record is the terminal "EOS"

// Each sample to Ogg Vorbis, in parallel; the results are put together in the font's own order.
string work = Directory.CreateTempSubdirectory("stx-sf3-").FullName;
var encoded = new byte[count][];
try
{
    Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
    {
        var h = Header(i);
        if ((h.Type & 0x8000) != 0) throw new InvalidDataException($"sample {i} is a ROM sample, which can't be converted");
        int frames = checked((int)(h.End - h.Start));
        string wav = Path.Combine(work, $"{i}.wav"), ogg = Path.Combine(work, $"{i}.ogg");
        File.WriteAllBytes(wav, Wav(font.AsSpan(smpl.Start + checked((int)h.Start) * 2, frames * 2), h.Rate));
        var start = new ProcessStartInfo(ffmpeg)
        {
            ArgumentList = { "-hide_banner", "-loglevel", "error", "-y", "-i", wav, "-c:a", "libvorbis", "-q:a", quality,
                             "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:a", "+bitexact", ogg },
            RedirectStandardError = true,
        };
        using var p = Process.Start(start)!;
        string errors = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"FFmpeg couldn't encode sample {i} ({h.Rate} Hz): {errors}");
        encoded[i] = File.ReadAllBytes(ogg);
        File.Delete(wav);
        File.Delete(ogg);
    });
}
finally { Directory.Delete(work, true); }

// The new smpl chunk: the Ogg streams one after another, each starting on an even byte.
var data = new MemoryStream();
var offsets = new (uint Start, uint End)[count];
for (int i = 0; i < count; i++)
{
    offsets[i] = ((uint)data.Length, (uint)(data.Length + encoded[i].Length));
    data.Write(encoded[i]);
    if (data.Length % 2 == 1) data.WriteByte(0);
}

// The new sample headers: byte offsets, loops relative to each sample, and the Ogg Vorbis flag.
byte[] headers = font.AsSpan(shdr.Start, shdr.Size).ToArray();
for (int i = 0; i < count; i++)
{
    var h = Header(i);
    var at = headers.AsSpan(i * HeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(at[20..], offsets[i].Start);
    BinaryPrimitives.WriteUInt32LittleEndian(at[24..], offsets[i].End);
    BinaryPrimitives.WriteUInt32LittleEndian(at[28..], h.LoopStart - h.Start);
    BinaryPrimitives.WriteUInt32LittleEndian(at[32..], h.LoopEnd - h.Start);
    BinaryPrimitives.WriteUInt16LittleEndian(at[44..], (ushort)(h.Type | 0x10));
}

// The INFO list as it was, with version 3.1 and this tool named as the editor.
var info = new MemoryStream();
foreach (var (id, start, size) in Chunks(font, lists["INFO"].Start, lists["INFO"].End))
{
    byte[] body = id switch
    {
        "ifil" => [3, 0, 1, 0],
        "ISFT" => Zstr($"{Zstring(font.AsSpan(start, size))}:STX Make-Sf3 (Ogg Vorbis q{quality})"),
        _ => font.AsSpan(start, size).ToArray(),
    };
    WriteChunk(info, id, body);
}

var pdta = new MemoryStream();
foreach (var (id, start, size) in Chunks(font, lists["pdta"].Start, lists["pdta"].End))
    WriteChunk(pdta, id, id == "shdr" ? headers : font.AsSpan(start, size).ToArray());

var sdta = new MemoryStream();
WriteChunk(sdta, "smpl", data.ToArray());

var riff = new MemoryStream();
riff.Write("sfbk"u8);
WriteChunk(riff, "LIST", [.. "INFO"u8, .. info.ToArray()]);
WriteChunk(riff, "LIST", [.. "sdta"u8, .. sdta.ToArray()]);
WriteChunk(riff, "LIST", [.. "pdta"u8, .. pdta.ToArray()]);
using (var file = File.Create(output)) WriteChunk(file, "RIFF", riff.ToArray());
Console.WriteLine($"{input} ({font.Length:N0} bytes, {count} samples) -> {output} ({new FileInfo(output).Length:N0} bytes)");
return 0;

(uint Start, uint End, uint LoopStart, uint LoopEnd, uint Rate, ushort Type) Header(int i)
{
    var r = font.AsSpan(shdr.Start + i * HeaderSize, HeaderSize);
    return (BinaryPrimitives.ReadUInt32LittleEndian(r[20..]), BinaryPrimitives.ReadUInt32LittleEndian(r[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(r[28..]), BinaryPrimitives.ReadUInt32LittleEndian(r[32..]),
            BinaryPrimitives.ReadUInt32LittleEndian(r[36..]), BinaryPrimitives.ReadUInt16LittleEndian(r[44..]));
}

static string Ascii(byte[] b, int at) => Encoding.ASCII.GetString(b, at, 4);

static IEnumerable<(string Id, int Start, int Size)> Chunks(byte[] b, int start, int end)
{
    for (int i = start; i + 8 <= end;)
    {
        int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 4)));
        yield return (Ascii(b, i), i + 8, size);
        i += 8 + size + (size & 1);
    }
}

static void WriteChunk(Stream to, string id, byte[] body)
{
    to.Write(Encoding.ASCII.GetBytes(id));
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
    to.Write(size);
    to.Write(body);
    if (body.Length % 2 == 1) to.WriteByte(0);
}

// A text field ends with a zero and keeps an even length, as the SoundFont spec asks.
static byte[] Zstr(string text)
{
    var bytes = Encoding.ASCII.GetBytes(text).ToList();
    bytes.Add(0);
    if (bytes.Count % 2 == 1) bytes.Add(0);
    return [.. bytes];
}

static string Zstring(ReadOnlySpan<byte> field) => Encoding.ASCII.GetString(field[..(field.IndexOf((byte)0) is >= 0 and var z ? z : field.Length)]);

static byte[] Wav(ReadOnlySpan<byte> pcm16, uint rate)
{
    var wav = new byte[44 + pcm16.Length];
    "RIFF"u8.CopyTo(wav);
    BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(36 + pcm16.Length));
    "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
    BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 1);       // PCM
    BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), 1);       // mono
    BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24), rate);
    BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), rate * 2);
    BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), 16);
    "data"u8.CopyTo(wav.AsSpan(36));
    BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), (uint)pcm16.Length);
    pcm16.CopyTo(wav.AsSpan(44));
    return wav;
}
