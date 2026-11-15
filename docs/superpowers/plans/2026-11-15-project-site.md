# STX Project Site (the page as a rack) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `site/` with a ground-up, one-page project site built as hyper-real rack gear whose parts are drawn by STX's own art code, aimed at Windows users and ending in a download.

**Architecture:** A small .NET console tool (`tools/site/SiteArt`) runs `src/Stx.Art` and writes the fixed-size parts (screws, LEDs, meter segments, digits, pads) and the brushed-grain tiles as PNGs into `site/art/`, which is committed. A static `site/index.html` with one stylesheet and one script lays the parts into rack units; plate shape, glass and screen are CSS built from the app's own numbers. `tools/site/check-site.mjs` gates every publish, including parity with the README's download table.

**Tech Stack:** .NET 10 (`net10.0`), xUnit v3 on Microsoft.Testing.Platform, plain HTML/CSS/ES2022 (no framework, no bundler), Node 24 (`node --test`) for the site check, GitHub Pages via `.github/workflows/pages.yml`.

**Spec:** `docs/superpowers/specs/2026-11-15-project-site-design.md` (commit 3d45caa). Approved look: `docs/design/site-v2/hero-rack-stack.html` and option **B** of `docs/design/site-v2/reading-text.html`. Read both before any page task.

## Global Constraints

- Nothing is kept from the old `site/index.html`, `site/llms*.txt` or `tools/site/check-site.mjs` (spec, Intent). Old `site/screenshots/*` are reusable captures of 1.2.0.
- Audience: Windows users who want to save and play media. Success: they download. Pitch, Rocky's words: "all in one app: save, check and play."
- Every feature or safety claim is worded no wider than its anchor in the Claims table below (Gate 4). No anchor, no claim.
- No toggle switches and no control that does nothing. The only decorative motion is the L/R meter pair, silkscreened `DEMO`.
- Reading text sits behind glass (cyan VFD windows), per approved option B.
- Finishes: black anodized (default) and brushed aluminum; colors from `src/Stx.Look/LookTokens.cs:11-40`.
- Brand art from `icons/` is scaled only, never masked, trimmed or recolored. Badge: `icons/stax badge.png`.
- No em dashes (U+2014) and no emojis in any published text file or README.
- `prefers-reduced-motion: reduce` stops all motion; pointer light is off on touch (`(hover: none)`).
- Text contrast WCAG AA minimum on both finishes and inside glass; touch targets 44px minimum; keyboard reachable.
- Page base path `/STX-Downloader/`; canonical `https://git-rocky-stack.github.io/STX-Downloader/`.
- `TreatWarningsAsErrors` is on for every .NET project (`Directory.Build.props:13`).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Run `/verify` before each commit that touches code (not docs-only commits).

## Claims table (the only claims the page may make)

| Claim (max width) | Anchor |
|---|---|
| Paste a link; yt-dlp handles single items and playlists; a playlist asks first | `README.md:89` |
| Video isn't re-encoded by default: original streams, MP4 when they fit, MKV when not | `README.md:90` |
| Captures HLS, DASH and direct media from a page via a Chrome/Edge extension | `README.md:98` |
| DRM pages get no Save button; STX doesn't save or play DRM media | `README.md:100`, `README.md:136` |
| Source Truth measures the saved audio (spectrum, roll-off, loudness, true peak) and never says re-encoding improved anything | `README.md:110-111` |
| Plays on VLC 3.0.24's engine, pinned by sha256, network-port and site-script parts left out | `README.md:125` |
| Plays 120 types, each proven by a corpus file | `README.md:135` |
| Unencrypted DVD/Blu-ray, streams and internet radio; encrypted discs refused | `README.md:136-137` |
| BIT-PERFECT and ENHANCE (SHARPEN, UPSCALE where the GPU has super resolution) | `README.md:138-139` |
| Library holds what you saved, what you opened, and your Videos and Music folders; removing a row never removes a file | `README.md:115`, `README.md:119` |
| SHRINK makes a smaller AV1/HEVC + Opus copy, kept only after it checks out; originals only read | `README.md:144-145` |
| Signed setup (Azure Artifact Signing) | `README.md:49` |
| No administrator prompt | `README.md:34`, `README.md:46` |
| Setup: first launch downloads nothing and works offline (setup only; the zip downloads its tools) | `README.md:47` |
| Browser hand-off opens no network port | `README.md:101` |
| Tokens, cookies and headers stay in memory and are scrubbed from the copyable log | `README.md:105-106` |
| MIT licensed | `README.md:201` |
| Requirements: Windows 10 2004+ x64, ~400 MB app + ~430 MB tools, no runtime to install | `README.md:32-35` |
| Version, file names, sizes, SHA-256, URLs | `README.md:37-42` (machine-checked, Task 5) |
| Tested: PROVEN / NOT YET / KNOWN lists | `README.md:167-169` |

