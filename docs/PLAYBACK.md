# The playback engine

STX plays media with VLC's engine, LibVLC, through LibVLCSharp. The engine is in `src/Stx.Playback/`, and the PLAYER plate that uses it is `src/Stx.App/Panels/PlayerPanel.cs`, in the rack at `src/Stx.App/MainWindow.cs:58`.

## Which VLC STX ships

STX ships the **official VLC 3.0.24 build for 64-bit Windows**, the same zip VideoLAN publishes for VLC itself. It doesn't use VideoLAN's NuGet package (`VideoLAN.LibVLC.Windows`).

- **Why the official build:** the NuGet package is a smaller build. It has 321 plugins where VLC 3.0.24 has 360. Among those missing are the AC3 packetizer, so raw `.ac3` and `.eac3` files didn't play, and webcam capture, the Real, Musepack and SID demuxers, the FAAD and MAD decoders, the high-quality resampler, and the deblocking filter. STX's promise is to play what VLC plays, so it takes VLC's own build.
- **Pinned:** the download URL and its sha256 are in `tools/vlc/engine.json`. The hash matches the `.sha256` file VideoLAN publishes beside the zip. A download with any other hash is thrown away (`tools/vlc/Get-Engine.ps1`).
- **Built in:** building `src/Stx.Playback` runs `tools/vlc/Get-Engine.ps1` once (`src/Stx.Playback/Stx.Playback.csproj:22`). That copies the engine beside every program that uses it, under `libvlc\` (`src/Stx.Playback/Stx.Playback.csproj:25`).
- **Left out:** only `libvlc.dll`, `libvlccore.dll` and the plugins ship. Plugins that would open network ports, run scripts or show VLC's own windows are removed, each with its reason in `tools/vlc/engine.json`: the interface, remote control, Lua, service discovery, streaming out, and the Last.fm and RTSP server modules. VLC's plugin cache is rebuilt afterwards.
- **No DVD decryption:** VLC's two DVD plugins (`access/libdvdnav_plugin.dll`, `access/libdvdread_plugin.dll`) ship for unencrypted DVDs. They have libdvdcss built in, which can decrypt CSS-protected DVDs, so STX checks every disc first and refuses an encrypted one before LibVLC is given it (`src/Stx.Core/Playback/DiscCheck.cs`, `src/Stx.Core/Playback/PlaybackTarget.cs`). Lua is switched off when the engine starts (`src/Stx.Playback/LibVlcOptions.cs:24`).

## What STX does differently from VLC, and why

### Dirac and VC-2 video are refused

VLC 3.0.24's engine crashes the whole program on Dirac / VC-2 video. That happens in the official desktop build too, with a real video window, and with a genuine Dirac sample from FFmpeg's archive as well as one made by FFmpeg. If STX passed such a file to the engine, STX would close.

So STX reads a file's tracks before playing it. If any video track is Dirac, it doesn't play the file. It shows **DIRAC VIDEO CAN'T BE PLAYED SAFELY** instead (`src/Stx.Playback/LibVlcEngine.cs:19`, `src/Stx.Playback/LibVlcEngine.cs:275`), and the rest of the console keeps working.

Dirac is rare. It's mostly found in BBC archive material. VLC 3.0.24 doesn't play it safely either.

### MIDI plays with a bundled SoundFont

VLC plays MIDI through FluidSynth, which needs a SoundFont. VLC doesn't ship one, and Windows' built-in `gm.dls` can't be loaded by it. STX ships **FluidR3_GM** by Frank Wen, under the MIT license. Its copyright file is shipped beside it, as `libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt`. STX passes it to the engine when the engine starts (`src/Stx.Playback/LibVlcEngine.cs:22`, `src/Stx.Playback/LibVlcOptions.cs:28`).

### Mono sound reaches WASAPI in stereo

Windows refuses a one-channel stream in WASAPI shared mode (`0x88890008`, `AUDCLNT_E_UNSUPPORTED_FORMAT`), and LibVLC 3.0.24 then falls back to DirectSound. For mono files, STX starts muted. When the output lands on DirectSound, it switches the output to stereo, which reopens it on WASAPI, and then lets the sound through (`src/Stx.Playback/LibVlcEngine.cs:261`, `src/Stx.Playback/LibVlcEngine.cs:417`). Files with 2, 6 or 8 channels keep their own layout, so surround is never downmixed. LibVLC still logs Windows' first refusal once for each mono file.

### Discs start at the main title, and encrypted ones are refused first

Discs, disc folders and disc images go to VLC's DVD and Blu-ray readers as `dvd:///` or `bluray:///` addresses (`src/Stx.Core/Playback/PlaybackTarget.cs`). Before that, STX checks the disc itself (`src/Stx.Core/Playback/DiscCheck.cs`):

