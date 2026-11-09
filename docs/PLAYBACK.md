# The playback engine

STX plays media with VLC's engine, LibVLC, through LibVLCSharp. The engine is in `src/Stx.Playback/`, and the PLAYER plate that uses it is `src/Stx.App/Panels/PlayerPanel.cs`, in the rack by `MainWindow`.

## Which VLC STX ships

STX ships the **official VLC 3.0.24 build for 64-bit Windows**, the same zip VideoLAN publishes for VLC itself. It doesn't use VideoLAN's NuGet package (`VideoLAN.LibVLC.Windows`).

- **Why the official build:** the NuGet package is a smaller build. It has 321 plugins where VLC 3.0.24 has 360. Among those missing are the AC3 packetizer, so raw `.ac3` and `.eac3` files didn't play, and webcam capture, the Real, Musepack and SID demuxers, the FAAD and MAD decoders, the high-quality resampler, and the deblocking filter. STX's promise is to play what VLC plays, so it takes VLC's own build.
- **Pinned:** the download URL and its sha256 are in `tools/vlc/engine.json`. The hash matches the `.sha256` file VideoLAN publishes beside the zip. A download with any other hash is thrown away (`tools/vlc/Get-Engine.ps1`).
- **Built in:** building `src/Stx.Playback` runs `tools/vlc/Get-Engine.ps1` once (`src/Stx.Playback/Stx.Playback.csproj`, target `StxPrepareEngine`). That copies the engine beside every program that uses it, under `libvlc\` (target `StxEngineFiles`).
- **Left out:** only `libvlc.dll`, `libvlccore.dll` and the plugins ship. Plugins that would open network ports, run scripts or show VLC's own windows are removed, each with its reason in `tools/vlc/engine.json`: the interface, remote control, Lua, service discovery, streaming out, and the Last.fm and RTSP server modules. VLC's plugin cache is rebuilt afterwards.
- **No DVD decryption:** VLC's two DVD plugins (`access/libdvdnav_plugin.dll`, `access/libdvdread_plugin.dll`) ship for unencrypted DVDs. They have libdvdcss built in, which can decrypt CSS-protected DVDs, so STX checks every disc first and refuses an encrypted one before LibVLC is given it (`src/Stx.Core/Playback/DiscCheck.cs`, `src/Stx.Core/Playback/PlaybackTarget.cs`). Lua is switched off when the engine starts (`LibVlcOptions.For`).

## What STX does differently from VLC, and why

### Dirac and VC-2 video are refused

VLC 3.0.24's engine crashes the whole program on Dirac / VC-2 video. That happens in the official desktop build too, with a real video window, and with a genuine Dirac sample from FFmpeg's archive as well as one made by FFmpeg. If STX passed such a file to the engine, STX would close.

So STX reads a file's tracks before playing it. If any video track is Dirac, it doesn't play the file. It shows **DIRAC VIDEO CAN'T BE PLAYED SAFELY** instead (`LibVlcEngine.DiracRefused`, `LibVlcEngine.Inspect`), and the rest of the console keeps working. When LibVLC can't finish reading a file's tracks (a slow share, a parse that times out or fails), STX doesn't take the file as safe: it reads the first megabyte itself for Dirac's sequence header, `BBCD` and parse code 0, which FFmpeg writes within the first few kilobytes in MKV, MOV, MP4, MPEG-TS, NUT and AVI (`src/Stx.Core/Playback/DiracSniff.cs`; added 2026-11-07). An http(s) stream is read too (added later on 2026-11-07): LibVLC reads local files' tracks only, so before a stream that may carry video is given to it, STX reads its start, up to a megabyte or 2 seconds, and refuses Dirac with the same message (`DiracSniff.InStreamAsync`, `LibVlcEngine.Open`). Sound isn't read (its server says it's sound, or its address names a sound file, which isn't even asked), nor are playlists, manifests (HLS, DASH) or pages; a stream slower than that reaches the player with what arrived checked. RTSP, RTMP and MMS aren't read; Dirac isn't carried that way in practice.

**Why no engine setting avoids it** (2026-11-07, a probe that plays a file in a child process): the crash is FFmpeg's decoder inside VLC's `avcodec` plugin, `Assertion frame->data[i] failed at src/libavcodec/decode.c:1628`, then abort. It happened with the Dirac demuxer or packetizer taken out of the engine, with one decoding thread, and with hardware decoding off, on two Dirac files ten runs each; never on H.264.

Dirac is rare. It's mostly found in BBC archive material. VLC 3.0.24 doesn't play it safely either.

### MIDI plays with a bundled SoundFont

VLC plays MIDI through FluidSynth, which needs a SoundFont. VLC doesn't ship one, and Windows' built-in `gm.dls` can't be loaded by it. STX ships **FluidR3_GM** by Frank Wen, under the MIT license. Its copyright file is shipped beside it, as `libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt`. STX passes it to the engine when the engine starts (`LibVlcEngine.ShippedSoundFont`, `LibVlcOptions.For`).

**It ships as SoundFont 3** (2026-11-07): `FluidR3_GM.sf3`, the same font with each of its 1,418 samples compressed in Ogg Vorbis, 148 MB down to 27 MB. The build makes it from the pinned SoundFont 2 (`tools/vlc/Make-Sf3.cs`, with an FFmpeg build `tools/vlc/engine.json` pins; the result's sha256 is pinned too, so a change to either fails the build). The format is FluidSynth 2's own (`fluid_sffile.c`, `fluid_defsfont.c`): version 3, each sample its own Ogg stream flagged `0x10`, its start and end byte offsets into the sample chunk, its loop points counted from the sample's start. VLC 3.0.24's FluidSynth reads it: its plugin carries libsndfile and libVorbis. **Checked by ear's proxy:** a ten-second MIDI file (piano, strings, hi-hats, and organ, flute and strings held eight seconds so their loops sound) rendered through VLC with the SF2 and with the SF3 came out with a waveform correlation of 0.9998, the difference 33 dB below the music, and each second's level within 0.1 dB; an SF3 with its loop points left as SF2 has them correlated 0.30. `tests/Stx.Playback.Tests/MidiTests.cs` holds the shipped SF3 to the SF2's levels.

### Mono sound reaches WASAPI in stereo

Windows refuses a one-channel stream in WASAPI shared mode (`0x88890008`, `AUDCLNT_E_UNSUPPORTED_FORMAT`), and LibVLC 3.0.24 then falls back to DirectSound. For mono files, STX starts muted. When the output lands on DirectSound, it switches the output to stereo, which reopens it on WASAPI, and then lets the sound through (`LibVlcEngine.Inspect`, `LibVlcEngine.Open`). Files with 2, 6 or 8 channels keep their own layout, so surround is never downmixed. LibVLC still logs Windows' first refusal once for each mono file.

### Discs start at the main title, and encrypted ones are refused first

Discs, disc folders and disc images go to VLC's DVD and Blu-ray readers as `dvd:///` or `bluray:///` addresses (`src/Stx.Core/Playback/PlaybackTarget.cs`). One on a network share keeps the share's two leading slashes, `dvd://///nas/Films/My%20Film/`: the plain form reached VLC's DVD reader as `nas\Films\...`, a path on the current drive, so a disc on a share never opened. Checked on 2026-11-07 against LibVLC 3.0.24 with a disc folder reached as `\\localhost\C$\...`; the reader then read the folder's `VIDEO_TS.IFO`, as it does for the same folder on C: (`tests/Stx.Playback.Tests/LibVlcEngineTests.cs`, Category=Network). Before that, STX checks the disc itself (`src/Stx.Core/Playback/DiscCheck.cs`):

