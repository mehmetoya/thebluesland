"""Builds TheBluesland logo set from the Bluesland playlist cover's identity (docs/brand).

Royal blue, white rounded lettering (Fredoka, SIL OFL), an orange acoustic guitar and white notes.
Run with fonttools installed: FREDOKA_DIR=<dir with Fredoka-600.ttf, Fredoka-700.ttf> python build.py
(static weights instantiated from Google Fonts' Fredoka[wdth,wght].ttf). See README.md.
"""
import math
import os
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

OUT = Path(__file__).parent
FONT_DIR = Path(os.environ['FREDOKA_DIR'])
BLUE, WHITE, ORANGE, NECK, DARK, RING = '#004aad', '#ffffff', '#ff904b', '#c4632f', '#1b1b1d', '#e8773a'


def text_path(weight, text, x, baseline, size, tracking=0.0):
    """Outlines for text set at (x, baseline) with extra tracking; no font needed to view the SVG."""
    font = TTFont(FONT_DIR / f'Fredoka-{weight}.ttf')
    scale = size / font['head'].unitsPerEm
    cmap, glyphs, hmtx = font.getBestCmap(), font.getGlyphSet(), font['hmtx']
    pen = SVGPathPen(glyphs, lambda v: f'{v:.2f}')
    cursor = x
    for character in text:
        name = cmap[ord(character)]
        glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, cursor, baseline)))
        cursor += hmtx[name][0] * scale + tracking
    return pen.getCommands(), cursor


def guitar(angle=40):
    """Acoustic guitar drawn upright in a 512 box (centre 256,259), then tilted like the cover's."""
    pegs = ''.join(f'<circle cx="{x}" cy="{y}" r="7" fill="{NECK}"/>' for x in (225, 287) for y in (50, 68, 86))
    return (f'<g transform="rotate({angle} 256 259)">'
            f'<rect x="228" y="30" width="56" height="72" rx="14" fill="{NECK}"/>{pegs}'
            f'<rect x="241" y="96" width="30" height="170" fill="{NECK}"/>'
            f'<circle cx="256" cy="272" r="78" fill="{ORANGE}"/><circle cx="256" cy="388" r="102" fill="{ORANGE}"/>'
            f'<circle cx="256" cy="304" r="44" fill="none" stroke="{RING}" stroke-width="7"/>'
            f'<circle cx="256" cy="304" r="34" fill="{DARK}"/>'
            f'<rect x="218" y="414" width="76" height="16" rx="6" fill="{DARK}"/></g>')


def notes(x, y, scale, colour=WHITE):
    """An eighth note and a beamed pair, as on the cover."""
    eighth = ('<ellipse cx="0" cy="0" rx="13" ry="9.5" transform="rotate(-20)"/>'
              '<rect x="9" y="-58" width="5.5" height="57"/>'
              '<path d="M 14.5 -58 C 22 -44 40 -40 34 -16 C 31 -30 24 -35 14.5 -38 Z"/>')
    beamed = ('<g transform="translate(58 -6)"><ellipse cx="0" cy="0" rx="13" ry="9.5" transform="rotate(-20)"/>'
              '<ellipse cx="44" cy="-8" rx="13" ry="9.5" transform="rotate(-20 44 -8)"/>'
              '<rect x="9" y="-60" width="5.5" height="59"/><rect x="53" y="-68" width="5.5" height="59"/>'
              '<path d="M 9 -60 L 58.5 -68 L 58.5 -55 L 9 -47 Z"/></g>')
    return f'<g fill="{colour}" transform="translate({x} {y}) scale({scale})">{eighth}{beamed}</g>'


def svg(width, height, body):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}">{body}</svg>\n'


# Square marks: guitar fills the tile, notes in the free top-left corner (inside a circle crop).
inner = f'<g transform="translate(40 52) scale(0.86)">{guitar()}</g>' + notes(118, 196, 1.2)
(OUT / 'avatar.svg').write_text(svg(512, 512, f'<rect width="512" height="512" fill="{BLUE}"/>' + inner))
(OUT / 'mark.svg').write_text(svg(512, 512, f'<rect width="512" height="512" rx="96" fill="{BLUE}"/>' + inner))

# Horizontal lockups: guitar + "The" (small) over "Bluesland" (large), 40 margins.
GUITAR = f'<g transform="translate(0 -8) scale(0.62)">{guitar()}</g>'
TEXT_X = 296
for name, background, the_colour, word_colour in [
        ('logo-blue', BLUE, ORANGE, WHITE),
        ('logo-light', WHITE, BLUE, BLUE),
        ('logo-transparent', None, BLUE, BLUE)]:
    the, _ = text_path(600, 'The', TEXT_X + 4, 112, 56)
    word, right = text_path(700, 'Bluesland', TEXT_X, 226, 136, tracking=-1)
    width = math.ceil(right + 40)
    fill = f'<rect width="{width}" height="300" fill="{background}"/>' if background else ''
    (OUT / f'{name}.svg').write_text(svg(width, 300, fill + GUITAR + f'<path d="{the}" fill="{the_colour}"/><path d="{word}" fill="{word_colour}"/>'))
    print(name, width)
