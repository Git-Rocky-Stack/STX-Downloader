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
- A new file with the same bytes as one already saved is removed, and the library points at the first copy (`src/Stx.ViewModels/IdenticalFileCheck.cs:11`, `src/Stx.App/Composition.cs:50`).
- Car USB and Phone presets convert when you ask them to (`src/Stx.Core/Presets/PresetCatalog.cs:30`, `src/Stx.Core/Presets/PresetCatalog.cs:31`).
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

- Black anodized by default, brushed aluminum as light mode (`src/Stx.App/Panels/SettingsPanel.cs:20`).
- Choose best quality, up to 1080p or 720p, video only or audio only, and which metadata to embed (`src/Stx.App/Panels/SettingsPanel.cs:32`, `src/Stx.Tools/DownloadChoices.cs:9`).
- Tray mode (`src/Stx.App/Platform/Tray.cs:6`), start with Windows (`src/Stx.App/Platform/StartupRegistry.cs:7`) and an optional clipboard offer, off by default (`src/Stx.App/Platform/ClipboardWatcher.cs:7`).
- A USER GUIDE pad and a TUTORIAL pad on the Settings plate (`src/Stx.App/Panels/SettingsPanel.cs:90`, `src/Stx.App/Panels/SettingsPanel.cs:91`). The guide opens by itself on first launch, once (`src/Stx.App/MainWindow.cs:99`), and covers each plate (`src/Stx.ViewModels/GuideContent.cs:12`).
- The tutorial is a 76-second captioned walkthrough with no sound that ships with the app. It starts by itself (`src/Stx.App/Panels/TutorialPanel.cs:85`). With reduce motion on, it shows a poster and waits for PLAY (`src/Stx.App/Panels/TutorialPanel.cs:70`).

**Playing**

- STX plays media with VLC's engine: the official VLC 3.0.24 build for Windows, pinned by sha256 (`tools/vlc/engine.json`). The parts that would open network ports, run site scripts or decrypt DVDs are left out (`tools/vlc/engine.json`, `src/Stx.Playback/LibVlcOptions.cs:20`).
- A PLAYER plate sits beside the Library, above Source Truth (`src/Stx.App/MainWindow.cs:57`). OPEN in the Library plays the file there and never hands it to another app (`src/Stx.ViewModels/LibraryViewModel.cs:48`, `src/Stx.App/Composition.cs:168`).
- A file opened from File Explorer joins the Library and plays (`src/Stx.App/App.xaml.cs:27`, `src/Stx.App/Composition.cs:176`). A second launch with a file hands it to the copy already running, over a named pipe only your account can open (`src/Stx.App/Platform/SingleInstance.cs:26`, `src/Stx.Bridge/OpenRequests.cs`).
- FULL fills the screen and Esc brings the console back. The video keeps playing through it (`src/Stx.App/Panels/PlayerPanel.cs:194`, `src/Stx.App/Panels/PlayerPanel.cs:117`, `src/Stx.App/MainWindow.cs:73`).
- Hidden in the tray, the player stops decoding pictures and the sound carries on (`src/Stx.App/MainWindow.cs:90`, `src/Stx.Playback/LibVlcEngine.cs:231`).
- A PERFORMANCE knob in Settings sets how hard playback may work: AUTO picks from the PC, or choose ECO, BALANCED or MAX (`src/Stx.App/Panels/SettingsPanel.cs:45`, `src/Stx.Core/Playback/PerformanceProfile.cs:16`).
- MIDI plays, using the FluidR3_GM SoundFont that ships with the app (`src/Stx.Playback/LibVlcEngine.cs:22`).
- **Dirac and VC-2 video are refused.** VLC 3.0.24's engine crashes on them, the desktop VLC build included. Rather than let STX close, it shows "DIRAC VIDEO CAN'T BE PLAYED SAFELY" (`src/Stx.Playback/LibVlcEngine.cs:19`).
- Parity with VLC is tested. VideoLAN's feature list for 3.0.24 is written down (`tests/corpus/vlc-features.json`), and a corpus of 99 files plays through STX's engine (`tests/corpus/manifest.json`, `tests/Stx.Playback.Tests/CorpusTests.cs`). It covers 89 of the 91 formats and codecs on the list, plus four modern profiles: HEVC Main10, HDR10, AV1 and VP9. The two that can't be tested are SVCD/CVD subtitles, for which no public sample exists, and CMML, which LibVLC 3.0.24 has no decoder for. Details are in [docs/PLAYBACK.md](docs/PLAYBACK.md).

## Install

Windows 10 version 2004 or later, x64.

1. Run `STXDownloader-1.0.0-setup.exe`. It installs for your account only, with no administrator prompt, and uninstalls from Windows Settings (`tools/installer/STXDownloader.iss:37`). Your saved videos are never removed.
2. The first run downloads yt-dlp, FFmpeg and Deno, about 430 MB. See [third-party tools](docs/THIRD_PARTY.md).

The setup and the app's own files are signed through Azure Artifact Signing. Windows shows the certificate's validated name as the publisher: Sharif Elsalaymeh.

To capture from pages:

3. In Chrome or Edge, open the extensions page, turn on **Developer mode**, choose **Load unpacked**, and pick the `extension` folder inside the install folder. The setup offers to show you that folder.

The setup already registers the browser hand-off (`tools/installer/STXDownloader.iss:82`).

A zip is also released. Unzip it anywhere, run `STXDownloader.exe`, and run `tools\Register-NativeHost.ps1` once to connect the browser. It writes to your account only, and `-Remove` undoes it.

Downloads go to your Videos folder unless you change it in Settings. If OneDrive manages that folder, they'll sync.

## What has and hasn't been tested

[Real-site results](docs/REAL-SITE-CHECKS.md) and [performance numbers](docs/PERFORMANCE.md) are written down, including what wasn't tested: a registered webinar, a playlist with a private entry, more than one commercial DRM page, a real unplugged network, and high-DPI displays.

## Build

```
dotnet build src/Stx.App -c Release -p:Platform=x64
dotnet test tests/Stx.Core.Tests
node --test "extension/test/*.test.js"
tools\Publish.ps1        # add -Sign to sign, with an az login that may use the Strategia-X signing account
```

## License

[MIT](LICENSE). Copyright (c) 2026 Rocky Elsalaymeh, Strategia-X. Third-party software, the parts shipped inside the app and the tools it downloads, keeps its own license; see [third-party software](docs/THIRD_PARTY.md).