## Review Focus

1. **A release without a page update.** The README table moves to 1.3.0 and the page still says 1.2.0. Expected: publish fails with a message naming the stale field. Test: Task 5 `fails_when_hash_differs_from_readme`, `fails_when_version_differs_from_readme`.
2. **No JavaScript, or a script error.** Expected: every unit, all text, both DOWNLOAD links and the hashes are present and usable; only motion and the finish switch are lost. Test: Task 5 `download_links_are_plain_anchors` and Task 10 Step 3 (load with JS disabled).
3. **localStorage blocked** (private window, blocked site data). Expected: finish pads still work for the visit, no thrown error. Test: Task 6 Step 4.
4. **The README's `#screens` link** (`README.md:72`) points at this page. Expected: the anchor still exists. Test: Task 5 `readme_anchors_into_page_exist`.
5. **Phone at 360px, and 200% zoom.** Expected: no horizontal scroll, DOWNLOAD visible in the sticky strip, glass text not clipped. Test: Task 10 Steps 1-2.

---

### Task 1: Grain tile in the app's art code, app plates unchanged

**Files:**
- Modify: `src/Stx.Art/PlateArt.cs`
- Test: `tests/Stx.Art.Tests/PlateAndScrewArtTests.cs`

**Interfaces:**
- Produces: `public static PixelBuffer PlateArt.GrainTile(int width, int height, LookMode mode, double gain)`: opaque, neutral gray 128 plus the plate's grain and row offset times `gain`; horizontally seamless; no gradient, sheen or chamfer.

- [ ] **Step 1: Pin golden hashes of today's plates.** Add `[Theory] Render_is_unchanged(int w, int h, LookMode m, string hash)` with rows for (400,200,Dark), (400,200,Light), (64,64,Dark), (1,1,Light). Get each hash by running `PlateArt.Render(w,h,m).Hash()` on the current code once and pasting it in.
- [ ] **Step 2: Run, expect PASS** (it pins current behavior). `dotnet test --project tests/Stx.Art.Tests --filter-method "*Render_is_unchanged*"`.
- [ ] **Step 3: Write failing tile tests:**
  - `GrainTile_wraps_horizontally`: for a 256x64 tile at gain 1, mean |luma(0,y) - luma(255,y)| over all rows is less than 1.5 times the tile's `MeanAbsDeltaHorizontal()`.
  - `GrainTile_is_neutral`: mean luma within 128 ± 1 for both modes.
  - `GrainTile_grain_runs_horizontally`: `MeanAbsDeltaVertical() > MeanAbsDeltaHorizontal() * 2`.
  - `GrainTile_gain_scales_texture`: vertical delta at gain 3 is between 2.5 and 3.5 times that at gain 1.
  - `GrainTile_is_deterministic`.
