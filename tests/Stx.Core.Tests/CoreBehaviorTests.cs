using Stx.Core.Audio;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Logging;
using Xunit;

namespace Stx.Core.Tests;

public class JobTransitionTests
{
    [Fact]
    public void Completed_is_final()
    {
        foreach (var to in Enum.GetValues<JobState>()) Assert.False(JobTransitions.CanMove(JobState.Completed, to));
    }

    [Fact]
    public void Expired_link_goes_to_recapture() => Assert.True(JobTransitions.CanMove(JobState.Running, JobState.NeedsRecapture));

    [Theory]
    [InlineData(JobState.Queued, JobState.Running)]
    [InlineData(JobState.Running, JobState.Paused)]
    [InlineData(JobState.Paused, JobState.Queued)]
    [InlineData(JobState.Running, JobState.Waiting)]
    [InlineData(JobState.Waiting, JobState.Running)]
    [InlineData(JobState.Running, JobState.Analyzing)]
    [InlineData(JobState.Analyzing, JobState.Completed)]
    [InlineData(JobState.Running, JobState.Completed)]
    [InlineData(JobState.Running, JobState.Failed)]
    [InlineData(JobState.Failed, JobState.Queued)]
    [InlineData(JobState.NeedsRecapture, JobState.Queued)]
    [InlineData(JobState.Canceled, JobState.Queued)]
    public void Allowed_moves(JobState from, JobState to) => Assert.True(JobTransitions.CanMove(from, to));

    // A job interrupted by a crash or sleep must be able to go back in line, or it stays stuck in Running.
    [Theory]
    [InlineData(JobState.Running)]
    [InlineData(JobState.Analyzing)]
    [InlineData(JobState.Waiting)]
    public void Interrupted_jobs_can_return_to_the_queue(JobState from) => Assert.True(JobTransitions.CanMove(from, JobState.Queued));

    [Theory]
    [InlineData(JobState.Queued, JobState.Completed)]
    [InlineData(JobState.Queued, JobState.Analyzing)]
    [InlineData(JobState.Failed, JobState.Completed)]
    [InlineData(JobState.Canceled, JobState.Running)]
    [InlineData(JobState.Running, JobState.Running)]
    public void Forbidden_moves(JobState from, JobState to) => Assert.False(JobTransitions.CanMove(from, to));
}

public class ErrorClassifierTests
{
    [Fact]
    public void Unsupported_is_classified() =>
        Assert.Equal(ErrorKind.UnsupportedSite, ErrorClassifier.Classify(
            "ERROR: Unsupported URL: https://www.chevrolet.com/performance/corvette/experience", 1));

    /// <summary>Representative wording for each kind. Task 9 adds output captured from the real tool.</summary>
    public static readonly (ErrorKind Kind, string Output)[] Fixtures =
    [
        (ErrorKind.UnsupportedSite, "ERROR: Unsupported URL: https://example.com/page"),
        (ErrorKind.SignInNeeded, "ERROR: [youtube] abc: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies for the authentication."),
        (ErrorKind.SignInNeeded, "ERROR: [vimeo] 1: This video is only available for registered users"),
        (ErrorKind.PrivateOrUnavailable, "ERROR: [youtube] abc: Private video. Sign in if you've been granted access to this video"),
        (ErrorKind.PrivateOrUnavailable, "ERROR: [youtube] abc: Video unavailable"),
        (ErrorKind.PrivateOrUnavailable, "ERROR: unable to download video data: HTTP Error 404: Not Found"),
        (ErrorKind.DrmDetected, "ERROR: [generic] x: This video is DRM protected"),
        (ErrorKind.LinkExpired, "ERROR: unable to download video data: HTTP Error 403: Forbidden"),
        (ErrorKind.LinkExpired, "ERROR: unable to download video data: HTTP Error 410: Gone"),
        (ErrorKind.Network, "ERROR: [generic] Unable to download webpage: <urlopen error [Errno 11001] getaddrinfo failed>"),
        (ErrorKind.Network, "ERROR: unable to download video data: The read operation timed out"),
        (ErrorKind.ToolFailure, "Traceback (most recent call last): something nobody planned for"),
        (ErrorKind.OutputFailure, "ERROR: unable to open for writing: [Errno 28] No space left on device"),
        (ErrorKind.OutputFailure, "ERROR: unable to create directory [WinError 3] The system cannot find the path specified"),
        (ErrorKind.OutputFailure, "ERROR: unable to write data: [Errno 13] Permission denied"),
    ];

    public static IEnumerable<object[]> FixtureRows() => Fixtures.Select(f => new object[] { f.Kind, f.Output });

    [Theory]
    [MemberData(nameof(FixtureRows))]
    public void Tool_output_is_classified(ErrorKind expected, string output) =>
        Assert.Equal(expected, ErrorClassifier.Classify(output, 1));

