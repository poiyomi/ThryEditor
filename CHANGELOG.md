# [2.73.7]
## Changes
- Optimized drawing sections by skipping ones that have no properties in them.
- Repeated property declarations are now distinct in the Cross Shader Editor, accounting for properties that are declared twice (such as `_RimBlur`, `_OutlineSaturation`, etc.).
- `A`/`RA` tagged properties are now carried through Global Links.
- Material resets are now detected against the correct undo group, otherwise resetting a material had to be done twice before animated properties cleared.
- The Cross Shader Editor's layout is now balanced when a drawer exits the GUI.
- The Cross Shader Editor now refreshes when a target is edited elsewhere.
- `[ThryHideInInspector]` attributes are gathered once per shader now.
- Several optimizations implemented that should resolve some inspector perfomance issues in the UI when various sections are expanded.

# [2.73.6]
## Fixes
- Increased height of Material Lock Manager toolbar so that it still appears fine on 1080p displays.
- Fixed an old regression that caused Render Queues to show incorrect readings on custom values.
  - This means Render Queue value readings now line up with Unity's convention. As an example, if the Render Queue is set to 2225, it will now correctly show as `Geometry +225` and a value of 2226 will now correctly appear as `AlphaTest -224`.

# [2.73.5]
## Added
- **Added Stencil Calculator**, contributed by MonadoArt.
  - New `[ThryStencilCalculator]` decorator that simulates the stencil test in the Inspector and shows the resulting buffer value as an editable bit grid.
  - Reference Value, Read Mask, and Write Mask can be edited bit by bit, with masked bits dimmed so it is clear which ones the test actually reads.
  - New `[ThryStencilSummary]` decorator that draws the same test as a decision flow, showing which of the Pass, ZFail, and Fail operations the outcome selects.
  - Added a `Simulate ZFail` toggle so the depth-fail case can be previewed alongside Pass and Fail.
  - Can be implemented to separate `Front` and `Back` stencils, e.g. `[ThryStencilCalculator(Back)]`.
- Added `[ByteSlider]`. Similar to `[IntRange]`, this slider includes a foldout revealing the individual bits.
- Added `[ByteBitField]`, a row of 8 toggles for editing the bits of a 0-255 value.
- Added `[GUILib.SliderFoldout]`, a reusable slider-with-foldout row for use by other drawers.
- Shader Developers: The example shader Thry/Example 3 demonstrates the Stencil Calculator's implementation technique.

## Changes
- Material Lock Manager now uses persistence in the sorting options. Whichever sorting options you last used will be the same upon re-opening the panel.
- Updated sorting options in Material Lock Manager with `All (Split)` sorting option, which should feel similar to the old Material Lock Manager.

# [2.73.4]
## Changes
- **Refreshed the Material Lock Manager**
  - New Toolbar, allowing the ability to Group, Filter, and Search for Materials directly in the UI.
    - Grouping: Sort the list by Shader, Folder, and Prefab/Scene.
    - Filter: Sort the list by Materials that are Locked, Unlocked, or ones that Need Attention.
    - Search Bar: Shows only Materials that match the typed-in keyword.
    - By default, the utility filters through the `Project/Assets` folder. Enabling `Packages` will expand the list to include found materials inside the `Project/Packages` directory.
  - Window Title now matches the actual script function.
  - Fixed an issue where the list and GUI are computed every frame. It is now computed once per rebuild, saving greatly on CPU.
  - Fixed an issue where script compiling triggered a full project re-scan.
- Fixed an issue where `GetRoot()` re-read `mat.GetParent()` every iteration, causing deep variant chains resolving to the wrong materials.

# [2.73.3]
- Fixed leftover call on GUI.

# [2.73.2]
- Fixed inlinedincludes for shaders that use it (very rare).
- Fixed some GUI issues if this version is being used on very... very old legacy shaders.

