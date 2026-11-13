namespace Stx.ViewModels;

/// <summary>
/// One step of a guided tour: the console dims, a spotlight lands on <see cref="Target"/> (a control or plate the
/// panels mark with that key), and a card beside it says <see cref="Line"/>. Controls and Readouts are the console's
/// names the line uses, set in the accent and held by tests to what the console really has, as the guide's are.
/// </summary>
public sealed record TourStep(string Target, string Title, string Line, IReadOnlyList<string> Controls, IReadOnlyList<string> Readouts)
{
    public IReadOnlyList<string> Marked => [.. Controls, .. Readouts];

    /// <summary>The plate the target is on: the key's first part ("queue.clear" is on "queue"), lit instead when the
    /// control itself isn't showing (a pad that appears only when it applies).</summary>
    public string Plate => Target.Split('.')[0];
}

/// <summary>How far into STX a chapter goes.</summary>
public enum TourLevel { Basics, Advanced }

/// <summary>One chapter of the tutorials: a short tour of the real console, start to end.</summary>
public sealed record Tour(string Id, string Title, string Summary, TourLevel Level, IReadOnlyList<TourStep> Steps)
{
    /// <summary>What the chapter's card says under its title: how long it is and how deep it goes.</summary>
    public string Meta => $"{Steps.Count} STEPS · {(Level == TourLevel.Basics ? "BASICS" : "ADVANCED")}";
}

/// <summary>
/// The tutorials: guided tours of the live console, one chapter per part of it. Each line says what the app does today,
/// in the guide's words (GuideContent carries the code anchors for every claim). Change the behavior, change the line.
/// </summary>
public static class TutorialContent
{
    /// <summary>The plates a step can land on, by key, as the panels mark them.</summary>
    public static IReadOnlyList<string> Plates { get; } = ["header", "source", "queue", "library", "player", "truth", "settings"];

