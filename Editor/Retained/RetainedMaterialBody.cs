#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal sealed class RetainedMaterialBody : VisualElement
    {
        internal readonly RetainedMaterialModel Model;
        private readonly MaterialInspectorView _view;
        private RetainedFields _fields;
        private readonly RetainedViewport _viewport;
        private int _revision = -1;
        private bool _editing;
        private Texture2D _expandedCaret, _collapsedCaret;
        internal RetainedMaterialBody(RetainedMaterialModel model, MaterialInspectorView view)
        {
            Model = model; _view = view; name = "thry-material-controls"; AddToClassList("thry-material-controls");
            _viewport = new RetainedViewport(this, SynchronizeViewport);
            Model.Changed += Synchronize;
            RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == this) Model.ReleaseCaches(); });
        }
        private void SynchronizeViewport()
        {
            Model.Shader.ActivateRetained();
            _fields?.SynchronizeVisuals();
        }
        internal void Synchronize()
        {
            Model.Shader.ActivateRetained();
            bool editing = Model.Shader.RootCategories.Any(g => SectionEditing.IsEditing(g));
            if (_revision != Model.Shader.RetainedRevision || editing != _editing)
            {
                _revision = Model.Shader.RetainedRevision; _editing = editing;
                // Track initializes bindings immediately, and attachment initializes
                // them again. Build belongs to the same synchronized snapshot as the
                // final pass, so section dots can share those default comparisons.
                using (RetainedPropertyDefaults.BeginEvaluation(Model.Shader)) Build();
            }
            _fields.Synchronize();
        }
        private void Build()
        {
            Clear(); _fields = new RetainedFields(Model,_view,_viewport);
            _expandedCaret = Resources.Load<Texture2D>("ThryToolbar/header-caret-down");
            _collapsedCaret = Resources.Load<Texture2D>("ThryToolbar/header-caret-right");
            var shader = Model.Shader;
            Add(RetainedMultiMaterial.SelectionSummary(Model));
            Add(Presets.CreateEditorControls(Model, _fields));
            var toolbar = SectionEditing.CreateToolbar?.Invoke(shader); if(toolbar != null) Add(toolbar);
            var materialActions = new VisualElement { name = "thry-material-actions" };
            materialActions.AddToClassList("thry-material-actions");
            Add(materialActions);
            if (shader.RetainedOptimizer != null)
            {
                var button = new Button(() =>
                {
                    shader.ActivateRetained();
                    // Optimizer properties may cover only one shader's subset. Snapshot
                    // the entire editor selection before shader swaps rebuild the view.
                    var materials = shader.Materials.Where(m => m != null).Distinct().ToArray();
                    if (materials.Any(m => m.IsLocked()))
                        ShaderOptimizer.UnlockMaterials(materials, ShaderOptimizer.ProgressBar.Uncancellable);
                    else
                        ShaderOptimizer.LockMaterials(materials, ShaderOptimizer.ProgressBar.Uncancellable);
                    shader.Reload(); Model.Notify();
                }) { name = "thry-lock-button" };
                button.AddToClassList("thry-lock-button");
                _fields.Track(button, () => button.text = shader.IsLockedMaterial ? RetainedText.Get(Model.Shader, "unlock_shader", "Unlock Shader") : RetainedText.Get(Model.Shader, "lock_shader", "Lock In Optimized Shader")); materialActions.Add(button);
                button.SetEnabled(!shader.Materials.Any(m=>m.isVariant));
                if (Config.Instance.allowCustomLockingRenaming || shader.HasCustomRenameSuffix)
                {
                    VisualElement suffixInput;
                    materialActions.Add(RetainedFields.Row(RetainedText.Get(Model.Shader, "locked_property_suffix", "Locked property suffix"), out suffixInput));
                    var suffix = new TextField { isDelayed = true, name = "thry-locked-property-suffix",
                        tooltip = "When you lock the material, this text is added to property names marked ‘Renamed Animated’. For example, entering Shirt changes _Color to _Color_Shirt. Animations must use the new names. Clear this field to use the material name." };
                    suffixInput.Add(suffix);
                    bool suffixEdited = false;
                    bool suffixCancelled = false;
                    string suffixDraft = null;
                    suffix.RegisterCallback<FocusInEvent>(e => { suffixEdited = false; suffixCancelled = false; suffixDraft = null; });
                    suffix.RegisterCallback<UnityEngine.UIElements.InputEvent>(e =>
                    {
                        if (suffixCancelled) return;
                        suffixEdited = true;
                        suffixDraft = e.newData;
                    });
                    suffix.RegisterCallback<KeyDownEvent>(e =>
                    {
                        if (e.keyCode == KeyCode.Escape) suffixCancelled = true;
                    }, TrickleDown.TrickleDown);
                    _fields.Track(suffix, () =>
                    {
                        var focused = suffix.panel?.focusController?.focusedElement as VisualElement;
                        // Periodic inspector refreshes must not replace an uncommitted text draft.
                        if (focused != suffix && (focused == null || !suffix.Contains(focused)))
                        {
                            suffix.SetValueWithoutNotify(shader.RenamedPropertySuffix);
                            suffix.showMixedValue = shader.HasMixedCustomPropertySuffix;
                        }
                        suffix.SetEnabled(Config.Instance.allowCustomLockingRenaming && !shader.IsLockedMaterial);
                    });
                    suffix.RegisterValueChangedCallback(e =>
                    {
                        // Unity can replace a delayed mixed field's text with its display placeholder
                        // before committing. Use the actual input draft, including intentional dashes.
                        if (suffixCancelled || (suffix.showMixedValue && !suffixEdited)) return;
                        var clean = ShaderOptimizer.CleanStringForPropertyNames((suffixEdited ? suffixDraft : e.newValue).Replace(" ", "_"));
                        Model.Mutate(RetainedText.Get(Model.Shader, "locked_property_suffix", "Locked property suffix"), m => m.SetOverrideTag("thry_rename_suffix", clean));
                        shader.Reload(); Model.Notify();
                    });
                }
            }
            var presetRow = new VisualElement(); presetRow.AddToClassList("thry-presets-row");
            Button presets = null;
            presets = new Button(() => {
                var names = Presets.GetFullPresetNames();
                var window=ScriptableObject.CreateInstance<PresetsPopupGUI>();window.Init("_full_",names,Presets.GetFullPresetGuids(),shader);window.titleContent=new GUIContent(RetainedText.Get(Model.Shader, "presets", "Presets"));window.ShowUtility();
            }) { name = "thry-presets-button", text = RetainedText.Get(Model.Shader, "presets", "Presets") }; presetRow.Add(presets);
            if(shader.RetainedPreset != null)
            {
                var renderingMode = _fields.Field(shader.RetainedPreset, true);
                renderingMode.AddToClassList("thry-rendering-mode");
                renderingMode.tooltip = "Rendering mode";
                presetRow.Add(renderingMode);
            }
            materialActions.Add(presetRow);
            foreach (var child in shader.RetainedRoot.Children) AddPart(this, child, 0);
            AddFooter();
        }
        private void AddPart(VisualElement parent, ShaderPart part, int depth)
        {
            var group = part as ShaderGroup;
            if (group == null) { var p = part as ShaderProperty; if(p != null) parent.Add(_fields.Field(p)); return; }
            var root = new VisualElement { name = "section-" + group.PropertyIdentifier, userData = group }; root.AddToClassList("thry-section");
            bool positioning = group.Children.OfType<ShaderProperty>().Any(RetainedFields.HasDecalPositioning);
            root.EnableInClassList("thry-positioning-panel", positioning);
            root.EnableInClassList("thry-subsection-panel", depth > 0);
            root.EnableInClassList("thry-root-section", depth == 0);
            var header = new VisualElement(); header.AddToClassList("thry-section-header"); root.Add(header);
            var changedProperties = SectionProperties(group);
            var fold = new Button { tooltip = "Expand or collapse section" }; fold.AddToClassList("thry-fold-arrow");
            var foldIcon = HeaderIcon("caret-right"); fold.Add(foldIcon); header.Add(fold);
            if (_editing && SectionEditing.IsEditing(group))
            {
                var toggle = new Toggle { tooltip = "Include this section in the shared shader" }; toggle.AddToClassList("thry-section-switch");
                var knob=new VisualElement();knob.AddToClassList("thry-switch-knob");toggle.Q(className:"unity-toggle__checkmark").Add(knob);
                _fields.Track(toggle, () => {toggle.SetValueWithoutNotify(SectionEditing.IsExcluded?.Invoke(group) != true);knob.style.left=toggle.value?14:2;});
                toggle.RegisterValueChangedCallback(e => { SectionEditing.SetIncluded?.Invoke(group,e.newValue); Synchronize(); }); header.Add(toggle);
            }
            var references = new List<string>();
            if (group.Options.reference_property != null) references.Add(group.Options.reference_property);
            if (group.Options.reference_properties != null) references.AddRange(group.Options.reference_properties);
            foreach (var id in references)
            {
                ShaderProperty reference;
                if(Model.Shader.PropertyDictionary.TryGetValue(id,out reference))
                {
                    var holder = new VisualElement(); holder.AddToClassList("thry-header-reference"); _fields.Toggle(holder,reference); header.Add(holder);
                }
            }
            var titleArea = new VisualElement { tooltip = group.TooltipText }; titleArea.AddToClassList("thry-section-title-area"); header.Add(titleArea);
            var title = new Label(SectionCaption(group)); title.AddToClassList("thry-section-title"); titleArea.Add(title);
            _fields.DecoratePreset(titleArea, group, true);
            var changedDot = new VisualElement { name = "changed-section-indicator" };
            changedDot.AddToClassList("thry-section-changed-dot"); titleArea.Add(changedDot);
            changedDot.RegisterCallback<TooltipEvent>(e =>
            {
                // Section dots are visual summaries; value details belong to property dots.
                e.tooltip = string.Empty;
                e.StopImmediatePropagation();
            });
            var animated = new VisualElement { name = "animated-descendant", pickingMode = PickingMode.Ignore };
            animated.AddToClassList("thry-header-animation-dot"); titleArea.Add(animated);
            var renamed = new VisualElement { name = "renamed-animated-descendant", pickingMode = PickingMode.Ignore };
            renamed.AddToClassList("thry-header-animation-dot"); titleArea.Add(renamed);
            _fields.TrackVisible(header, () =>
            {
                animated.style.display = Config.Instance.showAnimatedDotOnHeaders && group.HasAnimatedDescendant ? DisplayStyle.Flex : DisplayStyle.None;
                animated.style.backgroundColor = Styles.AnimatedColor;
                renamed.style.display = Config.Instance.showAnimatedDotOnHeaders && group.HasRenameAnimatedDescendant ? DisplayStyle.Flex : DisplayStyle.None;
                renamed.style.backgroundColor = Styles.AnimatedRenamedColor;
            });
            var note=new Label();note.AddToClassList("thry-note");header.Add(note);_fields.Track(note,()=>{note.text=group.Note;note.style.display=Config.Instance.showNotes&&!string.IsNullOrEmpty(note.text)?DisplayStyle.Flex:DisplayStyle.None;});
            var link = HeaderAction("link", RetainedText.Get(Model.Shader, "global_links", "Global links"), () => GlobalLinker.Popup(group)); header.Add(link);
            _fields.TrackVisible(link, () => link.EnableInClassList("thry-linked", group.MaterialProperty != null && Model.Shader.Materials.Any(m => GlobalLinker.IsGloballyLinked(m, group.MaterialProperty.name))));
            var resources = new[] { group.Options.button_help, group.Options.button_video, group.Options.button_author };
            var resourceIcons = new[] { "help", "video", "author" };
            for (int i = 0; i < resources.Length; i++)
            {
                var data = resources[i];
                if (data != null) header.Add(HeaderAction(resourceIcons[i], data.hover, () => data.action?.Perform(Model.Shader.Materials)));
            }
            if (group is ShaderHeader && Presets.DoesSectionHavePresets(group.MaterialProperty.name))
            {
                var presets = new Button(() =>
                {
                    string collection = group.MaterialProperty.name;
                    var names = Presets.GetSectionPresetNames(collection);
                    var window = ScriptableObject.CreateInstance<PresetsPopupGUI>();
                    Model.Shader.CurrentProperty = group;
                    window.Init(collection, names, names.Select(n => Presets.GetSectionPresetGuid(collection, n)).ToList(), Model.Shader);
                    window.titleContent = new GUIContent(group.Content.text + " presets"); window.ShowUtility();
                }) { tooltip = "Section presets" };
                presets.AddToClassList("thry-header-action");
                presets.Add(HeaderIcon("presets")); header.Add(presets);
            }
            var menu = HeaderAction("menu", "Section actions", () => {
                var actions = ShaderHeader.RetainedHeaderMenu(group, Model.Shader.Materials);
                actions.AddSeparator("");
                actions.AddItem(new GUIContent(RetainedText.Get(Model.Shader, "search_section", "Search in this section")), false, () => _view.SearchSection(group));
                _view.ShowLegacyMenu(actions, header);
            }); header.Add(menu);
            var children = new VisualElement(); children.AddToClassList("thry-section-content"); root.Add(children);
            bool built = false;
            _fields.TrackVisible(header, () => {
                title.text = SectionCaption(group);
                if (!Model.DeferSummaryRefresh)
                    changedDot.style.display = RetainedPropertyDefaults.HasChangedSection(Model.Shader, changedProperties) ? DisplayStyle.Flex : DisplayStyle.None;
                changedDot.style.backgroundColor = title.resolvedStyle.color;
            });
            Action update = () => {
                bool category = depth != 0 || Model.Shader.FocusedCategory == null || group.MaterialProperty.name == Model.Shader.FocusedCategory;
                root.style.display = category && group.RetainedVisible ? DisplayStyle.Flex : DisplayStyle.None;
                header.EnableInClassList("thry-excluded", SectionEditing.IsExcluded?.Invoke(group) == true);
                root.EnableInClassList("thry-section-open", group.RetainedExpanded);
                children.SetEnabled(group.RetainedChildrenEnabled && (group.Options.condition_enable == null || group.Options.condition_enable.Test()));
                children.style.display = group.RetainedExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                foldIcon.image = group.RetainedExpanded ? _expandedCaret : _collapsedCaret;
                if (!group.RetainedExpanded || built) return;
                built = true;
#if UNITY_2022_1_OR_NEWER
                if (!_editing && RetainedPathing.CanBuild(group, Model))
                {
                    children.Add(new RetainedPathing(Model, _fields, group,
                        (container, child) => AddPart(container, child, depth + 1)));
                    return;
                }
#endif
                // Sections and subsections organize their parent header's preset; only
                // headers own named preset collections (matching the legacy inspector).
                if (group is ShaderHeader && Model.Shader.IsSectionedPresetEditor)
                {
                    var presetName = new TextField("Preset name") { isDelayed = true, value = Presets.GetSectionPresetName(Model.Shader.Materials[0], group.MaterialProperty.name) };
                    children.Add(presetName);
                    presetName.RegisterValueChangedCallback(e => Model.Mutate("Section preset name", m => Presets.SetSectionPreset(m, group.MaterialProperty.name, e.newValue)));
                }
                foreach (var child in group.Children)
                {
                    // The positioning toolbar provides contextual guidance in place of the legacy banner.
                    if (positioning && child is ShaderProperty help && help.Content.text.Contains("Raycast")
                        && help.MyShader.GetPropertyAttributes(help.ShaderPropertyIndex).Any(a => new DrawerAttribute(a).Name == "Helpbox")) continue;
                    AddPart(children,child,depth+1);
                }
            };
            Action expand = () => { Model.SetExpanded(group, !group.RetainedExpanded); update(); };
            fold.clicked += expand;
            header.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                for (var target = e.target as VisualElement; target != null && target != header; target = target.parent)
                    if (target is Button || target is Toggle || target.ClassListContains("unity-base-field")) return;
                expand(); e.StopPropagation();
            });
            _fields.Track(root,update); parent.Add(root);
        }
        /// <summary>
        /// Builds hover text out of the parts that are worth showing, in reading order: the label,
        /// which the inspector may have clipped, then the shader's `tooltip:`, then the user's note.
        /// </summary>
        internal static string Hover(params string[] lines)
        {
            return string.Join("\n\n", lines.Where(line => !string.IsNullOrEmpty(line)).ToArray());
        }

        internal static string SectionCaption(ShaderPart part)
        {
            // Retained views use separate changed-value dots. Reading the legacy
            // marked Content would walk defaults just to add and strip an asterisk.
            // The original label also preserves authored punctuation and live locale edits.
            return part.UnmarkedLabel;
        }

        private List<KeyValuePair<ShaderProperty, string>> SectionProperties(ShaderGroup section)
        {
            var result = new List<KeyValuePair<ShaderProperty, string>>();
            var visited = new HashSet<ShaderPart>();
            Action<ShaderPart> collect = null;
            collect = part =>
            {
                if (part == null || !visited.Add(part)) return;
                var property = part as ShaderProperty;
                // The section dot only needs a boolean. Building localized paths for
                // every descendant allocated strings that no control displayed.
                if (property != null && property.MaterialProperty != null) result.Add(new KeyValuePair<ShaderProperty, string>(property, null));
                var group = part as ShaderGroup;
                if (group != null) foreach (var child in group.Children) collect(child);
                ShaderProperty reference;
                if (part.Options.reference_property != null && Model.Shader.PropertyDictionary.TryGetValue(part.Options.reference_property, out reference)) collect(reference);
                if (part.Options.reference_properties != null)
                    foreach (var name in part.Options.reference_properties)
                        if (name != null && Model.Shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference);
                if (part.AdditionalDefaultCheckProperties != null)
                    foreach (var name in part.AdditionalDefaultCheckProperties)
                        if (name != null && Model.Shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference);
            };
            foreach (var child in section.Children) collect(child);
            foreach (var name in new[] { section.Options.reference_property }.Concat(section.Options.reference_properties ?? Array.Empty<string>()))
            {
                ShaderProperty reference;
                if (name != null && Model.Shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference);
            }
            return result;
        }

        internal static bool HasChangedValue(ShaderProperty property) => RetainedPropertyDefaults.HasChangedValue(property);

        internal static bool HasChangedTextureTransform(ShaderProperty property) => RetainedPropertyDefaults.HasChangedTextureTransform(property);
        internal static Image HeaderIcon(string name)
        {
            var icon = new Image
            {
                image = Resources.Load<Texture2D>("ThryToolbar/header-" + name),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            icon.AddToClassList("thry-header-icon");
            return icon;
        }

        private static Button HeaderAction(string icon, string tooltip, Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("thry-header-action");
            button.Add(HeaderIcon(icon));
            return button;
        }

        private void AddFooter()
        {
            VisualElement input;
            if (Model.Shader.RetainedFallback != null)
            {
                var fallbackRow = RetainedFields.Row("VRChat Fallback Shader",out input);
                _fields.DecoratePreset(fallbackRow, Model.Shader.RetainedFallback);
                var fallback = new DropdownField(VRCFallbackProperty.RetainedNames.ToList(),0); _view.UseInspectorMenu(fallback); input.Add(fallback);
                fallback.name = "thry-vrc-fallback";
                _fields.TrackVisible(fallback, () => {
                    var materials = Model.Shader.Materials;
                    string tag = materials[0].GetTag("VRCFallback", false, "None");
                    bool mixed = false;
                    // The first owner's tag is already known. Avoid reading it again
                    // just to establish that a single material is never mixed.
                    for (int i = 1; i < materials.Length && !mixed; i++)
                        mixed = materials[i].GetTag("VRCFallback", false, "None") != tag;
                    int index = Array.IndexOf(VRCFallbackProperty.RetainedValues, tag);
                    RetainedFields.SynchronizeValue(fallback, index < 0 ? tag : VRCFallbackProperty.RetainedNames[index], mixed);
                });
                fallback.RegisterValueChangedCallback(e=>{if(fallback.index>=0)Model.Mutate("VRChat Fallback",m=>m.SetOverrideTag("VRCFallback",VRCFallbackProperty.RetainedValues[fallback.index]));}); Add(fallbackRow);
            }
            var row = RetainedFields.Row(RetainedText.Get(Model.Shader, "render_queue", "Render Queue"),out input);
            _fields.DecoratePreset(row, Model.Shader.RetainedQueue);
            _fields.Track(row, () => row.style.display = Config.Instance.showRenderQueue ? DisplayStyle.Flex : DisplayStyle.None);
            input.AddToClassList("thry-components");
            string[] queueNames = { RetainedText.Get(Model.Shader, "queue_shader", "From Shader"), RetainedText.Get(Model.Shader, "queue_background", "Background"), RetainedText.Get(Model.Shader, "queue_geometry", "Geometry"), RetainedText.Get(Model.Shader, "queue_alpha_test", "AlphaTest"), RetainedText.Get(Model.Shader, "queue_transparent", "Transparent"), RetainedText.Get(Model.Shader, "queue_overlay", "Overlay") };
            int[] queueValues = { -1, 1000, 2000, 2450, 3000, 4000 };
            var queuePreset = new DropdownField(queueNames.ToList(), 0); _view.UseInspectorMenu(queuePreset); input.Add(queuePreset); queuePreset.style.flexGrow = 1;
            queuePreset.style.flexBasis = 0; queuePreset.style.flexShrink = 1; queuePreset.style.minWidth = 0;
            _fields.TrackVisible(queuePreset, () => {
                int value = Model.Shader.Materials[0].renderQueue;
                int index = Array.IndexOf(queueValues, value);
                // A queue matching no preset is named by its offset from the nearest one, as Unity
                // does: 2225 reads "Geometry +225". The word "Custom" named nothing at all.
                RetainedFields.SynchronizeValue(queuePreset, index < 0 ? Helpers.RenderQueueHelper.GetDisplayName(value, queueNames, queueValues) : queueNames[index],
                    Model.Shader.Materials.Skip(1).Any(m => m.renderQueue != value)); });
                queuePreset.RegisterValueChangedCallback(e => { if (queuePreset.index >= 0) Model.Mutate(RetainedText.Get(Model.Shader, "render_queue", "Render Queue"), m => m.renderQueue = queueValues[queuePreset.index]);
            });
            var queue = new UnityEngine.UIElements.IntegerField { name = "thry-render-queue" }; input.Add(queue);
            queue.style.width = 64;
            queue.style.flexShrink = 0;
            _fields.TrackVisible(queue, () => { int value = Model.Shader.Materials[0].renderQueue;
                RetainedFields.SynchronizeValue(queue, value, Model.Shader.Materials.Skip(1).Any(m => m.renderQueue != value)); });
            queue.RegisterValueChangedCallback(e => Model.Mutate(RetainedText.Get(Model.Shader, "render_queue", "Render Queue"),m => m.renderQueue = e.newValue)); Add(row);
            var footer = new VisualElement(); footer.AddToClassList("thry-footer");
            var credit = new Label("@UI Made by Thryrallo"); credit.AddToClassList("thry-footer-credit"); footer.Add(credit);
            credit.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) Application.OpenURL("https://www.twitter.com/thryrallo"); });
            var thry = new Button(() => Application.OpenURL("https://www.twitter.com/thryrallo")) { tooltip = "Thryrallo" };
            thry.Add(new Image { image = Icons.thryIcon.normal.background, scaleMode = ScaleMode.ScaleToFit }); footer.Add(thry);
            foreach(var item in Model.Shader.RetainedFooters)
            {
                var data = item.Data; if(data == null) continue;
                var button = new Button(() => data.action?.Perform(Model.Shader.Materials)) { tooltip = data.hover };
                if(data.texture != null) button.Add(new Image { image = data.texture.loaded_texture, scaleMode = ScaleMode.ScaleToFit }); else button.text = data.text;
                footer.Add(button);
            }
            Add(footer);
        }
    }
    // Presentation-only groups use the same chrome as material subcategories,
    // without inventing shader properties to store their expansion state.
    internal sealed class RetainedSubcategory : VisualElement
    {
        readonly VisualElement content;
        public override VisualElement contentContainer => content ?? this;

        internal RetainedSubcategory(string title, string scope, string key, bool initiallyOpen)
        {
            AddToClassList("thry-section"); AddToClassList("thry-subsection-panel");
            var header = new VisualElement(); header.AddToClassList("thry-section-header"); hierarchy.Add(header);
            var arrow = new Button { tooltip = "Expand or collapse section" };
            arrow.AddToClassList("thry-fold-arrow");
            var icon = RetainedMaterialBody.HeaderIcon("caret-right"); arrow.Add(icon); header.Add(arrow);
            var titleArea = new VisualElement(); titleArea.AddToClassList("thry-section-title-area"); header.Add(titleArea);
            var label = new Label(title); label.AddToClassList("thry-section-title"); titleArea.Add(label);
            content = new VisualElement(); content.AddToClassList("thry-section-content"); hierarchy.Add(content);
            bool expanded = RetainedUiState.Get(scope, key, initiallyOpen);
            var opened = Resources.Load<Texture2D>("ThryToolbar/header-caret-down");
            var closed = Resources.Load<Texture2D>("ThryToolbar/header-caret-right");
            Action refresh = () => {
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                icon.image = expanded ? opened : closed;
            };
            Action toggle = () => { expanded = !expanded; RetainedUiState.Set(scope, key, expanded); refresh(); };
            arrow.clicked += toggle;
            header.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                for (var target = e.target as VisualElement; target != null && target != header; target = target.parent)
                    if (target is Button) return;
                toggle(); e.StopPropagation();
            });
            refresh();
        }
    }

}
#endif
