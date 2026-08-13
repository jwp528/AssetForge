"""Derive AssetForge app icon sizes from the transparent horizontal logo master."""

from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
BRAND = ROOT / "src" / "AssetForge.App" / "Assets" / "Brand"
ICONS = ROOT / "src" / "AssetForge.App" / "Assets" / "Icons"
SIZES = (16, 24, 32, 48, 64, 128, 256, 512)


def main() -> None:
    logo = Image.open(BRAND / "assetforge-logo.png").convert("RGBA")
    symbol_region = logo.crop((0, 0, round(logo.width * 0.32), logo.height))
    alpha_box = symbol_region.getchannel("A").getbbox()
    if alpha_box is None:
        raise RuntimeError("Logo contains no visible symbol pixels.")

    symbol = symbol_region.crop(alpha_box)
    side = max(symbol.size)
    padding = round(side * 0.08)
    master_side = side + padding * 2
    master = Image.new("RGBA", (master_side, master_side), (0, 0, 0, 0))
    master.alpha_composite(symbol, ((master_side - symbol.width) // 2, (master_side - symbol.height) // 2))
    master = master.resize((1024, 1024), Image.Resampling.LANCZOS)

    ICONS.mkdir(parents=True, exist_ok=True)
    master.save(ICONS / "app-icon-1024.png", optimize=True)
    rendered = []
    for size in SIZES:
        icon = master.resize((size, size), Image.Resampling.LANCZOS)
        path = ICONS / f"app-icon-{size}.png"
        icon.save(path, optimize=True)
        rendered.append(icon)

    rendered[-2].save(
        ICONS / "assetforge.ico",
        format="ICO",
        sizes=[(size, size) for size in (16, 24, 32, 48, 64, 128, 256)],
    )


if __name__ == "__main__":
    main()
