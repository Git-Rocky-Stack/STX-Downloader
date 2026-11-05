# Changelog

## Unreleased: the whole collection, the default player, discs, streams, bit-perfect sound, ENHANCE and Shrink

**Not in a published release yet.** Phases 2 to 7 of the player. The report, with what was and wasn't tested, is `docs/superpowers/reports/2026-11-05-player-phases-2-7-report.md`.

- **The Library is your whole collection.** Videos and Music are watched; add and remove folders in Settings. New files appear by themselves, renamed files keep their rows, deleted ones show FILE MISSING. Each row says ORIGINAL, OPENED or WATCHED. Search by title, file name or folder, on tens of thousands of rows (`src/Stx.Core/Library/CollectionWatcher.cs`, `src/Stx.ViewModels/LibraryViewModel.cs`).
- **STX as your default player.** Registered for every type the corpus proves it plays; asked once at first run; a DEFAULT PLAYER pad and an LED that reads what Windows really does (`src/Stx.Core/Library/FileAssociations.cs`).
- **Discs and streams.** PLAY DISC for unencrypted DVDs and Blu-rays; encrypted discs are refused by STX before VLC's engine sees them. VLC's DVD plugins ship again for this. PLAY STREAM for stream addresses and internet radio; UDP and RTP are refused because they'd need a listening port (`src/Stx.Core/Playback/DiscCheck.cs`, `src/Stx.ViewModels/StreamOpener.cs`).
- **BIT-PERFECT.** Lossless music reaches the device exactly as it is, through FFmpeg and STX's own exclusive renderer, because VLC 3.0.24's output can't do it. Films pass their compressed soundtrack to the receiver. The Player says what reaches the device (`src/Stx.Core/Playback/BitPerfect.cs`, `src/Stx.Playback/ExclusiveRenderer.cs`).
- **ENHANCE.** SHARPEN, or UPSCALE on a GPU with super resolution, for the picture on screen only; UPSCALE UNAVAILABLE elsewhere (`src/Stx.Core/Playback/Enhance.cs`).
- **Shrink.** AV1, HEVC and Opus copies beside the originals, one file or a whole folder, each checked before it's kept. The Library shows what deleting the originals would free; STX never deletes them (`src/Stx.Tools/CopyCheck.cs`, `src/Stx.ViewModels/ShrinkFolder.cs`).
- **Build:** the test suites moved from xUnit 2, marked Legacy on NuGet, to xunit.v3 on Microsoft.Testing.Platform, and CI's actions to their Node 24 releases.

## Unreleased: the media player

**Not in a published release yet.** Everything below is in the app built from this repository. `docs/PLAYBACK.md` has the details, and `docs/superpowers/reports/2026-11-04-player-phase1-report.md` the evidence.

- **A PLAYER plate**, beside the Library and above Source Truth. It has PREV, PLAY/PAUSE, NEXT, STOP, FULL, a position bar, the time, a VOLUME knob, and track pickers when a file has more than one choice (`src/Stx.App/Panels/PlayerPanel.cs`, `src/Stx.App/MainWindow.cs:58`).
- **OPEN plays inside STX.** OPEN in the Library plays the file on the PLAYER plate instead of handing it to another app (`src/Stx.ViewModels/LibraryViewModel.cs:109`).
- **Files from File Explorer.** A file opened with STX joins the Library and plays; a second launch hands its file to the copy already running (`src/Stx.App/App.xaml.cs:27`, `src/Stx.App/Platform/SingleInstance.cs:26`).
- **Full screen.** FULL fills the screen, and Esc or EXIT brings the console back without restarting the video (`src/Stx.App/Panels/PlayerPanel.cs:212`).
- **PERFORMANCE** in Settings: AUTO, ECO, BALANCED or MAX (`src/Stx.App/Panels/SettingsPanel.cs:46`).
- **VLC's engine.** The official VLC 3.0.24 build for Windows, pinned by sha256, without the parts that would open network ports, run site scripts or decrypt DVDs (`tools/vlc/engine.json`).
- **Dirac and VC-2 video are refused, not played.** VLC 3.0.24's engine crashes on them, the desktop VLC build included, so STX shows "DIRAC VIDEO CAN'T BE PLAYED SAFELY" instead of closing (`src/Stx.Playback/LibVlcEngine.cs:19`).
- **MIDI plays**, with the FluidR3_GM SoundFont (MIT), which VLC doesn't include (`src/Stx.Playback/LibVlcEngine.cs:22`).
- **Mono sound goes through WASAPI in stereo**, instead of falling back to DirectSound; surround is never downmixed (`src/Stx.Playback/LibVlcEngine.cs:417`).
- **A parity corpus.** 99 files covering 89 of the 91 formats and codecs on VideoLAN's list, plus HEVC Main10, HDR10, AV1 and VP9. The two left out are SVCD/CVD subtitles (no public sample) and CMML (no decoder in LibVLC 3.0.24) (`tests/corpus/manifest.json`).
