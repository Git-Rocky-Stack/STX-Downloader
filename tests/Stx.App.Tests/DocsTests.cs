using System.Text.RegularExpressions;
using Xunit;

namespace Stx.App.Tests;

/// <summary>
/// The README and docs point at the code that does what they say, so a reader can check it. They name files and
/// members, not line numbers: by 2026-11-08 most of the README's 90 line references pointed at a blank line or at
/// other code. These hold every reference to something that's there.
/// </summary>
public class DocsTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static readonly string[] CodeExtensions = [".cs", ".js", ".json", ".ps1", ".iss", ".csproj", ".props", ".manifest", ".md", ".yml", ".html"];

    // The living documents. The dated ones (docs/superpowers, REAL-SITE-CHECKS) record a moment, and stay as written.
    static IEnumerable<string> Docs()
    {
        string root = Root();
        yield return Path.Combine(root, "README.md");
        foreach (string name in new[] { "PLAYBACK.md", "PERFORMANCE.md", "THIRD_PARTY.md" }) yield return Path.Combine(root, "docs", name);
    }

    static IEnumerable<(string Doc, int Line, string Text)> Quoted()
    {
        foreach (string doc in Docs())
        {
            string[] lines = File.ReadAllLines(doc);
            bool fenced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
                if (fenced) continue;
                foreach (Match m in Regex.Matches(lines[i], "`([^`]+)`")) yield return (Path.GetFileName(doc), i + 1, m.Groups[1].Value);
            }
        }
    }

    static IEnumerable<string> RepoFiles() =>
        Directory.EnumerateFiles(Root(), "*", SearchOption.AllDirectories).Where(f =>
        {
            string path = f.Replace('\\', '/');
            return !path.Contains("/bin/") && !path.Contains("/obj/") && !path.Contains("/node_modules/") && !path.Contains("/.git/")
                && !path.Contains("/.engine/") && !path.Contains("/dist/") && !path.Contains("/TestResults/");
        });

    static bool IsFileReference(string text) =>
        !text.Contains(' ') && CodeExtensions.Contains(Path.GetExtension(text.Split(':')[0]), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Docs_name_members_not_line_numbers()
    {
        foreach (var (doc, line, text) in Quoted().Where(q => IsFileReference(q.Text)))
            Assert.False(Regex.IsMatch(text, @":\d+$"), $"{doc}:{line} points at a line, `{text}`: name the member instead.");
    }

    [Fact]
    public void Every_file_the_docs_name_is_in_the_repository()
    {
        string root = Root();
        var names = RepoFiles().Select(f => Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (doc, line, text) in Quoted().Where(q => IsFileReference(q.Text)))
        {
            string path = text.Split(':')[0].Replace('\\', '/');
            bool there = path.Contains('/') ? File.Exists(Path.Combine(root, path)) : names.Contains(path);
            Assert.True(there, $"{doc}:{line} names `{text}`, which isn't in the repository.");
        }
    }

    // `Type` and `Type.Member` must be names the code declares or uses, or the reader has nothing to search for.
    [Fact]
    public void Every_type_and_member_the_docs_name_is_in_the_code()
    {
        string code = string.Concat(RepoFiles().Where(f => Path.GetExtension(f) is ".cs" or ".js" or ".iss" or ".ps1" or ".csproj").Select(File.ReadAllText));
        var words = Regex.Matches(code, @"[A-Za-z_]\w*").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var (doc, line, text) in Quoted().Where(q => Regex.IsMatch(q.Text, @"^[A-Z][A-Za-z0-9]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") && !IsFileReference(q.Text)))
            foreach (string part in text.Split('.'))
                Assert.True(words.Contains(part), $"{doc}:{line} names `{text}`, and the code has no {part}.");
    }
}
