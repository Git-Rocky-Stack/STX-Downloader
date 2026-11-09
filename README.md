# STX Downloader

A Windows app that saves media you're allowed to keep, from YouTube and from web pages, tells you what the audio really is, and plays your media with VLC's engine. It looks like a piece of solid-state rack gear, because that's the point.

**DRM is out of scope.** This project does not support downloading DRM-protected media. When it sees DRM, it says so and stops.

**You are responsible for following each site's terms and the law where you live.**

Published by Strategia-X (Rocky Elsalaymeh). Support: support@strategia-x.com

## What it does

Each line points at the code that does it.

**Saving**

- Paste a link, and yt-dlp handles it, single items and playlists (`src/Stx.ViewModels/SourceViewModel.cs:32`, `src/Stx.Tools/YtDlpArgs.cs:43`).
- Video isn't re-encoded by default. It keeps the original streams, in MP4 when they fit and MKV when they don't (`src/Stx.Tools/YtDlpArgs.cs:44`, `src/Stx.Tools/Mp4Preference.cs:10`).
- A name already taken by another video never replaces it or gets mistaken for it. The new file gets its id added (`src/Stx.Tools/YtDlpRunner.cs:86`).
- A new file with the same bytes as one already saved is removed, and the library points at the first copy (`src/Stx.ViewModels/IdenticalFileCheck.cs:11`, `src/Stx.App/Composition.cs:53`).
- Car USB and Phone presets convert when you ask them to (`src/Stx.Core/Presets/PresetCatalog.cs:39`, `src/Stx.Core/Presets/PresetCatalog.cs:40`).
- A captured job whose link expired waits for a new capture from its page, and resumes from it (`src/Stx.Core/Queue/QueueEngine.cs:63`, `src/Stx.Bridge/CaptureHandler.cs:121`).

**Capturing from a page**

- A Chrome extension lists the page's HLS, DASH and direct media (`extension/worker.js:46`), collapses renditions, and hands over YouTube embeds by ID (`extension/detect.js:165`).
- Rows are labeled with whatever the page exposes: name, length, resolution (`extension/detect.js:236`).
- If the page uses DRM, its streams get no Save button (`extension/popup.js:35`).
- Hand-off uses Chrome native messaging and a named pipe only your Windows account can open. No network port is opened (`src/Stx.Bridge/PipeServer.cs:17`).

**Your secrets**

- Stream tokens, cookies and headers stay in memory (`src/Stx.Core/Queue/CaptureVault.cs:12`) and reach yt-dlp through a temp file only you can read, deleted when the job ends or the app quits (`src/Stx.Tools/CredentialFile.cs:73`, `src/Stx.Core/Queue/QueueEngine.cs:92`).
- They are scrubbed from the log you can copy (`src/Stx.Core/Logging/Redactor.cs:6`).

**Source Truth**

- Measures the saved file's audio rather than repeating what the site claims (`src/Stx.Audio/AudioMeasurer.cs:8`) and shows the spectrum (`src/Stx.App/Panels/SourceTruthPanel.cs:17`).
- Gives a plain verdict, and never says re-encoding improved anything (`src/Stx.Core/Audio/Verdict.cs:6`).

**The console**

- Black anodized by default, brushed aluminum as light mode (`src/Stx.App/Panels/SettingsPanel.cs:21`).
- Choose best quality, up to 1080p or 720p, video only or audio only, and which metadata to embed (`src/Stx.App/Panels/SettingsPanel.cs:33`, `src/Stx.Tools/DownloadChoices.cs:9`).
- Tray mode (`src/Stx.App/Platform/Tray.cs:6`), start with Windows (`src/Stx.App/Platform/StartupRegistry.cs:7`) and an optional clipboard offer, off by default (`src/Stx.App/Platform/ClipboardWatcher.cs:7`).
- A USER GUIDE pad and a TUTORIAL pad on the Settings plate (`src/Stx.App/Panels/SettingsPanel.cs:91`, `src/Stx.App/Panels/SettingsPanel.cs:92`). The guide opens by itself on first launch, once (`src/Stx.App/MainWindow.cs:100`), and covers each plate (`src/Stx.ViewModels/GuideContent.cs:12`).
- The tutorial is a 76-second captioned walkthrough with no sound that ships with the app. It starts by itself (`src/Stx.App/Panels/TutorialPanel.cs:85`). With reduce motion on, it shows a poster and waits for PLAY (`src/Stx.App/Panels/TutorialPanel.cs:70`).

