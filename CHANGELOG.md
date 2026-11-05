# Changelog

## Unreleased: the media player (in development)

**Not in any release yet.** These items are built and tested, but the PLAYER plate that uses them isn't on the console yet, so none of them can be reached from the app today. The plan is `docs/superpowers/plans/2026-11-04-player-phase1.md`, and `docs/PLAYBACK.md` has the details.

- **VLC's engine.** STX plays media with the official VLC 3.0.24 build for Windows, pinned by sha256 (`tools/vlc/engine.json`), with the parts that would open network ports or run scripts left out (`src/Stx.Playback/LibVlcEngine.cs`, `src/Stx.Playback/LibVlcOptions.cs`).
- **Dirac / VC-2 video is refused, not played.** VLC 3.0.24's engine crashes on it, so STX shows "DIRAC VIDEO CAN'T BE PLAYED SAFELY" instead of closing (`src/Stx.Playback/LibVlcEngine.cs:19`).
- **MIDI plays.** STX ships the FluidR3_GM SoundFont, under the MIT license, which VLC doesn't include (`src/Stx.Playback/LibVlcEngine.cs:22`).
- **Mono sound goes through WASAPI in stereo**, instead of falling back to DirectSound. Surround is never downmixed (`src/Stx.Playback/LibVlcEngine.cs:286`).
- **A parity corpus.** Every format, codec and subtitle format on VideoLAN's list is played through STX's engine, apart from two that can't be tested: SVCD/CVD subtitles (no public sample) and CMML (no decoder in LibVLC 3.0.24) (`tests/corpus/manifest.json`, `docs/PLAYBACK.md`).
