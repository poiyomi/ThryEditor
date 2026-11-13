# Shared Poiyomi inspector styling

Edit `Resources/ThryTheme.uss`. The active Studio visual direction uses compact text and section spacing while keeping the established input heights and label columns. There is no temporary theme selector.

## Appearance settings

Poi/Thry/Settings → Theme offers an Inspector Theme dropdown for third-party themes (described below), independent Dark gray, Medium gray, and Light gray sliders from −20 to 20, plus dropdowns for 11–14 px text and independent 18/20/22 px property heights and 18/20/22/24 px header heights. Gray sliders use broader neutral ranges for each Unity skin, with foreground adjustments at the extremes. Enabled text adapts to the extended range; the gray controls can also bring neighboring surfaces closer together. Reset appearance restores the default theme and shades, 12 px text, 18 px property height, and 22 px header height. Nested headers are 2 px shorter than the selected header height with an 18 px minimum.

Changes update attached inspectors, menus, and retained tool windows immediately without rebuilding the material editor. Preferences are saved per project in Thry/Config.json, or, with "Use shared appearance on this computer" on, in the per-user `ThryEditor/InspectorAppearance.json` in the local application data folder. Text changes use the shared typography tokens; input heights, 18 px checkboxes, locked-material dimming, material previews, and explicit drawer font sizes keep their existing behavior. Legacy IMGUI controls and independently styled tools retain their own appearance.

## Third-Party Themes

A theme is a stylesheet whose file name ends in `.thrytheme.uss`, placed anywhere in Assets or in a package. Installing one changes nothing until it is chosen in Poi/Thry/Settings → Theme → Inspector Theme, which lists Default and every installed theme by file name without the suffix. `RetainedTheme` then attaches the chosen theme after `ThryTheme.uss` on every inspector, menu, and retained window, so its rules win over base rules with the same selector. Keep theme files outside `_PoiyomiShaders` so reinstalling or updating Poiyomi does not remove them. ThryEditor ships one sample, `Examples/Themes/Classic.thrytheme.uss`, a Unity 2017–style theme for both skins that uses the gradient hooks below; copy it outside the Poiyomi folder and rename it to start a theme of your own.

One theme is active at a time. The choice is saved with the other Theme settings, so it follows "Use shared appearance on this computer", and Reset appearance returns to Default. If two themes share a name, the one in Assets wins over one in a package. Adding, editing, renaming, or deleting a theme updates open inspectors, windows, and the dropdown. A chosen theme that is no longer installed stays listed as not found and the default look is used until it returns or another theme is chosen.

Override tokens rather than control rules. Token names are what themes rely on, so rename or remove a `--thry-*` token only with care. Scope colors to a skin: some light-skin values are declared on `.thry-light` alone, so a color set on plain `.thry-inspector` in a theme would also replace them in the light skin.

```css
/* Both skins: shapes, spacing, and type. */
.thry-inspector, .thry-dropdown-menu {
    --thry-radius-header: 2px;
}

/* Dark skin colors. */
.thry-inspector.thry-dark, .thry-dropdown-menu.thry-dark {
    --thry-accent-color: #ff79c6;
    --thry-monokai-pink: #ff5fa2;
}

/* Light skin colors. */
.thry-inspector.thry-light, .thry-dropdown-menu.thry-light {
    --thry-accent-color: #a8306a;
    --thry-monokai-pink: #b0185a;
}
```

A gray slider other than zero sets the surface, panel, input, section, and action-button tokens with three-class selectors, which win over a theme's values for those tokens. Leave the sliders at zero with a theme that recolors surfaces, or match those selectors in the theme. To restyle a specific control, the theme's selector must be at least as specific as the base rule it replaces. IMGUI drawers and the C# colors in `Styles.cs` are not affected by themes.

### Gradient Hooks

USS has no gradients, so themes get two custom properties instead: `--thry-gradient-top` and `--thry-gradient-bottom`. Set either or both on a `Button`, a dropdown (`.unity-base-popup-field__input`), a text input (`.unity-base-text-field__input`), or a section header (`.thry-section-header`), and `RetainedGradient` paints a vertical gradient on that surface. One property alone paints a flat color. The colors become a small generated texture set as the surface's inline background image, so the gradient fills the padding, follows the rounded corners, sits over the background color, and stays under the border, text, and children. It replaces any background image a rule gives the surface. Colors may be translucent, which tints the background instead of replacing it. (Drawing the gradient as a mesh instead left flat strips at the sides of text inputs and headers, because a surface with hidden overflow clips its own drawing to its content box.)

