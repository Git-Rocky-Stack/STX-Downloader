using System.Text.RegularExpressions;

namespace Stx.Core.Logging;

/// <summary>Removes cookies, auth headers and stream tokens. Everything written to a log or the database passes through here first.</summary>
public static partial class Redactor
{
    const string Hidden = "[redacted]";

    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        string s = Headers().Replace(text, m => m.Groups["name"].Value + m.Groups["sep"].Value + Hidden);
        s = BearerTokens().Replace(s, "Bearer " + Hidden);
        s = UrlPasswords().Replace(s, "://" + Hidden + "@");
        s = QueryStrings().Replace(s, m => m.Groups["url"].Value + "?" + Hidden);
        s = TokenValues().Replace(s, m => m.Groups["name"].Value + "=" + Hidden);
        return s;
    }

    // A value runs to the end of the line, or to the next header on the same line.
    [GeneratedRegex(@"(?<name>\b(?:set-cookie|cookie|proxy-authorization|authorization|x-api-key|x-auth-token))(?<sep>[""']?\s*[:=]\s*)[^\r\n]*?(?=\s+(?:set-cookie|cookie|proxy-authorization|authorization|x-api-key|x-auth-token)\s*[:=]|\r?$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Headers();

    [GeneratedRegex(@"\bBearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokens();

    [GeneratedRegex(@"://[^/\s:@]+:[^/\s@]+@")]
    private static partial Regex UrlPasswords();

    // The whole query goes, not just known names, because stream hosts name their tokens freely.
    [GeneratedRegex(@"(?<url>[a-z][a-z0-9+.-]*://[^\s?""']+)\?[^\s""']*", RegexOptions.IgnoreCase)]
    private static partial Regex QueryStrings();

    // Some hosts put tokens in the path, and tools print loose name=value pairs.
    [GeneratedRegex(@"(?<name>\b(?:[a-z_-]*token|hdntl|hdnts|hdnea|sig|signature|auth|policy|key-pair-id|api_key|apikey))=[^\s&/""']+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenValues();
}
