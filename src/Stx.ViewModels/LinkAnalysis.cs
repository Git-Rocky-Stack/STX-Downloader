using System.Text;
using Stx.Audio;
using Stx.Core.Errors;
using Stx.Core.Logging;
using Stx.Tools;

namespace Stx.ViewModels;

public sealed record LinkAnalysis(string? Title, IReadOnlyList<PlaylistEntry> Entries, ErrorKind? Error)
{
    /// <summary>The best audio the site says it has, for showing before a download. Only ever labeled as reported.</summary>
    public YtDlpFormat? BestAudio { get; init; }

    public static LinkAnalysis Found(string? title, IReadOnlyList<PlaylistEntry> entries) => new(title, entries, null);
    public static LinkAnalysis Failed(ErrorKind kind) => new(null, [], kind);
}

public interface ILinkAnalyzer
{
    Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default);
}

/// <summary>Asks yt-dlp what a link holds, without downloading anything.</summary>
public sealed class YtDlpLinkAnalyzer(IProcessRunner process, Func<string> ytDlpPath, Func<ToolPaths> tools) : ILinkAnalyzer
{
    public async Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default)
    {
        var lines = new List<string>();
        int exit;
        try
        {
            exit = await process.RunAsync(ytDlpPath(), YtDlpArgs.ForEnumerate(url, tools()), line => { lock (lines) lines.Add(line); }, cancel).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return LinkAnalysis.Failed(ErrorKind.ToolFailure);
        }

        string json;
        string text;
        lock (lines)
        {
            json = lines.FirstOrDefault(l => l.StartsWith('{')) ?? "";
            text = Redactor.Scrub(string.Join('\n', lines.Where(l => !l.StartsWith('{'))));
        }
        var entries = PlaylistListing.Parse(json);
        // A list can still come back when some of its entries failed, so entries win over the exit code.
        return entries.Count > 0
            ? LinkAnalysis.Found(PlaylistListing.ParseTitle(json), entries) with { BestAudio = YtDlpFormat.Best(YtDlpFormat.ParseAll(json)) }
            : LinkAnalysis.Failed(ErrorClassifier.Classify(text, exit));
    }
}

public interface IShell
{
    void OpenPage(string url);
    void OpenFile(string path);
    void RevealFile(string path);
}
