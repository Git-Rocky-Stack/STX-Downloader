namespace Stx.ViewModels;

/// <summary>
/// One block of the user guide. Controls lists the pads, switches and knobs it names, so a test can hold them to
/// what's on the console; Readouts lists the words the console shows, held to the source the same way. Both are set
/// in the console's accent wherever a line names them, so a reader can find the pad the line is about.
/// </summary>
public sealed record GuideSection(string Title, string Summary, IReadOnlyList<string> Lines, IReadOnlyList<string> Controls, IReadOnlyList<string> Readouts)
{
    public IReadOnlyList<string> Marked => [.. Controls, .. Readouts];
}

/// <summary>One of the quick start's steps, across the top of the guide.</summary>
public sealed record GuideStep(string Title, string Line, IReadOnlyList<string> Controls, IReadOnlyList<string> Readouts)
{
    public IReadOnlyList<string> Marked => [.. Controls, .. Readouts];
}

/// <summary>A run of a guide line: plain words, or the name of something on the console.</summary>
public readonly record struct GuideSpan(string Text, bool Marked);

/// <summary>
/// The user guide. Each line says what the app does today, and the comment above it says where.
/// Change the behavior, change the line.
/// </summary>
public static class GuideContent
{
    /// <summary>Under the guide's title: how to get back here, since it opens by itself only once.</summary>
    // MainWindow.cs TakeFirstLaunchGuide, SettingsPanel.cs HelpPad
    public const string Reopen = "Open it again any time: SETTINGS, then USER GUIDE. TUTORIAL beside it plays an 89-second walkthrough.";

    public static IReadOnlyList<string> ReopenControls { get; } = ["SETTINGS", "USER GUIDE", "TUTORIAL"];

    public static IReadOnlyList<GuideStep> QuickStart { get; } =
    [
        // STXDownloader.iss installs the tools (2026-11-11); ToolManager.cs EnsureOnceAsync fetches any that are missing,
        // as from the zip; EngineStatusViewModel.cs Apply
        new("Let it set up",
            "The setup installs yt-dlp, FFmpeg and Deno with STX, and the first launch downloads any that are missing. You're ready when the light at the top right turns green and reads ENGINE CURRENT.",
            [], ["ENGINE CURRENT"]),
        // SourcePanel.cs PasteAsync, SourceViewModel.cs Accept
        new("Save a link",
            "Copy a video's link, then press PASTE on the Source plate. STX reads the link and puts the save in the Queue; a playlist asks first.",
            ["PASTE"], []),
        // QueuePanel.cs QueueRow, AppSettings.cs OutputFolder
        new("Watch it save",
            "The Queue shows each job's meter and percent. Saves go to STX Downloader in your Videos folder, unless you choose another in Settings.",
            [], []),
        // LibraryViewModel.cs OpenCommand, Composition.cs Select
        new("Play and check it",
            "Select it in the Library and press OPEN to play it here. Source truth shows what its audio really is.",
            ["OPEN"], []),
    ];

