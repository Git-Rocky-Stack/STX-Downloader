# STX Downloader design

Status: draft for Rocky's approval. Nothing is built yet.

## Intent

STX Downloader is a Windows desktop app for saving online video and music to your own computer. It has three purposes:

1. Save YouTube videos and music videos locally.
2. Capture video and media from the websites you visit, such as car manufacturer sites and published technical webinar recordings.
3. Tell audiophiles the truth about what they downloaded.

**Why it exists.** Rocky uses a mix of Store downloader apps and a browser plugin. The plugin caps his usage, and Firefox, which he ran it in, uses about 2 GB of RAM with one tab open by his report. He wants one app that he owns, with no limits, that stays light.

**Who it's for.** Rocky first. It will be published on GitHub under the MIT license, so others can use it too.

**The differentiator: Source Truth.** Most downloaders let you "convert to FLAC" or "320 kbps" and imply better sound. Online audio is already lossy, so that only makes a bigger file. Source Truth shows the real source format and quality, keeps the original audio untouched by default, and says plainly what any conversion will and won't do.

## Goals

- Save a pasted YouTube link at the best available quality.
- Capture video from a page where pasting the link fails.
- Download fast, with a queue that survives restarts and retries failures.
- Show the true source audio and never imply that a conversion adds quality.
- Offer a broad range of audio and video file types.
- Look like premium solid-state hardware, in the spirit of pro DJ equipment.
- Stay light on the machine, especially when idle.

## Non-goals for version 1

- Recording live streams. Rocky's webinars are published recordings.
- A Store listing, accounts, cloud sync, or any payment.
- Mac or Linux.
- Firefox extension. Chrome first; Edge should work from the same build.
- A built-in media player. Files open in the default app.
- USB and drive export, playlist watching, scheduled downloads, lyrics, and MusicBrainz tagging. These are later candidates.

## Anti-goals: failure even if it technically works

- **Usage limits of any kind:** daily caps, waiting periods, locked quality, or locked features.
- **A resource hog:** an app that sits heavy in the background the way Rocky describes Firefox.
- **False audio claims:** any label implying that re-encoding improved the sound.
- **DRM circumvention.** If a stream is DRM-protected, the app says so and stops.
- **Dark patterns:** share-to-unlock, rate-to-unlock, cross-promotion, or ads.
- **Telemetry by default.** The app contacts only the sites you ask it to and the tool update sources.
- **Flat UI.** If the equipment look reads as flat, it has failed.

## Constraints

- Windows 10 version 2004 (build 19041) or later, x64.
- MIT license for this project's code.
- Clean-room: no code, assets, strings or names from the Store apps or plugin that were studied. Their analysis notes never enter the public repository.
- Every README claim must be true and traceable to working code. The README states that DRM is out of scope and that users are responsible for following each site's terms.
- All new code is built with TDD, including the styling work, per Rocky's instruction.

## How it gets made

### Decided by Rocky

- XAML primitives for the equipment look.
- Black anodized is the default mode. Brushed aluminum is the light mode, switched inside the app.
- MIT license, on GitHub.
- The realism brief below.

### Builder's calls, made by Claude and open to challenge

- **Stack:** WinUI 3 on .NET 10, with Windows App SDK 2.6.4, MVVM with CommunityToolkit.Mvvm, and SQLite. A spike built and ran on this stack, apart from MVVM and SQLite.
- **Engine:** yt-dlp and FFmpeg run as external processes.
- **Tools are not bundled.** They download on first run, with checksum verification where the source publishes checksums. This keeps this repo's license simple and makes updates easy.
- **Distribution:** self-contained, unpackaged build on GitHub Releases. The spike folder was 157 MB, so trimming is worth attempting. The app will be unsigned at first, so Windows SmartScreen may warn on install.
- **Extension distribution:** loaded unpacked from the GitHub release, not published to the Chrome Web Store.
- **Mode:** always opens in the last chosen mode, starting with black. It ignores the Windows light or dark setting.

## What the app does

### Getting media in: two routes, one queue

1. **Paste or copy a link.** Runs through yt-dlp. Handles YouTube and other supported sites, single items and playlists. Optional clipboard detection, off by default, offers to grab a copied link.
2. **Capture from the page.** A Chrome extension shows media found on the current tab. One click sends the chosen item to the app.

If a pasted link fails as unsupported, the app suggests opening the page in the browser and capturing it there, not just an error.