- **DVD:** a sample of sectors spread through every VOB file (or through the image) is read. CSS sets the PES scrambling bits on every sector it scrambles; an unencrypted disc leaves them clear.
- **Blu-ray:** an `AACS` or `BDSVM` folder, which every protected disc carries, or AACS's copy-permission bits at the start of an m2ts's aligned units. In an image, those names are looked for in its directory area.
- A sector that can't be read counts as encrypted: a drive refuses to read CSS sectors it hasn't unlocked, and when in doubt STX doesn't play.

The check runs inside the engine, so nothing reaches VLC's readers unchecked, whether it came from PLAY DISC, OPEN, File Explorer or a watched folder. Discs start at the main title (`:no-dvdnav-menu`, `:no-bluray-menu`): their menus need a pointer on the picture, and a swap chain doesn't pass one back. No AACS or BD+ library ships, so libbluray reads only unencrypted Blu-rays.

**Not tested on a real disc.** No DVD or Blu-ray authoring tool was available where Phases 2 to 7 were built, so unencrypted disc playback hasn't been seen working. The refusal is tested on sectors built in the tests, as the spec asks: no encrypted media is in the repository.

### Streams connect out only

PLAY STREAM plays an address (`src/Stx.ViewModels/StreamOpener.cs`). Adaptive streams (`.m3u8`, `.mpd`) and direct media play as they are. A radio playlist (`.pls`, `.m3u`, `.xspf`) is read by STX, which plays its first stream and keeps the rest for NEXT. An address that doesn't say what it is gets its headers asked for, and nothing more. A web page is sent to ANALYZE. UDP, RTP and SAP would need STX to listen on a port, so they're refused (`src/Stx.Core/Playback/PlaybackTarget.cs`), and RTSP is told to stay on its one TCP connection (`:rtsp-tcp`).

### BIT-PERFECT doesn't use VLC's audio output for music

Spike S5 read VLC 3.0.24's source:

- `modules/audio_output/wasapi.c:546-548` opens plain PCM in **shared** mode, always. Exclusive mode is used only for S/PDIF and HDMI passthrough.
- `modules/audio_output/amem.c:191-193`, the audio callbacks an app could take PCM from, refuses anything but 16-bit (`strcmp(format, "S16N") /* TODO: amem-format */`). A 24-bit file would arrive already cut to 16 bits.

So, with Rocky's choice:

