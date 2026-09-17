#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // Presentation only: shader names, vector storage, animation bindings and masking are unchanged.
    internal sealed class RetainedPathing : VisualElement
    {
        readonly RetainedMaterialModel model;
        readonly RetainedFields fields;
        readonly ShaderGroup group;
        readonly string[] letters = { "R", "G", "B", "A" };
        readonly List<VisualElement>[] columns = { new List<VisualElement>(), new List<VisualElement>(), new List<VisualElement>(), new List<VisualElement>() };
        readonly VisualElement[] strips = new VisualElement[4];
        readonly Button[] selectButtons = new Button[4];
        readonly Label[] routeLabels = new Label[4];
        readonly VisualElement visual, classic;
        readonly Action<VisualElement, ShaderPart> addOriginal;
        readonly Label status;
        int selected, solo = -1;
        float previewTime = 2;
        double previous;
        bool playing = true, classicBuilt, previewHidden;
        IVisualElementScheduledItem previewSchedule;
        readonly List<VisualElement> visibilityAncestors = new List<VisualElement>();
        readonly List<ScrollView> visibilityScrolls = new List<ScrollView>();
        Button play;
        Slider scrub;
        VisualElement previewContent;

        internal static bool CanBuild(ShaderGroup section, RetainedMaterialModel model)
        {
            return section.MaterialProperty?.name == "m_start_pathing"
                && new[] { "_PathTypeR", "_PathTypeG", "_PathTypeB", "_PathTypeA", "_PathSpeed", "_PathWidth", "_PathSoftness", "_PathTime", "_PathOffset", "_PathSegments", "_PathColorR", "_PathColorG", "_PathColorB", "_PathColorA", "_PathGapLengths", "_PathEmissionStrength" }
                    .All(n => model.Shader.PropertyDictionary.ContainsKey(n));
        }
        ShaderProperty Property(string name) => model.Shader.PropertyDictionary.TryGetValue(name, out var p) ? p : null;
        float Number(string name) => Property(name)?.MaterialProperty.GetNumber() ?? 0;
        float Component(string name, int index) => Property(name)?.MaterialProperty.vectorValue[index] ?? 0;
        bool Mixed(string name, int component = -1)
        {
            var p = Property(name);
            if (p == null) return false;
            return component < 0 ? p.MaterialProperty.hasMixedValue : model.Owners(p).Select(m => m.GetVector(name)[component]).Distinct().Skip(1).Any();
        }

        internal RetainedPathing(RetainedMaterialModel model, RetainedFields fields, ShaderGroup group, Action<VisualElement, ShaderPart> addOriginal)
        {
            this.model = model; this.fields = fields; this.group = group; this.addOriginal = addOriginal;
            name = "pathing-studio"; AddToClassList("thry-pathing");
            var sheet = Resources.Load<StyleSheet>("ThryPathing"); if (sheet != null) styleSheets.Add(sheet);
            var bar = Row(); bar.AddToClassList("pathing-topbar");
            var original = new Toggle("Original controls") { name = "pathing-original" }; bar.Add(original); Add(bar);
            visual = new VisualElement(); Add(visual);
            classic = new VisualElement(); classic.style.display = DisplayStyle.None; Add(classic);
            original.RegisterValueChangedCallback(e => {
                if (e.newValue && !classicBuilt) { classicBuilt = true; foreach (var child in group.Children) addOriginal(classic, child); }
                visual.style.display = e.newValue ? DisplayStyle.None : DisplayStyle.Flex;
                classic.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                UpdatePreviewActivity();
            });
            BuildPreview();
            var source = Fold("Sources", true); visual.Add(source);
            var help = new Label("Direction = progress along a path. Masks = where it appears."); help.AddToClassList("pathing-help"); source.Add(help);
            AddField(source, "_PathSource"); AddField(source, "_PathGradientType"); AddField(source, "_PathingUVSelect");
            AddField(source, "_PathingMap"); AddField(source, "_PathingMaskMap"); AddField(source, "_PathingColorMap");
            var sampling = Fold("Sampling & UV directions", false); source.Add(sampling); AddField(sampling, "_PathPointSampling");
            for (int i = 0; i < 4; i++) AddField(sampling, "_PathSourceDir" + letters[i]);
            BuildMatrix();
            var actions = Row(); actions.AddToClassList("pathing-actions");
            var presets = new Button { name = "pathing-presets", text = "Starting look…", tooltip = "Apply a motion recipe to the selected path. Keeps its color, masks and AudioLink settings." };
            presets.clicked += () => ShowPresets(presets); actions.Add(presets); var copy = new Button { name = "pathing-copy", text = "Copy motion…", tooltip = "Copy the selected path's shape and timing to another path. Keeps destination color and masks." }; copy.clicked += () => ShowCopy(copy); actions.Add(copy);
            fields.Track(actions, () => { bool editable = MotionNames.All(n => model.CanEdit(Property(n))) && model.CanEdit(Property("_PathType" + letters[selected])); presets.SetEnabled(editable); copy.SetEnabled(editable); });
            visual.Add(actions);
            status = new Label("Select a path header to preview it or apply a starting look.") { name = "pathing-status" }; status.AddToClassList("pathing-help"); visual.Add(status);
            BuildMaskRouting();
            BuildRanges();
            var output = Fold("Output & blending", false); visual.Add(output); AddField(output, "_PathSurfaceBlendMode"); AddField(output, "_PathingOverrideAlpha");
            var note = new Label("Color alpha controls surface opacity; emission is independent. Surface paths layer R → G → B → A."); note.AddToClassList("pathing-help"); output.Add(note);
            // Keep the complete existing controls and their menus for complex mask/audio workflows.
            var handled = new HashSet<string> { "s_start_PathGlobalMasks", "s_start_PathAudioLink" };
            foreach (var child in group.Children.Where(c => handled.Contains(c.MaterialProperty?.name))) addOriginal(visual, child);
            fields.Track(this, Synchronize);
            // UI scheduler is detached with the element; never subscribe to a global editor tick.
            previewSchedule = schedule.Execute(() => {
                UpdatePreviewActivity();
                if (!previewSchedule.isActive) return;
                double now = EditorApplication.timeSinceStartup;
                if (panel != null && group.RetainedExpanded && visual.resolvedStyle.display != DisplayStyle.None && visible && worldBound.height > 0 && playing)
                {
                    previewTime += (float)Math.Min(.1, Math.Max(0, now - previous));
                    scrub.SetValueWithoutNotify(Mathf.Repeat(previewTime, 10) / 10);
                    foreach (var strip in strips) strip.MarkDirtyRepaint();
                }
                previous = now;
            }).Every(16);
            previewSchedule.Pause();
            RegisterCallback<AttachToPanelEvent>(e => { if (e.target == this) ObserveVisibility(); });
            RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == this) { previewSchedule.Pause(); UnobserveVisibility(); } });
            RegisterCallback<GeometryChangedEvent>(PreviewGeometryChanged);
        }
        void PreviewGeometryChanged(GeometryChangedEvent e) => UpdatePreviewActivity();
        void PreviewScrolled(float value) => UpdatePreviewActivity();
        void ObserveVisibility()
        {
            UnobserveVisibility();
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                visibilityAncestors.Add(ancestor);
                ancestor.RegisterCallback<GeometryChangedEvent>(PreviewGeometryChanged);
                if (ancestor is ScrollView scroll)
                {
                    visibilityScrolls.Add(scroll);
                    scroll.verticalScroller.valueChanged += PreviewScrolled;
                    scroll.horizontalScroller.valueChanged += PreviewScrolled;
                }
            }
            UpdatePreviewActivity();
        }
        void UnobserveVisibility()
        {
            foreach (var ancestor in visibilityAncestors) ancestor.UnregisterCallback<GeometryChangedEvent>(PreviewGeometryChanged);
            foreach (var scroll in visibilityScrolls)
            {
                scroll.verticalScroller.valueChanged -= PreviewScrolled;
                scroll.horizontalScroller.valueChanged -= PreviewScrolled;
            }
            visibilityAncestors.Clear(); visibilityScrolls.Clear();
        }
        void UpdatePreviewActivity()
        {
            if (previewSchedule == null) return;
            var preview = previewContent;
            bool active = panel != null && playing && !previewHidden && group.RetainedExpanded
                && visual.style.display != DisplayStyle.None && visible
                && RetainedFields.AncestorsDisplayed(this) && preview.worldBound.height > 0;
            if (active)
                foreach (var scroll in visibilityScrolls)
                    if (!scroll.contentViewport.worldBound.Overlaps(preview.worldBound)) { active = false; break; }
            if (active && !previewSchedule.isActive)
            {
                previous = EditorApplication.timeSinceStartup;
                previewSchedule.Resume();
            }
            else if (!active && previewSchedule.isActive) previewSchedule.Pause();
        }
        static VisualElement Row() { var row = new VisualElement(); row.AddToClassList("pathing-row"); return row; }
        Foldout Fold(string text, bool open) { var f = new Foldout { text = text, value = open }; f.AddToClassList("pathing-fold"); RetainedUiState.Bind(f, model.Shader.Shader.name, "pathing-studio:" + text, open); return f; }
        void AddField(VisualElement parent, string name)
        {
            var p = Property(name); if (p == null) return;
            var field = fields.Field(p); parent.Add(field);
            if (name == "_PathingMap" || name == "_PathingColorMap" || name == "_PathingOverrideAlpha")
                fields.Track(field, () => {
                    var label = field.Q<Label>(className:"thry-texture-caption") ?? field.Q<Label>(className:"thry-property-label");
                    if (label != null) label.text = name == "_PathingMap" ? (Number("_PathSource") == 1 ? "UV coverage map" : "Direction map")
                        : name == "_PathingColorMap" ? "Color & shared mask" : "Write material alpha";
                });
        }

        void BuildPreview()
        {
            var preview = new VisualElement { name = "pathing-preview", tooltip = "Shape and timing for the first selected material. Masks and AudioLink are excluded." }; preview.AddToClassList("pathing-preview"); visual.Add(preview);
            var title = Row(); title.AddToClassList("pathing-preview-toolbar"); var label = new Label("MOTION PREVIEW"); label.AddToClassList("pathing-eyebrow"); title.Add(label);
            play = new Button(() => { playing = !playing; play.text = playing ? "Pause" : "Play"; UpdatePreviewActivity(); }) { text = "Pause", name = "pathing-preview-play", tooltip = "Play or pause this inspector preview. Does not change the material." }; title.Add(play);
            var isolate = new Button(() => { solo = solo < 0 ? selected : -1; Synchronize(); }) { text = "Solo", name = "pathing-preview-solo", tooltip = "Isolate the selected lane in this preview only." };
            fields.Track(isolate, () => { isolate.text = solo < 0 ? "Solo" : "Show all"; }); title.Add(isolate);
            var hide = new Button { text = "Hide", name = "pathing-preview-hide", tooltip = "Hide the motion preview." };
            hide.clicked += () => {
                previewHidden = !previewHidden;
                previewContent.style.display = previewHidden ? DisplayStyle.None : DisplayStyle.Flex;
                hide.text = previewHidden ? "Show" : "Hide";
                hide.tooltip = previewHidden ? "Show the motion preview." : "Hide the motion preview.";
                preview.EnableInClassList("pathing-preview-hidden", previewHidden);
                UpdatePreviewActivity();
            };
            title.Add(hide); preview.Add(title);
            previewContent = new VisualElement { name = "pathing-preview-content" }; previewContent.AddToClassList("pathing-preview-content"); preview.Add(previewContent);
            for (int i = 0; i < 4; i++)
            {
                int channel = i; var row = Row(); var badge = new Button(() => Select(channel)) { text = letters[i], tooltip = "Select " + letters[i] + " path" }; badge.AddToClassList("pathing-lane-label"); badge.AddToClassList("pathing-accent-" + i); row.Add(badge);
                var strip = new VisualElement { name = "pathing-lane-" + i }; strip.AddToClassList("pathing-lane"); strip.AddToClassList("pathing-accent-" + i);
                strip.generateVisualContent += c => PaintLane(c, strip, channel); strip.RegisterCallback<PointerDownEvent>(e => Select(channel)); strips[i] = strip; row.Add(strip); previewContent.Add(row);
            }
            scrub = new Slider(0, 1) { name = "pathing-preview-scrub", tooltip = "Scrub a ten-second preview window. No material values are changed." };
            scrub.RegisterValueChangedCallback(e => { playing = false; play.text = "Play"; UpdatePreviewActivity(); previewTime = e.newValue * 10; foreach (var s in strips) s.MarkDirtyRepaint(); }); previewContent.Add(scrub);
        }
        void Select(int channel) { selected = channel; if (solo >= 0) solo = channel; Synchronize(); }
        VisualElement Cell(VisualElement row, int channel)
        {
            var cell = new VisualElement(); cell.AddToClassList("pathing-cell"); cell.AddToClassList("pathing-channel-" + channel); columns[channel].Add(cell); row.Add(cell); return cell;
        }
        VisualElement GridRow(VisualElement grid, string text, string tooltip = null, string propertyName = null)
        {
            var row = Row(); row.AddToClassList("pathing-grid-row"); VisualElement label = propertyName == null ? new Label(text) : fields.PathingCaption(Property(propertyName), text); label.tooltip = tooltip; label.AddToClassList("pathing-grid-label"); row.Add(label); grid.Add(row); return row;
        }
        void BuildMatrix()
        {
            var grid = new VisualElement { name = "pathing-grid" }; grid.AddToClassList("pathing-grid"); visual.Add(grid);
            var header = GridRow(grid, "Paths"); header.AddToClassList("pathing-grid-heading");
            for (int i = 0; i < 4; i++) { int ch = i; var cell = Cell(header, i); var button = new Button(() => Select(ch)) { text = letters[i] + " path", name = "pathing-select-" + i }; button.AddToClassList("pathing-accent-" + i); selectButtons[i] = button; cell.Add(button); }
            var shapes = GridRow(grid, "Shape", "Fill grows along the gradient; Pulse travels once; Loop wraps around; Dashes repeat.");
            var colors = GridRow(grid, "Color", "HDR path color. Its alpha controls surface opacity, not emission.");
            var themes = GridRow(grid, "Theme", "Choose a theme color or use the path color above.");
            for (int i = 0; i < 4; i++)
            {
                Cell(shapes, i).Add(fields.Field(Property("_PathType" + letters[i]), true));
                Cell(colors, i).Add(fields.Field(Property("_PathColor" + letters[i]), true));
                var p = Property("_PathColor" + letters[i] + "ThemeIndex"); if (p != null) Cell(themes, i).Add(fields.Field(p, true));
            }
            foreach (var entry in new[] { new[] { "Emission", "_PathEmissionStrength", "Adds light independently of surface opacity." }, new[] { "Length", "_PathWidth", "Length along the direction gradient, not world-space distance." }, new[] { "Softness", "_PathSoftness", "Feather the moving pulse's edges." }, new[] { "Gap", "_PathGapLengths", "Spacing between repeated dashes. Only used by Dashed paths." } })
            {
                var row = GridRow(grid, entry[0], entry[2], entry[1]);
                for (int i = 0; i < 4; i++) { var cell = Cell(row, i); cell.Add(fields.PathingComponent(Property(entry[1]), i)); if (entry[1] == "_PathGapLengths") { int ch = i; fields.Track(cell, () => cell.SetEnabled(Number("_PathType" + letters[ch]) == 3 && model.CanEdit(Property(entry[1])))); } }
            }
            var motion = GridRow(grid, "Playback", "Automatic uses speed. Manual holds the path at a position you can animate.", "_PathTime");
            var position = GridRow(grid, "Position", "Manual progress along the gradient. Values are preserved when switching back to Automatic.", "_PathTime");
            for (int i = 0; i < 4; i++)
            {
                int ch = i; var prop = Property("_PathTime");
                var menu = new DropdownField(new List<string> { "Auto", "Manual" }, 0) { name = "pathing-playback-" + i }; RetainedWindow.Dropdown(menu); Cell(motion, i).Add(menu);
                float remembered = 0;
                fields.Track(menu, () => { float value = Component("_PathTime", ch); if (value != -999) remembered = value; menu.SetValueWithoutNotify(value == -999 ? "Auto" : "Manual"); menu.showMixedValue = Mixed("_PathTime", ch); menu.SetEnabled(model.CanEdit(prop)); });
                menu.RegisterValueChangedCallback(e => model.VectorComponent(prop, ch, e.newValue == "Auto" ? -999 : remembered));
                var pos = Cell(position, i); var component = fields.PathingComponent(prop, i); pos.Add(component);
                var autoLabel = new Label("Automatic"); autoLabel.AddToClassList("pathing-auto-label"); pos.Add(autoLabel);
                fields.Track(pos, () => { bool auto = Component("_PathTime", ch) == -999 && !Mixed("_PathTime", ch); component.style.display = auto ? DisplayStyle.None : DisplayStyle.Flex; autoLabel.style.display = auto ? DisplayStyle.Flex : DisplayStyle.None; });
            }
            foreach (var entry in new[] { new[] { "Speed", "_PathSpeed", "Gradient cycles per second. Negative values reverse the direction; zero pauses." }, new[] { "Phase", "_PathOffset", "Offset the position of a path relative to the others." }, new[] { "Steps", "_PathSegments", "0 = continuous. Nonzero values quantize movement into that many steps." } })
            {
                var row = GridRow(grid, entry[0], entry[2], entry[1]); for (int i = 0; i < 4; i++) Cell(row, i).Add(fields.PathingComponent(Property(entry[1]), i));
            }
        }
        void BuildMaskRouting()
        {
            if (Property("_PathingMaskMap") == null) return;
            var fold = Fold("Mask routing", false); visual.Add(fold);
            var row = GridRow(fold, "PATH"); for (int i = 0; i < 4; i++) { var l = new Label(letters[i]); l.AddToClassList("pathing-accent-" + i); Cell(row, i).Add(l); }
            var channels = GridRow(fold, "Channel"); var notes = GridRow(fold, "Coverage");
            for (int i = 0; i < 4; i++) { var p = Property("_PathingMaskChannel" + letters[i]); if (p != null) Cell(channels, i).Add(fields.Field(p, true)); routeLabels[i] = new Label(); routeLabels[i].AddToClassList("pathing-routing-note"); Cell(notes, i).Add(routeLabels[i]); }
            var pack = new Button(() => {
                var properties = letters.Select(l => Property("_PathingMaskChannel" + l)).ToArray(); if (properties.Any(p => !model.CanEdit(p))) return;
                Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
                for (int i = 0; i < 4; i++) { int v = i; model.EditSingleProperty(properties[i], p => p.SetNumber(v)); }
                Undo.CollapseUndoOperations(undo); status.text = "R, G, B, A masks routed to their matching paths.";
            }) { text = "Match RGBA", tooltip = "Assign R → R, G → G, B → B and A → A. Undo restores all four." };
            var shared = new Button(() => {
                var properties = letters.Select(l => Property("_PathingMaskChannel" + l)).ToArray(); if (properties.Any(p => !model.CanEdit(p))) return;
                Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); foreach (var p in properties) model.EditSingleProperty(p, mp => mp.SetNumber(0)); Undo.CollapseUndoOperations(undo); status.text = "All paths use mask R. Ready for a single-channel BC4 texture.";
            }) { text = "Share R (BC4)", tooltip = "Use the red mask channel for every path." };
            var actions = Row(); actions.AddToClassList("pathing-actions"); actions.Add(pack); actions.Add(shared); fold.Add(actions);
            fields.Track(actions, () => actions.SetEnabled(letters.All(l => model.CanEdit(Property("_PathingMaskChannel" + l)))));
            var note = new Label("Masks restrict each path before overlap. Color texture alpha remains a separate shared mask."); note.AddToClassList("pathing-help"); fold.Add(note);
        }
        void BuildRanges()
        {
            if (Property("_EnablePathRemapping") == null) return;
            var fold = Fold("Gradient range", false); visual.Add(fold); AddField(fold, "_EnablePathRemapping");
            var help = new Label("Keep only the selected section of the gradient. The retained section stretches to cover the full motion."); help.AddToClassList("pathing-help"); fold.Add(help);
            for (int i = 0; i < 4; i++)
            {
                var p = Property("_PathRemap" + letters[i]); if (p == null) continue;
                fold.Add(fields.Field(p));
            }
        }
        static readonly string[] MotionNames = { "_PathSpeed", "_PathWidth", "_PathSoftness", "_PathGapLengths", "_PathTime", "_PathOffset", "_PathSegments" };
        void ShowCopy(VisualElement anchor)
        {
            var menu = new List<RetainedMenu.Item>(); int source = selected;
            for (int i = 0; i < 4; i++)
            {
                int target = i; if (target == source) continue;
                bool editable = model.CanEdit(Property("_PathType" + letters[target])) && MotionNames.All(n => model.CanEdit(Property(n)));
                if (!editable) { menu.Add(new RetainedMenu.Item { Text = "To " + letters[i] + " path" }); continue; }
                menu.Add(new RetainedMenu.Item { Text = "To " + letters[i] + " path", Action = () => {
                    Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
                    foreach (var n in MotionNames) { var p = Property(n); model.Edit(p, mp => { var v = mp.vectorValue; v[target] = v[source]; mp.vectorValue = v; }, true); }
                    model.Edit(Property("_PathType" + letters[target]), mp => { var owner = mp.targets.OfType<Material>().First(); mp.SetNumber(owner.GetFloat("_PathType" + letters[source])); }, true);
                    Undo.CollapseUndoOperations(undo); status.text = "Copied " + letters[source] + " motion to " + letters[target] + ". Color and masks kept.";
                } });
            }
            RetainedMenu.Open(anchor.worldBound, anchor, menu);
        }
        void ShowPresets(VisualElement anchor)
        {
            RetainedMenu.Open(anchor.worldBound, anchor, new[] {
                new RetainedMenu.Item { Text = "Traveling glow", Action = () => ApplyRecipe(2,.15f,.2f,.65f,0,-999) },
                new RetainedMenu.Item { Text = "Flowing dashes", Action = () => ApplyRecipe(3,.2f,.08f,.3f,.06f,-999) },
                new RetainedMenu.Item { Text = "Soft fill", Action = () => ApplyRecipe(0,.1f,.1f,.2f,0,-999) },
                new RetainedMenu.Item { Text = "Manual progress", Action = () => ApplyRecipe(0,0,.1f,0,0,.5f) }
            });
        }
        void ApplyRecipe(int type, float speed, float width, float softness, float gap, float time)
        {
            var typeProperty = Property("_PathType" + letters[selected]); if (!model.CanEdit(typeProperty) || MotionNames.Any(n => !model.CanEdit(Property(n)))) return;
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            model.EditSingleProperty(typeProperty, p => p.SetNumber(type));
            var values = new[] { speed, width, softness, gap, time, 0f, 0f };
            for (int i = 0; i < MotionNames.Length; i++) model.VectorComponent(Property(MotionNames[i]), selected, values[i]);
            Undo.CollapseUndoOperations(undo); status.text = "Starting motion applied to " + letters[selected] + ". Color, emission, masks and audio kept.";
        }
        void Synchronize()
        {
            UpdatePreviewActivity();
            if (selectButtons[0] == null) return;
            for (int i = 0; i < 4; i++)
            {
                bool off = Number("_PathType" + letters[i]) == 4 && !Mixed("_PathType" + letters[i]);
                foreach (var cell in columns[i]) { cell.EnableInClassList("pathing-selected", selected == i); cell.EnableInClassList("pathing-off", off); }
                selectButtons[i].tooltip = (off ? "Disabled. Choose a shape to enable." : "Select for recipes and preview solo.") + " Path " + letters[i];
                if (routeLabels[i] != null) { int channel = (int)Number("_PathingMaskChannel" + letters[i]); routeLabels[i].text = Mixed("_PathingMaskChannel" + letters[i]) ? "Mixed" : channel == 4 ? "Unmasked" : "Mask " + letters[Mathf.Clamp(channel,0,3)]; }
                strips[i].MarkDirtyRepaint();
            }
        }
        internal static float ShapeAlpha(float x, float time, float width, float softness, float gap, int type)
        {
            float half = width * .5f, inv = 1 / (half + .000001f), value = 0;
            if (type == 0) { float end = time * (1 + softness); value = softness > .00001f ? Smooth(end, end-softness, x) : (x <= time ? 1 : 0); }
            else if (type == 1) value = Mathf.Clamp01(1-Mathf.Abs(Mathf.Lerp(-half,1+half,time)-x)*inv);
            else if (type == 2) { float d = Mathf.Abs(time-x); value = Mathf.Clamp01(1-Mathf.Min(d,1-d)*inv); }
            else if (type == 3 && width+gap > .000001f) { float relative = width/(width+gap), pattern = Mathf.Repeat(x/(width+gap)-time,1), soft = Mathf.Min(softness*.5f*relative,relative*.499f); value = Smooth(0,soft,pattern)*Smooth(relative,relative-soft,pattern); }
            if (type == 1 || type == 2) value = Smooth(0,softness+.000001f,value);
            return x <= 0 ? 0 : value;
        }
        static float Smooth(float a, float b, float x) { if (Mathf.Abs(a-b)<.000001f) return x>=b?1:0; float t=Mathf.Clamp01((x-a)/(b-a)); return t*t*(3-2*t); }
        void PaintLane(MeshGenerationContext context, VisualElement strip, int channel)
        {
            float w=strip.contentRect.width,h=strip.contentRect.height; if(w<1||h<1)return;
            var color=Property("_PathColor"+letters[channel]).MaterialProperty.colorValue;
            float max=Mathf.Max(1,Mathf.Max(color.r,Mathf.Max(color.g,color.b))); color=new Color(color.r/max,color.g/max,color.b/max,1);
            var accent = strip.resolvedStyle.color;
            if (Mathf.Abs(color.r-color.g)+Mathf.Abs(color.g-color.b)<.01f) color=accent;
            var source = model.Owners(Property("_PathTime")).FirstOrDefault(); if(source==null)return;
            // Read the first material consistently (rather than mixing combined-property snapshots).
            float manual=source.GetVector("_PathTime")[channel],speed=source.GetVector("_PathSpeed")[channel],offset=source.GetVector("_PathOffset")[channel];
            float phase=Mathf.Repeat((manual == -999 ? previewTime*speed:manual)+offset,1),steps=Mathf.Abs(source.GetVector("_PathSegments")[channel]);
            if(steps>0)phase=(Mathf.Ceil(phase*steps)-.5f)/steps;
            float width=source.GetVector("_PathWidth")[channel],softness=Mathf.Max(0,source.GetVector("_PathSoftness")[channel]),gap=source.GetVector("_PathGapLengths")[channel]; int type=(int)source.GetFloat("_PathType"+letters[channel]);
            // A single mesh replaces 128 independently tessellated Painter2D paths.
            // Interpolated vertex colors also avoid seams between adjacent samples.
            const int samples = 128;
            var mesh = context.Allocate((samples+1)*4,(samples+1)*6);
            float isolation=solo>=0&&solo!=channel?.12f:1;
            var background=strip.resolvedStyle.backgroundColor;
            Color left=Color.Lerp(background,color,.07f+.93f*ShapeAlpha(0,phase,width,softness,gap,type)*isolation);
            for(int j=0;j<samples;j++)
            {
                Color right=Color.Lerp(background,color,.07f+.93f*ShapeAlpha((j+1f)/samples,phase,width,softness,gap,type)*isolation);
                PreviewQuad(mesh,j,j*w/samples,(j+1)*w/samples,3,h-3,left,right);left=right;
            }
            float marker=Mathf.Clamp(phase*w,0,Mathf.Max(0,w-1));
            PreviewQuad(mesh,samples,marker,marker+1,0,h,accent,accent);
        }
        static void PreviewQuad(MeshWriteData mesh,int quad,float left,float right,float top,float bottom,Color a,Color b)
        {
            mesh.SetNextVertex(new Vertex { position=new Vector3(left,top,Vertex.nearZ),tint=a });
            mesh.SetNextVertex(new Vertex { position=new Vector3(right,top,Vertex.nearZ),tint=b });
            mesh.SetNextVertex(new Vertex { position=new Vector3(right,bottom,Vertex.nearZ),tint=b });
            mesh.SetNextVertex(new Vertex { position=new Vector3(left,bottom,Vertex.nearZ),tint=a });
            ushort first=(ushort)(quad*4);
            mesh.SetNextIndex(first);mesh.SetNextIndex((ushort)(first+1));mesh.SetNextIndex((ushort)(first+2));
            mesh.SetNextIndex((ushort)(first+2));mesh.SetNextIndex((ushort)(first+3));mesh.SetNextIndex(first);
        }
    }
    internal sealed partial class RetainedFields
    {
        // Shared vector rows expose the same marking menu and A/RA state as
        // ordinary properties. RGBA cells are components of one shader property.
        internal VisualElement PathingCaption(ShaderProperty property, string text)
        {
            var root = new VisualElement { name = "pathing-caption-" + property.MaterialProperty.name, userData = property };
            root.AddToClassList("thry-property"); root.AddToClassList("pathing-property-caption");
            var row = new VisualElement(); row.AddToClassList("thry-property-row"); root.Add(row);
            var caption = new Label(text); caption.AddToClassList("thry-property-label"); row.Add(caption);
            root.tooltip = "Click for property options and Animated / Renamed marking. This float4 contains all four paths.";
            root.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                e.PreventDefault(); e.StopImmediatePropagation(); ShowPropertyMenu(root, property);
            }, TrickleDown.TrickleDown);
            DecorateAnimation(root, property, false); Context(root, property);
            ChangedPropertyIndicator(row, row.Q<Label>(className: "thry-property-label"), property);
            return root;
        }
        // Reuse the inspector's component gesture, per-owner edits and property menus.
        internal VisualElement PathingComponent(ShaderProperty property, int index)
        {
            var root=new VisualElement { name="pathing-component-"+property.MaterialProperty.name+"-"+index, userData=property };
            root.AddToClassList("thry-property"); root.AddToClassList("pathing-component");
            DecorateMultiMaterialProperty(root,property); Context(root,property);
            // A real FloatField label is Unity's native drag zone. Keep it visible
            // instead of putting a separate pointer handler over the numeric input.
            Vector(root,property,new[]{" "},index);
            var field = root.Q<FloatField>();
            field.labelElement.name = "pathing-grip-" + property.MaterialProperty.name + "-" + index;
            field.labelElement.AddToClassList("pathing-value-grip");
            field.labelElement.tooltip = "Drag left or right to adjust. Shift / Alt change precision. Escape cancels the drag. You can also type a value.";
            Track(root,()=>{property.RefreshRetainedProjection(Model.Renderers);root.SetEnabled(Model.CanEdit(property));});
            return root;
        }
    }
}
#else
#if UNITY_2021_3_OR_NEWER
namespace Thry.ThryEditor { internal static class RetainedPathing { internal static bool CanBuild(ShaderGroup group, RetainedMaterialModel model) => false; } }
#endif
#endif