If a pasted link needs a sign-in or bot check, the app asks you to open it in Chrome, where you're already signed in. The extension then sends that site's cookies to the app when you click, for that item or that whole playlist batch. No browser is built into the app, because a second browser engine would work against the light-footprint goal. *(Claude's call)*

### Queue

- Survives restarts.
- Per-item retry with backoff.
- Pause, resume, cancel.
- Configurable number of parallel downloads.
- Skips duplicates using source ID and file hash.
- Fetches faster than real time when the stream allows it.
- Captured items jump to the front and may run one over the parallel limit, because their links can expire. *(Claude's call)*
- A captured job that loses its link or cookies, through a restart or a long pause, moves to a **Needs re-capture** state. It is not dropped. One click opens its page, and a new capture from that page resumes the job. *(Claude's call)*

### Errors

Errors are in plain language with a next step, and a copyable raw log is always available. Tokens, cookies and auth headers are redacted from that log. Distinguish at minimum: unsupported site, sign-in needed, private or unavailable, DRM detected, expired stream link, network failure, tool failure.

### Source Truth panel

For each audio item, show measured facts from the actual file:

- **Source facts:** codec, bitrate, sample rate, channels.
- **Output and quality verdict:** one of the states below.
- **Spectrum:** a real spectrum display with the measured roll-off frequency. Computed from the file, never invented.
- **Loudness:** measured loudness and peak levels.

**Meters and spectrum show real data only (decided by Rocky).** This overrides the constant motion in mockup v2:

- **Idle:** they hold still at the selected item's measured peak levels and spectrum, like a meter with peak hold.
- **After a download:** they sweep through that item's actual measured levels for a few seconds as analysis finishes.
- **Nothing selected:** they stay dark.

Quality verdict wording must stay this honest:

| Output | Verdict |
|---|---|
| Original stream, no re-encode | Bit-perfect |
| FLAC, ALAC, or other lossless from a lossy source | Lossless copy of a lossy source; no quality gained |
| WAV or AIFF | Larger file, same sound |
| AAC, MP3, or Opus re-encode | Re-encoded; slight quality loss |

The default is always to keep the original audio. Choosing a conversion that adds nothing shows a warning but is never blocked.

Format can be chosen before download or applied later from the Library. Before download, the panel shows what the site reports, labeled as reported. After download, it shows what was measured from the file. A later conversion is its own job and keeps the original, so one media item can have several files. The same source in a different format is not a duplicate. *(Claude's call)*

### File types

- **Audio:** original stream, FLAC, ALAC, WAV, AIFF, AAC/M4A, MP3, Opus, Ogg Vorbis.
- **Video:** MP4 and MKV, best-available or chosen resolution, video-only or audio-only extraction.
- **Metadata:** embedded tags, cover art and chapters when the source provides them, each with its own switch. Filename templates.
- **Presets:** named outcomes, such as "Keep original", "Car USB", "Phone", and "Archive", that set format and quality together.

Loudness levelling is off by default and clearly labeled as changing the audio.

**Video is never re-encoded by default.** The app keeps the best original streams, using MP4 when they fit and MKV when they don't. Only device presets such as "Car USB" and "Phone" transcode, to H.264 and AAC, using a hardware encoder when one passes a test and software otherwise. Those jobs are labeled as re-encoded, with slight quality loss. *(Claude's call)*

### Tool upkeep

- yt-dlp updates automatically, because sites break often.
- Each update runs an offline self-test that the tool starts and reports its version, and it rolls back if that fails. No test traffic goes to any site.
- If real downloads then fail with a site-handling error after an update, the app retries that item once on the previous version. If that works, it rolls back and tells you. *(Claude's call)*
- A status light on the panel shows engine health.
- Tool updates must be switchable off.

### Library

A simple list of finished items with source link, file location, and Source Truth facts. Opens in the default app or File Explorer.

### Tray

Optional tray mode and start-with-Windows, both off by default. Hidden means all animation stopped.

## Browser extension

- Manifest V3, for Chrome. Finds media on the current tab, including HLS and DASH streams, direct files, and embedded players such as YouTube, where it hands over the real video ID.
- Labels what it finds using duration, resolution and title where the page exposes them, because one page can expose many streams.
- Stays small. It only detects and hands off. All downloading, stitching and conversion happen in the desktop app.
- Sends items through Chrome native messaging, restricted to this extension's ID. It does not open a local network port.
- Passes what the site needs for one item or batch, such as cookies or headers, only when you click. The app never writes those values to its database or logs.
- yt-dlp is a separate process, so they reach it through a temp file readable only by your Windows account, not on the command line. The file is deleted when the job ends, and any leftovers are deleted at startup. *(Claude's call: the one exception to memory-only handling.)*
- If it sees DRM, it reports that and offers no download.
- Stream links can expire, so the download starts right after capture.

## The look

### Realism brief, from Rocky

Hyper-realistic equipment primitives across the entire app, reflecting precision and craftsmanship, like modern solid-state control panels built by pro DJ equipment engineers. All pixels matter.

- **Panels:** real, faint brushed aluminum texture (a texture, not just a color).
- **Screws:** stainless socket-cap hex screws on the corners of every panel, even small ones.
- **LCDs and gauges:** readouts appear behind glass lenses.
- **LEDs and meters:** realistic lit look with background glow. A process start triggers a realistic flashing LED tied to its progress meter.
- **Controls:** switches, panels, buttons and pads have realistic deflection and depth, and never look flat.
- **Glow:** background glow on lighting, readouts and switches.

The approved visual reference is mockup v2, at `docs/design/console-v2.html`. Its sample data and constantly moving meters are illustration only; the behavior rules in this document win.

### Rendering approach, proven by the spike

Static art is rendered once into bitmaps, identical parts share bitmaps, and only brightness, position and rotation animate. Use compositor-thread animation where possible.

Fixed lighting matters: when a knob turns, its knurl and indicator rotate but its highlights stay put.

### Primitive set

Faceplate, screw, display, LED, LED bargraph, seven-segment readout, pad in a well, latching push switch with an LED slit, rotary selector with printed scale, engraved label. Both modes use the same primitives with different art and color tokens.

Rocky asked for this look app-wide, so the set grows as needed instead of falling back to standard Windows controls. Expect added primitives for scrolling lists, text entry, dialogs, and first-run setup, such as a list and an editable field inside a display. Every added primitive follows the same art, test and accessibility rules. *(Claude's call)*

### Motion and accessibility

- Honor Windows' reduce-motion setting.
- Keep keyboard navigation and visible focus on every control.
- All controls have accessible names.
- Text contrast meets WCAG AA in both modes, except decorative etching that carries no information.
- Hit targets are at least 44 px.
- Every state is covered: loading, error, empty, disabled.

## Data

SQLite with normalized tables for media items, media files, jobs and their state, playlists and membership, and measured audio facts. Each media item stores its page URL and source ID, and each file stores its path and hash. Stream tokens, tokenized links, cookies and headers are never written to the database.

## Testing

TDD for all behavior: queue states and retries, error classification, verdict wording, preset mapping, updater rollback, and the extension's detection and message format.

For the styling, TDD means:

- A failing test first for each primitive's behavior and states, such as LED strike timing, press travel, knob detents, a screw on every panel corner, and both modes supplying every token.
- Screenshot comparison against approved baselines for each primitive in each mode and state.
- Performance budgets checked in an automated test where practical, and otherwise a scripted measurement.

Real-site checks stay manual and separate from the unit suite, since sites change.

**Look review points.** Rocky approves baselines at two blocking points: after the display text glow is solved, and after the primitive set is complete. Screens aren't built on top of primitives until the second point passes. *(Claude's call)*

## Success measures

- A YouTube music video saves at best available quality with audio untouched, and the panel reports its true source.
- The Chevrolet Corvette Experience page can be captured through the real extension, end to end.
- Performance budgets on a machine like Rocky's (i5-8350U, integrated graphics):
  - Idle or hidden, with no work running: 0% CPU, measured over 30 seconds.
  - UI cost while a job's LEDs, meters and sweep animate, excluding yt-dlp and FFmpeg: under 2% of total CPU and 5% GPU.
  - Private memory for the app itself: under 150 MB.

  These budgets are Claude's proposal. The spike measured about 58 MB private memory, 0.6% CPU and 1% GPU, but on one small panel only.
- Rocky looks at the finished panels and doesn't call any of them flat or fake.

## Known risks

| Risk | Status |
|---|---|
| Display text glow | **Failed in the spike.** Showed little real glow plus a smear. Must be solved early. Win2D or pre-rendered glow are untested candidates |
| Webinars behind registration | **Untested.** No non-YouTube webinar has been captured end to end, and TNO's page stops at an email form. Rocky chose to proceed without a test link |
| Real extension | **Untested.** Detection ran in an automated browser, not a real Manifest V3 extension. Its service worker may need extra care to catch streams |
| Full-app cost | Measured on one panel. Real screens will cost more |
| Pointer-following light | Not built in XAML. May be cut if it costs too much |
| YouTube end to end | yt-dlp resolved a YouTube video but none was downloaded in testing. YouTube often changes what yt-dlp needs |
| Hidden memory | Stayed at about 58 MB private. Tearing down the window may lower it; untested |
| Takedown notices | Downloader projects on GitHub can draw them. No DRM support and honest docs reduce but don't remove this. Worth legal review before promoting it widely |
| FFmpeg licensing | Depends on the exact build. Check before distributing any link or instructions |

## Left to the builder

- Project structure and module boundaries.
- Screen layout beyond the approved mockup, using only the primitive set.
- Exact SQLite schema.
- FFmpeg build source, subject to the licensing check.
- Win2D or an alternative for text glow.
- How the spectrum is computed.
- Build order, except that the text-glow fix should come early because it could change the rendering approach.
