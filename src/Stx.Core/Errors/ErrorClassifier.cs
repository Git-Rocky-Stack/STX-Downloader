using System.Text.RegularExpressions;

namespace Stx.Core.Errors;

/// <summary>Reads what a tool printed and names the failure. Unknown output is a tool failure.</summary>
public static partial class ErrorClassifier
{
    /// <summary>Checked in order, because some messages hold another kind's words, such as "Private video. Sign in if...".</summary>
    static readonly (ErrorKind Kind, Regex Pattern)[] Rules =
    [
        (ErrorKind.DrmDetected, Rule(@"\bDRM\b")),
        (ErrorKind.OutputFailure, Rule(@"no space left|not enough space|errno 28|errno 13|permission denied|access is denied|cannot find the path|unable to open for writing|unable to write data|unable to create directory")),
        (ErrorKind.PrivateOrUnavailable, Rule(@"private video|video unavailable|not available|has been removed|geo.?restrict|HTTP Error 404")),
        (ErrorKind.SignInNeeded, Rule(@"sign in to confirm|use --cookies|registered users|login required|log in|requires authentication|HTTP Error 401")),
        (ErrorKind.LinkExpired, Rule(@"HTTP Error 403|HTTP Error 410|link expired|url has expired")),
        (ErrorKind.UnsupportedSite, Rule(@"unsupported url|no suitable (extractor|info extractor)")),
        (ErrorKind.Network, Rule(@"getaddrinfo|name resolution|timed out|connection (refused|reset|aborted)|network is unreachable|unable to connect|incompleteread|handshake|remote end closed")),
    ];

    public static ErrorKind Classify(string toolOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(toolOutput)) return ErrorKind.ToolFailure;

        // A link can hold any word, like /drm/ or /private-video/, and says nothing about what went wrong.
        string text = Links().Replace(toolOutput, " ");

        // Warnings can mention things that didn't cause the failure, so error lines decide when there are any.
        var errors = text.Split('\n').Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) && !line.TrimStart().StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)).ToArray();
        string evidence = errors.Length > 0 ? string.Join('\n', errors) : text;

        foreach (var (kind, pattern) in Rules)
            if (pattern.IsMatch(evidence)) return kind;
        return ErrorKind.ToolFailure;
    }

    static Regex Rule(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [GeneratedRegex(@"[a-z][a-z0-9+.-]*://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
}
