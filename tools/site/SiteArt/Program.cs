using System.Text;

namespace Stx.SiteArt;

/// <summary>
/// <c>--out DIR</c> writes every part at 1x and 2x, plus manifest.json. <c>--check DIR</c> draws them again and exits 1,
/// naming each file whose bytes differ from what is in DIR, so the committed art can't fall behind the art code.
/// </summary>
public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out);

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 2 || args[0] is not ("--out" or "--check"))
        {
            output.WriteLine("usage: SiteArt --out DIR | --check DIR");
            return 2;
        }

        var files = Draw();
        string dir = args[1];

        if (args[0] == "--out")
        {
            Directory.CreateDirectory(dir);
            foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(dir, name), bytes);
            output.WriteLine($"wrote {files.Count} files to {dir}");
            return 0;
        }

        var stale = files.Where(f => !File.Exists(Path.Combine(dir, f.Key)) || !File.ReadAllBytes(Path.Combine(dir, f.Key)).AsSpan().SequenceEqual(f.Value)).Select(f => f.Key).ToList();
        foreach (var name in stale) output.WriteLine($"differs: {name}");
        output.WriteLine(stale.Count == 0 ? $"{files.Count} files match" : $"{stale.Count} of {files.Count} files differ; run SiteArt --out {dir}");
        return stale.Count == 0 ? 0 : 1;
    }

    static SortedDictionary<string, byte[]> Draw()
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var manifest = new StringBuilder("[\n");
        var parts = Parts.All();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            files[$"{part.Name}@1x.png"] = PngWriter.Encode(part.Render(1));
            files[$"{part.Name}@2x.png"] = PngWriter.Encode(part.Render(2));
            manifest.Append($"  {{\"name\": \"{part.Name}\", \"w\": {part.LogicalWidth}, \"h\": {part.LogicalHeight}, \"x1\": \"{part.Name}@1x.png\", \"x2\": \"{part.Name}@2x.png\"}}");
            manifest.Append(i < parts.Count - 1 ? ",\n" : "\n");
        }
        manifest.Append("]\n");
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToString());
        return files;
    }
}
