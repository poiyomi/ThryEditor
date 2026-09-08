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
        private int _revision = -1;
        private bool _editing;
        internal RetainedMaterialBody(RetainedMaterialModel model, MaterialInspectorView view)
        {
            Model = model; _view = view; name = "thry-material-controls"; AddToClassList("thry-material-controls");
            Model.Changed += Synchronize;
        }
        internal void Synchronize()
        {
            Model.Shader.ActivateRetained();
            bool editing = Model.Shader.RootCategories.Any(g => SectionEditing.IsEditing(g));
            if (_revision != Model.Shader.RetainedRevision || editing != _editing)
            { _revision = Model.Shader.RetainedRevision; _editing = editing; Build(); }
            _fields.Synchronize();
        }
        private void Build()
        {
            Clear(); _fields = new RetainedFields(Model,_view);
            var shader = Model.Shader;
            Add(RetainedMultiMaterial.SelectionSummary(Model));
            Add(Presets.CreateEditorControls(Model, _fields));
            var toolbar = SectionEditing.CreateToolbar?.Invoke(shader); if(toolbar != null) Add(toolbar);
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
                _fields.Track(button, () => button.text = shader.IsLockedMaterial ? "Unlock Shader" : "Lock In Optimized Shader"); Add(button);
                button.SetEnabled(!shader.Materials.Any(m=>m.isVariant));
                if (Config.Instance.allowCustomLockingRenaming || shader.HasCustomRenameSuffix)
                {
                    VisualElement suffixInput;
                    Add(RetainedFields.Row("Locked property suffix", out suffixInput));
                    var suffix = new TextField { isDelayed = true };
                    suffixInput.Add(suffix);
                    _fields.Track(suffix, () => { suffix.SetValueWithoutNotify(shader.RenamedPropertySuffix); suffix.showMixedValue = shader.HasMixedCustomPropertySuffix; suffix.SetEnabled(Config.Instance.allowCustomLockingRenaming && !shader.IsLockedMaterial); });
                    suffix.RegisterValueChangedCallback(e =>
                    {
                        var clean = ShaderOptimizer.CleanStringForPropertyNames(e.newValue.Replace(" ", "_"));
                        Model.Mutate("Locked property suffix", m => m.SetOverrideTag("thry_rename_suffix", clean));
                        shader.Reload(); Model.Notify();
                    });
                }
            }
            var presetRow = new VisualElement(); presetRow.AddToClassList("thry-presets-row");
            Button presets = null;
            presets = new Button(() => {
                var names = Presets.GetFullPresetNames();
                var window=ScriptableObject.CreateInstance<PresetsPopupGUI>();window.Init("_full_",names,Presets.GetFullPresetGuids(),shader);window.titleContent=new GUIContent("Presets");window.ShowUtility();
            }) { text = "Presets" }; presetRow.Add(presets);
            if(shader.RetainedPreset != null)
            {
                var renderingMode = _fields.Field(shader.RetainedPreset, true);
                renderingMode.AddToClassList("thry-rendering-mode");
                renderingMode.tooltip = "Rendering mode";
                presetRow.Add(renderingMode);
            }
            Add(presetRow);
            foreach (var child in shader.RetainedRoot.Children) AddPart(this, child, 0);
            AddFooter();
        }
        private void AddPart(VisualElement parent, ShaderPart part, int depth)
        {
            var group = part as ShaderGroup;
            if (group == null) { var p = part as ShaderProperty; if(p != null) parent.Add(_fields.Field(p)); return; }
            var root = new VisualElement { name = "section-" + group.PropertyIdentifier, userData = group }; root.AddToClassList("thry-section");
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
            var titleArea = new VisualElement { tooltip = group.Content.tooltip }; titleArea.AddToClassList("thry-section-title-area"); header.Add(titleArea);
            var title = new Label(SectionCaption(group)); title.AddToClassList("thry-section-title"); titleArea.Add(title);
            var changedDot = new VisualElement { name = "changed-section-indicator", pickingMode = PickingMode.Ignore };
            changedDot.AddToClassList("thry-section-changed-dot"); titleArea.Add(changedDot);
            title.RegisterCallback<TooltipEvent>(e =>
            {
                var paths = changedProperties.Where(entry => HasChangedValue(entry.Key) || HasChangedTextureTransform(entry.Key))
                    .Select(entry => entry.Value + (HasChangedTextureTransform(entry.Key) ? " / Tiling & Offset" : "")).ToArray();
                e.tooltip = group.Content.tooltip;
                if (paths.Length > 0) e.tooltip = (string.IsNullOrEmpty(e.tooltip) ? "" : e.tooltip + "\n\n") + "Changed properties:\n" + string.Join("\n", paths);
                e.rect = title.worldBound; e.StopPropagation();
            });
            var animated = new VisualElement { name = "animated-descendant", pickingMode = PickingMode.Ignore };
            animated.AddToClassList("thry-header-animation-dot"); titleArea.Add(animated);
            var renamed = new VisualElement { name = "renamed-animated-descendant", pickingMode = PickingMode.Ignore };
            renamed.AddToClassList("thry-header-animation-dot"); titleArea.Add(renamed);
            _fields.Track(animated, () =>
            {
                animated.style.display = Config.Instance.showAnimatedDotOnHeaders && group.HasAnimatedDescendant ? DisplayStyle.Flex : DisplayStyle.None;
                animated.style.backgroundColor = Styles.AnimatedColor;
                renamed.style.display = Config.Instance.showAnimatedDotOnHeaders && group.HasRenameAnimatedDescendant ? DisplayStyle.Flex : DisplayStyle.None;
                renamed.style.backgroundColor = Styles.AnimatedRenamedColor;
            });
            var note=new Label();note.AddToClassList("thry-note");header.Add(note);_fields.Track(note,()=>{note.text=group.Note;note.style.display=Config.Instance.showNotes&&!string.IsNullOrEmpty(note.text)?DisplayStyle.Flex:DisplayStyle.None;});
            var link = HeaderAction("link", "Global links", () => GlobalLinker.Popup(group)); header.Add(link);
            _fields.Track(link, () => link.EnableInClassList("thry-linked", group.MaterialProperty != null && Model.Shader.Materials.Any(m => GlobalLinker.IsGloballyLinked(m, group.MaterialProperty.name))));
            var resources = new[] { group.Options.button_help, group.Options.button_video, group.Options.button_author };
            var resourceIcons = new[] { "help", "video", "author" };
            for (int i = 0; i < resources.Length; i++)
            {
                var data = resources[i];
                if (data != null) header.Add(HeaderAction(resourceIcons[i], data.hover, () => data.action?.Perform(Model.Shader.Materials)));
            }
            if (Presets.DoesSectionHavePresets(group.MaterialProperty.name))
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
            var menu = HeaderAction("menu", "Section actions", () => _view.ShowLegacyMenu(ShaderHeader.RetainedHeaderMenu(group,Model.Shader.Materials),header)); header.Add(menu);
            var children = new VisualElement(); children.AddToClassList("thry-section-content"); root.Add(children);
            bool built = false;
            Action update = () => {
                title.text = SectionCaption(group);
                changedDot.style.display = changedProperties.Any(entry => HasChangedValue(entry.Key) || HasChangedTextureTransform(entry.Key)) ? DisplayStyle.Flex : DisplayStyle.None;
                changedDot.style.backgroundColor = title.resolvedStyle.color;
                bool category = depth != 0 || Model.Shader.FocusedCategory == null || group.MaterialProperty.name == Model.Shader.FocusedCategory;
                root.style.display = category && group.RetainedVisible ? DisplayStyle.Flex : DisplayStyle.None;
                header.EnableInClassList("thry-excluded", SectionEditing.IsExcluded?.Invoke(group) == true);
                children.SetEnabled(group.RetainedChildrenEnabled && (group.Options.condition_enable == null || group.Options.condition_enable.Test()));
                children.style.display = group.RetainedExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                foldIcon.image = Resources.Load<Texture2D>("ThryToolbar/header-caret-" + (group.RetainedExpanded ? "down" : "right"));
                if (!group.RetainedExpanded || built) return;
                built = true;
                if (Model.Shader.IsSectionedPresetEditor)
                {
                    var presetName = new TextField("Preset name") { isDelayed = true, value = Presets.GetSectionPresetName(Model.Shader.Materials[0], group.MaterialProperty.name) };
                    children.Add(presetName);
                    presetName.RegisterValueChangedCallback(e => Model.Mutate("Section preset name", m => Presets.SetSectionPreset(m, group.MaterialProperty.name, e.newValue)));
                }
                foreach (var child in group.Children) AddPart(children,child,depth+1);
            };
            Action expand = () => { Model.Shader.ActivateRetained(); group.ExpandForNavigation(!group.RetainedExpanded); update(); };
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
        internal static string SectionCaption(ShaderPart part)
        {
            string text = part.Content.text ?? "";
            // Only remove the synthetic default-state suffix; authored punctuation stays intact.
            if (Config.Instance.showStarNextToNonDefaultProperties && !part.IsPropertyValueDefault && text.EndsWith("*", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
            return text;
        }

        private List<KeyValuePair<ShaderProperty, string>> SectionProperties(ShaderGroup section)
        {
            var result = new List<KeyValuePair<ShaderProperty, string>>();
            var visited = new HashSet<ShaderPart>();
            Action<ShaderPart, string> collect = null;
            collect = (part, prefix) =>
            {
                if (part == null || !visited.Add(part)) return;
                string caption = SectionCaption(part).Split('|')[0];
                string path = string.IsNullOrEmpty(prefix) ? caption : prefix + " / " + caption;
                var property = part as ShaderProperty;
                if (property != null && property.MaterialProperty != null) result.Add(new KeyValuePair<ShaderProperty, string>(property, path));
                var group = part as ShaderGroup;
                if (group != null) foreach (var child in group.Children) collect(child, path);
                var references = new List<string>();
                if (part.Options.reference_property != null) references.Add(part.Options.reference_property);
                if (part.Options.reference_properties != null) references.AddRange(part.Options.reference_properties);
                if (part.AdditionalDefaultCheckProperties != null) references.AddRange(part.AdditionalDefaultCheckProperties);
                foreach (var name in references)
                {
                    ShaderProperty reference;
                    if (name != null && Model.Shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference, path);
                }
            };
            // Relative paths make a parent indicator explain which nested section changed.
            foreach (var child in section.Children) collect(child, "");
            foreach (var name in new[] { section.Options.reference_property }.Concat(section.Options.reference_properties ?? Array.Empty<string>()))
            {
                ShaderProperty reference;
                if (name != null && Model.Shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference, "");
            }
            return result;
        }

        internal static bool HasChangedValue(ShaderProperty property)
        {
            var value = property.MaterialProperty;
            if (value.hasMixedValue) return true;
            // Compare current retained values against the same shader defaults used by
            // the legacy asterisk. Cached group state can lag external multi-edit changes.
            switch (value.GetPropertyType())
            {
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    return value.textureValue != null && value.textureValue.name != (string)property.PropertyDefaultValue;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    return (Vector4)value.colorValue != (Vector4)property.PropertyDefaultValue;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    return value.vectorValue != (Vector4)property.PropertyDefaultValue;
#if UNITY_2022_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    return value.intValue != Convert.ToInt32(property.PropertyDefaultValue);
#endif
                default: return value.floatValue != Convert.ToSingle(property.PropertyDefaultValue);
            }
        }

        internal static bool HasChangedTextureTransform(ShaderProperty property)
        {
            var value = property.MaterialProperty;
            if (value.type != MaterialProperty.PropType.Texture) return false;
            if (value.textureScaleAndOffset != new Vector4(1, 1, 0, 0)) return true;
            return value.targets.OfType<Material>().Any(material => material.HasProperty(value.name)
                && (material.GetTextureScale(value.name) != Vector2.one || material.GetTextureOffset(value.name) != Vector2.zero));
        }

        private static Image HeaderIcon(string name)
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
                var fallback = new DropdownField(VRCFallbackProperty.RetainedNames.ToList(),0); _view.UseInspectorMenu(fallback); input.Add(fallback);
                _fields.Track(fallback,()=>{string tag=Model.Shader.Materials[0].GetTag("VRCFallback",false,"None");int index=Array.IndexOf(VRCFallbackProperty.RetainedValues,tag);fallback.SetValueWithoutNotify(index<0?tag:VRCFallbackProperty.RetainedNames[index]); fallback.showMixedValue=Model.Shader.Materials.Select(m=>m.GetTag("VRCFallback",false,"None")).Distinct().Skip(1).Any();});
                fallback.RegisterValueChangedCallback(e=>{if(fallback.index>=0)Model.Mutate("VRChat Fallback",m=>m.SetOverrideTag("VRCFallback",VRCFallbackProperty.RetainedValues[fallback.index]));}); Add(fallbackRow);
            }
            var row = RetainedFields.Row("Render Queue",out input);
            _fields.Track(row, () => row.style.display = Config.Instance.showRenderQueue ? DisplayStyle.Flex : DisplayStyle.None);
            input.AddToClassList("thry-components");
            string[] queueNames = { "From shader", "Background", "Geometry", "Alpha test", "Transparent", "Overlay" };
            int[] queueValues = { -1, 1000, 2000, 2450, 3000, 4000 };
            var queuePreset = new DropdownField(queueNames.ToList(), 0); _view.UseInspectorMenu(queuePreset); input.Add(queuePreset); queuePreset.style.flexGrow = 1;
            queuePreset.style.flexBasis = 0; queuePreset.style.flexShrink = 1; queuePreset.style.minWidth = 0;
            _fields.Track(queuePreset, () => { var material = Model.Shader.Materials[0]; int index = Array.IndexOf(queueValues, material.renderQueue); queuePreset.SetValueWithoutNotify(index < 0 ? "Custom" : queueNames[index]); queuePreset.showMixedValue = Model.Shader.Materials.Select(m => m.renderQueue).Distinct().Skip(1).Any(); });
            queuePreset.RegisterValueChangedCallback(e => { if (queuePreset.index >= 0) Model.Mutate("Render Queue", m => m.renderQueue = queueValues[queuePreset.index]); });
            var queue = new UnityEngine.UIElements.IntegerField(); input.Add(queue);
            queue.style.width = 64;
            queue.style.flexShrink = 0;
            _fields.Track(queue, () => { queue.SetValueWithoutNotify(Model.Shader.Materials[0].renderQueue); queue.showMixedValue = Model.Shader.Materials.Select(m=>m.renderQueue).Distinct().Skip(1).Any(); });
            queue.RegisterValueChangedCallback(e => Model.Mutate("Render Queue",m => m.renderQueue = e.newValue)); Add(row);
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
}
#endif
