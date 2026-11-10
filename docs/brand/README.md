# TheBluesland logo

Late-night record room: an amber-rimmed record whose label is a crescent moon, with a Georgia
wordmark ("The" italic, "Bluesland" bold). Colours are the site's tokens: `#12141b`, `#0c0d12`,
`#d99a4e`, `#8a5220`, `#f7f1e6`.

| File | Use |
|---|---|
| `avatar.png` | Kit profile picture, social avatars (square; safe for circle crops, readable at 48 px) |
| `mark.png` | App-style icon with rounded tile |
| `logo-light.png` | E-mail header, light pages |
| `logo-dark.png` | Dark pages |
| `logo-transparent.png` | Over light backgrounds |

The SVGs are the sources; the wordmark is converted to outlines, so they need no font.
`build.py` regenerates the SVGs (needs `fonttools` and macOS's Georgia); the PNGs are rendered at
2x with headless Chrome.