    public static IReadOnlyList<IReadOnlyList<GuideSection>> Columns { get; } =
    [
        [
            new("The console",
                "One rack of plates: Source on top, Queue and Library on the left, Player and Source truth on the right.",
            [
                // EngineStatusViewModel.cs Apply and Report
                "The light at the top right reads the tools: amber while STX checks or sets them up, green with ENGINE CURRENT when they're ready, amber with UPDATE WAITING or LIMITED: SOME FORMATS MISSING when they mostly are.",
                // EngineStatusViewModel.cs Apply, HeaderPanel.cs _fault
                "The Fault light turns red, with TOOLS MISSING, when STX can't save anything until a tool is in place.",
                // HeaderPanel.cs openSettings, SettingsPanel.cs HelpPad, GuidePanel.cs and TutorialPanel.cs CLOSE
                "SETTINGS opens and closes the Settings plate, where USER GUIDE and TUTORIAL live. CLOSE puts either away.",
                // QueuePanel.cs, LibraryPanel.cs and SourceTruthPanel.cs PaintActions: IsEnabled
                "A pad that can't act on what's selected can't be pressed until it can.",
                // Pad.cs (a Button), PushSwitch.cs (a ToggleButton), Rotary.cs OnKeyDown
                "Tab moves between controls. Enter or Space presses a pad or switch, and the arrow keys turn a knob.",
            ], ["SETTINGS", "USER GUIDE", "TUTORIAL", "CLOSE"], ["ENGINE CURRENT", "UPDATE WAITING", "LIMITED: SOME FORMATS MISSING", "TOOLS MISSING"]),
            new("Save from a link",
                "The Source plate takes a link to a video, a playlist, or anything else yt-dlp can read.",
            [
                // SourcePanel.cs PasteAsync
                "Copy a link and press PASTE. It reads the clipboard and analyzes the link straight away.",
                // SourcePanel.cs constructor: ANALYZE, and Enter in the box
                "Or type the link in the box, then press ANALYZE or Enter.",
                // SourcePanel.cs Paint, SourceViewModel.cs JustThisVideo and DeclinePlaylist
                "A playlist asks first: ADD with its count, JUST THIS VIDEO when the link names one, or CANCEL. Private or unavailable items are skipped, and counted.",
                // SourceViewModel.cs Accept and IsDuplicate
                "Something already saved in that format isn't added twice.",
                // SourceViewModel.cs Say, SourcePanel.cs Paint
                "A link that can't be read says why, and what to try next, under the box.",
                // SourcePanel.cs BuildClipboardRow, AppSettings.cs ClipboardDetect
                "With CLIPBOARD on in Settings, a link you copy is offered under the box: GRAB IT analyzes it, NO THANKS lets it go.",
                // SettingsPanel.cs download knob, Composition.cs Source (SourceTruth.SelectedOutput)
                "The DOWNLOAD knob in Settings sets the quality, and the OUTPUT FORMAT knob on Source truth the format.",
            ], ["PASTE", "ANALYZE", "JUST THIS VIDEO", "CANCEL", "CLIPBOARD", "GRAB IT", "NO THANKS", "DOWNLOAD", "OUTPUT FORMAT"], []),
            new("Save from a web page",
                "For a video playing on a page, with no link to paste: the browser extension finds it.",
            [
                // README.md Install, STXDownloader.iss [Run] extension folder
                "In Chrome or Edge, open the extensions page, turn on Developer mode, choose Load unpacked, and pick the extension folder inside STX's install folder. Setup's last page can open that folder for you.",
                // extension/popup.html empty line
                "Start the video, then open the STX Downloader extension to see the media on that tab.",
                // extension/popup.js row and show
                "Press Save on a row, or Save all when two or more can be saved. A YouTube row also offers Save signed in, which sends your YouTube cookies with that one item.",
                // extension/popup.html drm line, extension/popup.js canDownload
                "A page that uses DRM says so, and its streams can't be saved.",
                // Stx.Bridge PipeServer.cs
                "The extension hands over through a pipe only your Windows account can open. No network port is opened.",
            ], [], []),
            new("Settings",
                "Every change applies and saves at once.",
            [
                // SettingsPanel.cs download, SettingsViewModel.cs DownloadLabels
                "DOWNLOAD picks BEST, up to 1080P or 720P, VIDEO only or AUDIO only.",
                // SettingsPanel.cs parallel, AppSettings.cs MaxParallel and MostParallel
                "PARALLEL DOWNLOADS runs 1 to 8 at once. It starts at 3.",
                // SettingsPanel.cs folder, template and BROWSE; FilenameTemplate.cs Default
                "Saves go to the output folder (type a path, or press BROWSE), named by the filename template. It starts as {title} [{id}], and can use {artist}, {date} and {height} too.",
                // SettingsPanel.cs embed switches, AppSettings.cs (all off to begin with)
                "TAGS, COVER ART and CHAPTERS embed those in each saved file. They start off.",
                // SettingsPanel.cs, LookState.cs
                "LIGHT MODE swaps black anodized for brushed aluminum.",
                // MainWindow.cs OnClosing and StartsHidden, Tray.cs menu
                "TRAY keeps downloads going after you close the window; the tray icon brings STX back or quits it. START WITH WINDOWS starts STX when you sign in, in the tray if TRAY is on.",
                // ToolManager.cs EnsureOnceAsync, FirstRunViewModel.cs Message
                "TOOL UPDATES keeps yt-dlp current and downloads missing tools. Off, STX downloads nothing for itself.",
                // LookState.cs ReduceMotion, Led.cs, ScrollingGlowText.cs, TutorialPanel.cs, MeterTimeline.Sweep
                "REDUCE MOTION holds lights steady, stops long titles scrolling, stills the meters' sweep, and makes the tutorial wait for PLAY. Windows' own animation setting does the same.",
                // SettingsPanel.cs WatchedFolders, AppSettings.cs DefaultWatched, MediaRepo.cs ForgetWatchedUnder
                "Videos and Music are watched to begin with. ADD FOLDER and REMOVE FOLDER change the list. Removing a folder takes its files out of the Library, never off your disk.",
            ], ["DOWNLOAD", "PARALLEL DOWNLOADS", "BROWSE", "TAGS", "COVER ART", "CHAPTERS", "LIGHT MODE", "TRAY", "START WITH WINDOWS", "TOOL UPDATES", "REDUCE MOTION", "PLAY", "ADD FOLDER", "REMOVE FOLDER"],
               ["BEST", "1080P", "720P", "VIDEO", "AUDIO"]),
        ],
        [
            new("Queue",
                "Every save, conversion and shrink runs here as a job.",
            [
                // QueuePanel.cs QueueRow
                "Each job shows a light, its name and status, a meter and its percent.",
                // QueuePanel.cs PaintActions
                "Select a job, then use PAUSE, RESUME, CANCEL or RETRY. Only the ones that fit the job can be pressed.",
                // QueuePanel.cs PaintActions _detail: ErrorMessage and NextStep
                "A job that fails says why under the list, and what to try next.",
                // QueuePanel.cs CopyLog, Stx.Core/Logging/Redactor.cs
                "COPY LOG copies the job's log, with tokens and cookies scrubbed out, ready to send to support.",
                // QueuePanel.cs _reopen, QueueEngine.cs, CaptureHandler.cs
                "OPEN PAGE appears when a captured link has expired. Capture from that page again and the job resumes.",
                // SettingsPanel.cs parallel
                "PARALLEL DOWNLOADS in Settings sets how many run at once.",
            ], ["PAUSE", "RESUME", "CANCEL", "RETRY", "COPY LOG", "OPEN PAGE", "PARALLEL DOWNLOADS"], []),
            new("Library",
                "Your whole collection: what you've saved, what you've opened, and your watched folders.",
            [
                // LibraryPanel.cs summary, CollectionWatcher.cs, OpenedFiles.cs, LibraryViewModel.cs row status
                "Each row says ORIGINAL for a save, OPENED for a file you opened with STX, or WATCHED for one found in a watched folder.",
                // LibraryViewModel.cs Query and LibrarySearch
                "Type in the search box to find a file by its title, file name or folder. Every word you type has to match.",
                // LibraryViewModel.cs CountText, ScanText and SpaceText
                "The line under it counts the files, follows a scan as it goes, and says how much deleting shrunk originals would free.",
                // CollectionWatcher.cs Apply
                "New files in a watched folder appear by themselves. A renamed file keeps its row; a deleted one stays listed as FILE MISSING.",
                // LibraryPanel.cs OPEN and FIND FILE, Composition.cs Select
                "Selecting a file shows its audio in Source truth. OPEN plays it on the Player; FIND FILE shows it in its folder.",
                // LibraryPanel.cs Convert, PresetCatalog.cs
                "CAR USB, PHONE and ARCHIVE convert the selected file for that use, as a job in the Queue.",
                // LibraryPanel.cs shrink row, LibraryViewModel.cs QueueShrink, FfmpegRunner.cs check, CopyCheck.cs
                "SHRINK makes a smaller copy beside the selected file: AV1 or HEVC for video, as the SHRINK TO knob says, and Opus for sound alone. It's lossy, and the copy is checked from start to end before it's kept.",
                // ShrinkFolder.cs QueueAsync, LibraryViewModel.cs SpaceText
                "SHRINK FOLDER does the same for every file in a folder you pick. The Library shows how much deleting the originals would free. STX never deletes them.",
            ], ["OPEN", "FIND FILE", "CAR USB", "PHONE", "ARCHIVE", "SHRINK", "SHRINK TO", "SHRINK FOLDER"], ["ORIGINAL", "OPENED", "WATCHED", "FILE MISSING"]),
            new("Source truth",
                "What the audio in a file really is, measured from the file, not taken from what the site claims.",
            [
                // SourceTruthViewModel.cs SourceLine; REPORTED: marks what wasn't measured
                "Select an item in the Library. The first line gives its codec, bitrate, sample rate and channels; REPORTED in front means it's what the site said, and the file hasn't been measured yet.",
                // SourceTruthViewModel.cs RollOffLine and LoudnessLine, SpectrumDisplay
                "The spectrum shows where the sound really stops: FULL BAND · NO CUTOFF, or the roll-off a lossy source leaves. Loudness is in LUFS, with the true peak.",
                // SourceTruthPanel.cs meters, MeterTimeline.Sweep
                "The LEFT and RIGHT meters sweep through the file's levels, then hold.",
                // Stx.Core/Audio/Verdict.cs
                "The verdict is plain: BIT-PERFECT for the original, LOSSLESS; NOTHING LOST, LARGER FILE, SAME SOUND, or RE-ENCODED; SLIGHT QUALITY LOSS. No format adds back what a source lost.",
                // SourceTruthPanel.cs _output, Composition.cs Source and ConvertSelected
                "The OUTPUT FORMAT knob picks the format for new saves and for CONVERT, which converts the selected file.",
                // SourceTruthViewModel.cs Warning
                "Turning a lossy file into FLAC, ALAC, WAV or AIFF is allowed, with a note that it adds no quality.",
            ], ["OUTPUT FORMAT", "CONVERT"], ["REPORTED", "FULL BAND · NO CUTOFF", "LEFT", "RIGHT", "BIT-PERFECT", "LOSSLESS; NOTHING LOST", "LARGER FILE, SAME SOUND", "RE-ENCODED; SLIGHT QUALITY LOSS", "FLAC", "ALAC", "WAV", "AIFF"]),
        ],
        [
            new("Player",
                "Plays your files inside STX, with VLC's engine.",
            [
                // LibraryViewModel.cs OpenCommand, Composition.cs Play, MainWindow.cs OpenFromOutside
                "OPEN in the Library plays the file here. So does a file opened from File Explorer with STX.",
                // PlayerPanel.cs pads, PlayerViewModel.cs Previous and Next, LibraryViewModel.cs PlayOrder (LibraryOrder)
                "PLAY and PAUSE share one pad. PREV goes back to the start, or to the file before if you're in the first 3 seconds. NEXT goes on in the Library's order, past files that are missing. STOP stops.",
                // PlayerPanel.cs SeekBar and Volume knob
                "Click or drag the position bar to move through the file. The VOLUME knob turns in steps of 10.",
                // PlayerPanel.cs Picker
                "AUDIO, SUBTITLES and CHAPTER knobs appear when a file has more than one to choose from.",
                // PlayerPanel.cs SetFullScreen, Surface.DoubleTapped, Esc accelerator
                "FULL, or a double-click on the picture, fills the screen. EXIT, or Esc, brings the console back.",
                // PlayerPanel.cs EnhanceDial, Enhance.cs Status, LibVlcEngine.cs EnhanceSeen
                "The ENHANCE knob sharpens the picture, or upscales it on a GPU with super resolution. It changes only what's on screen, never the file. Under it, the Player says what's really running, or why not: a GPU without super resolution says UPSCALE UNAVAILABLE.",
                // SettingsPanel.cs performance, PerformanceProfile.cs For
                "PERFORMANCE in Settings sets how hard playback works. AUTO picks from your PC; ECO uses two decoding threads and no ENHANCE; BALANCED half your cores and SHARPEN at most; MAX every core, and UPSCALE where the GPU can.",
                // RoutingEngine.cs AudioReadout, SettingsPanel.cs BitPerfectRow, PlayerPanel.cs VolumeFixed
                "BIT-PERFECT in Settings sends lossless music to your sound device exactly as it is, and the Player shows what arrives, such as 24/96 BIT-PERFECT. While it's on, other apps can't make sound and VOLUME is fixed. A file that can't go that way plays shared, and the Player says why.",
                // MainWindow.cs Activity.Register HideVideo
                "Hidden in the tray, the Player stops drawing pictures; the sound carries on.",
            ], ["OPEN", "PLAY", "PAUSE", "PREV", "NEXT", "STOP", "VOLUME", "AUDIO", "SUBTITLES", "CHAPTER", "FULL", "EXIT", "ENHANCE", "PERFORMANCE", "BIT-PERFECT"],
               ["UPSCALE UNAVAILABLE", "AUTO", "ECO", "SHARPEN", "BALANCED", "MAX", "UPSCALE"]),
            new("Streams, discs and your default player",
                "Play without saving, and let STX open your media from File Explorer.",
            [
                // SourcePanel.cs PLAY STREAM, Composition.cs PlayStreamAsync, StreamOpener.cs, PlaybackTarget.cs
                "PLAY STREAM plays the address in the Source box: a stream, or an internet radio station or its playlist. A web page goes to ANALYZE instead. UDP and RTP need a listening port, so STX refuses them.",
                // SourcePanel.cs PLAY DISC, Composition.cs PlayDisc, DiscCheck.cs
                "PLAY DISC plays a DVD or Blu-ray drive or disc folder from its main title. An encrypted disc is refused with THIS DISC IS ENCRYPTED, SO IT CAN'T BE PLAYED.",
                // LibVlcEngine.cs DiracRefused, DiracSniff.cs; docs/PLAYBACK.md
                "Dirac and VC-2 video crash VLC's engine, so STX refuses them, in files and streams alike, and shows DIRAC VIDEO CAN'T BE PLAYED SAFELY instead.",
                // DefaultPlayerOfferPanel.cs, DefaultPlayerViewModel.cs YesCommand and NotNowCommand
                "The first time, STX asks once whether it should be your player for videos and music. YES opens Windows' Default Apps, where you choose STX; Windows doesn't let an app choose itself. NOT NOW never asks again.",
                // SettingsPanel.cs DefaultPlayer, FileAssociations.cs Read
                "DEFAULT PLAYER in Settings opens that page any time. Its light shows what Windows really does: green for every type, amber for some.",
            ], ["PLAY STREAM", "ANALYZE", "PLAY DISC", "YES", "NOT NOW", "DEFAULT PLAYER"],
               ["THIS DISC IS ENCRYPTED, SO IT CAN'T BE PLAYED", "DIRAC VIDEO CAN'T BE PLAYED SAFELY"]),
            new("When something goes wrong",
                "Every problem says what happened and what to do next.",
            [
                // FirstRunPanel.cs, FirstRunViewModel.cs Message
                "If setup can't finish, a Setup plate says why. Check your connection, and that TOOL UPDATES is on, then press RETRY.",
                // QueuePanel.cs PaintActions, QueueViewModel.cs Retry
                "A failed job says why under the Queue. RETRY runs it again; COPY LOG copies what support needs.",
                // MainWindow.cs ReportRecovered, ErrorLog.cs FilePath
                "If STX recovers from a problem, the readout at the top right says so, and the details go to errors.log in %LOCALAPPDATA%\\STXDownloader\\logs.",
                // README.md
                "Support: support@strategia-x.com",
            ], ["TOOL UPDATES", "RETRY", "COPY LOG"], []),
            new("Good to know",
                "What STX won't do, and what it keeps to itself.",
            [
                // README.md, extension/popup.js canDownload, DiscCheck.cs
                "DRM-protected media isn't supported, to save or to play. When STX sees DRM, it says so and stops.",
                // README.md
                "You're responsible for following each site's terms and the law where you live.",
                // CaptureVault.cs, CredentialFile.cs
                "Stream tokens, cookies and headers stay in memory, and reach yt-dlp through a temp file only you can read, deleted when the job ends.",
                // ShrinkFolder.cs, MediaRepo.cs ForgetWatchedUnder, STXDownloader.iss, IdenticalFileCheck.cs
                "STX leaves your media alone: a shrink keeps the original, removing a watched folder keeps its files, and uninstalling keeps your saves. The one file it removes is a new download identical to one you already have; the Library points at the first.",
            ], [], []),
        ],
    ];

