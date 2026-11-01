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
        if (!string.IsNullOrEmpty(url)) config.AppendLine(Quote(url));

        string configPath = Path.Combine(root, name + ".conf");
        WritePrivate(configPath, config.ToString());
        return new CredentialFile(configPath, cookiePath);
    }

    /// <summary>Call at startup. A crash can leave these behind, and nothing still needs them.</summary>
    public static void DeleteLeftovers(string? root = null)
    {
        root ??= DefaultRoot();
        if (!Directory.Exists(root)) return;
        foreach (string file in Directory.GetFiles(root, Prefix + "*")) File.Delete(file);
    }

    public void Dispose()
    {
        File.Delete(ConfigPath);
        if (CookiePath is not null) File.Delete(CookiePath);
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