    [Fact]
    public void Fixtures_cover_every_kind()
    {
        foreach (var kind in Enum.GetValues<ErrorKind>()) Assert.Contains(Fixtures, f => f.Kind == kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("something entirely new")]
    public void Unknown_output_is_a_tool_failure(string output) =>
        Assert.Equal(ErrorKind.ToolFailure, ErrorClassifier.Classify(output, 1));

    [Fact]
    public void A_null_output_is_a_tool_failure() => Assert.Equal(ErrorKind.ToolFailure, ErrorClassifier.Classify(null!, 1));

    [Fact]
    public void Words_inside_a_link_do_not_decide_the_kind() =>
        Assert.Equal(ErrorKind.UnsupportedSite, ErrorClassifier.Classify("ERROR: Unsupported URL: https://example.com/drm/private-video/timed-out", 1));

    [Fact]
    public void Error_lines_outrank_warnings() =>
        Assert.Equal(ErrorKind.PrivateOrUnavailable, ErrorClassifier.Classify(
            "WARNING: [youtube] Use --cookies for the authentication\nERROR: [youtube] abc: Video unavailable", 1));

    // Unavailable outranks unsupported in the rule order, so only the warning filter gets this right.
    [Fact]
    public void A_warning_never_decides_when_an_error_line_exists() =>
        Assert.Equal(ErrorKind.UnsupportedSite, ErrorClassifier.Classify(
            "WARNING: one format: Video unavailable\nERROR: Unsupported URL: https://example.com/page", 1));

    [Fact]
    public void Wording_is_matched_in_any_case() =>
        Assert.Equal(ErrorKind.Network, ErrorClassifier.Classify("error: CONNECTION REFUSED", 1));

    [Theory]
    [InlineData(ErrorKind.UnsupportedSite)]
    [InlineData(ErrorKind.SignInNeeded)]
    [InlineData(ErrorKind.PrivateOrUnavailable)]
    [InlineData(ErrorKind.DrmDetected)]
    [InlineData(ErrorKind.LinkExpired)]
    [InlineData(ErrorKind.Network)]
    [InlineData(ErrorKind.ToolFailure)]
    [InlineData(ErrorKind.OutputFailure)]
    public void Every_kind_explains_itself_and_offers_a_next_step(ErrorKind kind)
    {
        Assert.False(string.IsNullOrWhiteSpace(kind.Message()));
        Assert.False(string.IsNullOrWhiteSpace(kind.NextStep()));
    }

    [Fact]
    public void Each_kind_has_its_own_words()
    {
        var kinds = Enum.GetValues<ErrorKind>();
        Assert.Equal(kinds.Length, kinds.Select(k => k.Message()).Distinct().Count());
        Assert.Equal(kinds.Length, kinds.Select(k => k.NextStep()).Distinct().Count());
    }

    [Fact]
    public void Drm_stops_and_offers_no_way_around() =>
        Assert.DoesNotContain("try", ErrorKind.DrmDetected.NextStep(), StringComparison.OrdinalIgnoreCase);
}

public class ServerErrorTests
{
    [Theory]
    [InlineData("ERROR: unable to download video data: HTTP Error 503: Service Unavailable")]
    [InlineData("ERROR: unable to download video data: HTTP Error 500: Internal Server Error")]
    [InlineData("ERROR: unable to download video data: HTTP Error 502: Bad Gateway")]
    [InlineData("ERROR: 504 Gateway Timeout")]
    public void A_struggling_server_is_a_retryable_network_failure_not_a_missing_video(string output)
    {
        Assert.Equal(ErrorKind.Network, ErrorClassifier.Classify(output, 1));
        Assert.True(Stx.Core.Queue.QueuePolicy.WorthRetrying(ErrorClassifier.Classify(output, 1)));
    }

    [Fact]
    public void A_video_that_is_really_gone_is_still_unavailable() =>
        Assert.Equal(ErrorKind.PrivateOrUnavailable, ErrorClassifier.Classify("ERROR: [youtube] abc: Video unavailable", 1));
}

public class ScrubLinkSecretTests
{
    [Theory]
    [InlineData("https://zoom.us/rec/share/x?pwd=SECRET", "pwd")]
    [InlineData("https://a.example/v?password=SECRET&t=1", "password")]
    [InlineData("https://a.example/v?sessionid=SECRET", "sessionid")]
    [InlineData("https://a.example/v?session_id=SECRET", "session_id")]
    [InlineData("https://a.example/v;jsessionid=SECRET", "jsessionid")]
    [InlineData("https://a.example/v?PHPSESSID=SECRET", "PHPSESSID")]
    [InlineData("https://a.example/v?sid=SECRET", "sid")]
    [InlineData("https://a.example/v?secret=SECRET", "secret")]
    public void Secrets_leave_a_stored_link_and_its_name_stays(string url, string name)
    {
        string scrubbed = Stx.Core.Logging.Redactor.ScrubLink(url);
        Assert.DoesNotContain("SECRET", scrubbed);
        Assert.Contains(name + "=", scrubbed);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ&list=PL123&si=abc")]
    [InlineData("https://a.example/watch?id=7&session=")]
    [InlineData("https://a.example/watch?consider=1&inside=2")]
    public void Ordinary_links_are_unchanged(string url) => Assert.Equal(url, Stx.Core.Logging.Redactor.ScrubLink(url));
}

public class VerdictTests
{
    [Fact]
    public void Verdict_wording_is_exact()
    {
        Assert.Equal("Bit-perfect", Verdict.For(OutputChoice.Original, true));
        Assert.Equal("Lossless copy of a lossy source; no quality gained", Verdict.For(OutputChoice.Flac, true));
        Assert.Equal("Larger file, same sound", Verdict.For(OutputChoice.Wav, true));
        Assert.Equal("Re-encoded; slight quality loss", Verdict.For(OutputChoice.Mp3, true));
    }