**Your whole collection**

- The Library holds everything: what you've saved, files you've opened with STX, and the media in your Videos and Music folders, which are watched from the first launch (`src/Stx.Core/Settings/AppSettings.cs:46`). ADD FOLDER and REMOVE FOLDER in Settings change the list. Removing a folder takes its files out of the Library, never off your disk (`src/Stx.App/Panels/SettingsPanel.cs:128`, `src/Stx.Data/MediaRepo.cs:281`).
- The first scan runs in the background, below normal priority, and rows appear as it goes (`src/Stx.Core/Library/CollectionWatcher.cs:52`, `src/Stx.Core/Library/CollectionWatcher.cs:94`). After that, new files appear by themselves, a renamed or moved file keeps its row, and a deleted one stays listed as FILE MISSING (`src/Stx.Core/Library/CollectionWatcher.cs:192`, `src/Stx.Core/Library/FolderScan.cs:55`).
- Watched files are indexed lightly: path, size and date, so a OneDrive file that lives only online isn't downloaded by being listed. A file's bytes are hashed the first time you choose it (`src/Stx.App/Composition.cs:197`).
- Each row says ORIGINAL, OPENED or WATCHED (`src/Stx.ViewModels/LibraryViewModel.cs:57`). Search looks through title, file name and folder, every word you type has to match, and it keeps up on tens of thousands of rows (`src/Stx.ViewModels/LibraryViewModel.cs:166`). The list only draws the rows on screen, and a change redraws only the rows it touched (`src/Stx.App/Panels/LibraryPanel.cs:50`, `src/Stx.ViewModels/LibraryViewModel.cs:278`).

**Playing**