- **DVD:** a sample of sectors spread through every VOB file (or through the image) is read. CSS sets the PES scrambling bits on every sector it scrambles; an unencrypted disc leaves them clear.
- **Blu-ray:** an `AACS` or `BDSVM` folder, which every protected disc carries, or AACS's copy-permission bits at the start of an m2ts's aligned units. In an image, those names are looked for in its directory area.
- A sector that can't be read counts as encrypted: a drive refuses to read CSS sectors it hasn't unlocked, and when in doubt STX doesn't play.

The check runs inside the engine, so nothing reaches VLC's readers unchecked, whether it came from PLAY DISC, OPEN, File Explorer or a watched folder. Discs start at the main title (`:no-dvdnav-menu`, `:no-bluray-menu`): their menus need a pointer on the picture, and a swap chain doesn't pass one back. No AACS or BD+ library ships, so libbluray reads only unencrypted Blu-rays.

**Not tested on a real disc.** No DVD or Blu-ray authoring tool was available where Phases 2 to 7 were built, so unencrypted disc playback hasn't been seen working. The refusal is tested on sectors built in the tests, as the spec asks: no encrypted media is in the repository.

### Streams connect out only

PLAY STREAM plays an address (`src/Stx.ViewModels/StreamOpener.cs`). Adaptive streams (`.m3u8`, `.mpd`) and direct media play as they are. A radio playlist (`.pls`, `.m3u`, `.xspf`) is read by STX, which plays its first stream and keeps the rest for NEXT. An address that doesn't say what it is gets its headers asked for, and nothing more. A web page is sent to ANALYZE. UDP, RTP and SAP would need STX to listen on a port, so they're refused (`src/Stx.Core/Playback/PlaybackTarget.cs`), and RTSP is told to stay on its one TCP connection (`:rtsp-tcp`). A stream that fails says which way: **THAT STREAM COULDN'T BE OPENED** if nothing played, **THE STREAM WAS CUT OFF** if it had been playing (`LibVlcEngine.StreamUnreachable`, `LibVlcEngine.StreamCutOff`; added 2026-11-07, when it said "THIS FILE COULDN'T BE PLAYED", or nothing).

