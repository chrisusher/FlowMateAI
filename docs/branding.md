# FlowMate AI brand guidelines

Asset location: `Web/wwwroot/branding/` (with the 32px browser fallback at `Web/wwwroot/favicon.png`).

The approved **Focus Orbit** mark uses an open, off-center ring, a waypoint, and a small focus diamond. Its original vector geometry is designed for FlowMate AI.

## Assets

| File | Use |
| --- | --- |
| `flowmate-symbol.svg` | Primary standalone symbol on light surfaces; transparent background |
| `flowmate-symbol-dark.svg` | Light symbol for dark surfaces; transparent background |
| `flowmate-symbol-mono.svg` | Single-colour indigo symbol; transparent background |
| `flowmate-symbol-mono-reversed.svg` | Single-colour white symbol for dark surfaces; transparent background |
| `flowmate-wordmark.svg` | Primary symbol and wordmark; transparent background |
| `flowmate-wordmark-dark.svg` | Light wordmark for dark surfaces; transparent background |
| `flowmate-app-icon.svg` | Rounded-square app icon and browser favicon master |
| `*.png` | Transparent raster exports; the app-icon PNGs have the indigo tile |

The SVG files are the editable vector masters. PNG files are exported from these masters at the pixel sizes in their filenames. `Web/wwwroot/index.html` uses the SVG favicon with 16px and 32px PNG fallbacks and the 180px Apple touch icon. The sidebar and sign-in screen use the standalone SVG symbol.

## Colour and use

- Orbit indigo: `#4240B7` (RGB 66, 64, 183)
- Waypoint mint: `#25A27C` (RGB 37, 162, 124); dark-surface variant `#60C5A2`
- Wordmark charcoal: `#242723`; secondary `.ai` gray: `#969994`
- App-icon tile: `#4240B7`; reversed symbol: `#FFFFFF`
- Clear space: keep at least one quarter of the symbol's viewBox (16/64 units) around the mark and wordmark lockup.
- Minimum digital symbol size: 16px; prefer 24px or larger for UI placement. Use the app-icon master for tiled launcher/favicon contexts.
- Minimum wordmark width: 150px. At smaller widths, use the symbol alone.
- Do not re-colour the waypoint independently of the supplied light/dark variants or add a tile behind the standalone symbol.

## Type and licensing

The wordmark SVG uses live `Manrope` text (800 for “flowmate”, 500 for “.ai”), which remains editable. The app already loads Manrope from Google Fonts. Manrope is licensed under the [SIL Open Font License 1.1](https://github.com/google/fonts/blob/main/ofl/manrope/OFL.txt). No font file is bundled here. Where Manrope is unavailable, the SVG falls back to Arial; for final marketing use, render the wordmark with Manrope or convert the text to paths in a vector editor.

The symbol is original FlowMate AI artwork; no third-party logo or stock artwork is included.
