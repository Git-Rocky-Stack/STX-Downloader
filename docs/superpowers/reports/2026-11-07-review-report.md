# Review of 2026-11-07: report

Picks up from `docs/superpowers/handoffs/2026-11-05-handoff.md`. Two things were asked: review the whole codebase for misuse of best practice, unwired code, stubs and problem areas, and fix what's found; then carry on where the project left off.

## How it was done

- **Four independent reviewers**, each given one area of the code (playback and audio; core, data, bridge and the extension; view-models and tools; the app shell, build and scripts) and not this session's conclusions. Between them they reported 50 findings.
- **Each finding was checked in the code before anything changed.** Most were real. Some were real but worse than reported (below). A few weren't defects, or were deliberate; those are listed, with the reason.
- **Every fix has a test that failed first.** Each was break-checked: the fix was undone, the one test run, and the file restored byte for byte. The break-check helper's own verdicts are quoted where they say something (for example, the stalled-pump test without its fix: 173,760 frames written after the next run had ended).
- A grep for TODO, FIXME, HACK, `NotImplementedException`, empty catches and `async void` found nothing beyond the one clipboard handler (an event handler, now total).

## The machine

Not the reference PC. A Core Ultra 7 258V (8 cores), Intel Arc 140V graphics, "Speakers (SoundWire Audio)" on Intel Smart Sound Technology, Windows 11 Enterprise 26300. It had no .NET SDK, FFmpeg or VLC engine; .NET 10.0.403 was installed for this user only (`%USERPROFILE%\.dotnet`), FFmpeg was fetched the way CI fetches it (sha256 checked) into `%USERPROFILE%\.stx-dev`, and the build fetched the pinned engine.

## Results

- **CI suites** (CI's filters): Look 177, Art 69, Core 419, Data 42, Tools 199, Audio 69, Bridge 57, App 290, Playback 49: 1,371, all passing. The session began at 1,322 (the reference PC's counts, reproduced here before any change). Besides those 49, 3 device tests and 2 network tests were added.
- **Extension:** 60 of 60, 3 of them new (the service worker had no tests; it's now loaded under Node against a stand-in for the chrome API).
- **Device gate:** 13 of 13, the 3 new device tests included (disposing twice, a decode that dies early, a pump left in a stalled read). **The 16/44.1 and 24/96 BIT-PERFECT rows pass here.** They failed on the reference PC because its Realtek takes 48 kHz only; this machine's device takes 44.1 and 96 kHz exclusively. That's the first time those rates have reached a device bit-perfect, which the handoff listed as needing a USB DAC. A loopback bit comparison is still open (it needs hardware loopback).
- **Network gate:** a real yt-dlp came through GitHub's redirects with every hop checked against the tool hosts; a disc folder on `\\localhost\C$` reached VLC's DVD reader.
- **Build:** the whole solution, VisualTests and NativeHost included, rebuilt with no incremental state: 0 warnings, 0 errors, with warnings now errors.
- **Not run here:** the corpus gate (no corpus on this machine), screenshots (their baselines belong to the reference PC's GPU and fonts), the budgets (a 50,000-file folder and the reference PC), the installer and signing (no Inno Setup or Windows SDK here).

## Worse than reported

- **The yt-dlp swap** could leave no `yt-dlp.exe` at all: if the second of its two moves failed, the error was caught and reported as "update pending" with the tool gone.
- **The encoder probe** was built with whatever path FFmpeg had when first asked, `""` if first run hadn't finished downloading it, and kept it (and an empty encoder list) for the session.
- **`Measure-Budgets.ps1`** could not only strand your Library in the temp folder; a failure partway through moving it aside made the restore delete the real `stx.db` it had never moved.
- **`CredentialFile.DeleteLeftovers`** runs in the composition root's constructor; one leftover held by a scan would have stopped STX starting.

## Checked, and not changed

- **`Restart` waits up to 6 seconds on the UI thread** when ENHANCE changes mid-open. Deliberate: the wait keeps a parse still running off a LibVLC instance about to be disposed. The getter race the reviewer described can't happen in the app: the getters and `Restart` both run on the UI thread.
- **CI downloads FFmpeg's rolling "latest" build.** Deliberate (commit `2107062`): CI tests the build STX itself downloads, which is also "latest". Its checksum comes from the same release, as the app's does.
- **`LookStartup.Choose`'s unused `windowsIsDark` parameter** is a seam: a test passes both values to prove Windows' theme can't change the result.
- **`MediaRepo.DeleteItem`, the synchronous `NativeMessaging` wrappers and the `playlists` tables** have no caller in the app. The first two are what tests use (the cascade test, the framing tests); dropping the tables would be a migration with nothing to gain.
- **Sync-over-async at shutdown (`Composition.cs`) and in `SingleInstance`:** every awaited call uses `ConfigureAwait(false)` and each has a time limit, so neither can deadlock.

## Recommendations, done later the same day

- **The FFmpeg builds moved.** `github.com/BtbN/FFmpeg-Builds` redirects to `github.com/FFmpeg-Builds/FFmpeg-Builds` (traced 2026-11-07). STX and CI now name the new owner, and a Network test fails if that address ever answers with a move (`src/Stx.Tools/FfmpegSource.cs:12`).
- **Streams are checked for Dirac.** An http(s) stream that may carry video is read from its start, up to a megabyte or 2 seconds, before LibVLC is given it; sound isn't read. No engine setting avoids the crash: VLC aborts in FFmpeg's decoder (`decode.c:1628`) with each Dirac plugin taken out, with one thread and with hardware decoding off (`src/Stx.Core/Playback/DiracSniff.cs:33`).
- **The Arc 140V's super resolution was seen running:** "Using Super Resolution scaler" in VLC's log with the player on screen (`tests/Stx.VisualTests/EnhanceSeenTests.cs`).

## The open items, afterwards

Library row storage, the encoder settings, the SoundFont, Linux runs and what this laptop's hardware could cover were all taken on after the review; `CHANGELOG.md` (its first section) has each, and `docs/superpowers/handoffs/2026-11-07-handoff.md` has what's left and why. The last full run here (2026-11-08), all passing: CI suites 1,410 (Look 177, Art 69, Core 438, Data 48, Tools 199, Audio 69, Bridge 57, App 301, Playback 52), device 13, network 4 (the live yt-dlp update, FFmpeg's home, the radio stream, the share disc), extension 60. The visual suite's behaviour tests pass here too (ENHANCE, HDR10, full screen, playback); its 96 DPI pixel baselines can't match this 175% HDR screen.