- [ ] **Step 4: Run, expect FAIL** (no `GrainTile`).
- [ ] **Step 5: Implement.** Extract the per-row noise generation from `Render` (`Random(11)`, `GrainWindow = 96`, `norm`, per-mode `grainAmp` and `rowAmp`) into one private method that both use, so the draw order of the random numbers is unchanged. `GrainTile` takes the window sum circularly (index modulo `width`) so the right edge meets the left. Do not copy the noise code.
- [ ] **Step 6: Run the whole Art suite, expect PASS**, golden hashes included: `dotnet test --project tests/Stx.Art.Tests`.
- [ ] **Step 7: Break-check.** Change `GrainWindow` to 95, run, and paste the `Render_is_unchanged` failures. Revert, run, and paste the pass.
- [ ] **Step 8: Commit** `feat(art): a seamless grain tile that shares the plate's noise`.

### Task 2: SiteArt tool writes the site's parts, with a drift check

**Files:**
- Create: `tools/site/SiteArt/SiteArt.csproj` (`net10.0`, Exe, references `src/Stx.Art`), `tools/site/SiteArt/Program.cs`, `tools/site/SiteArt/PngWriter.cs`, `tools/site/SiteArt/Parts.cs`
- Create: `tests/Stx.SiteArt.Tests/Stx.SiteArt.Tests.csproj` (copy `tests/Stx.Art.Tests/Stx.Art.Tests.csproj`'s shape), `tests/Stx.SiteArt.Tests/SiteArtTests.cs`
- Modify: `STXDownloader.sln` (add both projects), `.github/workflows/build.yml` (Windows job: drift check)
- Generate: `site/art/*.png`, `site/art/manifest.json`

**Interfaces:**
- Consumes: `PlateArt.GrainTile` (Task 1); `ScrewArt.Render(size, rotationDeg)`; `LedArt.Housing/Lit/Flare(color, size)`, `LedArt.SegmentLit/SegmentUnlit/SegmentPeak(string hex, w, h)`; `ControlArt.Digit(char, w, h)`, `PadWell(w,h,scale,mode)`, `PadFace(w,h,scale,tone)`, `PadPressShade(w,h,scale)`, `PadBloom(w,h,scale,tone)`; `BargraphLogic.ZoneHex(i, n, zones)`.
- Produces:
  - `static byte[] PngWriter.Encode(PixelBuffer buffer)`: 8-bit RGBA PNG, un-premultiplied, zlib via `System.IO.Compression.ZLibStream`.
  - `static IReadOnlyList<Part> Parts.All()`, where `record Part(string Name, int LogicalWidth, int LogicalHeight, Func<int, PixelBuffer> Render)`, and the argument is the scale (1 or 2).
  - CLI: `dotnet run --project tools/site/SiteArt -- --out site/art` writes `{Name}@1x.png`, `{Name}@2x.png` and `manifest.json` (`[{name, w, h, x1, x2}]`, where w and h are logical px). `-- --check site/art` exits 1 and names every file whose bytes differ.
  - **Part names (later tasks reference these exactly):**
    - `grain-dark`, `grain-light`: 512x256 logical tiles. Gain is chosen so the dark tile's luma standard deviation is 7.2 ± 0.5, matching the approved mockup's `docs/design/site-v2/grain.webp` (7.16).
    - `screw-a`, `screw-b`, `screw-c`, `screw-d`: 18x18, rotations 7, 22, 38, 51 degrees.
    - `led-housing`, `led-lit-{green,red,amber,blue}`, `led-flare-{green,red,amber,blue}`: `LedArt.LedArtLogical` square.
    - `seg-level-{lit,unlit,peak}`: sprite strips of 32 segments (zones `Vu`). `seg-progress-{lit,unlit}`: strips of 24 (zones `Progress`). Each cell is `SegmentArtWidth` x `SegmentArtHeight`, laid side by side.
    - `digits`: a strip of `0123456789` cells, `DigitWidth+2*DigitGlow` by `DigitHeight+2*DigitGlow`, plus a dead `8` cell for unlit positions.
    - `pad-well-{dark,light}`, `pad-face-{off,green,blue}`, `pad-press`, `pad-bloom-{green,blue}`: rendered at a reference 160x52 logical, used as CSS `border-image` 3-slices with slice width `PadRadius + WellInset + 2` (bloom adds `PadBloomOverhang`).

- [ ] **Step 1: Write failing tests** in `SiteArtTests.cs`:
  - `Png_round_trips_an_opaque_pixel`: a 2x1 buffer of (10,20,30,1) and (200,100,50,1) encodes to bytes that start with the PNG signature and whose IHDR says 2x1, 8-bit, color type 6.
  - `Png_unpremultiplies`: a pixel set with `Set(x,y,200,100,50,0.5)` encodes RGBA (200,100,50,128) ± 1.
  - `Every_part_renders_at_both_scales_at_its_logical_size_times_scale`.
  - `Part_names_are_unique_and_match_the_plan` (asserts the exact list above).
  - `Pad_face_middle_columns_are_identical`: for each `pad-face-*` at 2x, every column between the two slice widths is byte-identical to the first such column. Plain 3-slice stretching depends on this.
  - `Grain_dark_texture_matches_the_mockup`: luma std 7.2 ± 0.5.
  - `Check_mode_reports_a_changed_file`: write to a temp dir, flip one byte, `--check` returns 1 and names it.
- [ ] **Step 2: Run, expect FAIL.** `dotnet test --project tests/Stx.SiteArt.Tests`.
- [ ] **Step 3: Implement** `PngWriter`, `Parts`, `Program`. If `Pad_face_middle_columns_are_identical` fails because `PadFace` varies across its width, stop and report. Do not change the app's pad art to make it pass.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Break-check** `Png_unpremultiplies` (drop the divide by alpha) and `Check_mode_reports_a_changed_file` (always return 0). Paste each failure, then the restored pass.
- [ ] **Step 6: Generate.** `dotnet run --project tools/site/SiteArt -- --out site/art`, then open three PNGs (`screw-a@2x`, `led-lit-green@2x`, `pad-face-blue@2x`) with the Read tool and confirm they look like the app's parts.
- [ ] **Step 7: Wire the drift check** into `build.yml`'s Windows job, after Test: `dotnet run --project tools/site/SiteArt -c Release -- --check site/art`. Add `tests/Stx.SiteArt.Tests` to that job's test loop. Not the Linux job: bytes may differ across operating systems (spec, art pipeline).
- [ ] **Step 8: Commit** `feat(site): SiteArt draws the page's parts with the app's art code`.

### Task 3: Self-hosted fonts, license gate first

**Files:**
- Create: `site/fonts/cascadia-mono.woff2`, `site/fonts/selawik.woff2`, `site/fonts/selawik-semibold.woff2`, `site/fonts/selawik-bold.woff2`, `site/fonts/LICENSE-CascadiaCode.txt`, `site/fonts/LICENSE-Selawik.txt`
- Modify: `docs/THIRD_PARTY.md` (add both fonts and their licenses)

- [ ] **Step 1: Verify licenses.** Fetch the license files from the fonts' official repositories, github.com/microsoft/cascadia-code and github.com/microsoft/Selawik. Paste the license name and URL. If Selawik isn't SIL OFL 1.1 or another license that allows redistribution, stop and report; don't substitute a font.
- [ ] **Step 2: Subset** each to Basic Latin plus Latin-1 punctuation used on the page, as woff2 (`pyftsubset --flavor=woff2`). Cascadia Mono needs weights 400 and 700 (variable OK). Selawik needs Regular, Semibold and Bold.
- [ ] **Step 3: Verify** total `site/fonts/*.woff2` is under 160 KB (`ls -l`), and paste.
- [ ] **Step 4: Commit** `feat(site): self-hosted Cascadia Mono and Selawik, with licenses`.

### Task 4: Light captures of every screen, from v1.2.0

**Files:**
- Create: `site/screens/{console,sourcetruth,player,library,settings,guide,tutorial}-{dark,light}.webp` (+ `.png` for social and llms use)

- [ ] **Step 1: Build v1.2.0 in a worktree.** `git worktree add ../stx-120 v1.2.0`, then `dotnet build src/Stx.App -c Release -p:Platform=x64` there.
- [ ] **Step 2: Capture** each screen in both modes with that worktree's `tools/Capture-Gallery.ps1 -Name <n> -Mode <dark|light> -Out <path>`. Use gallery names from `src/Stx.App/Gallery/GalleryPages.cs:29-41` and `PlayerGallery.cs`, matched to the old captures by their pixel sizes: console 944x601, sourcetruth 944x601, library 944x761, settings 944x694, player 610x740, guide and tutorial 1344x881. Capture at 200% display scale if the machine can. Record the scale used.
- [ ] **Step 3: Verify** each light capture has the same pixel size as its dark partner (a short Python/PIL script printing both sizes), and paste the output.
- [ ] **Step 4: Convert** to WebP at quality 90 and remove the worktree (`git worktree remove ../stx-120`).
- [ ] **Step 5: Commit** `feat(site): dark and light captures of every screen from 1.2.0`.

### Task 5: The site check, rewritten for this page

**Files:**
- Create (replace): `tools/site/check-site.mjs` (exports `checkSite(root) -> string[]` problems; CLI exits 1 when any are found)
- Create: `tools/site/check-site.test.mjs`, `tools/site/fixtures/` (a minimal good site + README)

**Interfaces:**
- Produces: `checkSite(repoRoot: string): Promise<string[]>`. Later page tasks run `node tools/site/check-site.mjs` as their gate.

- [ ] **Step 1: Write failing tests** (`node --test tools/site/`). Each fixture variant breaks one thing, and the test asserts that a problem mentioning it is returned:
  - `passes_the_good_fixture` (expects `[]`)
  - `fails_when_version_differs_from_readme`, `fails_when_hash_differs_from_readme`, `fails_when_size_differs_from_readme`, `fails_when_download_url_differs_from_readme`. Parity reads the README table rows at `README.md:39-42`, located by the `| File | Size | SHA-256 |` header, not by line number. The page exposes the same values as `data-version`, `data-sha256`, `data-size` and `href` on elements with `data-release-file="setup|zip"`.
  - `download_links_are_plain_anchors` (the setup and zip URLs are `<a href>` in the HTML source, not built by script)
  - `readme_anchors_into_page_exist` (every `#id` in a README link to the project page exists in the page)
  - `fails_on_em_dash`, `fails_on_emoji` (page, txt, xml, webmanifest, README)
  - `fails_on_img_without_alt_or_size`, `fails_on_missing_local_ref` (src, href, srcset, CSS `url()` and `image-set()` in `site/css/*.css`)
  - `fails_on_broken_json_ld`, `fails_without_SoftwareApplication`
  - `fails_when_sitemap_lacks_lastmod`, `fails_when_robots_lacks_sitemap`, `fails_without_one_indexnow_key_file`
  - `fails_on_title_or_description_length`: title 30-65 characters, description 120-165.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** with Node built-ins only, no dependencies.
- [ ] **Step 4: Run, expect PASS.** Break-check the hash-parity rule (always pass it), paste the failure, restore, and paste the pass.
- [ ] **Step 5: Commit** `feat(site): a site check that holds the page to the README's release table`. The live `site/` will fail this check until Task 11, which is expected: pages.yml only runs on `main`, and this branch merges after Task 11.

### Task 6: Page frame, primitives and the sticky strip

**Files:**
- Create (replace): `site/index.html`, `site/css/stx.css`, `site/js/stx.js` (`<script type="module" defer>`)
- Delete: `site/img/social-card.png` only if Task 11 replaces it. Otherwise leave.

**Interfaces:**
- Consumes: `site/art/*` names (Task 2), `site/fonts/*` (Task 3).
- Produces (later tasks use these exact names):
  - Finish: `<html data-finish="dark|light">`. All colors are CSS custom properties: `--plate-top --plate-mid --plate-bot --ink --ink-dim --edge-hi --edge-lo --chassis --engrave-hi --engrave-lo --accent`, values from `LookTokens.cs` (Dark lines 13-24, Light lines 29-39).
  - Markup primitives:
    - `.rack` holds `.unit[data-u="N"]`, each a plate with ears and screws.
    - `.screw` (JS assigns `screw-a..d` round-robin).
    - `.led[data-color=green|red|amber|blue][data-state=on|off|strike]`
    - `.glass` > `.vfd`: the bezel, recessed screen, pixel grid and lens. Lens numbers come from `GlassArt.cs:13-17`, backlight from `ScreenArt.cs`.
    - `.pad[data-tone=off|green|blue]` on `<a>` or `<button>`, with `aria-pressed` for latching.
    - `.seven[data-value]`
    - `.meter[data-kind=level|progress][data-segments]`
    - `.silk` (silkscreen), `.engrave`
  - JS functions in `stx.js`, each idempotent and safe without the others: `initFinish()`, `initStrip()`, `initSeven()`, `initTyping()`, `initMeters()`, `initChannels()`, `initCopy()`, `initPointerLight()`.

- [ ] **Step 1: Build the head:** title, description, canonical, Open Graph and Twitter tags, icons, manifest, `SoftwareApplication` JSON-LD (operatingSystem "Windows 10, Windows 11", offers price 0, downloadUrl = setup URL, softwareVersion = README version), `@font-face` with `font-display: swap`. Preload the hero's console capture and `grain-*@2x`.
- [ ] **Step 2: Build the CSS primitives** to match `docs/design/site-v2/hero-rack-stack.html`. Plates use `grain-{finish}` via `image-set()` blended over the CSS gradient. Screws, LEDs, segments, digits and pad faces/wells/bloom use the Task 2 parts: pads as `border-image` 3-slices, segments and digits by `background-position` on their strips. Spacing is on an 8px grid. Use `1U = 44px` for unit heights.
- [ ] **Step 3: Build the sticky 1U strip:** POWER LED (green, strike on load), engraved name, BLACK and ALUMINUM latching pads (green when on), nav pads with a small LED that lights for the unit in view (IntersectionObserver), a small lit DOWNLOAD pad (blue) linking to `#download`, and a 24-segment `progress` meter that fills with scroll position.
- [ ] **Step 4: `initFinish()`:** starts from `prefers-color-scheme`, then a stored choice. Every `localStorage` access goes in try/catch. Pressing a finish pad sets `data-finish`, `aria-pressed`, and swaps every `<img data-src-dark data-src-light>`. Verify: in Playwright, override `localStorage.setItem` to throw, reload, press ALUMINUM, and confirm `data-finish="light"` and no console error. Paste the console output.
- [ ] **Step 4b: `initPointerLight()`:** a soft highlight on plates that follows the pointer, after `src/Stx.Look/PointerLightLayout.cs`: 760x260 ellipse, peak alpha 22/255 dark and 64/255 light. Set as CSS variables on `pointermove`, throttled to one rAF. Off under reduced motion and `(hover: none)`.
- [ ] **Step 5: Gate.** `node tools/site/check-site.mjs` passes for the page's rules (parity rules will fail until Task 9 adds the download unit; note which ones). Screenshot both finishes at 1440px wide, compare against the mockup, and paste the comparison notes.
- [ ] **Step 6: Commit** `feat(site): the rack, its parts and the sticky strip`.

### Task 7: Hero units

**Files:** Modify `site/index.html`, `site/css/stx.css`, `site/js/stx.js`

- [ ] **Step 1: Build the monitor unit:** the console capture (`site/screens/console-{dark,light}.webp`, `data-src-*`) behind deep glass, with `Captured from STX Downloader 1.2.0` silkscreened below. Set width and height to avoid layout shift.
- [ ] **Step 2: Build the 2U download unit:**
  - a `.glass .vfd` that types the pitch once (`initTyping()`: full text in the HTML, the typing is visual only, `aria-label` holds the full line; static under reduced motion)
  - `.seven[data-value="1.2.0"]` labeled VERSION
  - the lit DOWNLOAD pad: `<a data-release-file="setup">` with its size, holding the parity attributes Task 5 reads
  - the L/R `level` meters silkscreened `DEMO`. They run only while visible (IntersectionObserver), with ballistics from the mockup's `level()` shape (`docs/design/site-v2/hero-rack-stack.html`), the level ramp from `seg-level-*`, and a peak hold of 900 ms. They are still under reduced motion.
- [ ] **Step 3: Pitch copy:** headline `Save, check and play. All in one app.` and one supporting line from rows 1, 5 and 6 of the Claims table.
- [ ] **Step 4: Gate:** check-site, both-finish screenshots, and a Lighthouse performance run on the hero (`npx lighthouse http://localhost:<port>/ --only-categories=performance`). CLS must be 0, and the DOWNLOAD pad must be in the initial HTML. Paste the results.
- [ ] **Step 5: Commit** `feat(site): the hero, console behind glass and the download unit`.

### Task 8: Save / Check / Play, and More screens

**Files:** Modify `site/index.html`, `site/css/stx.css`, `site/js/stx.js`

- [ ] **Step 1: Build the Save / Check / Play unit.** Three columns, each with an engraved category, an LED (green, amber, blue), a glass window with a short heading and 2-3 lines from the Claims table (Save: rows 1-4; Check: row 5; Play: rows 6-9), and that job's capture in a small monitor: console, sourcetruth, player. Glass body text is at least 15px Cascadia Mono, glows less than the heading, and runs at most 60ch.
- [ ] **Step 2: Build the More screens unit (`id="screens"`).** One monitor plus channel pads Library, Settings, User guide and Tutorials. The pads are a `role="tablist"` with arrow-key movement, and the lit pad is the selected tab. Each channel shows its capture and a glass caption (rows 10-11 for Library and Shrink). The monitor opens the full-size capture in a `<dialog>` (Esc closes, focus returns). Without JS, all four captures render stacked.
- [ ] **Step 3: Gate:** check-site; keyboard pass (Tab, arrows, Enter, Esc) recorded as a list of what happened; both finishes screenshotted.
- [ ] **Step 4: Commit** `feat(site): save, check, play and the screens monitor`.

### Task 9: Is it safe?, Download, Proof, Footer

**Files:** Modify `site/index.html`, `site/css/stx.css`, `site/js/stx.js`

- [ ] **Step 1: Build the "Is it safe?" unit:** six LED rows (green), one per Claims table rows 12-17, each worded no wider than its anchor.
- [ ] **Step 2: Build the Download unit (`id="download"`).**
  - Setup and portable sub-units, each a pad plus a glass window with the file name, size and SHA-256. Values come from `README.md:39-42` and carry the parity attributes. Each hash gets a COPY pad: `initCopy()` uses the Clipboard API, falls back to selecting the text, and lights the pad green for 1.2 s.
  - Requirements (row 18) and three install steps (`README.md:46`, `README.md:65-66`).
  - The DRM notice beside the setup pad (row 4, plus "you're responsible for each site's terms and the law where you live").
- [ ] **Step 3: Build the Proof unit:** PROVEN (green LED), NOT YET (amber) and KNOWN (red), each a glass window, `README.md:167-169`. Then releases as Queue-style rows (version, date, one line, link) from `CHANGELOG.md`, and Build it yourself as commands in glass from `README.md:171-183`.
- [ ] **Step 4: Build the footer:** a blank panel with the engraved copyright line from `Directory.Build.props:9`, MIT, and links to source, releases, changelog and `support@strategia-x.com`.
- [ ] **Step 5: Gate:** `node tools/site/check-site.mjs` fully passes, parity included (paste). Break-check by editing one hash digit in the page, paste the failure, then restore.
- [ ] **Step 6: Commit** `feat(site): safety, download, proof and footer units`.

### Task 10: Phone width, accessibility, motion, performance

**Files:** Modify `site/css/stx.css`, `site/js/stx.js`, `site/index.html`

- [ ] **Step 1: Phone layout.** At 360px and 768px the rails thin to 8px, columns stack, the strip keeps POWER, a finish pad pair, DOWNLOAD and a MENU pad that opens the nav list, and there's no horizontal scroll. Verify with Playwright screenshots at 360, 768, 1024 and 1440 in both finishes, plus `document.documentElement.scrollWidth <= innerWidth` at each. Paste.
- [ ] **Step 2: Zoom and contrast.** At 200% zoom, no clipped glass text. Contrast: compute each text color against its background for both finishes and inside glass (glass is `#072029` at the darkest backlight) with `LookTokens.Contrast` math in a small script. Every pair must be at least 4.5:1. Paste the table.
- [ ] **Step 3: No-JS and reduced motion.** With JS disabled: every unit, all text, both download anchors and hashes are present (paste a text dump). With `prefers-reduced-motion: reduce` emulated: no animation runs. Check `document.getAnimations().length === 0` after 3 s and that meters are static. Paste.
- [ ] **Step 4: Performance.** Lighthouse mobile: Performance ≥ 90, Accessibility ≥ 95, CLS 0. Total transfer for the first view is under 1.2 MB. Paste the scores and the transfer size.
- [ ] **Step 5: Commit** `feat(site): phone layout, accessibility and motion pass`.

### Task 11: SEO/GEO files, publish workflow, retiring the old page

**Files:**
- Create (replace): `site/sitemap.xml`, `site/robots.txt`, `site/llms.txt`, `site/llms-full.txt`, `site/site.webmanifest`, `site/img/social-card.png` (1200x630 from the new hero, both finishes considered; dark wins)
- Keep: `site/45b2e560c134293479ee109ae9313d7c.txt` (IndexNow key)
- Delete: `site/screenshots/` (superseded by `site/screens/`) after `grep -rn "site/screenshots" README.md docs` shows each reference updated to `site/screens/` (`README.md:76-81`)
- Modify: `README.md` image paths. `.github/workflows/pages.yml` needs no change: its path filter already covers `site/**` and `tools/site/**`, and it runs `node tools/site/check-site.mjs` (Task 5 keeps that CLI). Confirm by reading it.

- [ ] **Step 1: Invoke `/rockys-seo-geo-diligence`** and follow it in full for `site/`: sitemap and lastmod, IndexNow, SEO meta, GEO meta, JSON-LD, `llms.txt` / `llms-full.txt` / `long-llms.txt` as the skill requires, and the pre-deploy review. `llms*.txt` content uses only Claims table rows.
- [ ] **Step 2: Update README image paths** to `site/screens/*-dark.png`. Run `node tools/site/check-site.mjs` and `dotnet test --project tests/Stx.App.Tests -p:Platform=x64 --filter-class "*DocsTests"`, which holds the README's references (`tests/Stx.App.Tests/DocsTests.cs`). Paste both.
- [ ] **Step 3: Final review.** Screenshots at 1440 and 360 in both finishes, next to the two approved mockups, with a written list of every visible difference and whether it's intended.
- [ ] **Step 4: Commit** `feat(site): SEO and GEO files, README pointed at the new captures`. Don't merge or push: Rocky reviews the branch, and publishing happens when it reaches `main`.
