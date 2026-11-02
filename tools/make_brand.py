"""Builds the app icon, the header badge and the installer's artwork from icons/icon_1.png and icons/badge_1.png.
Run again if those pictures change.

Both arrive on solid black. On the faceplate that would show as a black box, and in an icon it would shrink the S and
show as a black square on a light taskbar. So the black is cut away, and the S is scaled until it touches the edges of
the icon: the largest it can be.
"""
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ICON_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256]
# Where Inno Setup puts the top right picture at 100% to 250%, from its WizardSmallImageFile page.
CORNER_SIZES = [58, 77, 97, 116, 124, 143, 159]
# The panel beside the first and last pages, at 100% and 200%, from its WizardImageFile page.
PANEL_SIZES = [(202, 386), (430, 824)]
# The dark faceplate in src/Stx.Look/LookTokens.cs.
PLATE_TOP, PLATE_BOTTOM = (0x3A, 0x3D, 0x41), (0x1D, 0x1F, 0x22)
# What counts as the black background. The plates' rims fade in places, which leaves the first outline torn.
BACKGROUND_BELOW = 18
# The widest gap a fading rim can leave in the outline, in source pixels. Closing it mustn't reach across a real notch.
ICON_GAP, BADGE_GAP = 14, 8
# The S has a faint glass rim outside its bright ring, which reads as empty space at icon sizes. This trims it, in source
# pixels, so the ring itself meets the icon's edge. Past about 45 it starts cutting into the ring.
ICON_RIM_TRIM = 40


def reachable_from_edge(dark: np.ndarray) -> np.ndarray:
    height, width = dark.shape
    edge = [(y, x) for y in range(height) for x in (0, width - 1)] + [(y, x) for x in range(width) for y in (0, height - 1)]
    keep = np.zeros(dark.shape, bool)
    queue = deque(point for point in edge if dark[point])
    for point in queue:
        keep[point] = True
    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < height and 0 <= nx < width and dark[ny, nx] and not keep[ny, nx]:
                keep[ny, nx] = True
                queue.append((ny, nx))
    return keep


def grown(shape: Image.Image, by: int) -> Image.Image:
    """Grows a solid shape by about this many pixels, or shrinks it when negative, keeping its outline smooth."""
    blurred = shape.filter(ImageFilter.GaussianBlur(abs(by)))
    # A straight edge blurred this far is at 16% one blur radius out, and at 84% one radius in.
    return blurred.point(lambda level: 255 if level > (40 if by > 0 else 215) else 0)


def without_background(source: Image.Image, gap: int) -> Image.Image:
    """Only what's outside the plate goes. Its dark centers are the plate's own, so they stay solid."""
    rgb = source.convert("RGB")
    outside = reachable_from_edge(np.asarray(rgb.convert("L")) < BACKGROUND_BELOW)
    plate_shape = Image.fromarray((~outside * 255).astype(np.uint8), "L")
    closed = grown(grown(plate_shape, gap), -gap)
    opened = grown(grown(closed, -gap), gap)
    smooth = opened.filter(ImageFilter.GaussianBlur(3)).point(lambda level: 255 if level >= 128 else 0)
    # Filling from the edge removes any pinhole left, and the largest piece is the plate.
    solid = ~reachable_from_edge(np.asarray(smooth) < 128)
    # Smoothing treats the picture's border as more picture, so anything it grew past the plate's own extent goes.
    left, top, right, bottom = plate_shape.getbbox()
    within = np.zeros(solid.shape, bool)
    within[top:bottom, left:right] = True
    alpha = Image.fromarray((largest_piece(solid & within) * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(1.2))
    cut = rgb.copy()
    cut.putalpha(alpha)
    return cut


def largest_piece(solid: np.ndarray) -> np.ndarray:
    height, width = solid.shape
    seen = np.zeros(solid.shape, bool)
    best: list = []
    for start in zip(*np.nonzero(solid)):
        if seen[start]:
            continue
        seen[start] = True
        piece = [start]
        for y, x in piece:
            for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= ny < height and 0 <= nx < width and solid[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    piece.append((ny, nx))
        if len(piece) > len(best):
            best = piece
    result = np.zeros(solid.shape, bool)
    ys, xs = zip(*best)
    result[list(ys), list(xs)] = True
    return result


def cut_out(source: Image.Image) -> Image.Image:
    cut = without_background(source, ICON_GAP)
    plate_shape = cut.getchannel("A").point(lambda level: 255 if level >= 128 else 0)
    cut.putalpha(grown(plate_shape, -ICON_RIM_TRIM).filter(ImageFilter.GaussianBlur(1.2)))
    box = cut.getchannel("A").point(lambda level: 255 if level > 16 else 0).getbbox()
    cut = cut.crop(box)
    side = max(cut.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(cut, ((side - cut.width) // 2, (side - cut.height) // 2))
    return square


def scaled(master: Image.Image, size: int) -> Image.Image:
    small = master.resize((size, size), Image.LANCZOS)
    # Below this, fine grooves average out to grey unless the edges are lifted.
    return small.filter(ImageFilter.UnsharpMask(radius=1, percent=90, threshold=2)) if size <= 48 else small


def plate(width: int, height: int) -> Image.Image:
    ramp = np.linspace(0, 1, height)[:, None, None]
    colors = np.array(PLATE_TOP)[None, None, :] * (1 - ramp) + np.array(PLATE_BOTTOM)[None, None, :] * ramp
    rows = np.repeat(colors, width, axis=1)
    # Fine horizontal streaks, the same grain as anodized metal, so the panel doesn't band.
    grain = np.random.default_rng(7).normal(0, 1.4, (height, 1, 1))
    return Image.fromarray(np.clip(rows + grain, 0, 255).astype(np.uint8), "RGB")


def main() -> None:
    master = cut_out(Image.open(ROOT / "icons" / "icon_1.png"))
    without_background(Image.open(ROOT / "icons" / "badge_1.png"), BADGE_GAP).save(ROOT / "src" / "Stx.App" / "Assets" / "badge.png")
    icons = [scaled(master, size) for size in ICON_SIZES]
    icons[-1].save(ROOT / "src" / "Stx.App" / "Assets" / "stx.ico", format="ICO", sizes=[(s, s) for s in ICON_SIZES], append_images=icons[:-1])

    art = ROOT / "tools" / "installer" / "art"
    art.mkdir(parents=True, exist_ok=True)
    for size in CORNER_SIZES:
        scaled(master, size).save(art / f"corner-{size}.png")
    for width, height in PANEL_SIZES:
        panel = plate(width, height)
        mark = int(width * 0.62)
        small = scaled(master, mark)
        panel.paste(small, ((width - mark) // 2, (height - mark) // 2), small)
        panel.save(art / f"panel-{width}.png")

    extension = ROOT / "extension" / "icons"
    extension.mkdir(exist_ok=True)
    for size in (16, 32, 48, 128):
        scaled(master, size).save(extension / f"icon-{size}.png")


if __name__ == "__main__":
    main()
