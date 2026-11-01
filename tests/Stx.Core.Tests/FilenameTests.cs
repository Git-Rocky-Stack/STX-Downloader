using System.Globalization;
using System.Text;
using Stx.Core.Files;
using Xunit;

namespace Stx.Core.Tests;

public class FilenameTests
{
    const string Folder = @"D:\Media";

    static bool Nothing(string _) => false;

    static string Name(string title, string ext = ".mp4") => Path.GetFileName(Filename.Safe(title, ext, Folder, Nothing));

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("COM¹")]
    public void Reserved_device_names_are_made_safe(string title)
    {
        string stem = Path.GetFileNameWithoutExtension(Name(title));
        Assert.NotEqual(title, stem, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(title, stem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_reserved_name_with_a_dot_is_still_reserved_on_windows() =>
        Assert.False(Name("CON.backup").StartsWith("CON.", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Illegal_characters_are_removed()
    {
        string name = Name("a<b>:c?");
        Assert.Empty(name.Where(c => "<>:\"/\\|?*".Contains(c)));
        Assert.EndsWith(".mp4", name);
        Assert.Contains("a", name);
        Assert.Contains("c", name);
    }

    [Fact]
    public void A_title_cannot_climb_out_of_the_folder()
    {
        string path = Filename.Safe(@"..\..\Windows\evil", ".mp4", Folder, Nothing);
        Assert.Equal(Folder, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Control_characters_are_removed() =>
        Assert.Empty(Name("a\tb\nc\0d\u001f").Where(char.IsControl));

    [Theory]
    [InlineData("song.")]
    [InlineData("song ")]
    [InlineData("song. . ")]
    public void Trailing_dots_and_spaces_are_removed_because_windows_drops_them(string title) =>
        Assert.Equal("song.mp4", Name(title));

    [Fact]
    public void A_400_character_title_fits_a_240_character_full_path()
    {
        string path = Filename.Safe(new string('x', 400), ".mp4", Folder, Nothing);
        Assert.InRange(path.Length, 200, Filename.MaxPathLength);
        Assert.EndsWith(".mp4", path);
    }

    [Fact]
    public void A_deep_folder_shortens_the_name_further()
    {
        string deep = @"D:\" + new string('f', 180);
        string path = Filename.Safe(new string('x', 400), ".flac", deep, Nothing);
        Assert.True(path.Length <= Filename.MaxPathLength, $"{path.Length}");
        Assert.EndsWith(".flac", path);
    }

    [Fact]
    public void A_folder_too_deep_for_any_name_still_gives_a_usable_name()
    {
        string path = Filename.Safe("Song", ".mp4", @"D:\" + new string('f', 300), Nothing);
        Assert.False(string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(path)));
    }

    [Fact]
    public void Emoji_titles_survive() => Assert.Equal("Sunset 🌅 Drive.mp4", Name("Sunset 🌅 Drive"));

    [Fact]
    public void Non_latin_titles_survive() => Assert.Equal("東京の夜 ночь.mp4", Name("東京の夜 ночь"));

    [Fact]
    public void Cutting_a_long_emoji_title_never_splits_a_character()
    {
        string path = Filename.Safe(string.Concat(Enumerable.Repeat("👩🏽‍🎤", 200)), ".mp4", Folder, Nothing);
        Assert.True(path.Length <= Filename.MaxPathLength);
        string stem = Path.GetFileNameWithoutExtension(path);
        // A lone half of a surrogate pair can't be encoded, and that's what a careless cut leaves.
        Assert.NotNull(new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(stem));
        var text = StringInfo.GetTextElementEnumerator(stem);
        while (text.MoveNext()) Assert.Equal("👩🏽‍🎤", text.GetTextElement());
    }

    [Fact]
    public void An_existing_target_gets_a_number_and_never_overwrites()
    {
        var taken = new HashSet<string> { Path.Combine(Folder, "Song.mp4") };
        Assert.Equal(Path.Combine(Folder, "Song (2).mp4"), Filename.Safe("Song", ".mp4", Folder, taken.Contains));
    }

    [Fact]
    public void Numbers_keep_counting_until_one_is_free()
    {
        var taken = new HashSet<string> { Path.Combine(Folder, "Song.mp4"), Path.Combine(Folder, "Song (2).mp4"), Path.Combine(Folder, "Song (3).mp4") };
        Assert.Equal(Path.Combine(Folder, "Song (4).mp4"), Filename.Safe("Song", ".mp4", Folder, taken.Contains));
    }

    [Fact]
    public void A_number_on_a_full_length_name_still_fits()
    {
        bool First(string p) => !p.Contains('(');
        string path = Filename.Safe(new string('x', 400), ".mp4", Folder, First);
        Assert.True(path.Length <= Filename.MaxPathLength);
        Assert.Contains(" (2).mp4", path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("???")]
    public void An_empty_title_falls_back_to_the_source_id(string title) =>
        Assert.Equal("abc123.mp4", Path.GetFileName(Filename.Safe(title, ".mp4", Folder, Nothing, "abc123")));

    [Fact]
    public void With_no_source_id_either_there_is_still_a_name() =>
        Assert.False(string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(Filename.Safe("", ".mp4", Folder, Nothing))));

    [Fact]
    public void A_null_title_is_treated_as_empty() =>
        Assert.Equal("id1.mp4", Path.GetFileName(Filename.Safe(null!, ".mp4", Folder, Nothing, "id1")));

    [Fact]
    public void Names_that_differ_only_by_case_count_as_the_same_on_windows()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Folder, "song.mp4") };
        Assert.Equal(Path.Combine(Folder, "SONG (2).mp4"), Filename.Safe("SONG", ".mp4", Folder, taken.Contains));
    }
}

public class FilenameTemplateTests
{
    static readonly MediaInfo Info = new("My Song", "abc", "The Band", "2026-10-31", 1080, "m4a");

    [Theory]
    [InlineData("{title}", "My Song")]
    [InlineData("{title} [{id}]", "My Song [abc]")]
    [InlineData("{artist} - {title}", "The Band - My Song")]
    [InlineData("{date} {title} {height}p", "2026-10-31 My Song 1080p")]
    [InlineData("{title}.{format}", "My Song.m4a")]
    public void Tokens_fill_in(string template, string expected) =>
        Assert.Equal(expected, FilenameTemplate.Render(template, Info));

    [Theory]
    [InlineData("{nonsense} {title}", "{nonsense} My Song")]
    [InlineData("{", "{")]
    [InlineData("{title", "{title")]
    [InlineData("}{", "}{")]
    [InlineData("", "")]
    public void A_template_with_an_unknown_token_renders_it_literally_without_throwing(string template, string expected) =>
        Assert.Equal(expected, FilenameTemplate.Render(template, Info));

    [Fact]
    public void Tokens_are_found_in_any_case() => Assert.Equal("My Song", FilenameTemplate.Render("{TITLE}", Info));

    [Fact]
    public void A_missing_value_renders_empty()
    {
        var sparse = new MediaInfo("T", "i", null, null, null, null);
        Assert.Equal("T  ", FilenameTemplate.Render("{title} {artist} {date}", sparse));
    }

    [Fact]
    public void A_value_containing_a_token_is_not_expanded_again() =>
        Assert.Equal("{id}", FilenameTemplate.Render("{title}", Info with { Title = "{id}" }));

    [Fact]
    public void A_null_template_renders_empty() => Assert.Equal("", FilenameTemplate.Render(null!, Info));

    [Fact]
    public void Default_template_keeps_titles_apart_with_the_source_id() =>
        Assert.Equal("My Song [abc]", FilenameTemplate.Render(FilenameTemplate.Default, Info));

    [Fact]
    public void A_rendered_template_goes_safely_through_filename_cleaning()
    {
        var hostile = Info with { Title = @"..\..\CON", Uploader = "a/b:c" };
        string path = Filename.Safe(FilenameTemplate.Render("{artist} {title}", hostile), ".mp4", @"D:\Media", _ => false);
        Assert.Equal(@"D:\Media", Path.GetDirectoryName(path));
    }
}
