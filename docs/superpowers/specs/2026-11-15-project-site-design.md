# STX Downloader project site, ground-up redesign

Date: 2026-11-15. Status: design, builder check done (four questions, all settled below and marked "settled after the builder check"), awaiting Rocky's approval. Live at https://git-rocky-stack.github.io/STX-Downloader/ (published from `site/` by `.github/workflows/pages.yml`).

## Intent

The page is for **Windows users who want to save and play media**. It succeeds when they **download STX**. The reason to switch, in Rocky's words: **all in one app: save, check and play.**

The page should look and feel like STX itself: a piece of hyper-real rack gear with brushed aluminum, socket-cap screws, glowing displays, LEDs with bloom, segment meters, and the app's silicone pushbuttons. Realism and texture are the point, at a quality level a stranger reads as "this product is made with care" before reading a word.

The existing `site/index.html` was a 15-minute placeholder. **Nothing is kept from it:** not its design, copy, structure, section order, nav, or its `tools/site/check-site.mjs`. True facts (version, sizes, hashes, feature claims) are reused as facts only, re-sourced from the release and the README.

## Goals

- A visitor understands within one screen what STX is (save, check, play in one Windows app) and can download it.
- DOWNLOAD is reachable from every scroll position.
- The materials match the app pixel for pixel where the app has the part, because the parts are drawn by the app's own art code.
- Both finishes the app has: black anodized (default) and brushed aluminum.
- Reads well for a long time despite the reading text sitting behind glowing glass.

## Non-goals

- A blog, docs site, or multi-page site. One page.
- Explaining every feature. The README and in-app guide do that; the page links to them.
- A web version of the app or anything that pretends to be the app working.

## Anti-goals (a failure even if it works)

