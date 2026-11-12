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
            EnableInClassList("thry-material-locked", Model.Shader.IsLockedMaterial);
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
            var queueWarning = new VisualElement { name = "thry-grabpass-queue-warning" };
            queueWarning.Add(new HelpBox(GrabPassQueueWarning.Message, HelpBoxMessageType.Warning));
            Add(queueWarning);
            _fields.Track(queueWarning, () => queueWarning.style.display =
                GrabPassQueueWarning.HasAffected(Model.Shader.Materials) ? DisplayStyle.Flex : DisplayStyle.None);
            Add(Presets.CreateEditorControls(Model, _fields));
            var toolbar = SectionEditing.CreateToolbar?.Invoke(shader); if(toolbar != null) Add(toolbar);
            var materialActions = new VisualElement { name = "thry-material-actions" };
            materialActions.AddToClassList("thry-material-actions");
            Add(materialActions);
            var actionRow = new VisualElement { name = "thry-action-row" };
            actionRow.AddToClassList("thry-action-row");
            if (shader.RetainedPreset != null)
            {
                var renderingMode = _fields.Field(shader.RetainedPreset, true);
                renderingMode.AddToClassList("thry-rendering-mode");
                renderingMode.tooltip = "Rendering mode";
                var caption = new Label { pickingMode = PickingMode.Ignore };
                caption.AddToClassList("thry-rendering-caption"); caption.AddToClassList("thry-dropdown-caption");
                renderingMode.Q(className: BasePopupField<string, string>.inputUssClassName)?.Insert(0, caption);
                _fields.Track(caption, () => caption.text = RetainedText.Get(Model.Shader, "rendering", "Rendering"));
                actionRow.Add(renderingMode);
            }
            var queueBox = new VisualElement(); queueBox.AddToClassList("thry-queue-field");
            var queueField = new UnityEngine.UIElements.IntegerField { name = "thry-render-queue", isDelayed = true };
            // The caption is the named-queue picker; the number beside it stays a plain number box.
            var queueCaption = new Label { name = "thry-render-queue-preset", focusable = true, tabIndex = 0 };
            queueCaption.AddToClassList("thry-rendering-caption"); queueCaption.AddToClassList("thry-queue-caption");
            queueField.Q(className: UnityEngine.UIElements.IntegerField.inputUssClassName)?.Insert(0, queueCaption);
            string[] queueNames = { RetainedText.Get(Model.Shader, "queue_shader", "From Shader"), RetainedText.Get(Model.Shader, "queue_background", "Background"), RetainedText.Get(Model.Shader, "queue_geometry", "Geometry"), RetainedText.Get(Model.Shader, "queue_alpha_test", "AlphaTest"), RetainedText.Get(Model.Shader, "queue_transparent", "Transparent"), RetainedText.Get(Model.Shader, "queue_overlay", "Overlay") };
            int[] queueValues = { -1, 1000, 2000, 2450, 3000, 4000 };
            Action pickQueue = () =>
            {
                int current = Model.Shader.Materials[0].renderQueue;
                bool mixedQueue = Model.Shader.Materials.Skip(1).Any(m => m.renderQueue != current);
                RetainedMenu.Open(queueBox.worldBound, queueCaption, queueNames.Select((label, i) => new RetainedMenu.Item
                {
                    Text = label, Checked = !mixedQueue && queueValues[i] == current,
                    Action = () => Model.Mutate(RetainedText.Get(Model.Shader, "render_queue", "Render Queue"), m => m.renderQueue = queueValues[i])
                }));
            };
            queueCaption.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                Helpers.EventExtensions.PreventDefaultHandling(e); e.StopImmediatePropagation(); pickQueue();
            }, TrickleDown.TrickleDown);
            queueCaption.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                Helpers.EventExtensions.PreventDefaultHandling(e); e.StopImmediatePropagation(); pickQueue();
            }, TrickleDown.TrickleDown);
            queueBox.Add(queueField);
            _fields.DecoratePreset(queueBox, Model.Shader.RetainedQueue, true);
            _fields.Track(queueBox, () =>
            {
                queueBox.style.display = Config.Instance.showRenderQueue ? DisplayStyle.Flex : DisplayStyle.None;
                queueCaption.text = RetainedText.Get(Model.Shader, "queue", "Queue");
                int value = Model.Shader.Materials[0].renderQueue;
                bool mixed = Model.Shader.Materials.Skip(1).Any(m => m.renderQueue != value);
                RetainedFields.SynchronizeValue(queueField, value, mixed);
                int index = Array.IndexOf(queueValues, value);
                // A queue matching no named one is described by its offset from the nearest, as Unity
                // does: 2225 reads "Geometry +225".
                string tooltip = RetainedText.Get(Model.Shader, "render_queue", "Render Queue")
                    + (mixed ? "" : ": " + (index < 0 ? Helpers.RenderQueueHelper.GetDisplayName(value, queueNames, queueValues) : queueNames[index]));
                if (queueBox.tooltip != tooltip) queueBox.tooltip = tooltip;
            });
            queueField.RegisterValueChangedCallback(e => Model.Mutate(RetainedText.Get(Model.Shader, "render_queue", "Render Queue"), m => m.renderQueue = e.newValue));
            actionRow.Add(queueBox);
            if (shader.RetainedOptimizer != null)
            {
                var variantNotice = new HelpBox("Material variants can't be locked. Clear Parent above to lock this material.", HelpBoxMessageType.Info) { name = "thry-variant-notice" };
                materialActions.Add(variantNotice);
                var relockNotice = new HelpBox("", HelpBoxMessageType.Info) { name = "thry-relock-notice" };
                var relockText = relockNotice.Q<Label>(className: HelpBox.labelUssClassName);
                relockText.style.flexGrow = 1; relockText.style.flexShrink = 1;
                var outdated = new List<Material>();
                var relock = new Button(() =>
                {
                    shader.ActivateRetained();
                    ShaderOptimizer.RelockMaterials(outdated, ShaderOptimizer.ProgressBar.Uncancellable);
                    shader.Reload(); Model.Notify();
                }) { name = "thry-relock-button", text = "Relock", tooltip = "Unlock, update to the installed shader version and lock again. The look changes wherever the update changed it." };
                relock.style.flexShrink = 0; relock.style.alignSelf = Align.Center;
                relockNotice.Add(relock);
                materialActions.Add(relockNotice);
                int relockKey = -1;
                _fields.Track(relockNotice, () =>
                {
                    int key = shader.IsLockedMaterial ? 1 : 0;
                    if (key != 0) foreach (var m in shader.Materials) key = key * 31 + (m == null ? 0 : m.GetHashCode() * 397 ^ (m.shader != null ? m.shader.GetHashCode() : 0));
                    if (key == relockKey) return;
                    relockKey = key; outdated.Clear();
                    string lockedVersion = null, installedVersion = null; bool sameVersion = true;
                    if (shader.IsLockedMaterial)
                        foreach (var m in shader.Materials.Distinct())
                        {
                            if (!MaterialLockScanner.IsLockOutdated(m, out string from, out string to)) continue;
                            sameVersion &= outdated.Count == 0 || (from == lockedVersion && to == installedVersion);
                            lockedVersion = from; installedVersion = to; outdated.Add(m);
                        }
                    relockNotice.style.display = outdated.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    if (outdated.Count == 0) return;
                    relockNotice.text = sameVersion && outdated.Count == shader.Materials.Distinct().Count()
                        ? "Locked with " + lockedVersion + ". The installed shader is " + installedVersion + "."
                        : outdated.Count + (outdated.Count == 1 ? " material is" : " materials are") + " locked with a different shader version than the installed one.";
                });
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
                var lockIcon = new VisualElement { pickingMode = PickingMode.Ignore }; lockIcon.AddToClassList("thry-lock-icon");
                lockIcon.generateVisualContent += context => DrawPadlock(context.painter2D, lockIcon, shader.IsLockedMaterial);
                var lockText = new Label { pickingMode = PickingMode.Ignore };
                button.Add(lockIcon); button.Add(lockText);
                bool? drawnOpen = null;
                _fields.Track(button, () =>
                {
                    bool locked = shader.IsLockedMaterial;
                    lockText.text = locked ? RetainedText.Get(Model.Shader, "unlock_short", "Unlock") : RetainedText.Get(Model.Shader, "lock_short", "Lock");
                    button.tooltip = locked ? RetainedText.Get(Model.Shader, "unlock_shader", "Unlock Shader") : RetainedText.Get(Model.Shader, "lock_shader", "Lock In Optimized Shader");
                    if (drawnOpen != locked) { drawnOpen = locked; lockIcon.MarkDirtyRepaint(); }
                }); actionRow.Add(button);
                materialActions.Add(actionRow);
                _fields.Track(variantNotice, () =>
                {
                    bool variant = shader.Materials.Any(m => m != null && m.isVariant);
                    variantNotice.style.display = variant ? DisplayStyle.Flex : DisplayStyle.None;
                    button.SetEnabled(!variant);
                });
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
            if (actionRow.parent == null && actionRow.childCount > 0) materialActions.Add(actionRow);
            foreach (var child in shader.RetainedRoot.Children) AddPart(this, child, 0);
            AddFooter();
        }
        private static void DrawPadlock(Painter2D painter, VisualElement icon, bool open)
        {
            float unit = icon.contentRect.width / 14f;
            if (unit <= 0) return;
            Vector2 P(float x, float y) => new Vector2(x * unit, y * unit);
            painter.strokeColor = icon.resolvedStyle.color;
            painter.fillColor = icon.resolvedStyle.color;
            painter.lineWidth = 1.5f * unit;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(P(4.25f, 6.5f)); painter.LineTo(P(4.25f, 4.5f));
            painter.Arc(P(7, 4.5f), 2.75f * unit, 180, open ? 320 : 360);
            if (!open) painter.LineTo(P(9.75f, 6.5f));
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(P(3.7f, 6.5f));
            painter.LineTo(P(10.3f, 6.5f)); painter.ArcTo(P(11.5f, 6.5f), P(11.5f, 7.7f), 1.2f * unit);
            painter.LineTo(P(11.5f, 11.3f)); painter.ArcTo(P(11.5f, 12.5f), P(10.3f, 12.5f), 1.2f * unit);
            painter.LineTo(P(3.7f, 12.5f)); painter.ArcTo(P(2.5f, 12.5f), P(2.5f, 11.3f), 1.2f * unit);
            painter.LineTo(P(2.5f, 7.7f)); painter.ArcTo(P(2.5f, 6.5f), P(3.7f, 6.5f), 1.2f * unit);
            painter.ClosePath();
            painter.Fill();
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
            var fold = new Button { tooltip = "Expand or collapse section. Alt+click also opens or closes everything inside." }; fold.AddToClassList("thry-fold-arrow");
            fold.clickable.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Alt });
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
            var headerReferences = new List<KeyValuePair<VisualElement, ShaderProperty>>();
            foreach (var id in references)
                foreach (var reference in _fields.ScopedReferences(group, id))
                {
                    var holder = new VisualElement { userData = reference }; holder.AddToClassList("thry-header-reference");
                    _fields.Toggle(holder, reference); _fields.BindProperty(holder, reference); header.Add(holder);
                    headerReferences.Add(new KeyValuePair<VisualElement, ShaderProperty>(holder, reference));
                }
            var titleArea = new VisualElement { tooltip = group.TooltipText }; titleArea.AddToClassList("thry-section-title-area"); header.Add(titleArea);
            var title = new Label(SectionCaption(group)); title.AddToClassList("thry-section-title"); titleArea.Add(title);
            foreach (var entry in headerReferences)
                _fields.DecorateHeaderAnimation(headerReferences.Count == 1 ? titleArea : entry.Key, entry.Value,
                    headerReferences.Count == 1 ? title : null);
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
                bool hasAnimated = group.HasAnimatedDescendant, hasRenamed = group.HasRenameAnimatedDescendant;
                header.EnableInClassList("thry-has-animation", hasAnimated || hasRenamed);
                animated.style.display = Config.Instance.showAnimatedDotOnHeaders && hasAnimated ? DisplayStyle.Flex : DisplayStyle.None;
                animated.style.backgroundColor = Styles.AnimatedColor;
                renamed.style.display = Config.Instance.showAnimatedDotOnHeaders && hasRenamed ? DisplayStyle.Flex : DisplayStyle.None;
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
                if (data == null) continue;
                bool isVideo = data == group.Options.button_video;
                var resource = HeaderAction(resourceIcons[i], data.hover, () =>
                {
                    if (isVideo) { if (!string.IsNullOrEmpty(data.action?.data)) VideoPlayerWindow.OpenUrl(data.action.data, data.text ?? "Video Tutorial"); }
                    else data.action?.Perform(Model.Shader.Materials);
                });
                header.Add(resource);
                if (data.condition_show != null)
                    _fields.TrackVisible(resource, () => resource.style.display = data.condition_show.Test() ? DisplayStyle.Flex : DisplayStyle.None, header);
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
            foreach (var extra in SectionExtensions.CreateHeaderButtons(group.MaterialProperty?.name, Model.Shader.Materials))
            {
                var current = extra;
                Button button = null;
                button = new Button(() => {
                    if (current.Click != null) { try { current.Click(); } catch (Exception e) { Debug.LogException(e); } return; }
                    GenericMenu built = null;
                    try { built = current.Menu(); } catch (Exception e) { Debug.LogException(e); }
                    if (built != null && built.GetItemCount() > 0) _view.ShowLegacyMenu(built, button);
                }) { tooltip = current.Tooltip };
                button.AddToClassList("thry-header-action");
                if (!string.IsNullOrEmpty(current.Class)) button.AddToClassList(current.Class);
                var icon = new Image { image = current.Icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("thry-header-icon");
                button.Add(icon);
                header.Add(button);
                if (current.Lit != null) _fields.TrackVisible(button, () => button.EnableInClassList("thry-linked", current.Lit()));
            }
            var menu = HeaderAction("menu", "Section actions", () => {
                var actions = ShaderHeader.RetainedHeaderMenu(group, Model.Shader.Materials,
                    search => search.AddItem(new GUIContent(RetainedText.Get(Model.Shader, "search_section", "Search in this section")), false, () => _view.SearchSection(group)));
                _view.ShowLegacyMenu(actions, header);
            }); header.Add(menu);
            var children = new VisualElement(); children.AddToClassList("thry-section-content"); root.Add(children);
            bool built = false;
            _fields.TrackVisible(header, () => {
                title.text = SectionCaption(group);
                if (!Config.Instance.showStarNextToNonDefaultProperties) changedDot.style.display = DisplayStyle.None;
                else if (!Model.DeferSummaryRefresh)
                    changedDot.style.display = RetainedPropertyDefaults.HasChangedSection(Model.Shader, changedProperties) ? DisplayStyle.Flex : DisplayStyle.None;
            });
            Action update = () => {
                bool category = depth != 0 || Model.Shader.FocusedCategory == null || group.MaterialProperty.name == Model.Shader.FocusedCategory;
                root.style.display = category && group.RetainedVisible ? DisplayStyle.Flex : DisplayStyle.None;
                root.EnableInClassList("thry-section-after-property", depth > 0 && !positioning && FollowsProperty(root));
                header.EnableInClassList("thry-excluded", SectionEditing.IsExcluded?.Invoke(group) == true);
                root.EnableInClassList("thry-section-open", group.RetainedExpanded);
                children.SetEnabled(group.RetainedChildrenEnabled && (group.Options.condition_enable == null || group.Options.condition_enable.Test()));
                children.style.display = group.RetainedExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                foldIcon.image = group.RetainedExpanded ? _expandedCaret : _collapsedCaret;
                if (!group.RetainedExpanded || built) return;
                built = true;
                if (!_editing && RetainedPathing.CanBuild(group, Model))
                {
                    children.Add(new RetainedPathing(Model, _fields, group,
                        (container, child) => AddPart(container, child, depth + 1)));
                    return;
                }
                if (!_editing && RetainedUVDiscard.CanBuild(group, Model))
                {
                    children.Add(new RetainedUVDiscard(Model, _fields, group,
                        (container, child) => AddPart(container, child, depth + 1)));
                    return;
                }
                string sectionName = group.MaterialProperty?.name;
                var extension = _editing || !SectionExtensions.Has(sectionName) ? null
                    : SectionExtensions.Create(sectionName, new SectionExtensions.Context(Model, _fields));
                if (extension != null) children.Add(extension);
                // Sections and subsections organize their parent header's preset; only
                // headers own named preset collections.
                if (group is ShaderHeader && Model.Shader.IsSectionedPresetEditor)
                {
                    var presetName = new TextField("Preset name") { isDelayed = true, value = Presets.GetSectionPresetName(Model.Shader.Materials[0], group.MaterialProperty.name) };
                    children.Add(presetName);
                    presetName.RegisterValueChangedCallback(e => Model.Mutate("Section preset name", m => Presets.SetSectionPreset(m, group.MaterialProperty.name, e.newValue)));
                }
                _fields.AddDecalBake(children, group);
                foreach (var child in group.Children)
                {
                    // The positioning toolbar provides contextual guidance in place of the legacy banner.
                    if (positioning && child is ShaderProperty help && help.Content.text.Contains("Raycast")
                        && help.MyShader.GetPropertyAttributes(help.ShaderPropertyIndex).Any(a => new DrawerAttribute(a).Name == "Helpbox")) continue;
                    if (extension != null && SectionExtensions.IsHidden(sectionName, child.MaterialProperty?.name)) continue;
                    AddPart(children,child,depth+1);
                }
            };
            Action<bool> expand = everything =>
            {
                if (everything) Model.SetExpandedDeep(group, !group.RetainedExpanded);
                else Model.SetExpanded(group, !group.RetainedExpanded);
                update();
            };
            fold.clickable.clickedWithEventInfo += e => expand(e is IPointerEvent pointer ? pointer.altKey : e is IMouseEvent mouse && mouse.altKey);
            header.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                for (var target = e.target as VisualElement; target != null && target != header; target = target.parent)
                    if (target is Button || target is Toggle || target.ClassListContains("unity-base-field")) return;
                expand(e.altKey); e.StopPropagation();
            });
            _fields.Track(root,update); parent.Add(root);
        }
        private static bool FollowsProperty(VisualElement section)
        {
            var parent = section.parent;
            if (parent == null) return false;
            for (int i = parent.IndexOf(section) - 1; i >= 0; i--)
            {
                var sibling = parent[i];
                if (sibling.style.display == DisplayStyle.None) continue;
                return !sibling.ClassListContains("thry-section");
            }
            return false;
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
            return part.UnmarkedLabel;
        }

        private List<KeyValuePair<ShaderProperty, string>> SectionProperties(ShaderGroup section)
            => ChangeScope(Model.Shader, section, section.Children,
                new[] { section.Options.reference_property }.Concat(section.Options.reference_properties ?? Array.Empty<string>()));

        internal static List<KeyValuePair<ShaderProperty, string>> ChangeScope(ShaderEditor shader, ShaderPart owner, IEnumerable<ShaderPart> parts, IEnumerable<string> references)
        {
            var result = new List<KeyValuePair<ShaderProperty, string>>();
            var visited = new HashSet<ShaderPart> { owner };
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
                if (part.Options.reference_property != null && shader.PropertyDictionary.TryGetValue(part.Options.reference_property, out reference)) collect(reference);
                if (part.Options.reference_properties != null)
                    foreach (var name in part.Options.reference_properties)
                        if (name != null && shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference);
                if (part.AdditionalDefaultCheckProperties != null)
                    foreach (var name in part.AdditionalDefaultCheckProperties)
                        if (name != null && shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference);
            };
            foreach (var child in parts) collect(child);
            foreach (var name in references)
            {
                ShaderProperty reference;
                if (name != null && shader.PropertyDictionary.TryGetValue(name, out reference)) collect(reference);
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
                fallback.RegisterValueChangedCallback(e=>{if(fallback.index>=0)Model.Mutate("VRChat Fallback",m=>m.SetOverrideTag("VRCFallback",VRCFallbackProperty.RetainedValues[fallback.index]));});
                input.AddToClassList("thry-components");
                fallback.style.flexGrow = 1; fallback.style.flexBasis = 0; fallback.style.flexShrink = 1; fallback.style.minWidth = 0;
                var matchFallback = new Button(() => Model.Mutate("VRChat Fallback", m => Helpers.RenderingPresets.MatchFallback(m)))
                    { name = "thry-vrc-fallback-match", text = "Match", tooltip = "Match the fallback to this material's render mode and culling." };
                matchFallback.AddToClassList("thry-component-spaced");
                matchFallback.style.flexShrink = 0; matchFallback.style.marginTop = 0; matchFallback.style.marginRight = 0; matchFallback.style.marginBottom = 0;
                input.Add(matchFallback); Add(fallbackRow);
            }
            var footer = new VisualElement(); footer.AddToClassList("thry-footer");
            var credit = new VisualElement(); credit.AddToClassList("thry-footer-credit"); footer.Add(credit);
            var designed = new Label("UI 3.0 by Poiyomi Labs"); designed.AddToClassList("thry-footer-credit-line"); credit.Add(designed);
            var original = new Label("Original UI by Thryrallo"); original.AddToClassList("thry-footer-credit-line"); credit.Add(original);
            original.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) Application.OpenURL("https://www.x.com/Thryrallo"); });
            credit.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) Application.OpenURL("https://www.github.com/poiyomi/ThryEditor"); });
            var thry = new Button(() => Application.OpenURL("https://www.x.com/Thryrallo")) { tooltip = "Thryrallo" };
            var links = new VisualElement(); links.AddToClassList("thry-footer-links"); footer.Add(links);
            thry.Add(new Image { image = Icons.thryIcon.normal.background, scaleMode = ScaleMode.ScaleToFit }); links.Add(thry);
            foreach(var item in Model.Shader.RetainedFooters)
            {
                var data = item.Data; if(data == null) continue;
                var button = new Button(() => data.action?.Perform(Model.Shader.Materials)) { tooltip = data.hover };
                if(data.texture != null) button.Add(new Image { image = data.texture.loaded_texture, scaleMode = ScaleMode.ScaleToFit }); else button.text = data.text;
                links.Add(button);
            }
            // A row that cannot hold every icon is split evenly, so one never wraps by itself.
            footer.RegisterCallback<GeometryChangedEvent>(e =>
            {
                int count = links.childCount;
                if (count == 0 || e.newRect.width <= 0 || Mathf.Approximately(e.oldRect.width, e.newRect.width)) return;
                var first = links[0];
                float each = first.resolvedStyle.width + first.resolvedStyle.marginLeft + first.resolvedStyle.marginRight;
                if (!(each > 0)) return;
                footer.EnableInClassList("thry-footer-stacked", e.newRect.width < credit.resolvedStyle.minWidth.value + count * each);
                int rows = Mathf.CeilToInt(count / (float)Mathf.Max(1, Mathf.FloorToInt(e.newRect.width / each)));
                links.style.maxWidth = Mathf.CeilToInt(count / (float)rows) * each;
            });
            Add(footer);
        }
    }
    // Presentation-only groups use the same chrome as material subcategories,
    // without inventing shader properties to store their expansion state.
    internal sealed class RetainedSubcategory : VisualElement
    {
        readonly VisualElement content;
        readonly Action reveal;
        public override VisualElement contentContainer => content ?? this;
        // Opens for this view without changing the remembered state.
        internal void Reveal() => reveal();

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
            reveal = () => { expanded = true; refresh(); };
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