# [2.73.1]
## Changes
- Fixed a nasty null element found when running `PropertyValueAction`, which mainly affected 9.3 and older shaders.
- Fixed a rare bug where the `shader_master_label` would get an asterisk* for some weird reason.

# [2.73.0]

**THIS IS A MAJOR UPDATE!**

## Shader Optimizer Improvements

This update introduces improvements to our Shader Optimizer pipeline by further improving it's behavior over shared samplers.

For the longest time, Materials that are locked with the same settings share the same Locked hash. However, this has never always been the case. ThryEditor 2.73.0 goes further by making sure this is done more often whenever possible. By doing so, we can eliminate messy variants for each independent material that use the same set of features enabled.

In order to support these new changes, newly-locked materials will now have all their optimized shader files placed centrally in `Assets/_LockedShaderCache`. Please carefully read the Changelog below for info.

## Changes
- Overhauled the Shader Optimizer system.
  - Each material now caches/shares it's own samplers in a central `Assets/_LockedShaderCache` folder, separated by each Shader. Locked Materials no longer have their own separate `OptimizedShaders` file.
  - Unlocking no longer deletes cache-resident shaders, saving greatly on CPU. Installs get cleaned on Unlock rather than leaving orphans scattered around. This should effectively make Unlocking twice as faster than before.
  - By default, the Locked Shader Cache is capped at 2048 MB. When it reaches the limit, older unused samplers get deleted.
    - *This is configurable in ThryEditor Settings, but change that value at your own risk!*
  - **CREATORS:** As locked materials now go to `Assets/_LockedShaderCache`, this folder will be auto-generated when locking materials for the first time starting with this version.
    - *As this folder contains all locked samplers, deleting this folder will require materials to be recompiled. However, it is still advised to ignore it from your exported Avatar assets (as you shouldn't export Avatars with locked materials anyways)!*
    - *A README.txt file is inserted as a reminder about the folder's importance and instructions on how to manage it.*
    - *As a side note: A script is also included to automatically handle missing locked shaders, if for some reason the `_LockedShaderCache` folder is missing. This should hopefully reduce the occurrence of Pink Materials from missing locked shader files.*
- Fixed Inspector Rebuild occurring often in some usage scenarios, especially during Lock/Unlock.
- Fixed multiple ShaderProperty name collision bugs.
- The Levenshtein sweep over `GuessShader` function no longer runs constantly and only executes when absolutely needed.
- Fixed parser parity.
- Fixed `A`/`RA` dot indicator on Headers not showing if a collapsed section has a tagged `A`/`RA` in it's context.
- Fixed dead URL on `Right-Click -> Locking Explanation` context menu option.
- Fixed a NullReferenceException on the Render Queue property when Right-Clicked.

# [2.72.8]
## Changes
- Fixed uneven header spacing under fractional DPI scaling.
- Added Top Bar Button registration point, letting packages build on this UI add their own Top Bar Buttons without an assembly needing to reference them.
- Ensure the Rendering Presets dropdown property name is properly recognized (affects Grab Pass, Triplanar Projection, etc.).

# [2.72.7]
## Changes
- Fixed an issue where Global Linking failed when there are 2 properties with the same name.

# [2.72.6]
## Changes
- `ShaderOptimizer`: Anchor the regex to line beginning as well. This fixes an issue where renaming `_Metallic` also rewrites the tail of `_LTCGI_Metallic` as well, as an example.
- Added some new decorative drawers for future usage, including `[ThryHeader]` and `[ThryDescription]`.

# [2.72.5]
## Changes
- Installing Poiyomi Shaders through VCC or ALCOM should now prompt you to install ThryEditor as a dependency. Updates for ThryEditor and Poiyomi Shaders are now separate.
- Fixed auto-collapsing Global Linked slots.
- Fixed VRCFallback tag carrying over on upgraded materials.
- Fixed Rendering Presets handling.
- Fixed inline slider can't be marked as animatable.
- Fixed headers can't be marked as animatable.
- Added a 'Clear' button for inline RGBA packer.
- Turned off random debugging log prints.
