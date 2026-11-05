# Player, Phase 1: report

**Date:** 2026-11-04. **Plan:** `docs/superpowers/plans/2026-11-04-player-phase1.md`. **Spec:** `docs/superpowers/specs/2026-11-04-vlc-player-design.md`. **Spike:** `docs/superpowers/spikes/2026-11-04-s1-libvlc-winui.md`.

Every number below was measured on the reference machine: i5-8350U (8 logical cores), UHD 620, Windows 11 Pro 10.0.26300.

## What Phase 1 delivers

- **VLC's engine.** The official VLC 3.0.24 win64 build, pinned by sha256 (`tools/vlc/engine.json`). It ships without the plugins that open network ports, run Lua site scripts, show VLC's own windows, or decrypt DVDs.
- **A PLAYER plate** in the deck's right column, above Source Truth (`src/Stx.App/MainWindow.cs:57`). It has:
  - PREV, PLAY/PAUSE, NEXT, STOP and FULL;
  - a keyboard- and screen-reader-usable position bar;
  - the time and a VOLUME knob;
  - track pickers when a file has more than one choice.
  Music shows its title on glass.
- **Full screen in place.** It folds every other plate away, makes the picture fill the window, and puts the controls in one row over the picture. Esc or EXIT returns, and playback never restarts.
- **OPEN plays inside STX** (`src/Stx.ViewModels/LibraryViewModel.cs:48`).
- **Files from File Explorer** join the Library and play. A second launch hands its file to the copy already running, over a per-user named pipe (`src/Stx.App/App.xaml.cs:27`, `src/Stx.App/Platform/SingleInstance.cs:26`).
- **PERFORMANCE** in Settings: AUTO, ECO, BALANCED or MAX. It applies from the next file.
- **Hidden in the tray,** the player stops decoding pictures and the sound carries on.
- **Dirac / VC-2 is refused** with "DIRAC VIDEO CAN'T BE PLAYED SAFELY". MIDI plays with the bundled FluidR3_GM SoundFont.
- **Docs.** `docs/PLAYBACK.md`, `docs/THIRD_PARTY.md`, `docs/PERFORMANCE.md`, `README.md` ("Playing", every line anchored), `CHANGELOG.md`, and the in-app guide (a Player section, including the Dirac line).

## Spike S1 findings: where each was closed

| # | Finding | Closed by | Test |
|---|---|---|---|
| F1 | The video view must never change parent | Full screen in place (`PlayerPanel.SetFullScreen`, `MainWindow` fold) | `Full_screen_keeps_playing_without_a_restart` |
| F2 | LibVLC must start from the view's swap chain | `LibVlcEngine.UseSwapChain`; an open is held until the swap chain exists | `Open_without_a_surface_waits_for_one`, `Swap_chain_options_are_appended` |
| F3 | The package copied x86, ARM64 and Lua | The official build, x64 only, Lua removed (`tools/vlc/engine.json`) | `Only_x64_libvlc_ships_and_no_lua`, `Nothing_that_opens_ports_or_runs_scripts_ships` |
| F4 | SharpDX has been unmaintained since 2019 | Stated in `docs/THIRD_PARTY.md` | n/a (documentation) |
| F5 | WASAPI refused the format at every start | Root cause: Windows refuses a **one-channel** shared-mode stream. Mono now goes to WASAPI in stereo; surround is untouched | `Mono_audio_plays_through_wasapi_in_stereo`, `Surround_is_not_downmixed` |
| F6 | "Failed to set on top" | Root cause in VLC 3.0: `display.c:1171-1173` and `win32/common.c:697-699`. It's logged once for every windowless output, and no option avoids it. It's classified as harmless | `The_video_output_logs_no_errors` |
| F7 | Screws and border in full screen | `Faceplate.Bare` | Baseline `player-full-dark`, approved by Rocky |
| N1 | No real click had been made | A real click on PAUSE over the picture in full screen. The window is made topmost first, and the test checks the point is STX's own before clicking | `A_real_click_on_a_pad_over_the_video_lands` |
| N2 | Compositor drops weren't measured | 4K60 HEVC full screen for 60 s: LibVLC's lost pictures and DWM's dropped plus missed frames are each under 1% | `Full_screen_4K60_loses_under_1_percent` |
| N3 | Only synthetic 8-bit SDR had been played | The corpus has HEVC Main10, HDR10, AV1 and VP9 | `Real_world_profiles_are_present`, `Every_corpus_file_plays` |
| N4 | Audio was counted, not heard | The default output device's peak meter rises while STX plays a tone, after first checking the PC was quiet | `Audio_reaches_the_output_device` |
| P1 | PERFORMANCE needed a rebuilt engine | The profile travels with each file; nothing is rebuilt | `Media_options_carry_the_profile`, `Hardware_decoding_follows_the_profile` |

## Rocky's decisions during Phase 1

- **D1:** ship the official VLC 3.0.24 build instead of VideoLAN's NuGet package. The NuGet package has 321 plugins against VLC's 360; without the AC3 packetizer, raw `.ac3` and `.eac3` didn't play.
- **D2:** refuse Dirac/VC-2 with a plain message, and say so in the docs, release notes and guide. VLC 3.0.24's own desktop engine crashes on Dirac too.
- **D3:** ship a free General MIDI SoundFont, with the license checked first. FluidR3_GM is MIT, from the copyright file in Debian's package. GeneralUser GS was rejected because its samples' origins aren't fully known.
- Zero deferrals: every S1 finding and "not proven" item got a task and a test.