- Any claim on the page that is not anchored in the README or code (Rocky's Gate 4; this is a public repo).
- Hardware that pretends to be a control and does nothing. Every pad does something real. No toggle switches, no dead knobs.
- A decorative meter passed off as measuring something. The one decorative meter pair is labeled as a demo.
- Realism bought with a slow or janky page: layout shift, scroll jank, a heavy hero that delays the DOWNLOAD pad.
- Text that fails WCAG AA, or motion that ignores `prefers-reduced-motion`.

## Constraints

- Static GitHub Pages from `site/`, base path `/STX-Downloader/`. Published by `pages.yml` on pushes to `main`.
- Rocky's standing SEO/GEO diligence applies (`/rockys-seo-geo-diligence`): sitemap with lastmod, IndexNow, SEO and GEO meta, JSON-LD (SoftwareApplication at least), `llms.txt` / `llms-full.txt`, robots. These come from Rocky's global rules, not the old page.
- Brand art in `icons/` ships exactly as provided, scaled only (memory: brand-art-exact). The badge is `icons/stax badge.png` (transparent).
- Feature claims are anchored to README sections, which already name the code behind each line.
- No em dashes and no emojis in page text (my call, kept from Rocky's writing style; flag if wrong).

## Approaches considered

1. **Hand-built CSS/SVG primitives plus one generated grain texture.** Lightest, no tooling, every part fluid. Realism ceiling is what the mockups showed.
2. **App's own art code renders the parts (CHOSEN by Rocky).** A small .NET tool runs `src/Stx.Art` and writes the screws, LEDs, pads, glass, seven-segment digits and plate grain as images; CSS does layout and any part that must stretch. Pixel-identical to the app; costs a second tool and heavier assets.
3. **Live 3D materials (WebGL).** Dropped: heavy script, battery and motion costs, little gain over a CSS pointer-light.

A framework (React/Vite) was also dropped: one static page gains nothing from a build step.

## What was decided, and by whom

Rocky:
- The page **is the rack**: every section is a rack unit bolted into rails.
- **Hero = rack stack**: a 1U strip, a monitor unit holding the real console screenshot behind deep glass, a 2U download unit. Approved mockup: `docs/design/site-v2/hero-rack-stack.html`.
- **The app's pushbuttons only**, no mechanical toggles.
- **Reading text behind glass**: section and feature text sits inside cyan VFD display windows. Approved: option **B** in `docs/design/site-v2/reading-text.html` (the file also shows rejected options A and C).
- **Meters: both kinds.** An honest scroll-position bargraph in the sticky strip, and a labeled decorative L/R level pair in the download unit.
- **Approach 2**: the app's own art code draws the parts.
- Audience, goal and pitch as in Intent.

Mine (Rocky can override):
- **BLACK / ALUMINUM latching pads** in the strip switch the whole page's finish and swap every screenshot to its light capture. Default follows `prefers-color-scheme`, then remembers the choice (localStorage, wrapped in try/catch).
- **Sticky 1U strip**: POWER indicator LED, engraved name, BLACK/ALUMINUM pads, nav pads whose LED lights for the section in view, a small lit DOWNLOAD pad, and the scroll bargraph (aqua to violet, the app's progress colors).
- **Rack units, top to bottom:**
  1. Hero: strip, monitor unit (console screenshot), 2U download unit: VFD types "Save, check and play. All in one app." (or close), seven-segment version, lit DOWNLOAD pad with size, the demo L/R meter pair silkscreened DEMO.
  2. **Save / Check / Play**: three glass windows, each paired with the real screen for that job (Console, Source Truth, Player). This is the pitch, proven.
  3. **More screens**: one monitor plus channel pads (Library, Settings, User guide, Tutorials) that switch it; the monitor opens the capture full size. No "Light mode" pad: the finish pads already show every screen in both finishes.
- **Light captures of every screen** (settled after the builder check): only the console has one today (`site/screenshots/`). Capture the rest with `tools/Capture-Gallery.ps1 -Mode light` (its line 3) from a build of the `v1.2.0` tag, so every capture is truly 1.2.0, dark and light.
  4. **Is it safe?**: LED rows, each worded no wider than its README anchor: signed setup (`README.md:49`), no administrator prompt (`README.md:34`), the setup's first launch downloads nothing and works offline (`README.md:47`; the setup only, since the zip downloads its tools), the browser hand-off opens no network port (`README.md:101`; not a whole-app claim), tokens stay in memory and are scrubbed from the log (`README.md:105-106`), MIT open source (`README.md:201`).
  5. **Download**: setup and portable units, requirements, short install steps, SHA-256 hashes in glass with COPY pads, and the DRM notice beside the button.
  6. **Proof and history**: what's tested (PROVEN green, NOT YET amber, KNOWN red), releases as Queue-style rows, build from source.
  7. Footer: a blank panel with the engraved copyright and links.
- **Copy is rewritten from scratch** for this audience: short, plain, Windows-user language; every feature line anchored.
- **Fonts self-hosted** (woff2, subset): Cascadia Mono (SIL OFL) for all glass text, since glass carries most of the reading; Selawik (Microsoft's OFL Segoe UI-compatible font) for silkscreen and engraving so Mac and phone visitors see the app's type. ASSUMPTION: Selawik's license and metric compatibility to be confirmed by the builder before use.
- **Glass readability**: body text in glass larger and less glowy than glass headings; line length about 60ch or less.
- **Motion**: LEDs strike on load, VFDs type once, the demo meters run only while on screen, plate sheen follows the pointer (after the app's `PointerLightLayout.cs`). All of it off under `prefers-reduced-motion`; pointer light off on touch.
- **Rack at phone width**: rails thin to a sliver, units stack to one column, the strip collapses its nav pads into a single pad that opens a list.
- **Generated art is committed** under `site/art/` so the page previews locally with no .NET; a check in `build.yml`'s Ubuntu job (which already has .NET 10) regenerates the art and fails if any committed file differs.
- **A new `tools/site/check-site.mjs`** written for this page (links, images with alt and size, JSON-LD, sitemap, banned characters), replacing the old one.
- **Release facts can't go stale** (settled after the builder check): the README's download table (`README.md:39-42`) is the source of truth and each release already updates it. The check fails when the page's version, file sizes, SHA-256 hashes or download URLs disagree with that table, so a release that forgets the page can't publish it.

## The art pipeline (approach 2)

- `src/Stx.Art` targets `net10.0` with no WinUI dependency (`src/Stx.Art/PixelBuffer.cs:5`), so a console tool can reference it directly. Parts available: `PlateArt.Render`, `ScrewArt.Render`, `LedArt.Housing/Lit/Flare/Segment*`, `ControlArt.PadWell/PadFace/PadPressShade/PadBloom/Digit`, `GlassArt.Render`, `ScreenArt.Render`, with tones from `src/Stx.Look` (`LookMode`, `LedColor`, `PadTone`, `DisplayTone`) and colors from `LookTokens.cs`.
- `PixelBuffer` is premultiplied BGRA; export must un-premultiply before encoding.
- Each part renders at 1x and 2x (`srcset` / `image-set`) for both finishes where it differs.
- Plates and glass must stretch. Neither exists in a stretchable form today: `PlateArt.Render` bakes the gradient, sheen and chamfer into one finished plate and its row noise doesn't wrap (`PlateArt.cs:24-46`), and `GlassArt.Render` places reflections as fractions of the window size (`GlassArt.cs:13-17`). Settled after the builder check:
  - **Grain:** add a tile function to `src/Stx.Art` (e.g. `PlateArt.GrainTile(width, height)`) that wraps horizontally and shares the exact noise code with `Render` by extracting it, not copying it. Before touching `PlateArt`, pin golden hashes of `Render` for both modes at a few sizes in `Stx.Art.Tests`; they must be identical after, which proves the app's plates are unchanged. The tile is used by the site tool, which is its wiring (Gate 2).
  - **Glass:** the reflection is CSS, using `GlassArt`'s numbers (diagonal slope 0.47, bands at 0.18 and 0.62 to 0.8 of width, top edge 0.14 of height). `ScreenArt`'s backlight and pixel grid likewise.
  - Plate gradient, sheen, chamfer and shadows are CSS from `LookTokens.cs` and `PlateArt`'s constants, as in the mockups.
- So "pixel-identical to the app" holds for the fixed-size parts (screws, LEDs, segments, digits, pad faces and wells, bloom) and the grain. Plate and glass shapes match by shared numbers, not shared pixels.
- Drift check risk for the builder: the art is generated on Windows (where it's previewed) and re-checked in `build.yml`'s Ubuntu job. Floating-point math may not give identical bytes across operating systems. Run the check on the generating OS, or compare decoded pixels within a tolerance of 1 level.

## Left to the builder

- Tool name and location (suggest `tools/site/SiteArt/`), output format (PNG vs WebP), sprite sheets vs separate files, the drift check's exact form.
- Exact sizes for each part, breakpoints, the 8px spacing grid in rack-unit terms.
- The pad and seven-segment implementation details (image parts vs CSS from the mockups) wherever the image version doesn't stretch.
- Lightbox behavior for the monitor, keyboard handling of the channel pads (they act as a tab list).
- Final copy, within the anchoring rule; JSON-LD shape; `llms*.txt` content.
- Replacing or retiring the old `site/` files; the old screenshots are fine to reuse as captures of 1.2.0.
- Performance budget: suggest the hero (strip, monitor, download unit) renders without layout shift and the DOWNLOAD pad is interactive before any decorative motion starts.
