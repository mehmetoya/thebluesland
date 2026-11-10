"""Builds TheBluesland logo SVGs (docs/brand). Wordmark is Georgia converted to outlines.

Run: <venv with fonttools>/bin/python docs/brand/build.py, then render the PNGs (see README.md).
"""
import math
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

OUT = Path(__file__).parent
BG, DISC, AMBER, AMBER_DARK, CREAM = '#12141b', '#0c0d12', '#d99a4e', '#8a5220', '#f7f1e6'
FONTS = '/System/Library/Fonts/Supplemental/'


def text_path(font_file, text, x, baseline, size, tracking=0.0):
    """Outlines for text set at (x, baseline), with the font's kern pairs and extra tracking."""
    font = TTFont(FONTS + font_file)
    scale = size / font['head'].unitsPerEm
    cmap, glyphs, hmtx = font.getBestCmap(), font.getGlyphSet(), font['hmtx']
    kern = {}
    if 'kern' in font:
        for table in font['kern'].kernTables:
            kern.update(getattr(table, 'kernTable', {}))
    pen = SVGPathPen(glyphs, lambda v: f'{v:.2f}'.rstrip('0').rstrip('.'))
    cursor, previous = x, None
    for character in text:
        name = cmap[ord(character)]
        if previous is not None:
            cursor += kern.get((previous, name), 0) * scale
        glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, cursor, baseline)))
        cursor += hmtx[name][0] * scale + tracking
        previous = name
    return pen.getCommands(), cursor


def crescent(cx, cy, r, dx, dy):
    """The part of circle (cx,cy,r) outside the same circle moved by (dx,dy): one closed path, no seams."""
    d = math.hypot(dx, dy)
    mx, my = cx + dx / 2, cy + dy / 2
    h = math.sqrt(r * r - (d / 2) ** 2)
    ux, uy = -dy / d, dx / d
    p1 = (mx + h * ux, my + h * uy)
    p2 = (mx - h * ux, my - h * uy)
    return (f'M {p1[0]:.2f} {p1[1]:.2f} A {r} {r} 0 1 1 {p2[0]:.2f} {p2[1]:.2f} '
            f'A {r} {r} 0 0 0 {p1[0]:.2f} {p1[1]:.2f} Z')


def disc(full=True, label=76, ring=22):
    """The record in a 512 box: amber rim, grooves (full only), amber crescent label for 'late night'."""
    k = label / 100
    grooves = ''.join(
        f'<circle cx="256" cy="256" r="{r}" fill="none" stroke="{AMBER}" stroke-opacity="0.3" stroke-width="3"/>'
        for r in (166, 146, 126, 106)) if full else ''
    return (f'<circle cx="256" cy="256" r="{196 if full else 192}" fill="{DISC}" stroke="{AMBER}" stroke-width="{ring}"/>'
            f'{grooves}<path d="{crescent(256, 256, label, 34 * k, -26 * k)}" fill="{AMBER}"/>')


def svg(width, height, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}">{body}</svg>\n'


(OUT / 'mark.svg').write_text(svg(512, 512, f'<rect width="512" height="512" rx="96" fill="{BG}"/>' + disc()))
(OUT / 'avatar.svg').write_text(svg(512, 512, f'<rect width="512" height="512" fill="{BG}"/>' + disc(full=False, label=100, ring=34)))

# Horizontal lockup: mark ~215 tall centred on the text block, 44 gap, 40 margins all round.
SCALE = 215 / 512
DISC_R = 207 * SCALE
TEXT_X = 40 + 2 * DISC_R + 44
the, _ = text_path('Georgia Italic.ttf', 'The', TEXT_X - 2, 111, 56)
word, right = text_path('Georgia Bold.ttf', 'Bluesland', TEXT_X, 219, 128, tracking=-2)
WIDTH, HEIGHT = math.ceil(right + 40), 300
mark = f'<g transform="translate({40 - 49 * SCALE:.2f},{150 - 256 * SCALE:.2f}) scale({SCALE:.4f})">{disc()}</g>'
for name, text_color, the_color, background in [
        ('logo-dark', CREAM, AMBER, BG), ('logo-light', BG, AMBER_DARK, CREAM), ('logo-transparent', BG, AMBER_DARK, None)]:
    fill = f'<rect width="{WIDTH}" height="{HEIGHT}" fill="{background}"/>' if background else ''
    (OUT / f'{name}.svg').write_text(svg(WIDTH, HEIGHT, fill + mark + f'<path d="{the}" fill="{the_color}"/><path d="{word}" fill="{text_color}"/>'))
print(WIDTH, HEIGHT)