## Measurements

**Installer.**
- 195 MB, up from 48.5 MB on 2026-11-01. That earlier build predates the 9.5 MB tutorial video too.
- Compressed with LZMA2 at maximum, the installer's setting: the SoundFont is about 112 MB and the engine about 21 MB.
- Unpacked: the release is 431 MB, of which the engine is 107 MB and the SoundFont 141.5 MB.
- Taken from `tools/Publish.ps1`'s own output, 2026-11-04:

```
...\dist\STXDownloader-1.0.0-win-x64.zip  zip 250 MB, unpacked 431 MB
...\dist\STXDownloader-1.0.0-setup.exe  195 MB
```

**Idle budgets with the player in the rack, nothing playing.** `tools/Measure-Budgets.ps1 -Assert`, every run exiting 0:

```
idle     dark 207.60     97.90        99.00   0.01     0.00 0/10         False
idle     light 209.40     99.00        99.20   0.01     0.00 0/10         False
hidden   dark 208.70     98.70        99.00   0.01     0.00 0/10         False
```

Private memory rose by about 19 MB over 2026-11-01's 79 MB, which is LibVLC being ready. The budget is 150 MB.

**4K60 HEVC full screen.**
- 3,891 pictures shown.
- 12 lost in the whole run, all at the start, outside the measured minute.
- D3D11VA hardware decoding.
- DWM's dropped plus missed frames were under 1% (asserted by the test).

## Final verification (2026-11-04)

```
Look: Passed!  - Failed:     0, Passed:   177
Art: Passed!  - Failed:     0, Passed:    69
Core: Passed!  - Failed:     0, Passed:   268
Data: Passed!  - Failed:     0, Passed:    30
Tools: Passed!  - Failed:     0, Passed:   190
Audio: Passed!  - Failed:     0, Passed:    68
Bridge: Passed!  - Failed:     0, Passed:    56
App: Passed!  - Failed:     0, Passed:   204
Playback: Passed!  - Failed:     0, Passed:    31
Corpus: Passed!  - Failed:     0, Passed:     1
App build: 0 Warning(s) 0 Error(s)
Visual: Failed!  - Failed:     2, Passed:   101   (the two: BaselineTests settings dark/light; the new PERFORMANCE knob changed that plate, and its new images wait for approval)
Extension: pass 57 fail 0
Sweep: CLEAN — 204 source files, no findings.
```

After the final review's fix pass:
- Playback: 31 of 31, now with the new `Dispose_right_after_open_is_safe` test.
- Corpus: passed again.
- Player visual tests and player accessibility scan: 10 of 10.

**Baselines approved and made repeatable.** Rocky approved the seven new images. Two fixes made their comparisons pass every time:
- **The held frame.**
  - Before: the player galleries paused after the first picture and jumped back to the start. That landed on a different frame each run.
  - Now: they open the file paused at its start and step one frame, so every capture is the first frame (`src/Stx.App/Gallery/PlayerGallery.cs:53`, `src/Stx.Playback/LibVlcEngine.cs:53`).
  - Rocky re-approved `player-light` and `player-full-dark`, which had shown a later frame.
- **Black captures.**
  - The problem: a window that hasn't drawn a frame yet captures as pure black. This hit 3 of 15 console captures, even after the 3-second wait.
  - The fix: `tools/Capture-Gallery.ps1` captures again while the picture is pure black, for up to 5 seconds. A window that really draws black still fails its comparison.
  - The test: `A_capture_is_never_a_window_that_has_not_drawn_yet` (`tests/Stx.VisualTests/BaselineTests.cs`).

The suites, run again on 2026-11-04 after these fixes:

```
Visual: Passed!  - Failed:     0, Passed:   113
Playback: Passed!  - Failed:     0, Passed:    33
App: Passed!  - Failed:     0, Passed:   204   (Look 177, Art 69, Core 268, Data 30, Tools 190, Audio 68, Bridge 56: all passed, 0 failed)
Extension: pass 57 fail 0
App build: 0 Warning(s) 0 Error(s)
```

Every test added in Phase 1 was break-checked: each was made to fail by breaking the code it covers, then the code was restored. The breaks and results are in the plan's ledger.

## Final review: a self-review

Rocky asked for no subagents, so the whole-branch review is a self-review by the author. It is weaker than an independent review. It found two Important races, both fixed:

- **R1:** `Dispose` could free LibVLC while an open was still reading the file's tracks. Fixed: `Dispose` waits for that open. It did **not** reproduce in 120 open-and-dispose cycles, so `Dispose_right_after_open_is_safe` is a regression guard, not proof the race occurred.
- **R2:** The mono switch-over could call into a player that had just been disposed. Fixed: it runs under the engine's lock and skips a disposed player.

## Not done, and why

- **Installer size.** The SoundFont is about 112 MB of the 195 MB installer. A smaller SoundFont would cut that, at some cost in sound quality, but STX hasn't tested whether its FluidSynth build reads the compressed SF3 format.
- **Code signing** comes after the final phase (Rocky). The README says the setup and app files are signed through Azure Artifact Signing. `tools/Publish.ps1` can sign (`-Sign`), but this build was made without it, and nothing here shows a signed release: UNVERIFIED.
- **The installer itself** was built but not installed and uninstalled on a clean machine.
- **Not covered:** high-DPI displays, other GPUs, and HDR displays. One machine at 96 DPI, as for the earlier measurements.
- **Phases 2–7** (discs and streams, bit-perfect audio, ENHANCE, compression, default player, watched folders) have their own plans to come.
