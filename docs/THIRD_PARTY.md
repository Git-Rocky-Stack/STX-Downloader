# Third-party software

STX Downloader's own code is MIT licensed. Some third-party software ships inside the app; other tools are downloaded on first run.

## Shipped inside the app

The player uses VLC's engine. The build fetches each item below from its publisher and checks it against the sha256 pinned in `tools/vlc/engine.json`; a download with any other hash is thrown away (`tools/vlc/Get-Engine.ps1`).

| Component | Used for | Source | License |
|---|---|---|---|
| VLC 3.0.24 engine (libvlc, libvlccore and plugins) | Playing media | The official [VLC 3.0.24 win64 zip](https://download.videolan.org/vlc/3.0.24/win64/) from VideoLAN | [LGPL-2.1-or-later](https://code.videolan.org/videolan/vlc/-/blob/3.0.x/COPYING.LIB), dynamically linked. Some plugins are GPL, as in VLC itself. |
| LibVLCSharp and LibVLCSharp.WinUI 3.10.2 | .NET access to LibVLC, and the video view the player draws into | NuGet ([LibVLCSharp](https://www.nuget.org/packages/LibVLCSharp/3.10.2), [LibVLCSharp.WinUI](https://www.nuget.org/packages/LibVLCSharp.WinUI/3.10.2)) | [LGPL-2.1-or-later](https://code.videolan.org/videolan/LibVLCSharp/-/blob/3.x/LICENSE) |
| SharpDX 4.2.0 | Direct3D access for LibVLCSharp.WinUI's video view | NuGet, as a dependency of LibVLCSharp.WinUI | [MIT](https://github.com/sharpdx/SharpDX/blob/master/LICENSE). SharpDX has been unmaintained since 2019. STX uses it only through LibVLCSharp.WinUI's video view. |
| NAudio.Wasapi and NAudio.Core 2.2.1 | BIT-PERFECT: holding the sound device exclusively and handing it PCM (`src/Stx.Playback/ExclusiveRenderer.cs`) | NuGet ([NAudio.Wasapi](https://www.nuget.org/packages/NAudio.Wasapi/2.2.1)) | [MIT](https://github.com/naudio/NAudio/blob/master/license.txt) |
| FluidR3_GM SoundFont, by Frank Wen | Playing MIDI files | Debian's [fluid-soundfont-gm 3.1-5.3](https://packages.debian.org/source/sid/fluid-soundfont) package, shipped as SoundFont 3 (`FluidR3_GM.sf3`): the same font with its samples compressed in Ogg Vorbis at build time (`tools/vlc/Make-Sf3.cs`, with a pinned FFmpeg build that isn't shipped), 148 MB down to 27 MB | MIT. The copyright file ships beside it (`libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt`). The conversion changes the format, not the licence. |

Some of VLC's plugins aren't shipped, each with its reason in `tools/vlc/engine.json`: the ones that would open network ports, run site scripts or show VLC's own windows. VLC's two DVD plugins (`libdvdnav`, `libdvdread`) do ship, for unencrypted DVDs (Rocky's decision for Phase 4). Both have libdvdcss built in, which can decrypt CSS-protected DVDs. **STX doesn't use it**: before any disc, disc folder or disc image reaches LibVLC, STX reads the disc's own CSS and AACS markings and refuses an encrypted one (`src/Stx.Core/Playback/DiscCheck.cs`). The README's no-DRM rule applies to playback too. No AACS or BD+ library ships, so libbluray reads only unencrypted Blu-ray discs.

## Downloaded on first run

None of these are included in the app. When its tool setup runs, it downloads each one directly from the publisher to your own machine (`src/Stx.Tools/ToolManager.cs`), and it runs them as separate programs.

| Tool | Used for | Source | License |
|---|---|---|---|
| yt-dlp | Reading and downloading media | [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp) releases (`ToolVersions.YtDlpExe`) | [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE) |
| Deno | JavaScript runtime that yt-dlp uses for full YouTube extraction | [denoland/deno](https://github.com/denoland/deno) releases (`ToolVersions.DenoZip`) | [MIT](https://github.com/denoland/deno/blob/main/LICENSE.md) |
| FFmpeg and FFprobe | Merging streams, converting, measuring audio, and decoding lossless music for BIT-PERFECT | [FFmpeg-Builds/FFmpeg-Builds](https://github.com/FFmpeg-Builds/FFmpeg-Builds) (formerly BtbN/FFmpeg-Builds) release branch 9.0, `win64-gpl` (`FfmpegSource.ZipName`) | [GPL](https://ffmpeg.org/legal.html), because this build enables GPL components |

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php), under [its own license](https://jrsoftware.org/files/is/license.txt).

## Why the GPL build of FFmpeg

The GPL build chosen in `FfmpegSource` includes libx264, a software H.264 encoder. The LGPL builds leave it out. FFmpeg's license applies to FFmpeg itself. Because FFmpeg is a separate program that you download from its publisher, and it is not part of this repository or linked into this app, this repository's code stays under MIT.

Each download is checked against the checksum its publisher posts, and a tool with no matching checksum is not installed.

This note describes how the project is set up. It isn't legal advice.
