using System.Diagnostics;
using System.Text;

namespace Stx.Tools;

public interface IProcessRunner
{
    Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel);
}

/// <summary>Starts a tool with no shell, so nothing in an argument can run as a command. Canceling stops it and anything it started.</summary>
public sealed class ProcessRunner(ProcessPriorityClass? priority = null) : IProcessRunner
{
    public async Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        // yt-dlp is Python. Without this, titles outside the Windows code page arrive garbled.
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.Start();
        if (priority is { } level)
        {
            try { process.PriorityClass = level; }
            catch (InvalidOperationException) { }
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        return process.ExitCode;
    }
}
