namespace Stx.Core.Logging;

/// <summary>
/// Where STX writes what went wrong when nobody was looking: a background task that threw, a listener that had to
/// start again, a crash. One plain text file, <c>%LOCALAPPDATA%\STXDownloader\logs\errors.log</c>, kept under 1 MB
/// (the older half moves to <c>errors.1.log</c>). Every line passes through <see cref="Redactor"/>, so a token in a
/// link never reaches the disk. Writing never throws: a log that can't be written is skipped, not fatal.
/// </summary>
public static class ErrorLog
{
    const long MostBytes = 1024 * 1024;
    static readonly object Gate = new();
    static string? _path;

    /// <summary>The file being written. Settable once at startup, and by tests.</summary>
    public static string FilePath
    {
        get
        {
            lock (Gate)
                return _path ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STXDownloader", "logs", "errors.log");
        }
        set { lock (Gate) _path = value; }
    }

    /// <summary>Raised after each entry is written, with the line as written. For tests and the console's status line.</summary>
    public static event EventHandler<string>? Written;

    /// <summary>Records an exception with where it happened, in a word or two ("library watcher", "startup").</summary>
    public static void Write(string where, Exception error) => Write(where, error.ToString());

    /// <summary>Records a plain message with where it happened.</summary>
    public static void Write(string where, string message)
    {
        string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{where}] {Redactor.Scrub(message)}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                string path = FilePath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MostBytes)
                    File.Move(path, System.IO.Path.ChangeExtension(path, ".1.log"), overwrite: true);
                File.AppendAllText(path, line);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            // Nowhere to write it. The app carries on; the log is a help, never a requirement.
        }
        try { Written?.Invoke(null, line); }
        catch (Exception) { /* A listener's failure must not become the caller's. */ }
    }

    /// <summary>Runs a background task and records anything it throws, other than a cancellation while closing.</summary>
    public static async Task Observe(Task task, string where)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Write(where, e); }
    }
}
