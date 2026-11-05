# Player, Phases 2 to 7: report

**Date:** 2026-11-05. **Plan:** `docs/superpowers/plans/2026-11-05-player-phases-2-7.md`. **Spec:** `docs/superpowers/specs/2026-11-04-vlc-player-design.md`.

**Read this first.** These phases were built in a Linux cloud container with no Windows desktop, GPU, sound device or disc drive. Everything below is marked by how it was checked:

- **TESTED:** a test runs it and passes, here or on CI's Windows runner, or both.
- **CI:** passes on CI's Windows runner, with the real VLC engine, headless.
- **LOCAL GATE:** a test is written but needs the reference PC (Device, Corpus or screenshot). **Not yet run.**
- **NOT PROVEN:** built, but nothing here could show it working on real hardware.

No phase's hardware "done when" was proven on the reference PC. Every phase's logic was.

## Scope ledger

| Phase | Delivered | How it's checked | Still to prove on the reference PC |
|---|---|---|---|
| 2: whole collection | Watched folders (Videos, Music, ADD/REMOVE FOLDER); background first scan; live add, rename, move, delete; origin on each row; search; diff refresh; virtualized list; first-use hashing | TESTED: 3,000-file scan off the caller's thread at below-normal priority; live add/rename/folder rename/delete with real FileSystemWatcher; 50,000-row search under 150 ms a keystroke; one reset for big changes; schema migration from an old database | The real plate with a big folder; idle memory with 50,000 rows (`docs/PERFORMANCE.md`); screenshots |
| 3: default player | Per-user registration from one plan, installer generated from it; ask once; DEFAULT PLAYER pad and LED | TESTED: every registered type proven by a corpus file; installer equals the plan; nothing writes a user choice; LED counts read from user choices; Windows 10/11 links | Install, choose STX in Default Apps on Windows 10 and 11, double-click each type with STX running and not; whether `UserChoiceLatest` reads right on 24H2 |
| 4: discs and streams | DVD plugins back (D4); STX's own CSS/AACS check inside the engine; PLAY DISC; PLAY STREAM; radio playlists; UDP/RTP refused; RTSP over TCP | TESTED + CI: encrypted disc never reaches LibVLC (built from scrambled sectors, as the spec asks); clear/scrambled DVD folders, images, VOBs; Blu-ray AACS folder, copy bits, images in both name encodings; break-checked. Stream sorting and playlist reading with a fake server | **NOT PROVEN:** an unencrypted DVD or Blu-ray playing (no authoring tool here); a real encrypted disc refused at the drive; a live radio stream |
| 5: BIT-PERFECT | S5 failed the spec's route; D6: FFmpeg into STX's exclusive renderer for lossless music; film passthrough; readout; VOLUME fixed; shared fallback with reasons | TESTED (real FFmpeg, here and CI): random 24/96 and 16/44.1 samples come out unaltered in every layout offered; a seek lands on the exact sample; routing; break-checked. CI: without FFmpeg it plays shared and says why | LOCAL GATE: `BitPerfectDeviceTests` (readout says 24/96 and 16/44.1 BIT-PERFECT; device let go on stop). **NOT PROVEN:** a loopback bit comparison (needs hardware loopback); film passthrough to a receiver |
| 6: ENHANCE | S6 from source; GPU read through DXGI; UPSCALE only where it exists; SHARPEN; PERFORMANCE ceiling; live restart that resumes; readout from VLC's log | TESTED: availability rules (UHD 620 unavailable, RTX 530+ available); ceiling; no wording claims improvement. CI: SHARPEN loads VLC's filter and a live change resumes the file | **NOT PROVEN:** SHARPEN visible on screen (screenshot gate); the restart with a real swap chain; UPSCALE on an RTX or Arc GPU; the DXGI read on the reference PC |
| 7: Shrink | AV1, HEVC, Opus presets; SHRINK, Shrink to knob, SHRINK FOLDER; copy check; encoder check; savings and space shown | TESTED (real FFmpeg): AV1 and HEVC copies made beside the original, original byte-identical; a copy failing its check is deleted and never marked done; a broken original leaves nothing; a missing encoder writes nothing; **a folder of three files (one in a subfolder) shrunk end to end through the Queue, originals hashed identical, a second pass finds nothing to do**; break-checked | That the BtbN Windows build has SVT-AV1, x265 and Opus (A7). CI installs Chocolatey's FFmpeg, which isn't BtbN's |

## Spike findings

### S5: VLC 3.0.24 can't deliver 24-bit PCM unaltered (failed)

Read from VLC 3.0.24's source (`github.com/videolan/vlc` at tag `3.0.24`):

- `modules/audio_output/wasapi.c:546-548`: plain PCM always opens `AUDCLNT_SHAREMODE_SHARED`. Exclusive is used only for S/PDIF (`:535`) and HDMI (`:542`) passthrough. **Assumption A4 confirmed.**
- `modules/audio_output/amem.c:191-193`: the callback output rejects any format but `S16N` (`/* TODO: amem-format */`). The spec's route, callbacks into STX's own renderer, would cut 24-bit files to 16.

The work stopped there, as the spec says, and Rocky chose FFmpeg for music (D6). One part of that choice wasn't built: **a film's 16-bit PCM through the callbacks into exclusive mode.** Keeping it in sync with the picture couldn't be checked without the reference PC, so those soundtracks play shared, and the readout says why.

### S6: super resolution is real on NVIDIA, assumed by VLC on Intel

Read from `modules/video_output/win32/direct3d11.c:77-80, 1761-1770` and `d3d11_scaler.cpp:60-150`:

