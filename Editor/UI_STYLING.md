# Shared Poiyomi inspector styling

Edit `Resources/ThryTheme.uss`. The active Studio visual direction keeps the established control heights, text sizes, and label columns while changing surfaces, shapes, and hierarchy. There is no temporary theme selector.

## Studio direction

- The original neutral dark/light palettes and focus colors, with the new Studio shapes and layout.
- The original Monokai icons in a compact toolbar well, and a joined search/filter strip that stacks at narrow widths.
- Neutral shader-lock, Presets, and rendering-mode actions with matching surfaces and hover states.
- All retained buttons automatically share normal, hover, pressed, disabled, and focused styling, including Bake Color Adjust, positioning, Pathing, toolbars, and companion windows. Disabled actions keep muted text and respond visually to hover while remaining unclickable. Bake Color Adjust has a Monokai yellow palette icon; Raycast and Scene Tools retain their original green icons, with light-skin equivalents for each.
- Connected section surfaces and matching header/child frames for nested groups.
- Outlined editable fields, quiet property rows, neutral checkboxes, and slim slider handles without outline rings.
- Basic material fields, vector components, slider number boxes, and inline actions use the same `--thry-control-height` (22px). Pathing cells cannot stretch these fields taller; previews and multi-row drawers keep their own height.
- Texture fields show a thumbnail only when assigned and use the same text contrast for empty and assigned values.
- Shared surface and interaction colors for Pathing, retained menus, and companion windows.

The last section of the stylesheet defines this treatment. Palette and shape tokens remain shared; section heights, field heights, hit targets, and font sizes retain their existing values. `RetainedMaterialBody` exposes expansion through `thry-section-open`. Toolbar icons retain their original Monokai artwork and colors.

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

The palette below these settings retains the original neutral dark/light colors. Additional Studio roles, including primary actions, slider handles, and disabled fields, live in the final section. Component rules follow in labeled sections; remaining component-specific palette details still live in this same file.

## Coverage

The material inspector, search, popup menus, settings and other retained companion windows, texture studio, stencil, pathing, special controls, and multi-material controls all load the shared stylesheet. Fixed settings-search spacing, cross-editor padding, texture slice/face controls, vector-length number fields, search placeholders, and ramp height now use USS instead of overriding it in C#. The custom search icon reads its stroke color from the stylesheet. The ramp reads its own custom drawing properties from USS.

Old `ThryInspector.uss`, `ThrySearch.uss`, and the other component resources are compatibility imports. Keep new styling in `ThryTheme.uss` so existing resource names and asset GUIDs remain valid.

For future experiments, change the shared tokens first. Test narrow and wide inspectors, Unity's light/dark skins, companion windows, and focus/disabled/selected states. Some sizes are coupled (for example, a search field contains an inner field); change related dimensions together. Make permanent visual changes here so all retained surfaces share them.

Button surface states are defined once near the start of `ThryTheme.uss`. Use `--thry-button-background`, `--thry-button-hover-background`, `--thry-button-pressed-background`, and `--thry-button-disabled-background` to theme them. New `Button` elements inherit these rules without an opt-in class. Transparent icon/navigation buttons and selected buttons override only these tokens; texture disclosure labels stay transparent and borderless in every state. Component rules keep layout, typography, and icon colors. Do not add component-specific button background or hover rules.

## Boundaries

The normal inspector is UI Toolkit. A stylesheet cannot change the interiors of legacy IMGUI fallback controls, native dialogs, or third-party embedded tools. Material-authored colors, animated-property markers, explicit shader drawer settings, and layout measurements stay data-driven. Pathing preview paint still represents material data; its neutral background and channel markers read the shared theme. Independent TPS, shader-generator/debugger, and translator UIs have separate styling and require explicit integration before this can be described as a theme for every Poiyomi tool.

## Pathing

Pathing uses the shared panel, subsection, field, selection, focus, and RGBA channel roles. Its preview, headings, action buttons, drag grips, selected-column indicator, labels, and painted lane markers no longer define a separate palette. The Monokai channel colors (pink, green, cyan, and off-white, with darker light-skin equivalents) also style the existing texture-packer channel badges. Material-authored path colors remain preview data. Starting-look and copy-motion actions use the same retained popup menu as the inspector.

Motion preview is unframed, with a Hide/Show control beside Pause and Solo. Hiding the lanes and scrubber suspends the preview timer; showing them preserves the previous play/pause state. Preview limitations are available in its tooltip.

The four-column grid retains compact labels and layout constraints to fit narrow inspectors. Shared radii, borders, text sizes, action-button heights, and spacing still propagate into it.

Pathing deliberately overrides the flat decorative treatment with continuous column dividers, a framed main grid, and subtle alternating column fills. `--thry-pathing-column-divider` and `--thry-pathing-column-alternate` control this local contrast in dark and light skins. Disabled paths dim their contents while keeping the column dividers visible; Mask routing uses the same separation.

## Header and button borders

Decorative borders on headers and buttons match their own fill colors, including hover and disabled fills. Border widths stay in place so control sizes and focus outlines keep the default geometry. Subsection and positioning panels retain their child frames; those frames use `--thry-subsection-background` to continue the header's color around the children. Focus, selection, and status indicators remain visible.

## Input and dropdown borders

Text, numeric, object, and search fields keep their original contrasting borders. Dropdown borders match their own fill colors, including the rendering-mode and search-filter dropdowns, disabled controls, Pathing, and retained companion windows. Keyboard focus outlines remain visible. The dropdown rules live at the end of `ThryTheme.uss`.

Property and companion-window dropdowns use a distinct button-like surface through `--thry-dropdown-background` and `--thry-dropdown-hover-background`, with a high-contrast arrow. Dark-skin dropdowns are lighter than editable input wells; light-skin dropdowns are darker. The rendering-mode control retains its matching Presets surface.

Dropdown fields retain the shared control height, with their fill inset by `--thry-border-width` to match the interior of outlined text inputs. This keeps the visible fill from occupying the entire row without reducing the field's click target.

The temporary **Input borders** comparison toggle and its session preference controller have been removed; this appearance is now the shared default.
