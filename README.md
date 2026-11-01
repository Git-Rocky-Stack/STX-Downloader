# STX Downloader

A Windows app that saves media you're allowed to keep, from YouTube and from web pages, and tells you what the audio really is. It looks like a piece of solid-state rack gear, because that's the point.

**DRM is out of scope.** This project does not support downloading DRM-protected media. When it sees DRM, it says so and stops.

**You are responsible for following each site's terms and the law where you live.**

## What it does

Each line points at the code that does it.

**Saving**

- Paste a link, and yt-dlp handles it, single items and playlists (`src/Stx.ViewModels/SourceViewModel.cs:12`, `src/Stx.Tools/YtDlpArgs.cs:37`).
- Video isn't re-encoded by default. It keeps the original streams, in MP4 or MKV (`src/Stx.Tools/YtDlpArgs.cs:38`).
- Car USB and Phone presets convert when you ask them to (`src/Stx.Core/Presets/PresetCatalog.cs:30`, `src/Stx.Core/Presets/PresetCatalog.cs:31`).
- A captured job whose link expired waits for a new capture from its page, and resumes from it (`src/Stx.Core/Queue/QueueEngine.cs:63`, `src/Stx.Bridge/CaptureHandler.cs:85`).

**Capturing from a page**

- A Chrome extension lists the page's HLS, DASH and direct media (`extension/worker.js:46`), collapses renditions, and hands over YouTube embeds by ID (`extension/detect.js:147`).
- Rows are labeled with whatever the page exposes: name, length, resolution (`extension/detect.js:218`).
- If the page uses DRM, its streams get no Save button (`extension/popup.js:36`).
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

## Install

Windows 10 version 2004 or later, x64.

1. Unzip the release anywhere and run `STXDownloader.exe`. The app isn't signed, so Windows SmartScreen may warn you. Choose **More info**, then **Run anyway**.
2. The first run downloads yt-dlp, FFmpeg and Deno, about 430 MB. See [third-party tools](docs/THIRD_PARTY.md).

To capture from pages:

3. In Chrome, open `chrome://extensions`, turn on **Developer mode**, choose **Load unpacked**, and pick the `extension` folder.
4. Run `tools\Register-NativeHost.ps1` once from the unzipped folder. It writes to your account only. `-Remove` undoes it.

Downloads go to your Videos folder unless you change it in Settings. If OneDrive manages that folder, they'll sync.

## What has and hasn't been tested

[Real-site results](docs/REAL-SITE-CHECKS.md) and [performance numbers](docs/PERFORMANCE.md) are written down, including what wasn't tested: real Chrome and Edge, a registered webinar, a playlist with a private entry, commercial DRM, tray menu clicks, a first run with no connection, and high-DPI displays.

## Build

```
dotnet build src/Stx.App -c Release -p:Platform=x64
dotnet test tests/Stx.Core.Tests
node --test extension/test/
tools\Publish.ps1
```

## License

[MIT](LICENSE). The third-party tools it downloads keep their own licenses.
