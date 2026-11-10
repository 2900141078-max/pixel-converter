"""Build the monochrome six-blade aperture icon from vector geometry."""

from __future__ import annotations

import math
from pathlib import Path

import cairosvg
from PIL import Image

HERE = Path(__file__).resolve().parent
SILVER = "#D7DBDF"
DARK = "#1A1E23"
SIZES = (16, 24, 32, 48, 64, 128, 256)


def point(angle: float, radius: float) -> tuple[float, float]:
    radians = math.radians(angle)
    return 128 + math.cos(radians) * radius, 128 + math.sin(radians) * radius


blades = []
for blade in range(6):
    start = -90 + blade * 60
    inner_a = point(start, 49)
    outer_a = point(start - 34, 85)
    outer_b = point(start + 26, 85)
    inner_b = point(start + 60, 49)
    coords = [inner_a, outer_a, outer_b, inner_b]
    path = " ".join(("M" if index == 0 else "L") + f"{x:.2f},{y:.2f}" for index, (x, y) in enumerate(coords))
    blades.append(f'<path d="{path} Z" fill="{SILVER}" stroke="{DARK}" stroke-width="5" stroke-linejoin="round"/>')

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <title>像素转换：六叶光圈与照片</title>
  <rect x="0" y="0" width="256" height="256" rx="52" fill="{DARK}"/>
  <defs><clipPath id="aperture"><circle cx="128" cy="128" r="85"/></clipPath></defs>
  <g clip-path="url(#aperture)">{''.join(blades)}</g>
  <circle cx="128" cy="128" r="89" fill="none" stroke="{SILVER}" stroke-width="8"/>
  <circle cx="128" cy="128" r="43" fill="{DARK}" stroke="{SILVER}" stroke-width="5"/>
  <circle cx="111" cy="113" r="5" fill="{SILVER}"/>
  <path d="M101 147 L114 129 L123 139 L137 120 L155 147" fill="none" stroke="{SILVER}" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>
</svg>'''

(HERE / "aperture.svg").write_text(svg, encoding="utf-8")
symbol_svg = svg.replace(f'<rect x="0" y="0" width="256" height="256" rx="52" fill="{DARK}"/>', '')
(HERE / "aperture-symbol.svg").write_text(symbol_svg, encoding="utf-8")
cairosvg.svg2png(bytestring=symbol_svg.encode(), write_to=str(HERE / "aperture-symbol.png"), output_width=256, output_height=256)
master = Image.open(__import__("io").BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=1024, output_height=1024))).convert("RGBA")
for size in SIZES:
    master.resize((size, size), Image.Resampling.LANCZOS).save(HERE / f"aperture-{size}.png")
master.save(HERE / "aperture.ico", format="ICO", sizes=[(size, size) for size in SIZES])