- STX plays media with VLC's engine: the official VLC 3.0.24 build for Windows, pinned by sha256 (`tools/vlc/engine.json`). The parts that would open network ports or run site scripts are left out (`tools/vlc/engine.json`, `src/Stx.Playback/LibVlcOptions.cs:24`). VLC's DVD plugins ship for unencrypted DVDs. They contain libdvdcss, which can decrypt CSS, so STX checks every disc itself and never hands an encrypted one to them (`src/Stx.Core/Playback/DiscCheck.cs:14`, `src/Stx.Playback/LibVlcEngine.cs:226`).
- A PLAYER plate sits beside the Library, above Source Truth (`src/Stx.App/MainWindow.cs:58`). OPEN in the Library plays the file there and never hands it to another app (`src/Stx.ViewModels/LibraryViewModel.cs:109`, `src/Stx.App/Composition.cs:212`).
- A file opened from File Explorer joins the Library and plays (`src/Stx.App/App.xaml.cs:27`, `src/Stx.App/Composition.cs:253`). A second launch with a file hands it to the copy already running, over a named pipe only your account can open (`src/Stx.App/Platform/SingleInstance.cs:26`, `src/Stx.Bridge/OpenRequests.cs`).
- FULL fills the screen and Esc brings the console back. The video keeps playing through it (`src/Stx.App/Panels/PlayerPanel.cs:212`, `src/Stx.App/Panels/PlayerPanel.cs:119`, `src/Stx.App/MainWindow.cs:74`).
- Hidden in the tray, the player stops decoding pictures and the sound carries on (`src/Stx.App/MainWindow.cs:91`, `src/Stx.Playback/LibVlcEngine.cs:311`).
- A PERFORMANCE knob in Settings sets how hard playback may work: AUTO picks from the PC, or choose ECO, BALANCED or MAX (`src/Stx.App/Panels/SettingsPanel.cs:46`, `src/Stx.Core/Playback/PerformanceProfile.cs:16`).
- MIDI plays, using the FluidR3_GM SoundFont that ships with the app (`src/Stx.Playback/LibVlcEngine.cs:22`).
- **Dirac and VC-2 video are refused.** VLC 3.0.24's engine crashes on them, the desktop VLC build included. Rather than let STX close, it shows "DIRAC VIDEO CAN'T BE PLAYED SAFELY" (`src/Stx.Playback/LibVlcEngine.cs:19`).
- **STX as your default player.** The setup registers STX, for your account only, for every type the parity corpus proves it plays (`tools/installer/STXDownloader.iss:90`, `src/Stx.Core/Library/FileAssociations.cs:45`, `src/Stx.Core/Library/MediaTypes.cs:9`). The first time, STX asks once whether it should be your player. YES opens Windows' Default Apps, where you choose it; Windows doesn't let an app choose itself (`src/Stx.ViewModels/DefaultPlayerViewModel.cs:39`, `src/Stx.Core/Library/FileAssociations.cs:109`). DEFAULT PLAYER in Settings opens that page any time, and its light reads what Windows really opens each type with (`src/Stx.App/Panels/SettingsPanel.cs:182`, `src/Stx.Core/Library/FileAssociations.cs:89`).
- **Discs.** PLAY DISC plays an unencrypted DVD or Blu-ray from its drive, its folder or an image, from the main title (`src/Stx.App/Panels/SourcePanel.cs:29`, `src/Stx.Playback/LibVlcOptions.cs:39`). **Encrypted discs are refused by STX itself before VLC's engine sees them**: it reads the CSS scrambling bits in a DVD's sectors, and a Blu-ray's AACS folder and copy-permission bits, and says THIS DISC IS ENCRYPTED, SO IT CAN'T BE PLAYED. A sector it can't read counts as encrypted (`src/Stx.Core/Playback/DiscCheck.cs:39`, `src/Stx.Core/Playback/DiscCheck.cs:134`, `src/Stx.Core/Playback/DiscCheck.cs:78`).
- **Streams and internet radio.** PLAY STREAM plays a stream address, a radio station, or its .pls or .m3u playlist. A web page is sent to ANALYZE and the capture path, as before (`src/Stx.ViewModels/StreamOpener.cs:23`, `src/Stx.Core/Playback/PlaybackTarget.cs:74`). VLC's engine only connects out: UDP and RTP would need a listening port, so they're refused, and RTSP runs over its one TCP connection (`src/Stx.Core/Playback/PlaybackTarget.cs:79`).
- **BIT-PERFECT**, a Settings switch, off by default (`src/Stx.App/Panels/SettingsPanel.cs:206`). Lossless music is decoded by FFmpeg and sent to your sound device, which STX holds exclusively at the file's own rate and depth. Nothing resamples, mixes, dithers or changes the volume (`src/Stx.Core/Playback/BitPerfect.cs:84`, `src/Stx.Core/Playback/BitPerfect.cs:95`, `src/Stx.Core/Playback/BitPerfect.cs:103`, `src/Stx.Playback/ExclusiveRenderer.cs:90`). A film's AC3, E-AC3, DTS or TrueHD soundtrack goes to your receiver as it is, from the next launch (`src/Stx.Playback/LibVlcOptions.cs:26`). The Player shows what reaches the device, such as 24/96 BIT-PERFECT, or why a file plays shared (`src/Stx.Playback/RoutingEngine.cs:50`). While it's on, other apps can't make sound, and VOLUME is fixed (`src/Stx.App/Panels/PlayerPanel.cs:147`). VLC 3.0.24's own audio output can't do this; [docs/PLAYBACK.md](docs/PLAYBACK.md) says why.
- **ENHANCE** on the Player: OFF, SHARPEN or UPSCALE, for the picture on screen only (`src/Stx.App/Panels/PlayerPanel.cs:159`, `src/Stx.Core/Playback/Enhance.cs:49`). UPSCALE is GPU super resolution, offered only where the GPU has it: NVIDIA RTX with a 530 or later driver, and Intel Arc or Xe. Anywhere else it says UPSCALE UNAVAILABLE rather than pretend (`src/Stx.Core/Playback/Enhance.cs:34`, `src/Stx.Playback/HardwareProbe.cs:45`). PERFORMANCE sets how far it may go (`src/Stx.Core/Playback/Enhance.cs:42`). Under the knob, the Player says what VLC's engine confirms is running (`src/Stx.Playback/LibVlcEngine.cs:407`).
- Parity with VLC is tested. VideoLAN's feature list for 3.0.24 is written down (`tests/corpus/vlc-features.json`), and a corpus of 109 files plays through STX's engine (`tests/corpus/manifest.json`, `tests/Stx.Playback.Tests/CorpusTests.cs`). It covers 89 of the 91 formats and codecs on the list, plus four modern profiles: HEVC Main10, HDR10, AV1 and VP9. The two that can't be tested are SVCD/CVD subtitles, for which no public sample exists, and CMML, which LibVLC 3.0.24 has no decoder for. Details are in [docs/PLAYBACK.md](docs/PLAYBACK.md).

