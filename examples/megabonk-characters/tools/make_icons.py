"""Draw the example characters' icons. Original pixels, not game assets.

    python3 examples/megabonk-characters/tools/make_icons.py
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1] / "characters" / "icons"
SIZE = 128


def _icon(name: str, bg: tuple[int, int, int], fg: tuple[int, int, int], kind: str) -> None:
    im = Image.new("RGBA", (SIZE, SIZE), (*bg, 255))
    d = ImageDraw.Draw(im)
    d.rectangle((8, 8, SIZE - 9, SIZE - 9), outline=(*fg, 255), width=4)
    if kind == "character":
        d.ellipse((36, 28, 92, 84), fill=(*fg, 255))
        d.rectangle((52, 84, 76, 108), fill=(*fg, 255))
    elif kind == "weapon":
        d.polygon([(64, 20), (78, 78), (64, 70), (50, 78)], fill=(*fg, 255))
        d.rectangle((60, 74, 68, 110), fill=(*fg, 255))
    elif kind == "passive":
        d.ellipse((40, 40, 88, 88), outline=(*fg, 255), width=6)
        d.ellipse((58, 58, 70, 70), fill=(*fg, 255))
    else:
        d.rectangle((34, 34, 94, 94), fill=(*fg, 255))
        d.rectangle((48, 48, 80, 80), fill=(*bg, 255))
    ROOT.mkdir(parents=True, exist_ok=True)
    im.save(ROOT / name, "PNG")


def main() -> None:
    specs = {
        "lumen": ((92, 48, 16), (255, 214, 120)),
        "bulwark": ((32, 48, 64), (186, 198, 210)),
        "flicker": ((48, 24, 72), (244, 220, 255)),
    }
    for slug, (bg, fg) in specs.items():
        for kind in ("character", "weapon", "passive", "skin"):
            _icon(f"{slug}-{kind}.png", bg, fg, kind)


if __name__ == "__main__":
    main()