- UPSCALE is `--d3d11-upscale-mode=super`.
- NVIDIA needs a WDDM driver past 530 and its video-processor extension (`:76-118`).
- Intel and Moore Threads are assumed to work: "detection doesn't work" (`:119-126`). AMD needs an AMF build.
- If none holds, VLC logs "Super Resolution filter not supported" and scales linearly.

STX doesn't assume. On the reference UHD 620 it shows UPSCALE UNAVAILABLE. VLC also makes the video output under the player (`src/input/resource.c:241`, `lib/media_player.c:742`), so ENHANCE has to be an engine option. A change restarts LibVLC in place.

### S4: covered in code, not on a drive

The detection works on built sectors, and LibVLC is never given an encrypted disc (CI test `An_encrypted_disc_never_reaches_libvlc`). Not checked: how the reference PC's drive behaves on a real CSS disc. STX treats a read error as encrypted, so the expected result is a refusal either way.

## Decisions Rocky made

- **D4:** re-add VLC's dvdnav/dvdread plugins for unencrypted DVDs, with STX's own check in front. README, THIRD_PARTY and PLAYBACK say so.
- **D5:** build Phases 5 and 6, test what CI can, and mark the rest unproven.
- **D6:** after S5 failed: FFmpeg for lossless music, VLC passthrough for films.
- **D7:** no deprecated dependencies. CI actions moved from Node 20 to their Node 24 releases (checkout v6, setup-dotnet v5, setup-node v6, cache v5, each checked to declare `node24`). xUnit 2.9.3, marked Legacy on NuGet, moved to xunit.v3 4.0.2 on Microsoft.Testing.Platform (`global.json`); CI uses `dotnet test --project` and `--filter-not-trait`. The xUnit1051 suggestion is switched off in the test projects, with the reason there.

## Calls I made

- **Search keeps every row in memory** and matches lower-case text. It's fast (measured below), but memory with a very large collection isn't measured.
- **Removing a watched folder** takes its watched rows out of the Library. Downloads and opened files under it stay.
- **The downloads folder isn't watched**, even inside Videos: the queue records downloads itself, and yt-dlp's part files have media names. Copies being written (`stx.partial-…`) are ignored too.
- **DEFAULT PLAYER is a pad, not a switch.** Windows doesn't let an app make itself the default, so a switch that can't switch itself would lie. The LED is the state.
- **Discs start at the main title.** Their menus need a pointer on the picture, which a swap chain doesn't pass back.
- **PLAY STREAM and PLAY DISC sit on the Source plate**, where media comes from, so the PLAYER plate's approved layout is unchanged.
- **Lossy music plays shared under BIT-PERFECT.** It has no original samples to keep, and the readout says so.
- **UPSCALE on Intel** is offered for Arc and Xe only, though VLC would try any Intel GPU.
- **Shrink settings:** SVT-AV1 preset 6 CRF 30, x265 medium CRF 24, both 10-bit; Opus 160k with video, 128k alone. These can be tuned.
- **A shrink copy is named "Name (AV1).mkv"** beside its original.

## Measurements and output

All Linux-runnable suites, at the final commit:

```
Look    total: 177  failed: 0  succeeded: 177
Art     total: 69   failed: 0  succeeded: 69
Core    total: 387  failed: 0  succeeded: 387
Data    total: 38   failed: 0  succeeded: 38
Audio   total: 68   failed: 0  succeeded: 68
App     total: 262  failed: 3  succeeded: 259
Tools   total: 190  failed: 28 succeeded: 162
Bridge  total: 56   failed: 12 succeeded: 44
Extension: pass 57, fail 0
App type-check (scratch project, no XAML): 0 errors
```

The App, Tools and Bridge failures are Linux-only and were there before these phases: Windows paths, `cmd.exe`, Windows named pipes, and the VLC engine beside the built app. They pass on CI.

CI on Windows (`.github/workflows/build.yml`), per phase:

- Run 17 (Phases 2 to 4, after the `LibraryImport` fix): passed.
- Run 18 (Phase 5): passed.
- Run 19 (Node 24 actions): passed.
- Run 21 (Phase 6, on xunit.v3): passed.
- Run 22 (scan test fix): passed.

Two failures along the way were fixed:

- **Run 16:** `LibraryImport` needs unsafe code, which the app project doesn't allow. My type-check project had allowed it, which hid the error; it no longer does.
- **Run 20:** a 250 ms stopwatch assertion in a scan test failed at 252 ms on a busy runner. The test now checks the thread and the order of events, not the clock.

Performance, measured here (Linux, in-process):

- Search on 50,000 rows: under 150 ms a keystroke, asserted.
- 20,000 watched files written to SQLite in batches of 500: well under the test's 20 s limit.

**Break-checks** (each test made to fail by breaking its code, then restored):

- CSS bit test: 5 disc tests fail.
- Volume filter slipped into FFmpeg's arguments: 4 bit-perfect tests fail.
- Copy check skipped: the failed-check test fails.

## Not done, and why

- **Screenshot baselines.** The Library, Settings, Source and Player plates changed, so `console`, `library`, `settings` and the `player` baselines will differ. They need capturing with `tools/Capture-Gallery.ps1` and Rocky's approval, as before.
- **Device, Corpus and screenshot tests** for these phases are written and not run.
- **Film PCM through the callbacks** (part of D6); see S5.
- **Performance budgets** weren't re-measured. `tools/Measure-Budgets.ps1` needs a run on the reference PC, with a large watched folder.
- **The installer** wasn't built or installed. Its registry lines are tested against the plan, but no install, Default Apps choice or uninstall was run.
- **Code signing** stays after Phase 7, as Rocky decided.
- **A self-review only.** The work wasn't reviewed independently; as in Phase 1, that is weaker than an independent review.