    public static IReadOnlyList<Tour> Tours { get; } =
    [
        new("first-save", "Your first save", "From a copied link to a file you've played and checked, in nine steps.", TourLevel.Basics,
        [
            new("header.engine", "The engine light",
                "This light reads STX's tools. Green, with ENGINE CURRENT, means yt-dlp, FFmpeg and Deno are ready to save.",
                [], ["ENGINE CURRENT"]),
            new("source.paste", "Paste a link",
                "Copy a video's link anywhere, then press PASTE. STX reads your clipboard and analyzes the link straight away.",
                ["PASTE"], []),
            new("source.link", "Or type it",
                "You can type or paste the link into this box instead, then press ANALYZE or Enter. A playlist asks first.",
                ["ANALYZE"], []),
            new("queue.list", "Watch it save",
                "Each save runs in the Queue as a job, with a light, its status, a meter and its percent. Saves go to STX Downloader in your Videos folder.",
                [], []),
            new("library.list", "Find it in the Library",
                "Everything you save lands here, with the files you've opened and your watched folders. Select a row to see its audio truth.",
                [], []),
            new("library.open", "Play it",
                "OPEN plays the selected file right here, on the Player.",
                ["OPEN"], []),
            new("player", "The Player",
                "VLC's engine plays it inside STX. FULL fills the screen, and Esc brings the console back.",
                ["FULL"], []),
            new("truth", "Check what you really got",
                "Source truth measures the audio from the file itself, not from what the site claimed, and gives a plain verdict.",
                [], []),
            new("header.settings", "Make it yours",
                "SETTINGS holds the quality, the folders and every switch. The USER GUIDE and the other TUTORIALS live there too.",
                ["SETTINGS", "USER GUIDE", "TUTORIALS"], []),
        ]),
        new("saving", "Saving from links and pages", "Links, playlists, streams, discs, and what each save keeps.", TourLevel.Basics,
        [
            new("source", "The Source plate",
                "Every save starts here: a link to a video, a playlist, or anything else yt-dlp can read.",
                [], []),
            new("source.paste", "PASTE",
                "PASTE reads the link on your clipboard and analyzes it at once.",
                ["PASTE"], []),
            new("source.analyze", "ANALYZE",
                "ANALYZE reads whatever is typed in the box. Enter does the same.",
                ["ANALYZE"], []),
            new("source.link", "Playlists",
                "A playlist asks before anything is queued: ADD with its count, JUST THIS VIDEO when the link names one, or CANCEL. Private or unavailable items are skipped, and counted.",
                ["JUST THIS VIDEO", "CANCEL"], []),
            new("source.playstream", "PLAY STREAM",
                "PLAY STREAM plays the address in the box without saving it: a stream, or an internet radio station.",
                ["PLAY STREAM"], []),
            new("source.playdisc", "PLAY DISC",
                "PLAY DISC plays a DVD or Blu-ray drive, or a disc folder, from its main title. An encrypted disc is refused.",
                ["PLAY DISC"], []),
            new("settings.switches", "Catch copied links",
                "With CLIPBOARD on, a link you copy is offered under the Source box: GRAB IT analyzes it, NO THANKS lets it go.",
                ["CLIPBOARD", "GRAB IT", "NO THANKS"], []),
            new("settings.download", "Choose the quality",
                "The DOWNLOAD knob sets what a save keeps: BEST, up to 1080P or 720P, VIDEO only, or AUDIO only.",
                ["DOWNLOAD"], ["BEST", "1080P", "720P", "VIDEO", "AUDIO"]),
            new("truth.output", "Choose the format",
                "The OUTPUT FORMAT knob on Source truth picks the format new saves are converted to. ORIGINAL keeps what the site sent.",
                ["OUTPUT FORMAT"], ["ORIGINAL"]),
        ]),
        new("queue", "Managing the queue", "Read a job at a glance, steer it, and clear what's finished.", TourLevel.Basics,
        [
            new("queue", "The Queue",
                "Every save, conversion and shrink runs here as a job, and is still listed after STX restarts.",
                [], []),
            new("queue.list", "Reading a job",
                "Its light is amber while it works, green when it's complete, red when it fails, and blue when its page must be captured again. The meter and readout give its percent.",
                [], []),
            new("queue.actions", "Steer a job",
                "Select a job, then PAUSE, RESUME, CANCEL or RETRY it. A pad that doesn't fit the job can't be pressed.",
                ["PAUSE", "RESUME", "CANCEL", "RETRY"], []),
            new("queue.copylog", "COPY LOG",
                "COPY LOG copies the selected job's log, with tokens and cookies scrubbed out, ready to send to support.",
                ["COPY LOG"], []),
            new("queue.clear", "CLEAR",
                "CLEAR takes the selected finished job off the queue. Its file stays in the Library and on your disk.",
                ["CLEAR"], []),
            new("queue.cleardone", "CLEAR DONE",
                "CLEAR DONE takes every complete, canceled or failed job off the queue at once, older ones out of sight too.",
                ["CLEAR DONE"], []),
            new("settings.parallel", "How many at once",
                "PARALLEL DOWNLOADS sets how many jobs run together, 1 to 8. It starts at 3.",
                ["PARALLEL DOWNLOADS"], []),
        ]),
        new("library", "Your library", "Search, play, convert and tidy your whole collection.", TourLevel.Basics,
        [
            new("library", "The Library",
                "Your whole collection: your saves, the files you opened with STX, and everything in your watched folders.",
                [], []),
            new("library.search", "Search",
                "Every word you type has to be in the title, the file name or its folder. Case doesn't matter.",
                [], []),
            new("library.count", "The count line",
                "It counts the files, follows a folder scan as it runs, and says how much deleting shrunk originals would free.",
                [], []),
            new("library.list", "What a row says",
                "ORIGINAL marks a save, OPENED a file you opened with STX, and WATCHED one found in a watched folder. FILE MISSING means it's gone from disk.",
                [], ["ORIGINAL", "OPENED", "WATCHED", "FILE MISSING"]),
            new("library.open", "OPEN and FIND FILE",
                "OPEN plays the selected file on the Player. FIND FILE shows it in its folder.",
                ["OPEN", "FIND FILE"], []),
            new("library.convert", "Convert for a use",
                "CAR USB, PHONE and ARCHIVE make a copy for that use, as a job in the Queue. The original stays.",
                ["CAR USB", "PHONE", "ARCHIVE"], []),
            new("library.remove", "REMOVE",
                "REMOVE takes the selected row off the Library. The file on your disk is never touched.",
                ["REMOVE"], []),
            new("library.clearmissing", "CLEAR MISSING",
                "CLEAR MISSING takes every FILE MISSING row off the Library at once.",
                ["CLEAR MISSING"], ["FILE MISSING"]),
            new("settings.watched", "Watched folders",
                "Videos and Music are watched to begin with. ADD FOLDER and REMOVE FOLDER change the list, and new files appear by themselves.",
                ["ADD FOLDER", "REMOVE FOLDER"], []),
        ]),
        new("player", "Playing and enhancing", "Transport, the picture, and how hard playback works.", TourLevel.Basics,
        [
            new("player.screen", "The picture",
                "Files play here on VLC's engine. A double-click on the picture fills the screen.",
                [], []),
            new("player.transport", "Transport",
                "PLAY and PAUSE share one pad. PREV goes back to the start, or to the file before in the first 3 seconds. NEXT follows the Library's order. STOP stops.",
                ["PLAY", "PAUSE", "PREV", "NEXT", "STOP"], []),
            new("player.seek", "Position",
                "Click or drag the position bar to move through the file.",
                [], []),
            new("player.volume", "VOLUME",
                "The VOLUME knob turns in steps of 10. The arrow keys turn it too.",
                ["VOLUME"], []),
            new("player.enhance", "ENHANCE",
                "ENHANCE can SHARPEN the picture, or UPSCALE it on a GPU with super resolution: what's on screen, never the file. The line under it says what's really running.",
                ["ENHANCE"], ["SHARPEN", "UPSCALE"]),
            new("player.full", "FULL",
                "FULL fills the screen. EXIT, or Esc, brings the console back.",
                ["FULL", "EXIT"], []),
            new("settings.performance", "PERFORMANCE",
                "PERFORMANCE sets how hard playback works. AUTO picks from your PC; ECO, BALANCED and MAX set it yourself.",
                ["PERFORMANCE"], ["AUTO", "ECO", "BALANCED", "MAX"]),
            new("settings.bitperfect", "BIT-PERFECT",
                "BIT-PERFECT sends lossless music to your sound device exactly as it is. While it's on, other apps can't make sound.",
                ["BIT-PERFECT"], []),
        ]),
        new("truth", "Source truth and converting", "Measure what a file really is, then convert or shrink it.", TourLevel.Advanced,
        [
            new("truth", "Source truth",
                "It measures the audio in the selected file itself: what it really is, not what the site claimed.",
                [], []),
            new("truth.verdict", "The readout",
                "Codec, bitrate, sample rate and channels come first; REPORTED in front means not measured yet. Then where the sound stops, its loudness, and the verdict.",
                [], ["REPORTED"]),
            new("truth.spectrum", "The spectrum",
                "The spectrum shows where the sound really stops: FULL BAND · NO CUTOFF, or the roll-off a lossy source leaves.",
                [], ["FULL BAND · NO CUTOFF"]),
            new("truth.meters", "LEFT and RIGHT",
                "The LEFT and RIGHT meters sweep through the file's levels, then hold.",
                [], ["LEFT", "RIGHT"]),
            new("truth.output", "OUTPUT FORMAT",
                "OUTPUT FORMAT picks the format for new saves and for CONVERT. A lossy file can go to FLAC, ALAC, WAV or AIFF, with a note that it adds no quality.",
                ["OUTPUT FORMAT", "CONVERT"], ["FLAC", "ALAC", "WAV", "AIFF"]),
            new("truth.convert", "CONVERT",
                "CONVERT converts the selected file as a job in the Queue, and keeps the original.",
                ["CONVERT"], []),
            new("library.shrink", "SHRINK",
                "SHRINK makes a smaller copy beside the file: AV1 or HEVC for video, as the SHRINK TO knob says, and Opus for sound alone. Each copy is checked start to end before it's kept.",
                ["SHRINK", "SHRINK TO"], []),
            new("library.shrinkfolder", "SHRINK FOLDER",
                "SHRINK FOLDER shrinks every file in a folder you pick. STX never deletes the originals.",
                ["SHRINK FOLDER"], []),
        ]),
        new("settings", "Settings and advanced", "Where saves go, what they carry, and how STX behaves.", TourLevel.Advanced,
        [
            new("settings", "The Settings plate",
                "Every change on this plate applies and saves at once.",
                [], []),
            new("settings.folder", "Where saves go",
                "Type a folder, or press BROWSE. The filename template names each save; {artist}, {date} and {height} work too.",
                ["BROWSE"], []),
            new("settings.embed", "What a save carries",
                "TAGS, COVER ART and CHAPTERS embed those in each saved file. They start off.",
                ["TAGS", "COVER ART", "CHAPTERS"], []),
            new("settings.switches", "The switches",
                "LIGHT MODE swaps to brushed aluminum. TRAY keeps downloads going with the window closed. START WITH WINDOWS, CLIPBOARD, TOOL UPDATES and REDUCE MOTION do what they say.",
                ["LIGHT MODE", "TRAY", "START WITH WINDOWS", "CLIPBOARD", "TOOL UPDATES", "REDUCE MOTION"], []),
            new("settings.defaultplayer", "DEFAULT PLAYER",
                "DEFAULT PLAYER opens Windows' Default Apps to choose STX. Its readout counts the types STX plays and, after DEFAULT FOR, how many of them Windows already opens with it.",
                ["DEFAULT PLAYER"], ["DEFAULT FOR"]),
            new("settings.watched", "Watched folders",
                "ADD FOLDER and REMOVE FOLDER change which folders join the Library. Removing one never touches its files.",
                ["ADD FOLDER", "REMOVE FOLDER"], []),
            new("settings.help", "Help is here",
                "USER GUIDE explains every plate and control. TUTORIALS brings you back to these chapters.",
                ["USER GUIDE", "TUTORIALS"], []),
        ]),
    ];

    /// <summary>The chapter that shows a guide section's plate, for its SHOW ME pad; null when no chapter does.</summary>
    public static Tour? ForGuideSection(string title) => title switch
    {
        "The console" => Find("first-save"),
        "Save from a link" or "Save from a web page" or "Streams, discs, default app" => Find("saving"),
        "Queue" => Find("queue"),
        "Library" => Find("library"),
        "Player" => Find("player"),
        "Source truth" => Find("truth"),
        "Settings" => Find("settings"),
        _ => null,
    };

    public static Tour? Find(string id) => Tours.FirstOrDefault(t => t.Id == id);
}
