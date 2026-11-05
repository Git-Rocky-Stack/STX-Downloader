# The playback engine

STX plays media with VLC's engine, LibVLC, through LibVLCSharp. The engine is in `src/Stx.Playback/`, and the PLAYER plate that uses it is `src/Stx.App/Panels/PlayerPanel.cs`, in the rack at `src/Stx.App/MainWindow.cs:57`.

## Which VLC STX ships

STX ships the **official VLC 3.0.24 build for 64-bit Windows**, the same zip VideoLAN publishes for VLC itself. It doesn't use VideoLAN's NuGet package (`VideoLAN.LibVLC.Windows`).

- **Why the official build:** the NuGet package is a smaller build. It has 321 plugins where VLC 3.0.24 has 360. Among those missing are the AC3 packetizer, so raw `.ac3` and `.eac3` files didn't play, and webcam capture, the Real, Musepack and SID demuxers, the FAAD and MAD decoders, the high-quality resampler, and the deblocking filter. STX's promise is to play what VLC plays, so it takes VLC's own build.
- **Pinned:** the download URL and its sha256 are in `tools/vlc/engine.json`. The hash matches the `.sha256` file VideoLAN publishes beside the zip. A download with any other hash is thrown away (`tools/vlc/Get-Engine.ps1`).
- **Built in:** building `src/Stx.Playback` runs `tools/vlc/Get-Engine.ps1` once (`src/Stx.Playback/Stx.Playback.csproj:21`). That copies the engine beside every program that uses it, under `libvlc\` (`src/Stx.Playback/Stx.Playback.csproj:24`).
- **Left out:** only `libvlc.dll`, `libvlccore.dll` and the plugins ship. Plugins that would open network ports, run scripts or show VLC's own windows are removed, each with its reason in `tools/vlc/engine.json`: the interface, remote control, Lua, service discovery, streaming out, and the Last.fm and RTSP server modules. VLC's plugin cache is rebuilt afterwards.
- **No DVD decryption:** VLC's two DVD plugins (`access/libdvdnav_plugin.dll`, `access/libdvdread_plugin.dll`) have libdvdcss built in, which decrypts CSS-protected DVDs. STX doesn't support DRM, so both plugins are left out. Disc playback is a later phase, and it will have to read unencrypted discs without them. Lua is also switched off when the engine starts (`src/Stx.Playback/LibVlcOptions.cs:20`).

## What STX does differently from VLC, and why

### Dirac and VC-2 video are refused

VLC 3.0.24's engine crashes the whole program on Dirac / VC-2 video. That happens in the official desktop build too, with a real video window, and with a genuine Dirac sample from FFmpeg's archive as well as one made by FFmpeg. If STX passed such a file to the engine, STX would close.

So STX reads a file's tracks before playing it. If any video track is Dirac, it doesn't play the file. It shows **DIRAC VIDEO CAN'T BE PLAYED SAFELY** instead (`src/Stx.Playback/LibVlcEngine.cs:19`, `src/Stx.Playback/LibVlcEngine.cs:192`), and the rest of the console keeps working.

Dirac is rare. It's mostly found in BBC archive material. VLC 3.0.24 doesn't play it safely either.

### MIDI plays with a bundled SoundFont

VLC plays MIDI through FluidSynth, which needs a SoundFont. VLC doesn't ship one, and Windows' built-in `gm.dls` can't be loaded by it. STX ships **FluidR3_GM** by Frank Wen, under the MIT license. Its copyright file is shipped beside it, as `libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt`. STX passes it to the engine when the engine starts (`src/Stx.Playback/LibVlcEngine.cs:22`, `src/Stx.Playback/LibVlcOptions.cs:22`).

### Mono sound reaches WASAPI in stereo

Windows refuses a one-channel stream in WASAPI shared mode (`0x88890008`, `AUDCLNT_E_UNSUPPORTED_FORMAT`), and LibVLC 3.0.24 then falls back to DirectSound. For mono files, STX starts muted. When the output lands on DirectSound, it switches the output to stereo, which reopens it on WASAPI, and then lets the sound through (`src/Stx.Playback/LibVlcEngine.cs:178`, `src/Stx.Playback/LibVlcEngine.cs:312`). Files with 2, 6 or 8 channels keep their own layout, so surround is never downmixed. LibVLC still logs Windows' first refusal once for each mono file.

## The parity corpus

VideoLAN's feature list is written down in `tests/corpus/vlc-features.json`: 95 formats, codecs, subtitle formats and tags, read on 2026-11-04 for LibVLC 3.0.24. `tests/corpus/manifest.json` names one or more files for each.

Most files are made by FFmpeg. Some STX made itself, in `tests/corpus/own/`: a MIDI file, a tracker module, a Kate subtitle track, and text subtitles. The rest are fetched from FFmpeg's public sample archives and pinned by sha256. Fetched files are used for testing on this machine only, and are never committed or shipped.

`tools/corpus/Make-Corpus.ps1 -Ffmpeg <path>` builds the corpus. The test `tests/Stx.Playback.Tests/CorpusTests.cs` then plays every file through STX's engine, checking that its pictures and sound decode, that its subtitles appear, and that its tags read. It runs locally, like the screenshot tests. CI checks the manifest itself (`tests/Stx.Playback.Tests/CorpusManifestTests.cs`).

**Not covered, and why:**

- **SVCD / CVD subtitles.** No public sample with them was found. The one SVCD image in FFmpeg's archive plays, but it has no subtitle stream. LibVLC ships both decoders.
- **CMML.** VideoLAN's page lists it, but LibVLC 3.0.24 ships no CMML decoder, so neither VLC nor STX shows it.
