---
format: 1920x1080
duration: 76s
message: "Paste a link, watch it save, and check what you really got. That's the whole console."
arc: Welcome → Set up → Save → Watch → Find → Check → Browser → Make it yours
audience: someone who just installed STX Downloader
mode: collaborative
---

## Frame 1 — Welcome

- scene: Rocky's S blooms whole on black, then the line "Your first save, start to finish"
- duration: 5s
- poster: 3s
- transition_in: cut
- status: built
- src: compositions/frames/01-welcome.html
- motion: logo-assemble-lockup (spring-bloom variant)
- captions: "Your first save, start to finish."

Sets the promise and the look. Uses stx final icon.png untouched, on matching black.

Build (2026-11-04): the S blooms in (opacity and scale only, never masked or recolored), then the caption.

## Frame 2 — Set up

- scene: Real header plate, camera pushes to the engine light as it goes from busy to steady
- duration: 9s
- poster: 6s
- transition_in: crossfade
- status: built
- src: compositions/frames/02-setup.html
- motion: coordinate-target-zoom, depth-of-field-blur
- captions: "The first run downloads its tools, about 430 MB." / "Wait for the engine light to hold steady."

Answers the first thing a new user wonders: why isn't it ready yet. Guide: Getting started.

Build (2026-11-04): camera pushes in on the engine light, then spotlights it. The plate is a still of the steady light, so the busy-to-steady change is told by the caption, not shown.

## Frame 3 — Save from a link

- scene: Source plate, cursor presses PASTE, the sample link fills the display, a playlist offer appears
- duration: 12s
- poster: 5s
- transition_in: crossfade
- status: built
- src: compositions/frames/03-link.html
- motion: cursor-ui-demo, cursor-click-ripple, physics-press-reaction
- captions: "Copy a link and press PASTE. It reads the link straight away." / "A playlist asks first: add its items, or just this video."

The core action. Guide: Save from a link.

Build (2026-11-04): cursor presses PASTE with a ripple; the playlist row is dimmed until the press, then outlined. The link box is empty in the plate, so no link text is invented.

## Frame 4 — Watch the queue

- scene: Queue plate, the sample job's meter fills and its readout counts up, then the pad row lights in turn
- duration: 12s
- poster: 6s
- transition_in: crossfade
- status: built
- src: compositions/frames/04-queue.html
- motion: stat-bars-and-fills, coordinate-target-zoom
- captions: "Each job shows its status, a meter and its percent." / "Select a job to pause, resume, cancel or retry."

Payoff of the save. Guide: Queue.

Build (2026-11-04): camera pushes in on the running job, cursor selects it, and a highlight steps PAUSE → RESUME → CANCEL → RETRY. The plate is a still, so the meter fill and count-up are not animated.

## Frame 5 — Find it in the library

- scene: Library plate, cursor selects a saved row, OPEN and FIND FILE enable, then the three convert pads highlight
- duration: 10s
- poster: 5s
- transition_in: crossfade
- status: built
- src: compositions/frames/05-library.html
- motion: cursor-ui-demo
- captions: "OPEN plays it. FIND FILE shows it in its folder." / "CAR USB, PHONE and ARCHIVE convert it for that use."

Guide: Library.

Build (2026-11-04): cursor selects a row, presses OPEN, then FIND FILE; the three convert pads outline in turn. The pads' enabled state comes from the plate as captured.

## Frame 6 — Check what you really got

- scene: Source Truth plate with the sample measurement, spectrum sweeps in, the verdict line is spotlit
- duration: 10s
- poster: 6s
- transition_in: crossfade
- status: built
- src: compositions/frames/06-truth.html
- motion: device-surface-showcase (static tour), yt-feather-highlight
- captions: "Its audio is measured from the file, not taken from what the site claims." / "Re-encoding never improves anything, and the verdict says so."

The app's distinct idea, and the end of the message. Guide: Source truth.

Build (2026-11-04): slow pan across the measurement, then the verdict line is spotlit. The spectrum is part of the still, so it does not sweep.

## Frame 7 — Save from a web page

- scene: The extension popup, rebuilt from popup.html's own styles with sample rows, cursor presses Save, a hand-off line runs to the console
- duration: 10s
- poster: 5s
- transition_in: crossfade
- status: built
- src: compositions/frames/07-browser.html
- motion: cursor-ui-demo, cursor-click-ripple
- captions: "Start the video, then open the STX Downloader extension." / "Press Save. DRM pages say so, and can't be saved."

Guide: Save from a web page.

Build (2026-11-04): popup rises in, cursor presses Save with a ripple. The hand-off line to the console was not built; the console isn't on screen in this scene.

## Frame 8 — Make it yours

- scene: Settings plate with its captioned knobs, USER GUIDE pad spotlit, then a clean cut to Rocky's badge on black
- duration: 8s
- poster: 4s
- transition_in: crossfade
- status: built
- src: compositions/frames/08-settings.html
- motion: yt-feather-highlight, logo-assemble-lockup
- captions: "Set your quality, folder and how many run at once." / "Everything's in the USER GUIDE."

Closes by pointing at the guide. Guide: Settings.

Build (2026-11-04): wide Settings plate (settings.png, a copy of rack.png so the two crops are separate media) with DOWNLOAD, OUTPUT FOLDER and PARALLEL DOWNLOADS outlined, crossfade to the USER GUIDE pad spotlit, then a hard cut to the badge on black.