- **Lossless music** (FLAC, ALAC, WavPack, APE, TTA, WAV/AIFF PCM) is decoded by the FFmpeg STX already downloads. FFmpeg's raw PCM goes to STX's own renderer (`src/Stx.Playback/ExclusiveRenderer.cs`, on NAudio's WASAPI layer), which holds the default device exclusively at the file's own rate. The device layouts offered never cut a bit: 16-bit can go out as 16, 24 or 32, and 24-bit as 24-in-32, packed 24 or 32. A wider container only pads with zeros (`src/Stx.Core/Playback/BitPerfect.cs`). A lossless decoder gives the same samples whoever wrote it. The tests prove this sample by sample with the real FFmpeg (`tests/Stx.Core.Tests/BitPerfectTests.cs`).
- **Films** send AC3, E-AC3, DTS and TrueHD to the receiver through VLC's exclusive passthrough (`--mmdevice-passthrough=2`). VLC reads that once, when it starts, so it follows the switch from the next launch.
- **Everything else** plays shared, and the readout says why: a lossy file has no original samples to keep; a film's PCM can't leave VLC's engine unaltered; the device didn't take the format; another app holds it.
- **Not built:** the spec's route for a film's 16-bit PCM through the callbacks. Keeping that in sync with the picture couldn't be tested without the reference PC, and those soundtracks play shared, saying so.

**Not proven on hardware.** No sound device was available where Phase 5 was built. The device leg is the Device-category tests in `tests/Stx.Playback.Tests/BitPerfectEngineTests.cs`, which check the readout says 24/96 and 16/44.1 BIT-PERFECT and that the device goes back to Windows when STX stops. A loopback bit comparison needs a hardware loopback (an S/PDIF out wired to an in); Windows' own loopback capture hears nothing from an exclusive stream.

### ENHANCE changes only the picture on screen

Spike S6 read VLC 3.0.24's source. UPSCALE is VLC's Direct3D 11 super resolution scaler (`--d3d11-upscale-mode=super`, `modules/video_output/win32/direct3d11.c:77-80`). In `d3d11_scaler.cpp:76-123`, NVIDIA needs a 530 or later driver and its video-processor extension. For Intel, VLC says "detection doesn't work" and assumes it's there. Anywhere it isn't, VLC falls back to plain scaling. STX doesn't assume: it reads the GPU through DXGI (`src/Stx.Playback/HardwareProbe.cs`) and offers UPSCALE only on NVIDIA RTX with a 530 or later driver, and on Intel Arc or Xe (`src/Stx.Core/Playback/Enhance.cs`). On the reference UHD 620 it says UPSCALE UNAVAILABLE. SHARPEN is VLC's sharpen filter.

Both are options of the whole engine: VLC makes the video output under the player, not the file (VLC's own source, input/resource.c line 241), so a file's own options don't reach it. Turning the knob restarts LibVLC in place; the video view takes the new player, and the file carries on from where it was. The readout under the knob says what VLC's own log confirms is running.

**Not proven on hardware.** SHARPEN loading is tested in CI from VLC's log. Whether UPSCALE applies needs an RTX or Arc GPU, which wasn't available.

## The parity corpus

VideoLAN's feature list is written down in `tests/corpus/vlc-features.json`: 95 formats, codecs, subtitle formats and tags, read on 2026-11-04 for LibVLC 3.0.24. `tests/corpus/manifest.json` names one or more files for each.

109 files in all. Ten of them were added in Phase 3, so every type STX registers as a player has a file of that type (`tests/Stx.Playback.Tests/CorpusManifestTests.cs`). Most files are made by FFmpeg. Some STX made itself, in `tests/corpus/own/`: a MIDI file, a tracker module, a Kate subtitle track, and text subtitles. The rest are fetched from FFmpeg's public sample archives and pinned by sha256. Fetched files are used for testing on this machine only, and are never committed or shipped.

`tools/corpus/Make-Corpus.ps1 -Ffmpeg <path>` builds the corpus. The test `tests/Stx.Playback.Tests/CorpusTests.cs` then plays every file through STX's engine, checking that its pictures and sound decode, that its subtitles appear, and that its tags read. It runs locally, like the screenshot tests. CI checks the manifest itself (`tests/Stx.Playback.Tests/CorpusManifestTests.cs`).

**Not covered, and why:**

- **SVCD / CVD subtitles.** No public sample with them was found. The one SVCD image in FFmpeg's archive plays, but it has no subtitle stream. LibVLC ships both decoders.
- **CMML.** VideoLAN's page lists it, but LibVLC 3.0.24 ships no CMML decoder, so neither VLC nor STX shows it.
