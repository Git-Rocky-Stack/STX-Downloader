namespace Stx.ViewModels;

/// <summary>One block of the user guide. Controls lists the pads it names, so a test can hold them to what's on the console.</summary>
public sealed record GuideSection(string Title, IReadOnlyList<string> Lines, IReadOnlyList<string> Controls);

/// <summary>
/// The user guide. Each line says what the app does today, and the comment above it says where.
/// Change the behavior, change the line.
/// </summary>
public static class GuideContent
{
    public static IReadOnlyList<IReadOnlyList<GuideSection>> Columns { get; } =
    [
        [
            new("Getting started",
            [
                // README.md:52, Stx.App/Composition.cs StartAsync
                "The first run downloads yt-dlp, FFmpeg and Deno, about 430 MB.",
                // SourceViewModel.cs AnalyzeAsync, "Press Analyze again when the engine light is steady."
                "Wait for the engine light to hold steady before you analyze a link.",
                // FirstRunPanel.cs:15
                "If setup can't finish, a Setup plate says what happened. Press RETRY to try again.",
            ], ["RETRY"]),
            new("Save from a link",
            [
                // SourcePanel.cs PasteAsync
                "Copy a link and press PASTE. It reads the clipboard and analyzes the link straight away.",
                // SourcePanel.cs:37-44
                "Or type the link in the box, then press ANALYZE or Enter.",
                // SourcePanel.cs Paint and BuildClipboardRow fields, SourceViewModel.cs Accept
                "A playlist asks first: add its items, JUST THIS VIDEO when the link names one, or CANCEL.",
                // SourceViewModel.cs Accept and IsDuplicate
                "Something already saved in that format isn't added twice.",
            ], ["PASTE", "ANALYZE", "JUST THIS VIDEO", "CANCEL"]),
            new("Save from a web page",
            [
                // README.md:58
                "In Chrome or Edge, open the extensions page, turn on Developer mode, choose Load unpacked, and pick the extension folder inside the install folder.",
                // extension/popup.html:27
                "Start the video, then open the STX Downloader extension to see the media on that tab.",
                // extension/popup.js row and show
                "Press Save on a row. Save all appears when two or more can be saved.",
                // extension/popup.html:28, extension/popup.js:35
                "A page that uses DRM says so, and its streams can't be saved.",
            ], []),
        ],
        [
            new("Queue",
            [
                // QueuePanel.cs QueueRow
                "Each job shows its name, status, a meter and its percent.",
                // QueuePanel.cs:109-112
                "Select a job, then use PAUSE, RESUME, CANCEL or RETRY.",
                // QueuePanel.cs CopyLog, Stx.Core/Logging/Redactor.cs:6
                "COPY LOG copies the job's log, with tokens and cookies scrubbed out.",
                // QueuePanel.cs:91 and :113, QueueEngine.cs:63
                "OPEN PAGE appears when a captured link has expired. Capture from that page again and the job resumes.",
            ], ["PAUSE", "RESUME", "CANCEL", "RETRY", "COPY LOG", "OPEN PAGE"]),
            new("Library",
            [
                // LibraryPanel.cs summary, CollectionWatcher.cs, OpenedFiles.cs
                "Everything you've saved, every file you've opened with STX, and the media in your watched folders is listed here. Each row says ORIGINAL, OPENED or WATCHED.",
                // LibraryViewModel.cs Query, LibraryRowViewModel.Matches
                "Type in the search box to find a file by its title, file name or folder. Every word you type has to match.",
                // CollectionWatcher.cs Apply
                "New files in a watched folder appear by themselves. A renamed file keeps its row; a deleted one stays listed as FILE MISSING.",
                // LibraryPanel.cs:32-33
                "Select a file, then press OPEN to play it on the Player plate, or FIND FILE to show it in its folder.",
                // LibraryPanel.cs:34-36, README.md:21
                "CAR USB, PHONE and ARCHIVE convert the selected file for that use.",
            ], ["OPEN", "FIND FILE", "CAR USB", "PHONE", "ARCHIVE"]),
            new("Player",
            [
                // LibraryViewModel.cs OpenCommand, Composition.cs Play, MainWindow.cs OpenFromOutside
                "OPEN in the Library plays the file here. So does a file opened from File Explorer with STX.",
                // PlayerPanel.cs pads, PlayerViewModel.cs Previous
                "PLAY and PAUSE share one pad. PREV goes back to the start, or to the file before if you're in the first 3 seconds. NEXT and STOP do what they say.",
                // PlayerPanel.cs SetFullScreen, Esc accelerator
                "FULL fills the screen. EXIT, or Esc, brings the console back.",
                // SettingsPanel.cs PERFORMANCE, PerformanceProfile.cs For
                "PERFORMANCE in Settings sets how hard playback may work. AUTO picks from your PC.",
                // LibVlcEngine.cs DiracRefused and Inspect; docs/PLAYBACK.md
                "Dirac and VC-2 video crash VLC's engine, so STX refuses them and shows DIRAC VIDEO CAN'T BE PLAYED SAFELY instead.",
                // PlayerPanel.cs EnhanceDial, Enhance.cs Status, LibVlcEngine.cs EnhanceSeen
                "The ENHANCE knob sharpens the picture, or upscales it on a GPU with super resolution. It changes only what's on screen, never the file. Under it, the Player says what's really running, or why not: a GPU without super resolution says UPSCALE UNAVAILABLE.",
                // SourcePanel.cs PLAY STREAM, Composition.cs PlayStreamAsync, StreamOpener.cs
                "PLAY STREAM plays the address in the Source box: a stream, or an internet radio station or its playlist. A web page goes to ANALYZE instead. UDP and RTP need a listening port, so STX refuses them.",
                // SourcePanel.cs PLAY DISC, Composition.cs PlayDisc, DiscCheck.cs
                "PLAY DISC plays a DVD or Blu-ray drive or disc folder from its main title. An encrypted disc is refused with THIS DISC IS ENCRYPTED, SO IT CAN'T BE PLAYED.",
                // DefaultPlayerOfferPanel.cs, DefaultPlayerViewModel.cs YesCommand and NotNowCommand
                "The first time, STX asks once whether it should be your player for videos and music. YES opens Windows' Default Apps, where you choose STX. NOT NOW never asks again.",
                // SettingsPanel.cs DefaultPlayer, FileAssociations.cs Read
                "DEFAULT PLAYER in Settings opens that page any time. Its light shows what Windows really does: green for every type, amber for some.",
            ], ["OPEN", "PLAY", "PAUSE", "PREV", "NEXT", "STOP", "FULL", "EXIT", "YES", "NOT NOW", "DEFAULT PLAYER", "PLAY STREAM", "PLAY DISC"]),
            new("Source truth",
            [
                // README.md:38, Stx.Audio/AudioMeasurer.cs:8
                "Select a saved item. Its audio is measured from the file itself, not taken from what the site claims.",
                // Stx.Core/Audio/Verdict.cs:8-14
                "The verdict is plain: Bit-perfect, or Re-encoded; slight quality loss. Re-encoding never improves anything.",
                // Composition.cs Source constructor, SourceTruthPanel.cs:62-63
                "The knob on this plate picks the format for new saves and for CONVERT.",
            ], ["CONVERT"]),
        ],
        [
            new("Settings",
            [
                // HeaderPanel.cs, SettingsPanel.cs summary
                "SETTINGS opens and closes this plate. Every change applies and saves at once.",
                // SettingsPanel.cs:32, DownloadChoice in AppSettings.cs
                "Download picks best quality, up to 1080p or 720p, video only or audio only.",
                // SettingsPanel.cs:36, AppSettings.cs MaxParallel and MostParallel
                "Parallel downloads sets how many run at once, from 1 to 8. It starts at 3.",
                // MainWindow.cs OnClosing
                "TRAY keeps downloads going after you close the window.",
                // SourcePanel.cs BuildClipboardRow, AppSettings.cs ClipboardDetect
                "CLIPBOARD offers a link you've copied. It's off until you turn it on.",
                // SettingsPanel.cs:28-30
                "TAGS, COVER ART and CHAPTERS choose what gets embedded in the file.",
                // SettingsPanel.cs:44-49 and :73, AppSettings.cs OutputFolder
                "Saves go to the output folder, named by the filename template.",
                // SettingsPanel.cs BitPerfectRow, BitPerfect.cs Route, RoutingEngine.cs AudioReadout
                "BIT-PERFECT sends lossless music to your sound device exactly as it is, and the Player shows what arrives, such as 24/96 BIT-PERFECT. While it's on, other apps can't make sound, and VOLUME is fixed. A file that can't go that way plays shared, and the Player says why.",
                // SettingsPanel.cs WatchedFolders, AppSettings.cs DefaultWatched, MediaRepo.cs ForgetWatchedUnder
                "Videos and Music are watched to begin with. ADD FOLDER and REMOVE FOLDER change the list. Removing a folder takes its files out of the Library, never off your disk.",
            ], ["SETTINGS", "TRAY", "CLIPBOARD", "TAGS", "COVER ART", "CHAPTERS", "ADD FOLDER", "REMOVE FOLDER", "BIT-PERFECT"]),
            new("Good to know",
            [
                // README.md:5
                "DRM-protected media isn't supported. When the app sees DRM, it says so and stops.",
                // README.md:7
                "You're responsible for following each site's terms and the law where you live.",
                // README.md:33
                "Stream tokens, cookies and headers stay in memory, and reach yt-dlp through a temp file only you can read.",
                // README.md:9
                "Support: support@strategia-x.com",
            ], []),
        ],
    ];
}
