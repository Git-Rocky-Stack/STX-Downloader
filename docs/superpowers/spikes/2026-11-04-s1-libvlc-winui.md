# Spike S1: can LibVLC draw inside a WinUI 3 plate?

**Date:** 2026-11-04. **Result:** all seven checks passed on route (a). **Recommended route:** VideoLAN's `LibVLCSharp.WinUI` `VideoView`.

**Tag:** `spike/s1-evidence`, commit `87cdb82`, made on the branch `spike/s1-libvlc-winui`. The branch was never merged and has been deleted; the tag keeps the commit and is pushed to `origin`. The code is `src/Stx.App/Gallery/PlayerSpike.cs`. Every screenshot and statistics log quoted below is in `docs/superpowers/spikes/s1-evidence/` at that tag (`git show spike/s1-evidence:docs/superpowers/spikes/s1-evidence/<file>`).

**Machine:** the reference laptop: Intel Core i5-8350U, 8 logical cores, Intel UHD Graphics 620, Windows 11 Pro 10.0.26300. The app is the Release build, unpackaged and self-contained (`src/Stx.App/Stx.App.csproj:9-10`).

**Test media:** both made with ffmpeg from the `testsrc2` pattern with a 440 Hz tone, 70 s each.
- `h264-1080p30.mp4`: H.264, 1920x1080, 30 fps, 8.2 Mb/s.
- `hevc-2160p60.mkv`: HEVC Main (8-bit), 3840x2160, 60 fps, 39.4 Mb/s.

## Route

Route (a) exists. VideoLAN publishes `LibVLCSharp.WinUI` 3.10.2 on NuGet. It has a `net10.0-windows10.0.19041` build and depends on Windows App SDK 1.7.250909003 or later; we're on 2.6.4. Its `VideoView` is a XAML control drawn into a `SwapChainPanel`, and LibVLC renders into that swap chain through the `--winrt-d3dcontext` and `--winrt-swapchain` options the view passes in its `Initialized` event. It passed every check, so routes (b) and (c) were not tried.

## Results

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | The picture draws inside the plate, not in a floating window | **PASS** | `ui1080b-1-inplate.png`: the picture sits inside the faceplate, below the ABOVE plate, with the screws and brushed metal around it. |
| 2 | It scrolls and clips with the rack | **PASS** | `ui1080b-3-scrolled.png`: scrolled 300 px, the picture moves up with its plate and is cut off at the top of the window. `ui1080b-4-back.png`: scrolled back. |
| 3 | A Pad over or next to the picture stays visible and clickable | **PASS (visible + XAML hit test). A real mouse click is UNVERIFIED.** | `ui1080b-1-inplate.png`: the OVER pad draws on top of the moving picture. The log line `hittest over at (851,288) inWindow=True pad@4 view@6 padAboveView=True` comes from XAML's own hit test, and it shows the pad sits above the video at the pad's centre, so a click there goes to the pad. The NEXT pad, beside the picture, does the same once it's scrolled into view: `hittest next at (81,344) inWindow=True pad@4`. See "Not proven" for why no real click was made. |
| 4 | 1080p30 H.264 and 4K60 HEVC with lost pictures under 1% over 60 s | **PASS** | See the table below. Lost pictures were 0 in every 60 s window, both in the 360 px plate and in full screen. |
| 5 | Full screen in and out works | **PASS, with a rule** | `ui1080b-5-fullscreen.png` and `ui1080b-6-out.png`. Playback continued straight through, from 20.9 s to 22.7 s in `ui1080b.log`, and `VideoView.Initialized` fired once in the whole run. **Rule:** the `VideoView` must never change parent. On the first try, the view was moved into a full-window host, `Initialized` fired again, and a new swap chain and a new LibVLC instance were needed. Playback restarted at 0:00 (`ui1080-5-fullscreen-reparented-restarted.png`, `ui1080.log`). The version that passed does full screen in place: the other plates fold away, the plate drops its padding, the view takes the window's height, and the window goes `AppWindowPresenterKind.FullScreen`. |
| 6 | Installed size of LibVLC's x64 files (settles A2) | **Measured: 111.6 MB** | `libvlc/win-x64` is 111,588,790 bytes in 422 files, covering VLC's full plugin set (access, codec, demux, d3d11, lua, stream_out, and so on). A2 guessed 80–100 MB, so it's slightly larger. Compressed with LZMA2 at maximum, the setting the installer uses (`tools/installer/STXDownloader.iss:46`), it comes to 21.2 MB; that 7z measurement is an estimate of what it adds to the installer. The .NET side adds 1.0 MB: LibVLCSharp.dll is 295 KB and SharpDX's three DLLs are 707 KB. |
| 7 | Exact versions; whether `--no-lua` is accepted | **PASS** | From the log: `libvlc version 3.0.24 Vetinari; changeset 3.0.24-rc1-0-g6de05adcba; LibVLCSharp 3.10.2.0`, and `libvlc accepted options: --winrt-d3dcontext=… --winrt-swapchain=… --no-lua`. LibVLC refuses to start on an unknown option, so being accepted is the proof. No Lua module logged anything in any run. |

