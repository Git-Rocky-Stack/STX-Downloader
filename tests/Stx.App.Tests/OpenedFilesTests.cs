using Stx.Core.Queue;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>The catalog as the app has it: one store that both adds media and lists the library.</summary>
sealed class SharedCatalog : IMediaCatalog, ILibraryCatalog
{
    readonly MemoryCatalog _media = new();
    public List<MediaRecord> Items => _media.Items;
    public List<(long Media, string Path, string Format)> Files => _media.Files;

    public long AddItem(string? pageUrl, string? sourceId, string? title) => _media.AddItem(pageUrl, sourceId, title);
    public MediaRecord? GetItem(long id) => _media.GetItem(id);
    public long AddFile(long mediaId, string path, string? hash, string format) => _media.AddFile(mediaId, path, hash, format);
    public IReadOnlyList<MediaFact> ExistingFacts() => _media.ExistingFacts();
    public void RememberCapture(string key, long mediaId) { }
    public long? RememberedCapture(string key) => null;

    public IReadOnlyList<LibraryItem> Library() =>
        [.. Items.Select(i => new LibraryItem(i.Id, i.Title, i.PageUrl, [.. Files.Select((f, n) => (f, n)).Where(x => x.f.Media == i.Id).Select(x => new LibraryFile(x.n + 1, x.f.Path, x.f.Format))]))];
}

public class OpenedFilesTests
{
    readonly SharedCatalog _catalog = new();

    (OpenedFiles Opened, LibraryViewModel Library) Make()
    {
        var engine = new QueueEngine(new MemoryJobs(), new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(_catalog, engine, new ShellSpy(), _ => true, _ => { });
        library.Refresh();
        return (new OpenedFiles(_catalog, library), library);
    }

    [Fact]
    public void Adopting_a_new_path_adds_one_item_and_one_file()
    {
        var (opened, library) = Make();
        var row = opened.Adopt(@"D:\Films\Big Night.mkv");
        Assert.Single(_catalog.Items);
        Assert.Single(_catalog.Files);
        Assert.Equal(@"D:\Films\Big Night.mkv", row.Path);
        Assert.Contains(row, library.Rows);
    }

    [Fact]
    public void Adopting_the_same_path_again_in_other_case_adds_nothing()
    {
        var (opened, _) = Make();
        var first = opened.Adopt(@"D:\Films\Big Night.mkv");
        var again = opened.Adopt(@"d:\films\BIG NIGHT.MKV");
        Assert.Single(_catalog.Items);
        Assert.Single(_catalog.Files);
        Assert.Equal(first.FileId, again.FileId);
    }

    [Fact]
    public void The_adopted_row_is_titled_with_the_file_name()
    {
        var (opened, _) = Make();
        Assert.Equal("Big Night", opened.Adopt(@"D:\Films\Big Night.mkv").Title);
        Assert.Equal(SourceViewModel.OriginalFormat, _catalog.Files[0].Format);
    }
}
