using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Stx.Tools;

/// <summary>
/// Hands a captured job's cookies, headers and stream link to yt-dlp without putting them on a command line,
/// where other programs could read them. The files are readable only by you and exist only while the job runs.
/// </summary>
public sealed class CredentialFile : IDisposable
{
    const string Prefix = "stx-";

    public string ConfigPath { get; }
    public string? CookiePath { get; }

    CredentialFile(string configPath, string? cookiePath)
    {
        ConfigPath = configPath;
        CookiePath = cookiePath;
    }

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STXDownloader", "run");

    public static CredentialFile Create(string? cookies, IReadOnlyDictionary<string, string> headers, string? url, string? root = null)
    {
        root ??= DefaultRoot();
        Directory.CreateDirectory(root);
        string name = Prefix + Guid.NewGuid().ToString("N");
        string? cookiePath = null;
        var config = new StringBuilder();

        if (!string.IsNullOrEmpty(cookies))
        {
            cookiePath = Path.Combine(root, name + ".cookies");
            string text = cookies.StartsWith("# ", StringComparison.Ordinal) ? cookies : "# Netscape HTTP Cookie File\n" + cookies;
            WritePrivate(cookiePath, text);
            config.Append("--cookies ").AppendLine(Quote(cookiePath.Replace('\\', '/')));
        }
        foreach (var (key, value) in headers)
            config.Append("--add-headers ").AppendLine(Quote($"{key}:{value}"));
        // The address is a bare line in yt-dlp's config, where one starting with "-" would be read as an option. Every
        // caller passes a web address already; this keeps it so for any later one (review 2026-11-07).
        if (!string.IsNullOrEmpty(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
                throw new ArgumentException("Only a web address goes in a credential file.", nameof(url));
            config.AppendLine(Quote(url));
        }

        string configPath = Path.Combine(root, name + ".conf");
        WritePrivate(configPath, config.ToString());
        return new CredentialFile(configPath, cookiePath);
    }

    /// <summary>Call at startup. A crash can leave these behind, and nothing still needs them.</summary>
    public static void DeleteLeftovers(string? root = null)
    {
        root ??= DefaultRoot();
        if (!Directory.Exists(root)) return;
        // One file held by a scan mustn't stop STX starting: it goes at the next launch instead.
        foreach (string file in Directory.GetFiles(root, Prefix + "*")) TryDelete(file);
    }

    /// <summary>Deletes the files. One that can't go now (an antivirus scan holding it) goes at the next launch; this
    /// runs as a job ends, so it must never replace the job's outcome with an exception (review 2026-11-07).</summary>
    public void Dispose()
    {
        TryDelete(ConfigPath);
        if (CookiePath is not null) TryDelete(CookiePath);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // A line break would start a new option, and a stray quote would end this one.
    static string Quote(string value)
    {
        string flat = value.Replace("\r", "").Replace("\n", "");
        return "\"" + flat.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // The permissions are set as the file is created, so it's never readable by anyone else, even briefly.
    static void WritePrivate(string path, string text)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security);
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes);
    }
}