### Check 4 in detail

All figures come from LibVLC's own statistics (`Media.Statistics`), sampled once a second. "60 s window" means from t=5 s to t=65 s. "CPU" is the app process's total CPU time divided by wall time and 8 cores.

| Run | Log | Decoder | Shown in 60 s | Lost in 60 s | Lost at startup (0–5 s) | CPU |
|---|---|---|---|---|---|---|
| 1080p30 H.264, 360 px plate | `perf1080.log` | D3D11VA (UHD 620) | 1,815 (30.3/s) | **0** | 0 | 1.3% |
| 1080p30 H.264, full screen | `perf1080full.log` | D3D11VA | 1,800 (30.0/s) | **0** | 0 | 1.4% |
| 4K60 HEVC, 360 px plate | `perf2160.log` | D3D11VA | 3,623 (60.4/s) | **0** | 10 | 1.9% |
| 4K60 HEVC, full screen 1920x1080 | `perf2160full.log` | D3D11VA | 3,637 (60.6/s) | **0** | 8 | 1.8% |

The decoder line in every log reads: `Using D3D11VA (Intel(R) UHD Graphics 620, vendor 8086(Intel), device 5917, revision 7) for hardware decoding`.

## Not proven

- **A real mouse click on a pad over the video.** On the first run, the driver sent real mouse clicks. Windows wouldn't bring the spike window to the front, so the clicks went to the window that was on top, Rocky's open Outlook draft. No keys were sent. The screen captures from that run were deleted, and real input was taken out of the driver. Check 3 rests on the screenshot and XAML's hit test, not on a click. To close this by hand, run the spike and click OVER, NEXT and FULL; the log records each click as `click over-pad N`.
- **What reaches the screen.** "Lost pictures" is LibVLC's count of pictures it dropped for being late. Drops in the Windows compositor are not in it.
- **Real-world sources.** The test pattern is synthetic, and the HEVC is 8-bit SDR. 10-bit HEVC, HDR, AV1, VP9, and high-bitrate camera files were not tested. Phase 1's parity corpus covers those formats.
- **That the audio is audible.** The played-audio-buffer count rose steadily (about 50 a second, `playedAudio=3062` at 65 s), but nobody listened.

## What this changes for Phase 1

These go in the Phase 1 ledger as rulings when the tasks that own them start. They are findings, not changes to the approved design.

1. **The `VideoView` is created once and never moved.** Full screen happens in place, as in check 5. This affects Task 5 (PlayerPanel and VideoSurface).
2. **LibVLC for video is created from the view's `SwapChainOptions`, after its `Initialized` event.** The engine can't make its video-capable LibVLC instance before the view exists. Task 2's `LibVlcEngine` therefore needs a way to receive those options, through a constructor argument or a factory. The headless test options stay as planned.
3. **The VideoLAN package copies x86 and ARM64 LibVLC as well**: another 202 MB of output (108 MB x86, 94 MB ARM64). Task 8 must keep only x64, through the package's `VlcWindowsX86IncludeFiles` / `VlcWindowsArm64IncludeFiles` properties, and the publish test must check that. The Lua plugin is 397 KB (`plugins/lua`). It can also be left out of the install, as well as switched off with `--no-lua`.
4. **`LibVLCSharp.WinUI` depends on SharpDX 4.2.0, which has been unmaintained since 2019.** It works today. ASSUMPTION: it keeps working on future Windows App SDK versions. This is a dependency risk to note in `THIRD_PARTY.md`.
5. **Two LibVLC errors appear in every run.** Neither stopped playback.
   - `wasapi: cannot negotiate audio format (error 0x88890008)` appears once at startup, and then audio buffers play. This matters for Phase 3 (bit-perfect WASAPI), which has to find out why the first format is refused.
   - `main: Failed to set on top` comes from the video output's always-on-top request. It has no visible effect inside a swap chain. UNVERIFIED cause.
6. **The faceplate's corner screws show in full screen.** This is cosmetic: the real plate needs a full-screen visual state with no screws or border.

## Decision needed

Route (a) passes. The recommendation is to start Task 2 on `LibVLCSharp.WinUI` 3.10.2 with `VideoLAN.LibVLC.Windows` 3.0.24, with findings 1–6 above added to Phase 1. Nothing past this report is built until Rocky says go.
