# Toolbar icons

Phosphor filled icons: gear (settings), wrench (tools), pencil-simple (edit), and puzzle-piece (modules).
Source: https://github.com/phosphor-icons/core/tree/2b75f3ad12b420c9504ef05df8d2564a28f8500e/assets/fill
License: see LICENSE.txt (MIT).

Original SVGs are retained in Source as .svg.txt files so Unity does not require an SVG importer. PNGs are rendered at 100 x 100 for a 25-point toolbar slot, using a padded viewBox of -32 -32 320 320. Textures use no compression or mipmaps and retain their original dimensions.

Classic Monokai accents are used for the dark theme, with darker counterparts for contrast on Unity's light theme.

| Icon | Dark theme | Light theme |
| --- | --- | --- |
| settings | #AE81FF | #7545B8 |
| wrench | #66D9EF | #16798B |
| pencil | #E6DB74 | #82721E |
| puzzle | #F92672 | #BF1855 |

The section header icons use the same Phosphor revision, fill style, and MIT license as the toolbar: share-network (global links), question (help), sliders-horizontal (presets), dots-three-outline-vertical (actions), play-circle (video), and user-circle (author). These symbols are solid standalone shapes; the boxed link and ellipsis variants are not used. Their original SVG sources are retained as `Source/header-*.svg.txt`. The white PNGs use a 64 x 64 transparent canvas. Action glyphs are trimmed, fitted within 62 x 48 pixels, and centered; carets are fitted within 48 x 48 pixels. All use a 14-point UI image slot, so visible glyph height is approximately 10.5 points, matching the ink height of the 12-point header labels. Section action hit areas remain 24 points. Filled caret-right and caret-down share the same assets and sizing between section and texture disclosure controls. UI Toolkit applies the editor-theme tint.

The texture clear action uses trash-simple-fill from the same pinned Phosphor revision and MIT license. Its original source is Source/texture-clear.svg.txt, rendered as a 48-pixel-high glyph centered on a 64-pixel canvas for a 14-point image slot.
