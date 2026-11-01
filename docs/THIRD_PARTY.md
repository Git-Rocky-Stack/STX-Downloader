# Third-party tools

STX Downloader is MIT licensed. It does not include any of the tools below. When its tool setup runs, it downloads each one directly from the publisher to your own machine (`src/Stx.Tools/ToolManager.cs`), and it runs them as separate programs.

| Tool | Used for | Source | License |
|---|---|---|---|
| yt-dlp | Reading and downloading media | [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp) releases (`src/Stx.Tools/ToolVersions.cs:6`) | [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE) |
| Deno | JavaScript runtime that yt-dlp uses for full YouTube extraction | [denoland/deno](https://github.com/denoland/deno) releases (`src/Stx.Tools/ToolVersions.cs:10`) | [MIT](https://github.com/denoland/deno/blob/main/LICENSE.md) |
| FFmpeg and FFprobe | Merging streams, converting, and measuring audio | [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) release branch 9.0, `win64-gpl` (`src/Stx.Tools/FfmpegSource.cs:9`) | [GPL](https://ffmpeg.org/legal.html), because this build enables GPL components |

## Why the GPL build of FFmpeg

The GPL build includes libx264, a software H.264 encoder. The LGPL builds leave it out. FFmpeg's license applies to FFmpeg itself. Because FFmpeg is a separate program that you download from its publisher, and it is not part of this repository or linked into this app, this repository's code stays under MIT.

Each download is checked against the checksum its publisher posts, and a tool with no matching checksum is not installed.

This note describes how the project is set up. It isn't legal advice.