### BIT-PERFECT doesn't use VLC's audio output for music

Spike S5 read VLC 3.0.24's source:

- `modules/audio_output/wasapi.c:546-548` opens plain PCM in **shared** mode, always. Exclusive mode is used only for S/PDIF and HDMI passthrough.
- `modules/audio_output/amem.c:191-193`, the audio callbacks an app could take PCM from, refuses anything but 16-bit (`strcmp(format, "S16N") /* TODO: amem-format */`). A 24-bit file would arrive already cut to 16 bits.

So, with Rocky's choice:

- **Lossless music** (FLAC, ALAC, WavPack, APE, TTA, WAV/AIFF PCM) is decoded by the FFmpeg STX already downloads. FFmpeg's raw PCM goes to STX's own renderer (`src/Stx.Playback/ExclusiveRenderer.cs`, on NAudio's WASAPI layer), which holds the default device exclusively at the file's own rate. The device layouts offered never cut a bit: 16-bit can go out as 16, 24 or 32, and 24-bit as 24-in-32, packed 24 or 32. A wider container only pads with zeros (`src/Stx.Core/Playback/BitPerfect.cs`). A lossless decoder gives the same samples whoever wrote it. The tests prove this sample by sample with the real FFmpeg (`tests/Stx.Core.Tests/BitPerfectTests.cs`).
- **Films** send AC3, E-AC3, DTS and TrueHD to the receiver through VLC's exclusive passthrough (`--mmdevice-passthrough=2`). VLC reads that once, when it starts, so it follows the switch from the next launch.
- **Everything else** plays shared, and the readout says why: a lossy file has no original samples to keep; a film's PCM can't leave VLC's engine unaltered; the device didn't take the format; another app holds it.
- **A film's PCM stays shared, by Rocky's choice (2026-11-05).** The spec's route sent it through VLC's audio callbacks into exclusive mode. VLC 3's callback output gives VLC no timing back (`amem.c` sets `aout->time_get = NULL`), so VLC's drift correction never runs for it (`src/audio_output/dec.c`, `aout_DecSynchronize`, which returns when timing is unknown). The picture would keep VLC's clock and the sound the device's, and the two would drift apart over a film; the only cures, dropping or repeating samples or resampling, would end bit-perfect anyway. The callbacks also take 16-bit only, so a 24-bit soundtrack couldn't go that way at all. (Read from VLC's 3.0.x branch, not the 3.0.24 tag.)

**On hardware (reference PC, 2026-11-05).** The Device-category tests in `tests/Stx.Playback.Tests/BitPerfectEngineTests.cs` held its Realtek exclusively: 16/48 and 24/48 BIT-PERFECT, the device back with Windows after STOP, a track ending, seeking while held, and letting go just as a track ends. That Realtek takes 48 kHz only in exclusive mode (asked of it directly with NAudio, 44.1 to 192 kHz), so the 16/44.1 and 24/96 rows play shared there and say "THE DEVICE CAN'T TAKE … KHZ AS IT IS"; they need a DAC that takes those rates. Tests that open the device run one at a time (`tests/Stx.Playback.Tests/SoundDeviceCollection.cs`): a bit-perfect test holding it had pushed a shared one onto DirectSound. Still not proven: a loopback bit comparison, which needs a hardware loopback (an S/PDIF out wired to an in), because Windows' own loopback capture hears nothing from an exclusive stream.

