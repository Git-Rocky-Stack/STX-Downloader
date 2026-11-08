namespace Stx.Core.Playback;

/// <summary>
/// Whether a file carries Dirac / VC-2 video, read from its bytes rather than from LibVLC. VLC 3.0.24's engine crashes
/// the process on Dirac (the official desktop build too), so when LibVLC can't finish reading a file's tracks, STX
/// looks for Dirac itself instead of assuming the file is safe.
/// </summary>
/// <remarks>
/// Every Dirac stream opens with a sequence header: the parse-info prefix "BBCD" followed by parse code 0x00. FFmpeg
/// puts it within the first few kilobytes in MKV, MOV, MP4, MPEG-TS, NUT and AVI (checked 2026-11-07); Matroska also
/// names the codec "V_DIRAC". A five-byte signature in a megabyte of other data is a one-in-a-million coincidence.
/// </remarks>
public static class DiracSniff
{
    const int Window = 1024 * 1024;
    static ReadOnlySpan<byte> SequenceHeader => "BBCD\0"u8;
    static ReadOnlySpan<byte> MatroskaCodec => "V_DIRAC"u8;

    /// <summary>True when the file's first megabyte holds Dirac's sequence header or Matroska's Dirac codec name.
    /// A file that can't be read is false: LibVLC won't get further with it than STX did.</summary>
    public static bool Present(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var buffer = new byte[Window];
            int read = 0, n;
            while (read < buffer.Length && (n = file.Read(buffer, read, buffer.Length - read)) > 0) read += n;
            return Present(buffer.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The same check on bytes already read.</summary>
    public static bool Present(ReadOnlySpan<byte> head) => head.IndexOf(SequenceHeader) >= 0 || head.IndexOf(MatroskaCodec) >= 0;
}
