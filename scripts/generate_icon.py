#!/usr/bin/env python3
"""Render the STTmini app icon to a multi-resolution .ico.

The canonical source is the geometric SVG design (D:\\Temp\\gemini-svg.svg): a
#4285F4 blue disc, a white play triangle, and six #4285F4 subtitle bars — a
speech-to-subtitles metaphor. This script re-renders that geometry with Pillow
(supersampled 4x then LANCZOS-downscaled for clean edges) and emits an .ico
containing every size Windows / Explorer / taskbar / Avalonia needs.

Re-run anytime the design changes:

    python scripts/generate_icon.py

Requirements: Pillow (`pip install Pillow`). No cairo/svg native deps.
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

# ---- Design (mirrors gemini-svg.svg, coordinate space = 512x512) ------------
SIZE = 512
CENTER = (256, 256)            # disc center = triangle centroid (design stays centered)
BLUE = (66, 133, 244, 255)   # #4285F4
WHITE = (255, 255, 255, 255)

# play triangle (source design): M 128,34.3 L 128,477.7 L 512,256 Z — centroid = (256,256).
# six subtitle bars (source design): (x0, y0, x1, y1), all rx=10 — centroid of the
# bar group is also (256, 256), so both triangle and bars share the disc center.
TRIANGLE_DESIGN = [(128, 34.3), (128, 477.7), (512, 256)]
BARS_DESIGN = [
    (170, 146, 300, 166),
    (170, 186, 370, 206),
    (170, 226, 430, 246),
    (170, 266, 430, 286),
    (170, 306, 370, 326),
    (170, 346, 300, 366),
]
BAR_RADIUS_DESIGN = 10

# CONTENT_SCALE shrinks the triangle AND the bar group together about CENTER, so
# the two stay mutually aligned (bar centroid == triangle centroid == disc center)
# while reading slightly smaller inside the disc. 1.0 = original; 0.88 ≈ -12%.
CONTENT_SCALE = 0.88


def about_center(point, scale=CONTENT_SCALE):
    """Scale a design point toward/away from CENTER — keeps content centered."""
    cx, cy = CENTER
    x, y = point
    return (cx + scale * (x - cx), cy + scale * (y - cy))


TRIANGLE = [about_center(p) for p in TRIANGLE_DESIGN]
# Each bar box is (x0, y0, x1, y1); scale each corner toward CENTER, keep flat tuple.
BARS = [
    (*about_center((x0, y0)), *about_center((x1, y1)))
    for x0, y0, x1, y1 in BARS_DESIGN
]
BAR_RADIUS = max(1, round(BAR_RADIUS_DESIGN * CONTENT_SCALE))

SUPER = 4  # supersample factor for anti-aliasing
ICON_SIZES = (16, 24, 32, 48, 64, 128, 256)

OUTPUT = Path(__file__).resolve().parent.parent / "src" / "STTmini.App" / "Assets" / "app.ico"


def render(size: int) -> Image.Image:
    """Render the icon at the given pixel size, supersampled then downscaled."""
    big = size * SUPER
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    scale = big / SIZE

    def sx(x: float) -> float:
        return x * scale

    # blue disc
    d.ellipse((0, 0, big - 1, big - 1), fill=BLUE)

    # white play triangle
    d.polygon([(sx(x), sx(y)) for x, y in TRIANGLE], fill=WHITE)

    # subtitle bars
    for x0, y0, x1, y1 in BARS:
        box = (sx(x0), sx(y0), sx(x1), sx(y1))
        r = max(1, round(sx(BAR_RADIUS)))
        d.rounded_rectangle(box, radius=r, fill=BLUE)

    return img.resize((size, size), Image.LANCZOS)


def main() -> int:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    # Pillow's ICO writer keys each entry by image size: every target size must
    # appear both as a frame of matching dimensions and in `sizes`. Render the
    # largest as the base (Pillow uses it as the icon's primary image) and pass
    # the rest via append_images.
    largest = render(ICON_SIZES[-1])
    smaller = [render(s) for s in ICON_SIZES[:-1]]
    largest.save(
        OUTPUT,
        format="ICO",
        sizes=[(s, s) for s in ICON_SIZES],
        append_images=smaller,
    )
    print(f"wrote {OUTPUT} ({OUTPUT.stat().st_size} bytes) sizes={ICON_SIZES}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
