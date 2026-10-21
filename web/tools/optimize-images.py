#!/usr/bin/env python3
"""
optimize-images.py — produce modern image formats for the marketing site.

The application screenshots are 1920x1020 PNGs, which are large (80-350 KB
each) and slow to paint on phones. This script emits WebP variants that every
modern browser supports, at two widths:

    <name>.webp        1920px wide  (desktop / high-density)
    <name>-960.webp     960px wide  (mobile / 1x)

The original PNGs are kept as the <picture> fallback for older browsers, and
the social preview image (banner.png) is deliberately left untouched because
crawlers expect a PNG/JPEG for og:image.

Usage:
    pip install pillow
    python web/tools/optimize-images.py
"""
from __future__ import annotations

import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:  # pragma: no cover - dependency guidance
    print("Pillow is required: pip install pillow", file=sys.stderr)
    raise SystemExit(1)

WEB_DIR = Path(__file__).resolve().parent.parent
IMG_DIR = WEB_DIR / "assets" / "img"

# Files that are not app screenshots (social preview art).
SKIP = {"banner.png"}

WIDTHS = (1920, 960)
QUALITY = 82
METHOD = 6  # slowest/best WebP encoder setting


def human(size: int) -> str:
    return f"{size / 1024:8.1f} KB"


def main() -> int:
    if not IMG_DIR.is_dir():
        print(f"missing image folder: {IMG_DIR}", file=sys.stderr)
        return 1

    sources = sorted(p for p in IMG_DIR.glob("*.png") if p.name not in SKIP)
    if not sources:
        print("no source screenshots found")
        return 0

    total_before = total_after = 0
    print(f"{'source':26s} {'png':>10s} {'webp 1920':>12s} {'webp 960':>11s}   saved")
    print("-" * 76)

    for src in sources:
        with Image.open(src) as image:
            image = image.convert("RGB")
            variants = []
            for width in WIDTHS:
                out = src.with_name(
                    src.stem + (".webp" if width == WIDTHS[0] else f"-{width}.webp")
                )
                scaled = image
                if image.width > width:
                    height = round(image.height * width / image.width)
                    scaled = image.resize((width, height), Image.LANCZOS)
                scaled.save(out, "WEBP", quality=QUALITY, method=METHOD)
                variants.append((out, out.stat().st_size))

        before = src.stat().st_size
        after = variants[0][1]
        total_before += before
        total_after += sum(size for _, size in variants)
        print(
            f"{src.name:26s} {human(before)} {human(variants[0][1])} {human(variants[1][1])}"
            f"   {100 - round(after / before * 100)}% smaller"
        )

    print("-" * 76)
    print(
        f"total: {human(total_before)} of PNG  ->  {human(total_after)} of WebP "
        f"({100 - round(total_after / total_before * 100)}% lighter)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
