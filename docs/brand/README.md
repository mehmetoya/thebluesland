# TheBluesland logo

Built from the Bluesland playlist cover (thebluesland.com/playlists/bluesland): royal blue, white
rounded lettering, an orange acoustic guitar and white notes. The guitar and notes are our own
drawings in the cover's style; the wordmark is Fredoka (SIL Open Font License), converted to outlines.

| Colour | Hex | Where |
|---|---|---|
| Bluesland blue | `#004aad` | background, wordmark on white |
| White | `#ffffff` | wordmark and notes on blue |
| Guitar orange | `#ff904b` | guitar body, "The" on blue |
| Neck brown | `#c4632f` | neck, headstock, pegs |
| Sound hole | `#1b1b1d` | sound hole, bridge |

| File | Use |
|---|---|
| `avatar.png` | Kit profile picture, social avatars (square; safe for circle crops, readable at 48 px) |
| `mark.png` | App-style icon with rounded tile |
| `logo-blue.png` | Primary logo: e-mail header, site header on blue |
| `logo-light.png` | On white pages |
| `logo-transparent.png` | Over light backgrounds |

The SVGs are the sources and need no font. `build.py` regenerates them (needs `fonttools` and the
Fredoka static weights 600/700 instantiated from Google Fonts' `Fredoka[wdth,wght].ttf`, given as
`FREDOKA_DIR`); the PNGs are rendered at 2x with headless Chrome.