**Shrinking**

- SHRINK makes a smaller copy beside the selected file: AV1 or HEVC video with Opus sound, as the Shrink to knob says, or Opus for sound alone. Subtitles, chapters and attachments come along (`src/Stx.Core/Presets/PresetCatalog.cs:42`, `src/Stx.Tools/FfmpegArgs.cs:79`). SHRINK FOLDER does the same for every file in a folder, as jobs in the Queue (`src/Stx.ViewModels/ShrinkFolder.cs:15`).
- A copy is kept only after it checks out: it decodes from start to end without errors, has the original's picture and sound, and lasts as long. A copy that fails is deleted and the job says so (`src/Stx.Tools/CopyCheck.cs:16`, `src/Stx.Tools/FfmpegRunner.cs:61`). The original is only ever read.
- Source Truth calls a shrink copy lossy, never improved. Each copy says how much smaller it is, and the Library adds up what deleting the originals would free. STX never deletes them (`src/Stx.ViewModels/LibraryViewModel.cs:38`, `src/Stx.ViewModels/LibraryViewModel.cs:255`).

## Install

Windows 10 version 2004 or later, x64.

1. Run `STXDownloader-1.1.0-setup.exe`. It installs for your account only, with no administrator prompt, and uninstalls from Windows Settings (`tools/installer/STXDownloader.iss:37`). Your saved videos are never removed.
2. The first run downloads yt-dlp, FFmpeg and Deno, about 430 MB. See [third-party tools](docs/THIRD_PARTY.md).

The setup and the app's own files are signed through Azure Artifact Signing. Windows shows the certificate's validated name as the publisher: Sharif Elsalaymeh.

To capture from pages:

3. In Chrome or Edge, open the extensions page, turn on **Developer mode**, choose **Load unpacked**, and pick the `extension` folder inside the install folder. The setup offers to show you that folder.

The setup already registers the browser hand-off (`tools/installer/STXDownloader.iss:84`).

A zip is also released. Unzip it anywhere, run `STXDownloader.exe`, and run `tools\Register-NativeHost.ps1` once to connect the browser. It writes to your account only, and `-Remove` undoes it.

Downloads go to your Videos folder unless you change it in Settings. If OneDrive manages that folder, they'll sync.

## What has and hasn't been tested

[Real-site results](docs/REAL-SITE-CHECKS.md) and [performance numbers](docs/PERFORMANCE.md) are written down, including what wasn't tested: a registered webinar, a playlist with a private entry, more than one commercial DRM page, a real unplugged network, and high-DPI displays.

The player's Phases 2 to 7 were built without a Windows machine, so some of them are tested in code and CI but not yet on hardware: a real DVD or Blu-ray, BIT-PERFECT on a real sound device, UPSCALE on an RTX or Arc GPU, Default Apps on Windows 10 and 11, and the new plates' screenshots. The [Phase 2 to 7 report](docs/superpowers/reports/2026-11-05-player-phases-2-7-report.md) lists each one.

## Build

```
dotnet build src/Stx.App -c Release -p:Platform=x64
dotnet test --project tests/Stx.Core.Tests
node --test "extension/test/*.test.js"
tools\Publish.ps1        # add -Sign to sign, with an az login that may use the Strategia-X signing account
```

## License

[MIT](LICENSE). Copyright (c) 2026 Rocky Elsalaymeh, Strategia-X. Third-party software, the parts shipped inside the app and the tools it downloads, keeps its own license; see [third-party software](docs/THIRD_PARTY.md).
