# Shared Poiyomi inspector styling

Edit `Resources/ThryTheme.uss`. Default shapes, sizing, and colors are active. The temporary shape selector and all ten experiments have been removed.

## Shared controls

The first block is the shape, sizing, spacing, and typography source for the retained UI:

| Tokens | Coverage |
| --- | --- |
| `--thry-radius-control`, `--thry-radius-button` | Inputs, dropdowns, action buttons, toolbar controls |
| `--thry-radius-header`, `--thry-radius-subheader` | Top-level and nested section headers |
| `--thry-radius-panel`, `--thry-radius-panel-small`, `--thry-radius-card` | Panels, texture groups, cards, node frames |
| `--thry-radius-compact`, `--thry-radius-round`, `--thry-radius-switch` | Badges, small controls, and switches |
| `--thry-border-width`, `--thry-border-width-strong` | Existing thin and strong borders |
| `--thry-control-height`, `--thry-button-height`, `--thry-toolbar-height`, `--thry-window-control-height` | Common control heights |
| `--thry-section-height`, `--thry-nested-section-height` | Section density |
| `--thry-type-*`, `--thry-font-size` | Titles, headings, field labels, small text, captions |
| `--thry-space-*` | Shared positive padding and margin steps |
| `--thry-ramp-*` | Custom ramp height, stroke thickness, handle radius, and paint colors |

Button, subheader, and small-panel radii inherit the control radius by default. Each can be overridden independently. Spacing tokens keep the existing pixel values as their names so the migration is easy to audit; changing a value affects its uses throughout the stylesheet. Geometry needed for joined edges, asset aspect ratios, tiny icons, virtualization, and native control internals remains explicit rather than being forced onto one universal size.

Ramp height uses a length such as `54px`. Ramp stroke width and handle radius use unitless numbers because the custom drawing code consumes them directly. Its paint and drag coordinates share the same inset so larger handles remain aligned with interactions.

The palette below these settings retains the existing Unity dark/light colors. Component rules follow in labeled sections. Color experiments are not active. Remaining component-specific palette details still live in this same file.

## Coverage

The material inspector, search, popup menus, settings and other retained companion windows, texture studio, stencil, pathing, special controls, and multi-material controls all load the shared stylesheet. Fixed settings-search spacing, cross-editor padding, texture slice/face controls, vector-length number fields, search placeholders, and ramp height now use USS instead of overriding it in C#. The custom search icon reads its stroke color from the stylesheet. The ramp reads its own custom drawing properties from USS.

Old `ThryInspector.uss`, `ThrySearch.uss`, and the other component resources are compatibility imports. Keep new styling in `ThryTheme.uss` so existing resource names and asset GUIDs remain valid.

For future experiments, change the shared tokens first. Test narrow and wide inspectors, Unity's light/dark skins, companion windows, and focus/disabled/selected states. Some sizes are coupled (for example, a search field contains an inner field); change related dimensions together. Make permanent visual changes here so all retained surfaces share them.

## Boundaries

The normal inspector is UI Toolkit. A stylesheet cannot change the interiors of legacy IMGUI fallback controls, native dialogs, or third-party embedded tools. Material-authored colors, animated-property markers, explicit shader drawer settings, and layout measurements stay data-driven. Pathing preview paint still represents material data; its neutral background and channel markers read the shared theme. Independent TPS, shader-generator/debugger, and translator UIs have separate styling and require explicit integration before this can be described as a theme for every Poiyomi tool.

## Pathing

Pathing uses the shared panel, subsection, field, selection, focus, and RGBA channel roles. Its preview, headings, action buttons, drag grips, selected-column indicator, labels, and painted lane markers no longer define a separate palette. The path identity colors also style the existing texture-packer channel badges. Material-authored path colors remain preview data. Starting-look and copy-motion actions use the same retained popup menu as the inspector.

The four-column grid retains compact labels and layout constraints to fit narrow inspectors. Shared radii, borders, text sizes, action-button heights, and spacing still propagate into it.

## Header and button borders

Decorative borders on headers and buttons match their own fill colors, including hover and disabled fills. Border widths stay in place so control sizes and focus outlines keep the default geometry. Subsection and positioning panels retain their child frames; those frames use `--thry-subsection-background` to continue the header's color around the children. Focus, selection, and status indicators remain visible.

## Input and dropdown borders

Text, numeric, object, and search fields keep their original contrasting borders. Dropdown borders match their own fill colors, including the rendering-mode and search-filter dropdowns, disabled controls, Pathing, and retained companion windows. Keyboard focus outlines remain visible. The dropdown rules live at the end of `ThryTheme.uss`.

The temporary **Input borders** comparison toggle and its session preference controller have been removed; this appearance is now the shared default.
