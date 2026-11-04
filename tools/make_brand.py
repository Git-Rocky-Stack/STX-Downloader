"""Builds the app icon, the header badge and the installer's artwork from Rocky's pictures in icons/.
Run again if those pictures change.

    stx final icon.png  the app icon, the installer's corner picture, the browser extension's icon
    stax badge.png      the badge on the top plate
    setup.png           the tall panel on the installer's last page

Nothing here trims or recolors them. The badge is copied as it is, with its transparent background, and the others are
only scaled to the sizes Windows, Inno Setup and the browser ask for.
"""
import shutil
from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ICON_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256]
# Where Inno Setup puts the top right picture at 100% to 250%, from its WizardSmallImageFile page.
CORNER_SIZES = [58, 77, 97, 116, 124, 143, 159]
# The panel beside the last page, at 100% and 200%, from its WizardImageFile page.
PANEL_SIZES = [(202, 386), (430, 824)]
# setup.png has a transparent background, and the panel matches WizardImageBackColor in STXDownloader.iss.
BLACK = (0, 0, 0, 255)


def scaled(icon: Image.Image, size: int) -> Image.Image:
    small = icon.resize((size, size), Image.LANCZOS)
    # Below this, fine grooves average out to grey unless the edges are lifted.
    return small.filter(ImageFilter.UnsharpMask(radius=1, percent=90, threshold=2)) if size <= 48 else small


def panel(picture: Image.Image, width: int, height: int) -> Image.Image:
    """The whole picture, as large as fits, centered on black. Laid on black before scaling, so no edge picks up the color hidden in its transparent pixels."""
    flat = Image.alpha_composite(Image.new("RGBA", picture.size, BLACK), picture.convert("RGBA")).convert("RGB")
    scale = min(width / flat.width, height / flat.height)
    fitted = flat.resize((round(flat.width * scale), round(flat.height * scale)), Image.LANCZOS)
    plate = Image.new("RGB", (width, height), BLACK[:3])
    plate.paste(fitted, ((width - fitted.width) // 2, (height - fitted.height) // 2))
    return plate


def main() -> None:
    icons = ROOT / "icons"
    icon = Image.open(icons / "stx final icon.png").convert("RGB")
    shutil.copyfile(icons / "stax badge.png", ROOT / "src" / "Stx.App" / "Assets" / "badge.png")
    sized = [scaled(icon, size) for size in ICON_SIZES]
    sized[-1].save(ROOT / "src" / "Stx.App" / "Assets" / "stx.ico", format="ICO", sizes=[(s, s) for s in ICON_SIZES], append_images=sized[:-1])

    art = ROOT / "tools" / "installer" / "art"
    art.mkdir(parents=True, exist_ok=True)
    for size in CORNER_SIZES:
        scaled(icon, size).save(art / f"corner-{size}.png")
    setup = Image.open(icons / "setup.png")
    for width, height in PANEL_SIZES:
        panel(setup, width, height).save(art / f"panel-{width}.png")

    extension = ROOT / "extension" / "icons"
    extension.mkdir(exist_ok=True)
    for size in (16, 32, 48, 128):
        scaled(icon, size).save(extension / f"icon-{size}.png")


if __name__ == "__main__":
    main()
