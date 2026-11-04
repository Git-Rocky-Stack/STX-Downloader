---
workflow: general-video
flow: automation
storyboard: yes
message: "Paste a link, watch it save, and check what you really got. That's the whole console."
destination: in-app tutorial plate
aspect: 1920x1080
language: en
audience: someone who just installed STX Downloader
length: 76s
angle: walkthrough
---

## Intent

A first-time user's walkthrough of STX Downloader, played on the TUTORIAL plate in Settings. Silent, with on-screen captions beside the real console. Follows the order someone actually uses the app in.

## Assets

- ../../icons/stx final icon.png — Rocky's S, on its own black, unaltered. Opens the video.
- ../../icons/stax badge.png — Rocky's badge, transparent background, unaltered. Closing lockup.
- ../../tests/baselines/*.png and Release gallery captures — the real console's plates, holding sample data only.
- ../../extension/popup.html — the extension popup's own styles, for the browser scene.

## Customizations

- On-screen captions only, no voiceover (Rocky, 2026-11-02).

## Notes

- Every statement must match src/Stx.ViewModels/GuideContent.cs, which carries the code anchors. No claim that isn't in the guide.
- Sample data only, so no real user's files, folders or links appear.
- Rocky's pictures are never masked, trimmed or recolored. A black stage lets them sit seamlessly.
