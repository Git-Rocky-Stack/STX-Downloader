# Third-party tools

STX Downloader is MIT licensed. It does not include any of the tools below. When its tool setup runs, it downloads each one directly from the publisher to your own machine (`src/Stx.Tools/ToolManager.cs`), and it runs them as separate programs.

| Tool | Used for | Source | License |
|---|---|---|---|
| yt-dlp | Reading and downloading media | [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp) releases (`src/Stx.Tools/ToolVersions.cs:6`) | [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE) |
| Deno | JavaScript runtime that yt-dlp uses for full YouTube extraction | [denoland/deno](https://github.com/denoland/deno) releases (`src/Stx.Tools/ToolVersions.cs:10`) | [MIT](https://github.com/denoland/deno/blob/main/LICENSE.md) |
| FFmpeg and FFprobe | Merging streams, converting, and measuring audio | [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) release branch 9.0, `win64-gpl` (`src/Stx.Tools/FfmpegSource.cs:9`) | [GPL](https://ffmpeg.org/legal.html), because this build enables GPL components |

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php), under [its own license](https://jrsoftware.org/files/is/license.txt).

## Coming with the media player (in development, not in any release yet)

The player in development (`docs/PLAYBACK.md`) will ship these inside the app. Unlike the tools above, they won't be downloaded on first run. The build fetches them from their publishers and checks each against the sha256 pinned in `tools/vlc/engine.json`.

| Component | Used for | Source | License |
|---|---|---|---|
| VLC 3.0.24 engine (libvlc, libvlccore and plugins) | Playing media | The official [VLC 3.0.24 win64 zip](https://download.videolan.org/vlc/3.0.24/win64/) from VideoLAN | [LGPL-2.1-or-later](https://code.videolan.org/videolan/vlc/-/blob/3.0.x/COPYING.LIB), dynamically linked. Some plugins are GPL, as in VLC itself. |
| LibVLCSharp 3.10.2 | .NET access to LibVLC | [NuGet](https://www.nuget.org/packages/LibVLCSharp/3.10.2) (`src/Stx.Playback/Stx.Playback.csproj`) | [LGPL-2.1-or-later](https://code.videolan.org/videolan/LibVLCSharp/-/blob/3.x/LICENSE) |
| FluidR3_GM SoundFont, by Frank Wen | Playing MIDI files | Debian's [fluid-soundfont-gm 3.1-5.3](https://packages.debian.org/source/sid/fluid-soundfont) package | MIT. The copyright file ships beside it. |

VLC's two DVD plugins (`libdvdnav`, `libdvdread`) have libdvdcss built in, which decrypts CSS-protected DVDs. STX leaves both plugins out of the engine it ships (`tools/vlc/engine.json`), so it can't decrypt a DVD. The README's no-DRM rule applies to playback too.

## Why the GPL build of FFmpeg

The GPL build chosen in `src/Stx.Tools/FfmpegSource.cs:9` includes libx264, a software H.264 encoder. The LGPL builds leave it out. FFmpeg's license applies to FFmpeg itself. Because FFmpeg is a separate program that you download from its publisher, and it is not part of this repository or linked into this app, this repository's code stays under MIT.

Each download is checked against the checksum its publisher posts, and a tool with no matching checksum is not installed.

This note describes how the project is set up. It isn't legal advice.