**On a second PC (2026-11-07):** a Core Ultra 7 258V's "Speakers (SoundWire Audio)", on Intel Smart Sound Technology, take 44.1 and 96 kHz exclusively, and all 13 Device tests pass there, the **16/44.1 and 24/96 BIT-PERFECT** rows included: the first time those rates reached a device bit-perfect. New the same day, each break-checked on that device: disposing the renderer twice, a decode that dies before the end (FAILED with a message, not ENDED), and a pump left in a stalled read, which before the fix wrote 173,760 frames beside the next run.

### ENHANCE changes only the picture on screen

Spike S6 read VLC 3.0.24's source. UPSCALE is VLC's Direct3D 11 super resolution scaler (`--d3d11-upscale-mode=super`, `modules/video_output/win32/direct3d11.c:77-80`). In `d3d11_scaler.cpp:76-123`, NVIDIA needs a 530 or later driver and its video-processor extension. For Intel, VLC says "detection doesn't work" and assumes it's there. Anywhere it isn't, VLC falls back to plain scaling. STX doesn't assume: it reads the GPU through DXGI (`src/Stx.Playback/HardwareProbe.cs`) and offers UPSCALE only on NVIDIA RTX with a 530 or later driver, and on Intel Arc or Xe (`src/Stx.Core/Playback/Enhance.cs`). On the reference UHD 620 it says UPSCALE UNAVAILABLE. SHARPEN is VLC's sharpen filter.

Both are options of the whole engine: VLC makes the video output under the player, not the file (VLC's own source, input/resource.c line 241), so a file's own options don't reach it. Turning the knob restarts LibVLC in place; the video view takes the new player, and the file carries on from where it was. The readout under the knob says what VLC's own log confirms is running.

**Seen on hardware (2026-11-07).** SHARPEN loading is tested in CI from VLC's log. UPSCALE was seen running on an Intel Arc 140V: VLC's log says "Using Super Resolution scaler" with the player on screen (`tests/Stx.VisualTests/EnhanceSeenTests.cs`, which skips on a GPU without super resolution). Not seen on an RTX.

### HDR10 reaches an HDR screen as HDR

On a screen with Windows' HDR on (seen 2026-11-07 on a laptop panel, 10 bits per channel), VLC's Direct3D 11 output, drawing into STX's WinUI swap chain, offers the screen's Rec.2020 colour spaces and chooses Rec.2020 with the PQ curve (SMPTE ST 2084) for an HDR10 file, in P010 at 10 bits, so HDR video isn't flattened to SDR. `tests/Stx.VisualTests/HdrTests.cs` checks VLC's own log for that choice, and skips on a screen that isn't in HDR mode. Whether the picture's brightness matches another player's wasn't measured.

## The parity corpus

VideoLAN's feature list is written down in `tests/corpus/vlc-features.json`: 95 formats, codecs, subtitle formats and tags, read on 2026-11-04 for LibVLC 3.0.24. `tests/corpus/manifest.json` names one or more files for each.

109 files in all. Ten of them were added in Phase 3, so every type STX registers as a player has a file of that type (`tests/Stx.Playback.Tests/CorpusManifestTests.cs`). Most files are made by FFmpeg. Some STX made itself, in `tests/corpus/own/`: a MIDI file, a tracker module, a Kate subtitle track, and text subtitles. The rest are fetched from FFmpeg's public sample archives and pinned by sha256. Fetched files are used for testing on this machine only, and are never committed or shipped.

`tools/corpus/Make-Corpus.ps1 -Ffmpeg <path>` builds the corpus. The test `tests/Stx.Playback.Tests/CorpusTests.cs` then plays every file through STX's engine, checking that its pictures and sound decode, that its subtitles appear, and that its tags read. It runs locally, like the screenshot tests. CI checks the manifest itself (`tests/Stx.Playback.Tests/CorpusManifestTests.cs`).

**Not covered, and why:**

- **SVCD / CVD subtitles.** No public sample with them was found. The one SVCD image in FFmpeg's archive plays, but it has no subtitle stream. LibVLC ships both decoders.
- **CMML.** VideoLAN's page lists it, but LibVLC 3.0.24 ships no CMML decoder, so neither VLC nor STX shows it.
