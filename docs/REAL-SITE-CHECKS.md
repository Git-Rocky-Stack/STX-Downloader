# Real-site checks

Results from real sites, run on 2026-11-01 unless noted. Sites change, so these are a record of that day and not a promise.

| Check | Result | Evidence |
|---|---|---|
| YouTube video, audio untouched | Passed on an earlier build (2026-10-31), not repeated on the release zip | Saved 4K AV1 with AAC 5.1 at 386 kbps; Source Truth showed reported, then measured |
| YouTube video by ID from the extension's message path | Passed, not from the zip | A 310 MB webinar saved during the budget run in `docs/PERFORMANCE.md` |
| YouTube playlist with a private entry | **Not tested** | No such playlist was available: one 182-entry public playlist tried showed none. Covered only by tests against saved yt-dlp output |
| Chevrolet Corvette Experience capture | Passed, from the release zip | Below |
| Ferrari Purosangue capture | Passed, from the release zip | Below |
| Registered webinar | **Not tested** | No registration was made on anyone's behalf |
| Real Chrome 156 and Edge 156, from the installed app | Passed | Below |
| Chevrolet in real Chrome and Edge | Passed, saved as MP4 | Below |
| Commercial DRM (Widevine) in real Chrome and Edge | Passed, no Save button | Below |

Each Chevrolet and Ferrari capture below was a real click on Save in the real toolbar popup, in Playwright's Chromium with the unpacked extension from the zip. The native host started the app. The Chrome and Edge runs have their own section.

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

## Real Chrome and Edge

The real installer, then the extension loaded from the install folder into a fresh profile of Chrome 156.0.8078.74 and Edge 156.0.4314.58. The app wasn't running before each first click. Save was pressed in the real toolbar popup, driven through the browser's debugging protocol, so its click comes from a script. Output from the Edge then Chrome run:

```
=== msedge ===
   extension loaded with id ahbdmiddabpbfmoijlejkinopgppajfg | matches the host's allowed id: True
  popup: MEDIA ON THIS TAB 127.0.0.1 · 320×240 · HLS Save
  job: (1, 'Completed', None)
  files: ['master [1].mp4']
 Chevrolet:
  after walking the page, videos: 8 iframes: 8
  popup rows: 8 saves: 8
  clicked: ('Corvette Owner Academy Engine Break · 1:24 · 1920×1080 · HLS Save', 0.7, ['Added to the queue.'])
  job: (2, 'Completed', None)
  newest: Corvette Owner Academy Engine Break [2].mp4 ['h264,video', 'aac,audio']
 commercial DRM (dash.js Widevine sample):
  page attached real MediaKeys: {'hasVideo': True, 'keys': True, 'widevine': True, 'time': 11.926586}
  rows: ['media.axprod.net · DASH · DRM, no download', 'video-H264-288-400k-video-avc1 · File · DRM, no download', ...] | Save buttons: 0

=== chrome ===
   extension loaded with id ahbdmiddabpbfmoijlejkinopgppajfg | matches the host's allowed id: True
  clicked: ('127.0.0.1 · 320×240 · HLS Save', 2.6, ['That one is already saved or in the queue.'])
 Chevrolet:
  clicked: ('Chevy Corvette Owner Academy Driver Maintenance · 3:02 · 1920×1080 · HLS Save', 0.7, ['Added to the queue.'])
  job: (3, 'Completed', None)
  newest: Chevy Corvette Owner Academy Driver Maintenance [3].mp4 ['h264,video', 'aac,audio']
 commercial DRM (dash.js Widevine sample):
  page attached real MediaKeys: {'hasVideo': True, 'keys': True, 'widevine': True, 'time': 11.92522}
  rows: ['media.axprod.net · DASH · DRM, no download', ...] | Save buttons: 0

stx.db bytes 4096 fastly_token False boltdns False
stx.db-wal bytes 292552 fastly_token False boltdns False
```

Chrome's first click was the same stream Edge had already saved, across a restart of the app, so it was recognized and nothing was added. In Edge the popup closed itself after the click, so Edge's message wasn't read. The saved file shows the click worked.

These ran with Playwright's automation attached, not a person's everyday profile. Chrome's own idle stop of the worker still hasn't been observed. Only one commercial DRM page was tried.

## Worker stopped

The worker was stopped, then the same page requested one more stream:

```
worker status after stop: stopping > stopped
worker status after new stream: starting > starting > starting > running
items before stop 47 after new stream on same page 48
```

This was a forced stop. Chrome's own idle stop was not observed, because attaching the debugger prevents it.

## Name collisions, identical files and MP4

