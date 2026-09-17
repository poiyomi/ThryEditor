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
        readonly VisualElement visual;
        int selected;
        float previewTime = 2;
        double previous;
        bool playing = true, previewHidden;
        IVisualElementScheduledItem previewSchedule;
        readonly List<VisualElement> visibilityAncestors = new List<VisualElement>();
        readonly List<ScrollView> visibilityScrolls = new List<ScrollView>();
        Button play;
        Image playIcon;
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
            this.model = model; this.fields = fields; this.group = group;
            name = "pathing-studio"; AddToClassList("thry-pathing");
            visual = new VisualElement(); Add(visual);
            BuildPreview();
            var source = new VisualElement { name = "pathing-sources" }; visual.Add(source);
            AddField(source, "_PathSource"); AddField(source, "_PathGradientType"); AddField(source, "_PathingUVSelect");
            var directionMap = new VisualElement { name = "pathing-direction-map" }; source.Add(directionMap);
            AddField(directionMap, "_PathingMap");
            fields.Track(directionMap, () => directionMap.style.display = Number("_PathSource") == 0 ? DisplayStyle.Flex : DisplayStyle.None);
            AddField(source, "_PathingMaskMap"); AddField(source, "_PathingColorMap");
            var sampling = new VisualElement { name = "pathing-point-sampling" }; source.Add(sampling);
            AddField(sampling, "_PathPointSampling");
            fields.Track(sampling, () => sampling.style.display = Number("_PathSource") == 0 ? DisplayStyle.Flex : DisplayStyle.None);
            BuildMatrix();
            var actions = Row(); actions.AddToClassList("pathing-actions");
            var presets = new Button { name = "pathing-presets", text = "Channel preset", tooltip = "Apply a motion recipe to the selected path. Keeps its color, masks and AudioLink settings." };
            presets.clicked += () => ShowPresets(presets); actions.Add(presets); var copy = new Button { name = "pathing-copy", text = "Copy channel", tooltip = "Copy the selected path's shape and timing to another path. Keeps destination color and masks." }; copy.clicked += () => ShowCopy(copy); actions.Add(copy);
            fields.Track(actions, () => { bool editable = MotionEditable(selected); presets.SetEnabled(editable); copy.SetEnabled(editable); });
            visual.Add(actions);
            BuildRanges();
            var output = Fold("Output & blending", false); visual.Add(output); AddField(output, "_PathSurfaceBlendMode"); AddField(output, "_PathOverlapMode"); AddField(output, "_PathAntialiasing"); AddField(output, "_PathingOverrideAlpha");
            output.tooltip = "Color alpha controls surface opacity. Emission is separate. Layered overlap blends paths in R, G, B, A order.";
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
        RetainedSubcategory Fold(string text, bool open) => new RetainedSubcategory(text, model.Shader.Shader.name, "pathing-studio:" + text, open);
        void AddField(VisualElement parent, string name)
        {
            var p = Property(name); if (p == null) return;
            var field = fields.Field(p); parent.Add(field);
            if (name == "_PathingMap" || name == "_PathingColorMap" || name == "_PathingOverrideAlpha")
                fields.Track(field, () => {
                    var label = field.Q<Label>(className:"thry-texture-caption") ?? field.Q<Label>(className:"thry-property-label");
                    if (label != null) label.text = name == "_PathingMap" ? "Direction map"
                        : name == "_PathingColorMap" ? "Color & shared mask" : "Write material alpha";
                });
        }

        void BuildPreview()
        {
            var preview = new VisualElement { name = "pathing-preview", tooltip = "Shape and timing for the first selected material. Masks and AudioLink are excluded." }; preview.AddToClassList("pathing-preview"); visual.Add(preview);
            var title = Row(); title.AddToClassList("pathing-preview-toolbar"); var label = new Label("MOTION PREVIEW"); label.AddToClassList("pathing-eyebrow"); title.Add(label);
            play = new Button(() => { playing = !playing; UpdatePlaybackButton(); UpdatePreviewActivity(); }) { name = "pathing-preview-play" };
            play.style.width = 28;
            playIcon = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            playIcon.style.width = 16; playIcon.style.height = 16;
            play.style.alignItems = Align.Center; play.style.justifyContent = Justify.Center;
            play.Add(playIcon); UpdatePlaybackButton(); title.Add(play);
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
            scrub.RegisterValueChangedCallback(e => { playing = false; UpdatePlaybackButton(); UpdatePreviewActivity(); previewTime = e.newValue * 10; foreach (var s in strips) s.MarkDirtyRepaint(); }); previewContent.Add(scrub);
        }
        void UpdatePlaybackButton()
        {
            playIcon.image = EditorGUIUtility.IconContent(playing ? "PauseButton" : "PlayButton").image;
            play.tooltip = playing ? "Pause preview" : "Play preview";
        }
        bool MergedPreview => Number("_PathSource") == 0 && Number("_PathGradientType") == 1;
        void Select(int channel) { selected = channel; Synchronize(); }
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
            var directions = GridRow(grid, "UV direction", "UV axis used by each path.");
            directions.name = "pathing-uv-directions";
            for (int i = 0; i < 4; i++)
            {
                var property = Property("_PathSourceDir" + letters[i]);
                if (property != null) Cell(directions, i).Add(fields.Field(property, true));
            }
            fields.Track(directions, () => directions.style.display = Number("_PathSource") == 1 || Mixed("_PathSource") ? DisplayStyle.Flex : DisplayStyle.None);
            if (Property("_PathDirectionChannelR") != null)
            {
                var routing = GridRow(grid, "Direction", "Channel from the direction texture. Default follows Split or Merged Channels; select R to share a single-channel gradient.");
                routing.name = "pathing-direction-channels";
                for (int i = 0; i < 4; i++) Cell(routing, i).Add(fields.Field(Property("_PathDirectionChannel" + letters[i]), true));
                fields.Track(routing, () => routing.style.display = Number("_PathSource") == 0 || Mixed("_PathSource") ? DisplayStyle.Flex : DisplayStyle.None);
            }
            var shapes = GridRow(grid, "Shape", "Fill grows along the gradient; Pulse travels once; Loop wraps around; Dashes repeat.");
            var colors = GridRow(grid, "Color", "HDR path color. Its alpha controls surface opacity, not emission.");
            var themes = GridRow(grid, "Theme", "Choose a theme color or use the path color above.");
            for (int i = 0; i < 4; i++)
            {
                Cell(shapes, i).Add(fields.Field(Property("_PathType" + letters[i]), true));
                Cell(colors, i).Add(fields.Field(Property("_PathColor" + letters[i]), true));
                var p = Property("_PathColor" + letters[i] + "ThemeIndex"); if (p != null) Cell(themes, i).Add(fields.Field(p, true));
            }
            BuildMaskChannels(grid);
            foreach (var entry in new[] { new[] { "Emission", "_PathEmissionStrength", "Adds light independently of surface opacity." }, new[] { "Length", "_PathWidth", "Length along the direction gradient, not world-space distance." }, new[] { "Softness", "_PathSoftness", "Feather the moving pulse's edges." }, new[] { "Gap", "_PathGapLengths", "Spacing between repeated dashes. Only used by Dashed paths." } })
            {
                var row = GridRow(grid, entry[0], entry[2], entry[1]);
                for (int i = 0; i < 4; i++) { var cell = Cell(row, i); cell.Add(fields.PathingComponent(Property(entry[1]), i)); if (entry[1] == "_PathGapLengths") { int ch = i; fields.Track(cell, () => cell.SetEnabled(Number("_PathType" + letters[ch]) == 3 && model.CanEdit(Property(entry[1])))); } }
            }
            foreach (var entry in new[] { new[] { "Speed", "_PathSpeed", "Gradient cycles per second. Negative values reverse the direction; zero pauses." }, new[] { "Phase", "_PathOffset", "Offset the position of a path relative to the others." }, new[] { "Steps", "_PathSegments", "0 = continuous. Nonzero values quantize movement into that many steps." } })
            {
                var row = GridRow(grid, entry[0], entry[2], entry[1]); for (int i = 0; i < 4; i++) Cell(row, i).Add(fields.PathingComponent(Property(entry[1]), i));
            }
            BuildShapeMotion(grid);
        }
        void BuildMaskChannels(VisualElement grid)
        {
            if (Property("_PathingMaskMap") == null) return;
            var row = GridRow(grid, "Mask", "Channel from Packed Masks used by each path. None leaves the path unmasked. For a single-channel texture, use R.");
            row.name = "pathing-mask-channels";
            for (int i = 0; i < 4; i++)
            {
                var property = Property("_PathingMaskChannel" + letters[i]);
                if (property == null) continue;
                var field = fields.Field(property, true);
                field.tooltip = letters[i] + " path mask channel from Packed Masks. None disables this mask. Color texture alpha is a separate shared mask.";
                Cell(row, i).Add(field);
            }
        }
        void BuildRanges()
        {
            if (Property("_EnablePathRemapping") == null) return;
            var fold = Fold("Gradient range", false); visual.Add(fold); AddField(fold, "_EnablePathRemapping");
            fold.tooltip = "Remap the selected gradient range to 0–1.";
            for (int i = 0; i < 4; i++)
            {
                var p = Property("_PathRemap" + letters[i]); if (p == null) continue;
                fold.Add(fields.Field(p));
            }
        }
        void BuildShapeMotion(VisualElement grid)
        {
            if (Property("_PathMotionR") == null) return;
            foreach (var entry in new[] { new[] { "Motion", "_PathMotion", "Repeat or travel back and forth. Speed is cycles per second." }, new[] { "Easing", "_PathEasing", "Change acceleration within each trip." }, new[] { "Edges", "_PathEdges", "Linked uses Softness. Separate gives Path, Loop and Dashed independent leading and trailing edges." } })
            {
                var row = GridRow(grid, entry[0], entry[2]);
                for (int i = 0; i < 4; i++) Cell(row, i).Add(fields.Field(Property(entry[1] + letters[i]), true));
            }
            foreach (var entry in new[] { new[] { "Head", "_PathHeadSoftness" }, new[] { "Tail", "_PathTailSoftness" } })
            {
                var row = GridRow(grid, entry[0], "Edge softness in the direction of travel. Lower values sharpen the edge; higher values soften it.", entry[1]);
                for (int i = 0; i < 4; i++)
                {
                    int ch = i; var cell = Cell(row, i); cell.Add(fields.PathingComponent(Property(entry[1]), i));
                    fields.Track(cell, () => cell.SetEnabled((Number("_PathEdges" + letters[ch]) == 1 || Mixed("_PathEdges" + letters[ch])) && Number("_PathType" + letters[ch]) != 0 && model.CanEdit(Property(entry[1]))));
                }
            }
        }
        static readonly string[] MotionNames = { "_PathSpeed", "_PathWidth", "_PathSoftness", "_PathGapLengths", "_PathTime", "_PathOffset", "_PathSegments", "_PathHeadSoftness", "_PathTailSoftness" };
        static readonly string[] MotionChoices = { "_PathMotion", "_PathEasing", "_PathEdges" };
        bool MotionEditable(int channel) => MotionNames.Where(n => Property(n) != null).All(n => model.CanEdit(Property(n))) && MotionChoices.Where(n => Property(n + letters[channel]) != null).All(n => model.CanEdit(Property(n + letters[channel]))) && model.CanEdit(Property("_PathType" + letters[channel]));
        void ShowCopy(VisualElement anchor)
        {
            var menu = new List<RetainedMenu.Item>(); int source = selected;
            for (int i = 0; i < 4; i++)
            {
                int target = i; if (target == source) continue;
                bool editable = MotionEditable(target);
                if (!editable) { menu.Add(new RetainedMenu.Item { Text = "To " + letters[i] + " path" }); continue; }
                menu.Add(new RetainedMenu.Item { Text = "To " + letters[i] + " path", Action = () => {
                    Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
                    foreach (var n in MotionNames.Where(n => Property(n) != null)) { var p = Property(n); model.Edit(p, mp => { var v = mp.vectorValue; v[target] = v[source]; mp.vectorValue = v; }, true); }
                    model.Edit(Property("_PathType" + letters[target]), mp => { var owner = mp.targets.OfType<Material>().First(); mp.SetNumber(owner.GetFloat("_PathType" + letters[source])); }, true);
                    foreach (var prefix in MotionChoices)
                    {
                        var p = Property(prefix + letters[target]); if (p == null) continue;
                        model.Edit(p, mp => { var owner = mp.targets.OfType<Material>().First(); mp.SetNumber(owner.GetFloat(prefix + letters[source])); }, true);
                    }
                    Undo.CollapseUndoOperations(undo);
                } });
            }
            RetainedMenu.Open(anchor.worldBound, anchor, menu);
        }
        void ShowPresets(VisualElement anchor)
        {
            RetainedMenu.Open(anchor.worldBound, anchor, new[] {
                new RetainedMenu.Item { Text = "Traveling glow", Action = () => ApplyRecipe(2,.15f,.2f,.65f,0,-999) },
                new RetainedMenu.Item { Text = "Flowing dashes", Action = () => ApplyRecipe(3,.2f,.08f,.3f,.06f,-999) },
                new RetainedMenu.Item { Text = "Quick pulse", Action = () => ApplyRecipe(2,.65f,.07f,.15f,0,-999) },
                new RetainedMenu.Item { Text = "Slow sweep", Action = () => ApplyRecipe(2,.06f,.45f,.8f,0,-999) },
                new RetainedMenu.Item { Text = "Reverse flow", Action = () => ApplyRecipe(2,-.2f,.18f,.4f,0,-999) },
                new RetainedMenu.Item { Text = "Dotted chase", Action = () => ApplyRecipe(3,.25f,.025f,.15f,.1f,-999) },
                new RetainedMenu.Item { Text = "Long dashes", Action = () => ApplyRecipe(3,.12f,.2f,.1f,.08f,-999) },
                new RetainedMenu.Item { Text = "Stepped chase", Action = () => ApplyRecipeValues(2,.18f,.12f,.1f,0,-999,8) },
                new RetainedMenu.Item { Text = "Soft fill", Action = () => ApplyRecipe(0,.1f,.1f,.2f,0,-999) },
                new RetainedMenu.Item { Text = "Manual progress", Action = () => ApplyRecipe(0,0,.1f,0,0,.5f) }
            });
        }
        void ApplyRecipe(int type, float speed, float width, float softness, float gap, float time)
            => ApplyRecipeValues(type, speed, width, softness, gap, time, 0);
        void ApplyRecipeValues(int type, float speed, float width, float softness, float gap, float time, float steps)
        {
            var typeProperty = Property("_PathType" + letters[selected]); if (!MotionEditable(selected)) return;
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
            model.EditSingleProperty(typeProperty, p => p.SetNumber(type));
            var values = new[] { speed, width, softness, gap, time, 0f, steps, .1f, 1f };
            for (int i = 0; i < MotionNames.Length; i++) if (Property(MotionNames[i]) != null) model.VectorComponent(Property(MotionNames[i]), selected, values[i]);
            foreach (var prefix in MotionChoices) if (Property(prefix + letters[selected]) != null) model.EditSingleProperty(Property(prefix + letters[selected]), p => p.SetNumber(0));
            Undo.CollapseUndoOperations(undo);
        }
        void Synchronize()
        {
            UpdatePreviewActivity();
            if (selectButtons[0] == null) return;
            for (int i = 0; i < 4; i++)
            {
                bool off = Number("_PathType" + letters[i]) == 4 && !Mixed("_PathType" + letters[i]);
                foreach (var cell in columns[i]) { cell.EnableInClassList("pathing-selected", selected == i); cell.EnableInClassList("pathing-off", off); }
                selectButtons[i].tooltip = (off ? "Disabled. Choose a shape to enable." : "Select for channel presets and copying.") + " Path " + letters[i];
                strips[i].parent.style.display = MergedPreview && i > 0 ? DisplayStyle.None : DisplayStyle.Flex;
                var badge = strips[i].parent.Q<Button>();
                badge.text = MergedPreview ? "Mix" : letters[i];
                badge.tooltip = MergedPreview ? "Merged paths" : "Select " + letters[i] + " path";
                strips[i].MarkDirtyRepaint();
            }
        }
        internal static float ShapeAlpha(float x, float time, float width, float softness, float gap, int type)
            => ShapeAlphaWithEdges(x, time, width, softness, gap, type, softness, softness, 1, 0);
        internal static float MotionPhase(float phase, float steps, int motion, int easing)
        {
            phase = Mathf.Repeat(phase, 1);
            if (motion == 1) phase = 1 - Mathf.Abs(phase * 2 - 1);
            if (easing == 1) phase *= phase;
            else if (easing == 2) phase = 1 - (1 - phase) * (1 - phase);
            else if (easing == 3) phase = phase * phase * (3 - 2 * phase);
            steps = Mathf.Abs(steps);
            if (steps > 0) phase = (Mathf.Max(1, Mathf.Ceil(phase * steps)) - .5f) / steps;
            return phase;
        }
        internal static float ShapeAlphaWithEdges(float x, float time, float width, float softness, float gap, int type, float head, float tail, float direction, float pixelWidth)
        {
            if (type == 4 || x <= 0) return 0;
            width = Mathf.Max(0, width); gap = Mathf.Max(0, gap);
            softness = Mathf.Max(0, softness); head = Mathf.Max(0, head); tail = Mathf.Max(0, tail);
            float half = width * .5f, inv = 1 / (half + .000001f), value = 0;
            if (type == 0)
            {
                float end = time * (1 + softness);
                value = pixelWidth > .000001f ? 1 - Smooth(end - Mathf.Max(softness, pixelWidth * .5f), end + pixelWidth * .5f, x)
                    : softness > .00001f ? Smooth(end, end-softness, x) : (x <= time ? 1 : 0);
            }
            else if ((type == 1 || type == 2) && width > 0)
            {
                float d = x - (type == 1 ? Mathf.LerpUnclamped(-half, 1 + half, time) : time);
                if (type == 2) d = Mathf.Repeat(d + .5f, 1) - .5f;
                float aa = pixelWidth * inv * .5f;
                value = 1 - Mathf.Abs(d) * inv;
                if (aa <= 0) value = Mathf.Clamp01(value);
                value = Smooth(-aa, Mathf.Max((d * direction >= 0 ? head : tail) + .000001f, aa), value);
            }
            else if (type == 3 && width > 0)
            {
                float total = Mathf.Max(width + gap, .000001f), relative = width / total, pattern = Mathf.Repeat(x / total - time, 1);
                float rise = Mathf.Min((direction > 0 ? tail : head) * .5f * relative, relative * .499f);
                float fall = Mathf.Min((direction > 0 ? head : tail) * .5f * relative, relative * .499f);
                value = (rise > .000001f ? Smooth(0, rise, pattern) : 1) * (fall > .000001f ? 1 - Smooth(relative - fall, relative, pattern) : (pattern < relative ? 1 : 0));
                float aa = pixelWidth / total;
                if (aa > .000001f)
                {
                    float lo = pattern - aa * .5f, hi = pattern + aa * .5f;
                    float coverage = ((Mathf.Floor(hi) - Mathf.Floor(lo)) * relative + Mathf.Min(Mathf.Repeat(hi, 1), relative) - Mathf.Min(Mathf.Repeat(lo, 1), relative)) / aa;
                    value = Mathf.Lerp(value, coverage, Mathf.Clamp01(aa / Mathf.Max(Mathf.Max(rise, fall), .000001f)));
                }
            }
            return value;
        }
        static float Smooth(float a, float b, float x) { if (Mathf.Abs(a-b)<.000001f) return x>=b?1:0; float t=Mathf.Clamp01((x-a)/(b-a)); return t*t*(3-2*t); }
        struct PreviewPath
        {
            internal Color color;
            internal float phase, width, softness, gap, head, tail, direction, pixelWidth;
            internal int type;
        }
        int previewOverlap;
        readonly PreviewPath[] previewPaths = new PreviewPath[4];
        Color PreviewSample(float x, Color background, int count, bool merged)
        {
            var result = background; float brightest = -1;
            for (int i = 0; i < count; i++)
            {
                var p = previewPaths[i];
                float alpha = ShapeAlphaWithEdges(x, p.phase, p.width, p.softness, p.gap, p.type, p.head, p.tail, p.direction, p.pixelWidth);
                if (merged && previewOverlap == 1) result += p.color * alpha;
                else if (merged && previewOverlap == 2)
                {
                    float brightness = (p.color.r * .2126f + p.color.g * .7152f + p.color.b * .0722f) * alpha;
                    if (brightness > brightest) { brightest = brightness; result = Color.Lerp(background, p.color, alpha); }
                }
                else result = Color.Lerp(result, p.color, merged ? alpha : .07f + .93f * alpha);
            }
            return result;
        }
        void PaintLane(MeshGenerationContext context, VisualElement strip, int channel)
        {
            float w = strip.contentRect.width, h = strip.contentRect.height; if (w < 1 || h < 1) return;
            bool merged = MergedPreview;
            if (merged && channel > 0) return;
            var source = model.Owners(Property("_PathTime")).FirstOrDefault(); if (source == null) return;
            int count = merged ? 4 : 1;
            previewOverlap = (int)Number("_PathOverlapMode");
            for (int i = 0; i < count; i++)
            {
                int ch = merged ? i : channel;
                var colorProperty = Property("_PathColor" + letters[ch]);
                var color = source.GetColor(colorProperty.MaterialProperty.name);
                // Match Unity's HDR color swatch in the editor's display color space.
                if (colorProperty.MaterialProperty.GetPropertyFlags().HasFlag(UnityEngine.Rendering.ShaderPropertyFlags.HDR)) color = color.gamma;
                color.a = 1;
                float manual = source.GetVector("_PathTime")[ch];
                float phase = Mathf.Repeat((manual == -999 ? previewTime * source.GetVector("_PathSpeed")[ch] : manual) + source.GetVector("_PathOffset")[ch], 1);
                float steps = Mathf.Abs(source.GetVector("_PathSegments")[ch]);
                int motion = (int)Number("_PathMotion" + letters[ch]), easing = (int)Number("_PathEasing" + letters[ch]);
                float direction = source.GetVector("_PathSpeed")[ch] < 0 ? -1 : 1;
                if (motion == 1 && phase >= .5f) direction *= -1;
                phase = MotionPhase(phase, steps, motion, easing);
                float softness = Mathf.Max(0, source.GetVector("_PathSoftness")[ch]);
                bool separate = Number("_PathEdges" + letters[ch]) == 1;
                previewPaths[i] = new PreviewPath { color = color, phase = phase, width = source.GetVector("_PathWidth")[ch],
                    softness = softness, head = separate ? source.GetVector("_PathHeadSoftness")[ch] : softness, tail = separate ? source.GetVector("_PathTailSoftness")[ch] : softness, direction = direction, pixelWidth = Number("_PathAntialiasing") >= .5f ? 1 / w : 0, gap = source.GetVector("_PathGapLengths")[ch], type = (int)source.GetFloat("_PathType" + letters[ch]) };
            }
            const int samples = 128;
            var mesh = context.Allocate((samples + (merged ? 0 : 1)) * 4, (samples + (merged ? 0 : 1)) * 6);
            var background = strip.resolvedStyle.backgroundColor;
            Color left = PreviewSample(0, background, count, merged);
            for (int j = 0; j < samples; j++)
            {
                Color right = PreviewSample((j + 1f) / samples, background, count, merged);
                PreviewQuad(mesh, j, j*w/samples, (j+1)*w/samples, 3, h-3, left, right); left = right;
            }
            if (!merged)
            {
                float marker = Mathf.Clamp(previewPaths[0].phase*w, 0, Mathf.Max(0, w-1));
                PreviewQuad(mesh, samples, marker, marker+1, 0, h, previewPaths[0].color, previewPaths[0].color);
            }
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
#endif