    /// <summary>Every section, in reading order.</summary>
    public static IEnumerable<GuideSection> Sections => Columns.SelectMany(column => column);

    /// <summary>
    /// Splits a line into plain runs and the names it marks. A name counts only as whole words, so OPEN isn't found in
    /// OPENED, and the longest name wins where two overlap, so PLAY STREAM isn't read as PLAY.
    /// </summary>
    public static IReadOnlyList<GuideSpan> Spans(string line, IReadOnlyList<string> marked)
    {
        var spans = new List<GuideSpan>();
        var names = marked.Where(n => n.Length > 0).Distinct().OrderByDescending(n => n.Length).ToList();
        int plain = 0, at = 0;
        while (at < line.Length)
        {
            string? name = names.FirstOrDefault(n => string.CompareOrdinal(line, at, n, 0, n.Length) == 0 && StartsWord(line, at) && EndsWord(line, at + n.Length));
            if (name is null) { at++; continue; }
            if (at > plain) spans.Add(new(line[plain..at], false));
            spans.Add(new(name, true));
            at += name.Length;
            plain = at;
        }
        if (plain < line.Length) spans.Add(new(line[plain..], false));
        return spans;
    }

    static bool StartsWord(string line, int at) => at == 0 || !char.IsLetterOrDigit(line[at - 1]);

    static bool EndsWord(string line, int end) => end >= line.Length || !char.IsLetterOrDigit(line[end]);
}
