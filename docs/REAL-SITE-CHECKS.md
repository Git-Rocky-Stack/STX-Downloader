# Real-site checks

Results from real sites, run on 2026-11-01 unless noted. Sites change, so these are a record of that day and not a promise.

| Check | Result | Evidence |
|---|---|---|
| YouTube video, audio untouched | Passed on an earlier build (2026-10-31), not repeated on the release zip | Saved 4K AV1 with AAC 5.1 at 386 kbps; Source Truth showed reported, then measured |
| YouTube video by ID from the extension's message path | Passed, not from the zip | A 310 MB webinar saved during the budget run in `docs/PERFORMANCE.md` |
| YouTube playlist with a private entry | **Not tested** | No such playlist was available. Covered only by tests against saved yt-dlp output |
| Chevrolet Corvette Experience capture | Passed, from the release zip | Below |
| Ferrari Purosangue capture | Passed, from the release zip | Below |
| Registered webinar | **Not tested** | No registration was made on anyone's behalf |

Each capture was a real click on Save in the real toolbar popup, in Playwright's Chromium with the unpacked extension from the zip. The native host started the app. Only those Chromium runs are covered: Chrome and Edge themselves were not tested.

## Chevrolet

```
rows 8
clicked Save on: FullThrottlePremiere_F · 1:11 · 608×1080 · HLS
popup status after 2749 ms: Added to the queue.
saved: FullThrottlePremiere_F [1].mkv  31.3 MB
stream|codec_name=h264|codec_type=video|width=608|height=1080|bit_rate=N/A
stream|codec_name=aac|codec_type=audio|bit_rate=N/A
format|duration=71.317000
credential files left while running: 0
stx.db bytes=4096 fastly_token=False boltdns=False
stx.db-wal bytes=111272 fastly_token=False boltdns=False
```

## Ferrari

```
rows 9
clicked Save on: R9CF0A_aero_aerobridge_e_DRL_v04_DESKTOP_LFXDDS · 1920×1080 · HLS
popup status after 2466 ms: Added to the queue.
saved: R9CF0A_aero_aerobridge_e_DRL_v04_DESKTOP_LFXDDS.mp4  3.8 MB
stream|codec_name=h264|codec_type=video|width=1920|height=1080|bit_rate=3580482
format|duration=8.800000
stx.db bytes=4096 fastly_token=False boltdns=False
stx.db-wal bytes=90672 fastly_token=False boltdns=False
```

The name was cut short only because the test folder's path was very long.

## DRM

A local page attached real ClearKey `MediaKeys` and requested a real stream:

```
page attached real MediaKeys: true
rows: [ 'DRM test · HLS · DRM, no download' ]
Save buttons: 0 | notice shown: true | Save all shown: false
```

No commercial DRM service was tried.

## Worker stopped

The worker was stopped, then the same page requested one more stream:

```
worker status after stop: stopping > stopped
worker status after new stream: starting > starting > starting > running
items before stop 47 after new stream on same page 48
```

This was a forced stop. Chrome's own idle stop was not observed, because attaching the debugger prevents it.

## Known differences

- Captured Chevrolet HLS saved as MKV, even though H.264 and AAC fit MP4. yt-dlp made that choice from `--merge-output-format mp4/mkv`. The streams themselves are untouched.
- Sites may block automated browsers. Chevrolet returned "Access Denied" to headless Chromium, and worked in a visible window.