    [Theory]
    [InlineData(OutputChoice.Alac, "Lossless copy of a lossy source; no quality gained")]
    [InlineData(OutputChoice.Aiff, "Larger file, same sound")]
    [InlineData(OutputChoice.AacM4a, "Re-encoded; slight quality loss")]
    [InlineData(OutputChoice.Opus, "Re-encoded; slight quality loss")]
    [InlineData(OutputChoice.OggVorbis, "Re-encoded; slight quality loss")]
    [InlineData(OutputChoice.Mp4, "Bit-perfect")]
    [InlineData(OutputChoice.Mkv, "Bit-perfect")]
    public void Other_outputs_from_a_lossy_source(OutputChoice output, string verdict) =>
        Assert.Equal(verdict, Verdict.For(output, true));

    [Theory]
    [InlineData(OutputChoice.Flac)]
    [InlineData(OutputChoice.Alac)]
    public void Lossless_from_a_lossless_source_is_not_called_a_lossy_copy(OutputChoice output) =>
        Assert.Equal("Lossless; nothing lost", Verdict.For(output, false));

    [Fact]
    public void Every_output_has_a_verdict()
    {
        foreach (var output in Enum.GetValues<OutputChoice>())
        {
            Assert.False(string.IsNullOrWhiteSpace(Verdict.For(output, true)));
            Assert.False(string.IsNullOrWhiteSpace(Verdict.For(output, false)));
        }
    }
}

public class RedactorTests
{
    [Fact]
    public void Redactor_removes_tokens_cookies_and_auth()
    {
        var s = Redactor.Scrub("GET https://x/master.m3u8?fastly_token=ABC123 Cookie: a=b Authorization: Bearer q");
        Assert.DoesNotContain("ABC123", s);
        Assert.DoesNotContain("a=b", s);
        Assert.DoesNotContain("Bearer q", s);
    }

    [Theory]
    [InlineData("https://cdn.example/v.mp4?sig=SECRET&exp=1")]
    [InlineData("https://cdn.example/v.mp4?unknown_name=SECRET")]
    [InlineData("https://cdn.example/v.mp4?a=1&b=SECRET")]
    [InlineData("https://cdn.example/hdntl=exp=1~hmac=SECRET/index.m3u8")]
    [InlineData("https://user:SECRET@cdn.example/v.mp4")]
    [InlineData("Set-Cookie: session=SECRET; Path=/")]
    [InlineData("cookie:SECRET")]
    [InlineData("--add-header \"Cookie: id=SECRET\"")]
    [InlineData("--add-header Authorization:SECRET")]
    [InlineData("Proxy-Authorization: Basic SECRET")]
    [InlineData("X-Api-Key: SECRET")]
    [InlineData("{\"cookie\":\"SECRET\"}")]
    [InlineData("{\"Authorization\": \"Bearer SECRET\"}")]
    [InlineData("Bearer SECRET")]
    [InlineData("access_token=SECRET&x=1")]
    public void Secrets_never_survive(string line) =>
        Assert.DoesNotContain("SECRET", Redactor.Scrub(line));

    [Fact]
    public void Secrets_spanning_several_lines_are_each_removed()
    {
        var s = Redactor.Scrub("Cookie: one=SECRET\nnormal line\nAuthorization: Bearer SECRET2");
        Assert.DoesNotContain("SECRET", s);
        Assert.Contains("normal line", s);
    }

    [Fact]
    public void Useful_detail_stays_for_a_copyable_log()
    {
        var s = Redactor.Scrub("[download] Destination: song.m4a\nERROR: HTTP Error 403 at https://cdn.example/path/v.mp4?token=x");
        Assert.Contains("[download] Destination: song.m4a", s);
        Assert.Contains("HTTP Error 403", s);
        Assert.Contains("https://cdn.example/path/v.mp4", s);
    }

    [Fact]
    public void Nothing_in_nothing_out()
    {
        Assert.Equal("", Redactor.Scrub(""));
        Assert.Equal("", Redactor.Scrub(null!));
    }
}