- A surface whose background color is transparent is not painted, so icon, link, and search buttons stay clear.
- Set the properties on the surface's own selector; like every custom property read in C#, they are not inherited from a parent rule. Pseudo-classes such as `:hover`, `:disabled`, and `:active` work; also give the surface's normal state a gradient, as Classic does, so a state's colors are replaced when it ends.
- With an opaque gradient, a button's background color only shows through its border, so Classic sets the button fill tokens to its outline colors.

`ThryTheme.uss` sets neither property. The painters are installed only while the chosen theme's file mentions `--thry-gradient`, so the default theme and themes without gradients cost nothing. While a gradient theme is active, each inspector or window is scanned for new surfaces after its layout changes.

## Monokai palette

Use `--thry-monokai-pink`, `-green`, `-yellow`, `-orange`, `-cyan`, `-purple`, and `-foreground` from `Resources/ThryTheme.uss` for shared accents. Each has a light-skin counterpart in the same palette block. Reuse the existing neutral surface, button-state, text, spacing, radius, and typography tokens for control styling.

`--thry-animation-color` (A) and `--thry-animation-renamed-color` (RA) alias green and yellow. Preset, channel, toolbar, stencil, and UV-grid colors reference the same palette. Discarded UV tiles use their own pink `--thry-uv-discard-*` tokens, described below; mixed discard values use purple. Legacy C# animation colors in `Styles.cs` still supply IMGUI and existing inline animation indicators; keep their skin equivalents aligned when changing the palette defaults.

ButtonVector, Vector4Toggles, and multi-float tile buttons show their on state through the `--thry-toggle-on-*` tokens: a green fill that differs from the off buttons in brightness, not just hue, white bold text, and a 2px outline. The outline is Monokai green in the dark skin and dark green in the light skin, where a bright edge would disappear against light gray. Hover, focus, and press keep the outline, and locked materials use the muted disabled variants. Keep white text at 4.5:1 or better on every fill. The on state reduces its padding by the extra border width so the label stays put, so change `--thry-toggle-on-border-width` and that padding together.

Discarded UV tiles get the same treatment in pink through the `--thry-uv-discard-*` tokens: a bold pink fill, white bold labels, and a 2px outline that is light pink in the dark skin and dark wine in the light skin. Pink keeps the meaning "hidden", so discarded tiles do not look like enabled toggles. The same contrast targets, locked variants, and padding rule apply, and the discard outline takes precedence over the purple mixed-animation edge while the Mixed badge remains.

## Studio direction

