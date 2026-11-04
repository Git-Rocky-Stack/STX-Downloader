using Stx.Look;

namespace Stx.App.Gallery;

/// <summary>Args: --gallery=&lt;name&gt; --mode=dark|light --hold --reduce-motion</summary>
public sealed record GalleryOptions(string Name, LookMode Mode, bool Hold, bool ReduceMotion = false)
{
    public static GalleryOptions Parse(string[] args)
    {
        string Value(string key, string fallback) =>
            args.FirstOrDefault(a => a.StartsWith($"--{key}=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? fallback;

        return new GalleryOptions(
            Value("gallery", "empty"),
            Value("mode", "dark") == "light" ? LookMode.Light : LookMode.Dark,
            args.Contains("--hold"),
            args.Contains("--reduce-motion"));
    }
}
