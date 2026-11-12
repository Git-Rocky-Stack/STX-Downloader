# Third-party software

STX Downloader's own code is MIT licensed. Some third-party software ships inside the app; the setup installs the tools it runs beside it.

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

### The runtime and libraries

The app carries its own .NET and its own Windows App SDK, so the setup needs nothing else installed and asks for no administrator (`Stx.App.csproj`, `SelfContained` and `WindowsAppSDKSelfContained`; `tools/Publish.ps1` refuses a release without them). The build takes each item below from NuGet. .NET's patch is the newest the SDK knows when the release is built, so .NET's security fixes reach users with the next STX release, not through Windows Update.

| Component | Used for | Source | License |
|---|---|---|---|
| .NET 10 runtime (10.0.14 in 1.1.0) | Running the app | [Microsoft.NETCore.App.Runtime.win-x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64) on NuGet, from Microsoft | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| Windows App SDK 2.6.4 | WinUI 3: the window, its controls and text | [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.4) on NuGet, from Microsoft | [Microsoft Software License Terms](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.4/License), which allow shipping it inside an app |
| CommunityToolkit.Mvvm 8.4.0 | The app's view models | [NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.0) | [MIT](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| Microsoft.Data.Sqlite 10.0.14, with SQLitePCLRaw 2.1.13 and SQLite | The queue and the Library's database | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14) | [MIT](https://github.com/dotnet/efcore/blob/main/LICENSE.txt); SQLitePCLRaw [Apache-2.0](https://github.com/ericsink/SQLitePCL.raw/blob/master/LICENSE.TXT); SQLite is [public domain](https://sqlite.org/copyright.html) |

## The tools, installed by the setup

The setup installs each of these beside the app's data, in `%LOCALAPPDATA%\STXDownloader\tools` (`tools/installer/STXDownloader.iss`). The release build fetches them from the addresses the app's own updater uses, and ships one only when it matches the SHA-256 its publisher posts and starts and reports its version (`tools/Publish.ps1`). The app runs them as separate programs. Their licenses, and a list of the exact builds with their checksums and where their source is, are installed in `docs\licenses` in the install folder (`BUNDLED-TOOLS.txt`).

The zip carries the app alone. When the app finds a tool missing, as on the zip's first run, it downloads that tool directly from its publisher (`src/Stx.Tools/ToolManager.cs`), held to the same checksum. While TOOL UPDATES is on, it also replaces yt-dlp with each new release, and the setup never puts an older one back.

| Tool | Used for | Source | License |
|---|---|---|---|
| yt-dlp | Reading and downloading media | [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp) releases (`ToolVersions.YtDlpExe`) | [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE) |
| Deno | JavaScript runtime that yt-dlp uses for full YouTube extraction | [denoland/deno](https://github.com/denoland/deno) releases (`ToolVersions.DenoZip`) | [MIT](https://github.com/denoland/deno/blob/main/LICENSE.md) |
| FFmpeg and FFprobe | Merging streams, converting, measuring audio, and decoding lossless music for BIT-PERFECT | [FFmpeg-Builds/FFmpeg-Builds](https://github.com/FFmpeg-Builds/FFmpeg-Builds) (formerly BtbN/FFmpeg-Builds) release branch 9.0, `win64-gpl` (`FfmpegSource.ZipName`) | [GPL](https://ffmpeg.org/legal.html), because this build enables GPL components |

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php), under [its own license](https://jrsoftware.org/files/is/license.txt).

## Why the GPL build of FFmpeg

The GPL build chosen in `FfmpegSource` includes libx264, a software H.264 encoder. The LGPL builds leave it out. FFmpeg's license applies to FFmpeg itself. FFmpeg is a separate program that STX starts; it is not part of this repository and not linked into this app, so this repository's code stays under MIT.

Because the setup passes FFmpeg on, it carries what the GPL asks of whoever does: the license text, taken from the very archive the programs come from (`FFmpeg-LICENSE.txt`), and directions to the corresponding source, with the build's exact FFmpeg commit from its own version line and the repository of the scripts and libraries it was built from (`BUNDLED-TOOLS.txt`).

Each tool is checked against the checksum its publisher posts, and a tool with no matching checksum is neither shipped nor installed.

This note describes how the project is set up. It isn't legal advice.
