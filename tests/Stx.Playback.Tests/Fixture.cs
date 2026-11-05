using Stx.Core.Playback;

namespace Stx.Playback.Tests;

static class Fixture
{
    public static string Dir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "fixtures", "playback");
    }

    public static string File(string name) => Path.Combine(Dir(), name);

    public static readonly PerformanceProfile Profile = new(true, 2, EnhanceLevel.Off);

    /// <summary>Polls until the condition holds or the time runs out; returns whether it held.</summary>
    public static bool Within(TimeSpan limit, Func<bool> condition)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }
}