Not a real site. The packaged app, real yt-dlp and real FFmpeg, with a local server playing the site and messages sent over the real named pipe, using the filename template `{title}`:

```
1. video A, template {title}, token one
   job (1, 1, 'Completed', 0, None)
   files ['Same.mp4']
2. a DIFFERENT video with the SAME title
   job (2, 2, 'Completed', 0, None)
   files ['Same [2].mp4', 'Same.mp4']
3. the SAME BYTES as A from another link, new title
   job (3, 3, 'Completed', 0, None)
   files ['Same [2].mp4', 'Same.mp4']
4. repeat click on A with a fresh token
   reply {'ok': True, 'jobs': 0, 'resumed': 0, 'alreadyHere': 1}
5. HLS with undeclared audio codec (yt-dlp alone gives MKV)
   job (4, 4, 'Completed', 0, None)
   Hls.mp4 ['h264,video', 'aac,audio']
6. server answering 503
   after first failure: (5, 5, 'Waiting', 1, 'Network') server hits 1

file rows (media, title, name, hash):
   1 Same Same.mp4 5d060ffc6b54
   2 Same Same [2].mp4 8c4b56ce9f22
   3 Copy Same.mp4 5d060ffc6b54
   4 Hls Hls.mp4 a76bfbb0fe25
```

## Long-term behavior in the real window

The packaged app, with the same local server. It shows a repeat click recognized after a restart, a video downloaded again after its MKV moved to MP4 and matched by its streams, and a file saved before hashes were kept matched by its bytes:

```
4. app restarted, repeat click on video A with a fresh token
   reply {'ok': True, 'jobs': 0, 'resumed': 0, 'alreadyHere': 1}
5. the HLS video again under a token nobody knows, so it downloads as MKV beside its MP4
   job (5, 5, 'Captured', 'Completed', 0, None)
   files ['Hls.mp4', 'Same [2].mp4', 'Same.mp4']
6. a file saved before hashes were kept, then the same bytes from a new link
   hash cleared on 1 row
   job (6, 6, 'Captured', 'Completed', 0, None)
   files ['Hls.mp4', 'Same [2].mp4', 'Same.mp4']
database holds tokens: {'token=one': False, 'token=three': False, 'zzq=unknown': False, 'unknown': False}
```

In the window, read through UI Automation:

```
Copy3: COMPLETE · SAME FILE ALREADY SAVED
statuses seen ['Long: COMPLETE', 'Long: CONVERTING']
copied log: 40 lines; ffmpeg lines like ['  Duration: 00:01:30.00, start: 0.000000, bitrate: 15577 kb/s', 'Stream mapping:'] ; progress fields 0
```

A capture during that conversion showed the row at CONVERTING with its readout at 47.

Tray icon messages and menu, sent to the real window. No physical mouse was used:

```
started hidden: window visible: False
after left click: window visible: True
after the close button: window visible: False | app alive: True
menu window: True | chose open
after menu Open: window visible: True
menu window: True | chose quit
after menu Quit, app exited: True
```

A first run with no tools and no connection, with requests sent to a dead proxy, not a real unplugged network:

```
updates True -> ['TOOLS MISSING', "THE TOOLS COULDN'T DOWNLOAD. CHECK YOUR CONNECTION, THEN RETRY.", 'RETRY']
updates False -> ['TOOLS MISSING', "TOOL UPDATES ARE OFF, SO THE MISSING TOOLS WEREN'T DOWNLOADED. TURN TOOL UPDATES ON IN SETTINGS, THEN RETRY.", 'RETRY']
```

## Installer

Installed and uninstalled for real on Windows 11, with the app running in tray mode each time it was uninstalled. A second install over the running app also exited 0. Uninstalling stops the app, clears any leftover cookie files, removes the install folder, shortcuts and browser registration, and keeps saved videos. Answering No to the data question keeps settings, database and tools. Answering Yes removes that folder. A silent uninstall keeps them.

## Known differences

- The first Chevrolet capture above ran before the app moved fitting MKV files to MP4, so it shows an MKV. The later captures in real Chrome and Edge ended as MP4, on different videos from that page. That the move itself happened was only observed on the local HLS stream.
- An earlier version of these notes said the zip passed. It saved files correctly, but `dotnet publish` was leaving out the compiled XAML, so every release before 2026-11-01 opened with no faceplates or visible buttons. Nobody looked at the published window until then. `tools/Publish.ps1` now refuses to finish without those files.
- Sites may block automated browsers. Chevrolet returned "Access Denied" to headless Chromium, and worked in a visible window.