- The original neutral dark/light palettes and focus colors, with the new Studio shapes and layout.
- The title row ends in a row of icon tools: Presets first (the header presets glyph in `--thry-preset-color`), then Collapse All, registered top bar entries such as Edit Shader Sections, Fast Material Editing, Screenshot Inspector (which asks where to save and remembers the folder), Tools, Language, and Settings. The shader name is a button with a caret that lists the shaders published in the same menu folder, with URP variants in a submenu, and switches to the one picked; a locked material shows the name without the caret. The search strip ends in two icon buttons for the type and status filters. Each opens its menu; a filter that is on fills its button and tints its icon, and the tooltip names the current choice.
- Enums with two to four short choices (22 characters or fewer in total) show each choice as a button, and the picked one uses the shared selected-button surface. A choice button has hover text only while its label is cut off. Inline reference enums, enums with value actions, and enums with registered labels stay dropdowns. Channel pickers, whose choices include R, G, B and A, may have up to six choices; their buttons are sized to their labels and the four letters use the channel colors.
- Alt+click on a section header or its fold arrow opens or closes that section and every section inside it as one edit.
- A theme index beside a color is a small dropdown captioned Theme (`thry-theme-chip`), sized to its content. While a theme is picked it adds a round swatch of that Global Themes color and its number, or CC or AL plus a number for ColorChord and AudioLink themes. It opens the same list as a full dropdown.
- A property's right-click menu starts with Reset, Copy Value and Paste Value, each with a drawn icon, followed by the Animated and Renamed marks. The name, path and keyframe copies sit under Copy as, and Favorite, Set note and Edit label come last. A section's menu follows the same order: Reset, Copy and Paste with those icons, then the other paste and text entries, preset entries, Search Section and extension entries, and Set Note last. `RetainedMenu.TrimSeparators` drops separators left at either end or doubled by groups that are absent.
- Preset and animation markings use Monokai cyan for P, purple for PA (animation-only preset content), green for A, and yellow for RA. Animation labels and header dots share these colors, with darker counterparts for the light skin. In section headers the marker follows the title and is removed while the section is not in the preset; property rows keep an empty marker gutter. Preset colors live in `--thry-preset-color` and `--thry-preset-animation-color`; animation colors and IMGUI equivalents live in `Styles.cs`.
- Shader-lock, Presets, rendering mode, and all other filled action buttons share the darker action surface and hover colors (`--thry-action-background` and `--thry-action-hover-background`), including decal positioning and Bake Color Adjust. In the light skin, the lock button, rendering dropdown, and render queue box instead share the raised surface of in-row action buttons (#e6e6e6, #f0f0f0 on hover, #a8a8a8 border), with the focus color on focus and the disabled fill and border when disabled. The title and search strip, search strip and action block, and action block and first section are separated by 4px gaps. Rendering mode, the render queue and the lock button share one line with 6px gaps. The queue is a number box captioned Queue inside its border, and clicking that caption lists the named queues (From Shader, Background, Geometry, AlphaTest, Transparent, Overlay); the box's tooltip names the current one, such as Geometry +452. It is shown while the Render Queue setting is on and replaces the Render Queue row that used to sit above the footer. The rendering dropdown fills the rest of the line and carries a muted Rendering caption, and its list opens under the value to the right of that caption; the lock button is sized to its label, Lock with a closed padlock or Unlock with an open one, both drawn in the button's text color. Search and toolbar controls use a 22px control height, with 20px inner search fields.
- All retained buttons automatically share normal, hover, pressed, disabled, and focused styling, including Bake Color Adjust, positioning, Pathing, toolbars, and companion windows. Disabled actions keep muted text and respond visually to hover while remaining unclickable. Bake Color Adjust has a Monokai yellow palette icon; Raycast and Scene Tools retain their original green icons, with light-skin equivalents for each.
- Connected main section surfaces and standalone header bars for nested groups.
- Hover brightens the section header surface using separate main/nested hover colors. Only the directly hovered icon brightens; other header icons stay steady. Keyboard focus and linked-state indicators remain visible.
- Main inspector sections use bold labels and a separate `--thry-root-section-background` shade: lighter than nested headers in the dark skin, darker in the light skin. Their expanded contents have a 9px left inset without a guide line. Nested contents also use a 9px left inset without guide lines, with 4px gaps above and below. Nested headers retain `--thry-subsection-background`; their contents have no surrounding frame or right padding, preserving left indentation for hierarchy; unrelated windows and Texture Studio keep their existing section palette.
- Outlined editable fields, quiet property rows, neutral checkboxes, and slim slider handles without outline rings.
- Range sliders (MultiSlider and paired VectorToSliders, such as Pathing's Gradient Range) put the same slim handles at both ends of the track. The span between them is a 4px gradient from `--thry-range-fill-start` (deeper blue, low end) to `--thry-range-fill-end` (light blue, high end), with darker counterparts in the light skin. Dragging inside the span moves both ends; with keyboard focus, only the handle that the arrow keys move stays bright.
- Basic material fields, vector components, slider number boxes, and inline actions use `--thry-control-height`, overridden inside `.thry-material-controls` by Settings → Theme → Property height (18, 20, or 22px; default 18px). Visible input surfaces and color fields also use this height. Property checkboxes use a centered 16px square in 18px rows and an 18px square in 20px or 22px rows, with a stylesheet-drawn box and an uncompressed 128px checkmark texture generated from `Resources/ThryToolbar/Source/property-checkmark.svg.txt`. The white artwork uses the theme value color as its tint. Ordinary rows have 1px top and bottom margins; inline reference rows have no vertical margins, so they do not enlarge their parent row. Pathing cells cannot stretch these fields taller; previews and multi-row drawers keep their own height.
- Texture rows start with a small texture square and its picker, as in Unity's material inspector. The square follows the property height and is always visible; clicking it when empty opens the picker, and a texture can be dropped anywhere on the row. The value column holds the inline strength or color, or the texture's name, followed by its estimated VRAM size and clear button. In narrow inspectors the caption can extend into the value column, whose name and size give way first.
- Footer icons use their own 32px image and 36px button tokens. They sit beside the credit while they fit. Below that they move under it, centered at full size, and wrap into rows of equal length so one icon never sits alone.
- Shared surface and interaction colors for Pathing, retained menus, and companion windows.

The last section of the stylesheet defines this treatment. Palette, shape, and density tokens remain shared. Default inspector text is 12px, top-level headers default to 22px, nested headers default to 20px, and section gaps are 2px, including above a nested header that directly follows a field. Text size is independent of the 24px collapsed top-level spacing interval; ordinary material input hit targets default to 18px, with a 20px row interval including margins. Small captions, preview dimensions, and explicit drawer font sizes retain their own settings. `RetainedMaterialBody` exposes expansion through `thry-section-open`.

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
| `--thry-color-wheel-ring`, `--thry-color-wheel-gap`, `--thry-color-wheel-marker` | Color picker hue ring thickness, gap between the ring and the triangle's disc, and triangle marker radius (unitless) |

Button, subheader, and small-panel radii inherit the control radius by default. Each can be overridden independently. Spacing tokens keep the existing pixel values as their names so the migration is easy to audit; changing a value affects its uses throughout the stylesheet. Geometry needed for joined edges, asset aspect ratios, tiny icons, virtualization, and native control internals remains explicit rather than being forced onto one universal size.

Ramp height uses a length such as `54px`. Ramp stroke width and handle radius use unitless numbers because the custom drawing code consumes them directly. Its paint and drag coordinates share the same inset so larger handles remain aligned with interactions. The light skin darkens the line to #3775a8 and the anchors to #404040, which keep 3.9:1 and 5.2:1 against its #b8b8b8 ramp background; the dark-skin colors measure about 1.2:1 there. The same colors paint the four-point curve editor (`.thry-curve4`), and the active anchor uses the line color.

The palette below these settings retains the original neutral dark/light colors. Additional Studio roles, including primary actions, slider handles, and disabled fields, live in the final section. Component rules follow in labeled sections; remaining component-specific palette details still live in this same file.

## Coverage

The material inspector, search, popup menus, settings and other retained companion windows, texture studio, stencil, pathing, special controls, and multi-material controls all load the shared stylesheet. Fixed settings-search spacing, cross-editor padding, texture slice/face controls, vector-length number fields, search placeholders, and ramp height now use USS instead of overriding it in C#. The custom search icon reads its stroke color from the stylesheet. The ramp reads its own custom drawing properties from USS.

The color picker is a retained companion window with the shared theme. Its wheel reads ring, gap, marker, disc, outline, and focus values from USS; slider tracks read their focus color; the new and original swatches, dropdowns, and buttons use the shared tokens and control states.

Attach the theme once on each standalone inspector or window root with `RetainedTheme.Install` (or `RetainedWindow.Style`, which calls it). It adds `ThryTheme.uss` and, after it, the third-party theme chosen in Settings. Child controls inherit the theme; do not attach additional copies or component wrapper sheets.

For future experiments, change the shared tokens first. Test narrow and wide inspectors, Unity's light/dark skins, companion windows, and focus/disabled/selected states. Some sizes are coupled (for example, a search field contains an inner field); change related dimensions together. Make permanent visual changes here so all retained surfaces share them.

Button surface states are defined once near the start of `ThryTheme.uss`. Use `--thry-button-background`, `--thry-button-hover-background`, `--thry-button-pressed-background`, and `--thry-button-disabled-background` to theme them. New `Button` elements inherit these rules without an opt-in class. Transparent icon/navigation buttons and selected buttons override only these tokens; texture disclosure labels stay transparent and borderless in every state. Component rules keep layout, typography, and icon colors. Do not add component-specific button background or hover rules.

## Boundaries

The inspector is UI Toolkit. A stylesheet cannot change the interiors of the IMGUI controls it hosts, native dialogs, or third-party embedded tools. Material-authored colors, animated-property markers, explicit shader drawer settings, and layout measurements stay data-driven. Pathing preview paint still represents material data; its neutral background and channel markers read the shared theme. Independent TPS, shader-generator/debugger, and translator UIs have separate styling and require explicit integration before this can be described as a theme for every Poiyomi tool.

## Texture Studio

Texture Studio uses the shared neutral surfaces, controls, and text colors. Its canvas is darker through `--thry-studio-canvas`; nodes retain the shared panel and section surfaces. Settings inside the output node use compact, transparent disclosure rows instead of inspector category bars. Hover and keyboard focus remain visible.

Output settings have subtle section dividers, aligned fields, and inset help text. Heading text stays neutral when focused. Texture-picker icons have transparent backgrounds; light-skin channel pins use hollow colored rings so GraphView's dark cap fill does not show through.

Graph ports read the same Monokai RGBA tokens as Pathing through GraphView's `--port-color`, so connected wires follow those colors too. MAX uses the muted text color. The light skin uses the shared darker channel equivalents and softer section, field, and disabled surfaces.

## Outline Bake

The Outline Bake section replaces its property rows with goal cards, drawn by `PoiOutlineBakeArt` with Painter2D. Each goal sets `--thry-outline-bake-accent` to a Monokai color: cyan for Hard Edges, green for Smooth Normals, orange for Crevice Shrink, and purple for Texture UVs. The drawings read `--thry-outline-bake-band` and `--thry-outline-bake-shape` from USS. A goal's card shows its on state through its border, check badge, and bold title as well as its color. Cards wrap in pairs, four across or two by two. The bake button uses the Monokai pink surface while the bake is out of date.

## Pathing

Pathing uses the shared panel, subsection, field, selection, focus, and RGBA channel roles. Its preview, headings, action buttons, drag grips, selected-column indicator, labels, and painted lane markers no longer define a separate palette. The Monokai channel colors (pink, green, cyan, and off-white, with darker light-skin equivalents) also style the existing texture-packer channel badges. Material-authored path colors remain preview data. Starting-look and copy-motion actions use the same retained popup menu as the inspector.

Motion preview is unframed, with a Hide/Show control beside Pause and Solo. Hiding the lanes and scrubber suspends the preview timer; showing them preserves the previous play/pause state. Preview limitations are available in its tooltip.

The four-column grid retains compact labels and layout constraints to fit narrow inspectors. Shared radii, borders, text sizes, action-button heights, and spacing still propagate into it.

Pathing deliberately overrides the flat decorative treatment with continuous column dividers, a framed main grid, and subtle alternating column fills. `--thry-pathing-column-divider` and `--thry-pathing-column-alternate` control this local contrast in dark and light skins. Disabled paths dim their contents while keeping the column dividers visible; Mask routing uses the same separation.

## Header and button borders

Decorative borders on headers and buttons match their own fill colors, including hover and disabled fills. Border widths stay in place so control sizes and focus outlines keep the default geometry. Subsection and positioning panels are transparent, with no surrounding frame or right padding around their contents. Left padding preserves the indentation without guide lines. Their standalone header bars use `--thry-subsection-background`. Focus, selection, and status indicators remain visible.

## Input and dropdown borders

Text, numeric, object, and search fields keep their original contrasting borders. Dropdown borders match their own fill colors, including the rendering-mode and search-filter dropdowns, disabled controls, Pathing, and retained companion windows. Keyboard focus outlines remain visible. The dropdown rules live at the end of `ThryTheme.uss`.

Property and companion-window dropdowns use a distinct button-like surface through `--thry-dropdown-background` and `--thry-dropdown-hover-background`, with a high-contrast arrow. Dark-skin dropdowns are lighter than editable input wells; light-skin dropdowns are darker. The rendering-mode control retains its matching Presets surface.

Dropdown fields retain the shared control height, with their fill inset by `--thry-border-width` to match the interior of outlined text inputs. This keeps the visible fill from occupying the entire row without reducing the field's click target.

The temporary **Input borders** comparison toggle and its session preference controller have been removed; this appearance is now the shared default.
