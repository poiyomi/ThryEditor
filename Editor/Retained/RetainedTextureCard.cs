#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    /// <summary>A texture inspection surface; assignments still pass through the material field binding.</summary>
    internal sealed class RetainedTextureCard : VisualElement
    {
        readonly RetainedMaterialModel _model;
        readonly ShaderTextureProperty _property;
        readonly ObjectField _assignment;
        readonly bool _convertArray;
        readonly Image _thumbnail;
        readonly Label _placeholder, _name, _description;
        readonly Button _clear;
        Button _gradient;
        readonly VisualElement _channels, _summary, _frame;
        readonly IntegerField _sliceField;
        readonly Button _faceField;
        readonly IVisualElementScheduledItem _sourceRefresh;
        readonly RetainedTexturePreview _preview = new RetainedTexturePreview();
        Texture _texture;
        bool _mixed, _initialized;
        int _channel, _slice, _sourceDirty = -1, _projectRevision = -1, _sourceWidth, _sourceHeight;
        uint _sourceUpdate;
        string _sourceName;
        string _assetGuid;
        TextureDimension _assetDimension;
        bool _previewReleased;
        double _nextLiveRefresh;
        static readonly string[] Faces = { "+X", "−X", "+Y", "−Y", "+Z", "−Z" };

        internal RetainedTextureCard(RetainedMaterialModel model, ShaderTextureProperty property, ObjectField assignment, bool convertArray)
        {
            _model = model; _property = property; _assignment = assignment; _convertArray = convertArray;
            name = "texture-card-" + property.MaterialProperty.name;
            AddToClassList("thry-texture-card");
            var summary = _summary = new VisualElement(); summary.AddToClassList("thry-texture-summary"); Add(summary);
            var frame = new VisualElement { name = "texture-drop-target", focusable = true, tabIndex = 0, tooltip = Text("textureDropHint", "Drop a texture to assign it. Click to locate the assigned asset in the Project window.") };
            frame.AddToClassList("thry-texture-preview"); summary.Add(frame);
            _frame = frame;
            _thumbnail = new Image { name = "texture-thumbnail", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _thumbnail.style.flexGrow = 1; frame.Add(_thumbnail);
            _placeholder = new Label { pickingMode = PickingMode.Ignore }; _placeholder.AddToClassList("thry-texture-placeholder"); frame.Add(_placeholder);
            frame.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) { SelectAsset(); e.StopPropagation(); } });
            frame.RegisterCallback<NavigationSubmitEvent>(e => { SelectAsset(); e.StopPropagation(); });
            InstallDropTarget(frame);
            _clear = ActionButton(() => Assign(null));
            _clear.name = "clear-texture"; _clear.tooltip = Text("clearTexture", "Clear texture");
            _clear.AddToClassList("thry-texture-clear");
            var clearIcon = new Image { image = Resources.Load<Texture2D>("ThryToolbar/texture-clear"), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            clearIcon.AddToClassList("thry-texture-clear-icon"); _clear.Add(clearIcon);
            _clear.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());

            var info = new VisualElement(); info.AddToClassList("thry-texture-info"); summary.Add(info);
            var title = new VisualElement(); title.AddToClassList("thry-texture-title"); info.Add(title);
            title.Add(_clear);
            _name = new Label(); _name.AddToClassList("thry-texture-name"); title.Add(_name);
            _sliceField = new IntegerField { name = "texture-preview-slice", tooltip = "Preview slice (1-based). This does not change the material." };
            _sliceField.style.width = 48; _sliceField.style.minWidth = 32; _sliceField.style.flexShrink = 0;
            _sliceField.style.height = 18; _sliceField.style.marginTop = 0; _sliceField.style.marginBottom = 0;
            _sliceField.style.display = DisplayStyle.None; title.Add(_sliceField);
            _sliceField.RegisterValueChangedCallback(e =>
            {
                _slice = Mathf.Clamp(e.newValue - 1, 0, RetainedTexturePreview.SliceCount(_texture) - 1);
                _sliceField.SetValueWithoutNotify(_slice + 1); UpdatePreview();
            });
            _faceField = ActionButton(() => RetainedMenu.Open(_faceField.worldBound, _faceField,
                Faces.Select((face, index) => new RetainedMenu.Item { Text = face, Checked = index == _slice, Action = () => { _slice = index; UpdatePreview(); } })));
            _faceField.name = "texture-preview-face"; _faceField.tooltip = Text("textureCubeFaceHint", "Cubemap face. This does not change the material.");
            _faceField.style.width = 38; _faceField.style.minWidth = 38; _faceField.style.height = 18;
            _faceField.style.marginTop = 0; _faceField.style.marginBottom = 0;
            _faceField.style.display = DisplayStyle.None; title.Add(_faceField);
            _description = new Label(); _description.AddToClassList("thry-muted"); _description.AddToClassList("thry-texture-description"); info.Add(_description);
            FullTextTooltip(_name); FullTextTooltip(_description);
            _channels = new VisualElement(); _channels.AddToClassList("thry-texture-channels"); info.Add(_channels);
            string[] names = { "RGB", "R", "G", "B", "A" };
            string[] hints = { Text("textureFull", "Full texture"), Text("textureRed", "Red channel"), Text("textureGreen", "Green channel"), Text("textureBlue", "Blue channel"), Text("textureAlpha", "Alpha channel") };
            for (int i = 0; i < names.Length; i++)
            {
                int channel = i;
                var button = ActionButton(() => SetChannel(channel));
                button.name = "texture-channel-" + names[i]; button.text = names[i]; button.tooltip = hints[i];
                _channels.Add(button);
            }
            _sourceRefresh = schedule.Execute(RefreshSource).Every(200);
            RegisterCallback<DetachFromPanelEvent>(e => { _sourceRefresh.Pause(); ReleasePreview(); });
            RegisterCallback<AttachToPanelEvent>(e => { _sourceRefresh.Resume(); if (_initialized) RefreshSource(); });
            summary.RegisterCallback<GeometryChangedEvent>(e => UpdateResponsiveLayout());
        }

        static void FullTextTooltip(Label label)
        {
            // TextElement can replace its tooltip while recalculating ellipsis.
            // Resolve the full current text when hovering, after any resize.
            label.RegisterCallback<TooltipEvent>(e =>
            {
                e.tooltip = label.text; e.rect = label.worldBound;
                e.StopImmediatePropagation();
            });
        }

        string Text(string key, string fallback) => RetainedText.Get(_model.Shader, key, fallback);

        internal void SetGradientAction(Action create)
        {
            _gradient = ActionButton(() => { if (CanAssign) create(); });
            _gradient.name = "create-gradient"; _gradient.text = Text("gradient", "Gradient");
            _gradient.tooltip = Text("create_gradient_hint", "Create a gradient texture. Edit colors and texture settings before applying.");
            _gradient.AddToClassList("thry-texture-gradient-action"); _channels.Add(_gradient);
            Synchronize(); UpdateResponsiveLayout();
        }

        void UpdateResponsiveLayout()
        {
            float available = _summary.contentRect.width;
            if (float.IsNaN(available) || available <= 0) return;
            bool assigned = _texture != null && !_mixed;
            EnableInClassList("thry-texture-card-assigned", assigned);
            EnableInClassList("thry-texture-card-compact", available < 300);
            // A wide landscape thumbnail can use spare horizontal space without
            // making every expanded texture taller. Preserve room for all channels.
            float height = assigned ? (available >= 440 ? 66 : 64) : 40;
            float aspect = assigned && _texture.height > 0 ? Mathf.Clamp((float)_texture.width / _texture.height, 1, 1.8f) : 1;
            // One-dimensional ramps should show their colors across the preview,
            // rather than appearing as a nearly invisible line inside the card.
            _thumbnail.scaleMode = _gradient != null && assigned && (_texture.width > _texture.height * 4 || _texture.height > _texture.width * 4)
                ? ScaleMode.StretchToFill : ScaleMode.ScaleToFit;
            float width = Mathf.Min(height * aspect, Mathf.Max(40, available - (_gradient != null ? 215 : 150)));
            _frame.style.height = height;
            _frame.style.width = width;
        }

        static Button ActionButton(Action action)
        {
            var button = new Button(action);
            button.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefault(); e.StopImmediatePropagation(); action(); }, TrickleDown.TrickleDown);
            return button;
        }

        bool CanAssign => enabledInHierarchy && _assignment.enabledInHierarchy && _model.CanEdit(_property);
        bool CanInspectChannels => !_mixed && RetainedTexturePreview.Supports(_texture);

        internal void Synchronize()
        {
            var texture = _property.MaterialProperty.textureValue;
            bool mixed = _property.MaterialProperty.targets.OfType<Material>()
                .Where(m => m.HasProperty(_property.MaterialProperty.name))
                .Select(m => m.GetTexture(_property.MaterialProperty.name)).Distinct().Skip(1).Any();
            bool changed = !_initialized || texture != _texture || mixed != _mixed;
            _texture = texture; _mixed = mixed; _initialized = true;
            bool assigned = texture != null && !mixed;
            EnableInClassList("thry-texture-card-empty", texture == null && !mixed);
            _clear.style.display = texture != null || mixed ? DisplayStyle.Flex : DisplayStyle.None;
            _channels.style.display = assigned || _gradient != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (_gradient != null) _gradient.SetEnabled(CanAssign);
            foreach (var button in _channels.Children().OfType<Button>().Where(b => b != _gradient))
                button.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
            _clear.SetEnabled(CanAssign && (texture != null || mixed));
            if (changed)
            {
                string guid = assigned ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture)) : null;
                bool sameAsset = assigned && !string.IsNullOrEmpty(guid) && guid == _assetGuid && texture.dimension == _assetDimension;
                ReleasePreview();
                // Import can replace Unity's managed/native texture wrapper while
                // retaining the material's asset reference. Keep the inspection mode.
                if (!sameAsset) { _channel = 0; _slice = 0; }
                _assetGuid = guid; _assetDimension = assigned ? texture.dimension : TextureDimension.None;
                _sourceDirty = -1; _projectRevision = -1;
                _thumbnail.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
                _placeholder.style.display = assigned ? DisplayStyle.None : DisplayStyle.Flex;
                _placeholder.text = mixed ? Text("mixed", "Mixed") : Text("textureDrop", "Drop\ntexture");
                _thumbnail.image = assigned ? PreviewSource() : null;
                _name.text = mixed ? Text("multipleTextures", "Multiple textures") : assigned ? RetainedText.TextureCaption(texture) : Text("noTextureAssigned", "No texture assigned");
                if (assigned) RefreshDescription();
                else _description.text = Text("textureEmptyHint", "Drop a texture here or use the field above.");
                _name.tooltip = _name.text;
                _description.tooltip = _description.text;
                UpdateResponsiveLayout();
            }
            if (changed) UpdatePreview();
        }

        bool IsVisible()
        {
            if (panel == null || !visible || worldBound.width <= 0 || worldBound.height <= 0) return false;
            if (!worldBound.Overlaps(panel.visualTree.worldBound)) return false;
            for (var ancestor = (VisualElement)this; ancestor != null; ancestor = ancestor.parent)
            {
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return false;
                var scroll = ancestor as ScrollView;
                if (scroll != null && !worldBound.Overlaps(scroll.contentViewport.worldBound)) return false;
            }
            return true;
        }

        void RefreshDescription()
        {
            if (_texture == null || _mixed) return;
            string dimensions = _texture.width + " × " + _texture.height;
            if (_texture.dimension == TextureDimension.Tex2DArray || _texture.dimension == TextureDimension.Tex3D)
                dimensions += " × " + RetainedTexturePreview.SliceCount(_texture);
            _name.text = RetainedText.TextureCaption(_texture);
            _description.text = string.Join(" · ", new[] { dimensions, RetainedTexturePreview.Format(_texture),
                Text("textureMemory", "Memory") + " ≈ " + Helpers.TextureHelper.VRAM.ToByteString(RetainedTexturePreview.EstimateMemory(_texture)) }.Where(s => !string.IsNullOrEmpty(s)));
            _sourceName = _texture.name; _sourceWidth = _texture.width; _sourceHeight = _texture.height;
            _sourceDirty = EditorUtility.GetDirtyCount(_texture); _sourceUpdate = _texture.updateCount;
            _projectRevision = RetainedTextureRevision.Version;
            UpdateResponsiveLayout();
        }

        void RefreshSource()
        {
            if (!IsVisible()) { if (!_previewReleased) ReleasePreview(); return; }
            if (_texture == null || _mixed) return;
            bool sourceChanged = _sourceDirty != EditorUtility.GetDirtyCount(_texture) || _sourceUpdate != _texture.updateCount
                || _projectRevision != RetainedTextureRevision.Version || _sourceName != _texture.name
                || _sourceWidth != _texture.width || _sourceHeight != _texture.height;
            bool live = _texture is RenderTexture && (_channel != 0 || _texture.dimension != TextureDimension.Tex2D);
            if (sourceChanged) RefreshDescription();
            if (sourceChanged || _previewReleased || (live && EditorApplication.timeSinceStartup >= _nextLiveRefresh)) UpdatePreview();
            else if (!CanInspectChannels) _thumbnail.image = PreviewSource(); // AssetPreview can arrive asynchronously.
        }

        Texture PreviewSource()
        {
            if (_texture == null || _mixed) return null;
            if (_texture.dimension == TextureDimension.Tex2D) return _texture;
            return AssetPreview.GetAssetPreview(_texture) ?? AssetPreview.GetMiniThumbnail(_texture);
        }

        void SelectAsset()
        {
            if (_texture == null || _mixed) return;
            EditorGUIUtility.PingObject(_texture);
        }

        void SetChannel(int channel)
        {
            if (!CanInspectChannels && channel != 0) return;
            _channel = channel; UpdatePreview();
        }

        void UpdatePreview()
        {
            bool cube = _texture != null && !_mixed && _texture.dimension == TextureDimension.Cube;
            bool slices = _texture != null && !_mixed && (_texture.dimension == TextureDimension.Tex2DArray || _texture.dimension == TextureDimension.Tex3D);
            _faceField.style.display = cube && CanInspectChannels ? DisplayStyle.Flex : DisplayStyle.None;
            _sliceField.style.display = slices && CanInspectChannels ? DisplayStyle.Flex : DisplayStyle.None;
            _slice = Mathf.Clamp(_slice, 0, cube ? 5 : RetainedTexturePreview.SliceCount(_texture) - 1);
            _sliceField.SetValueWithoutNotify(_slice + 1); _faceField.text = Faces[Mathf.Clamp(_slice, 0, 5)];
            if (slices) _sliceField.tooltip = string.Format(Text("textureSliceHint", "Preview slice 1–{0}. This does not change the material."), RetainedTexturePreview.SliceCount(_texture));
            int index = 0;
            foreach (var button in _channels.Children().OfType<Button>().Where(b => b != _gradient))
            {
                button.EnableInClassList("thry-selected", index == _channel);
                button.SetEnabled(_texture != null && !_mixed && (index == 0 || CanInspectChannels)); index++;
            }
            _thumbnail.tooltip = CanInspectChannels ? (_channel == 4 ? Text("textureAlphaHint", "Alpha · black is transparent, white is opaque") : _channel == 0 ? Text("textureFull", "Full texture")
                : new[] { "", Text("textureRed", "Red channel"), Text("textureGreen", "Green channel"), Text("textureBlue", "Blue channel") }[_channel])
                : Text("textureUnsupportedChannels", "Asset preview · channel inspection is unavailable for this texture type.");
            if ((_channel == 0 && _texture != null && _texture.dimension == TextureDimension.Tex2D) || !CanInspectChannels)
            {
                ReleasePreview(); _thumbnail.image = PreviewSource(); _previewReleased = false; return;
            }
            if (!IsVisible()) { ReleasePreview(); return; }
            _thumbnail.image = _preview.Render(_texture, _channel, _slice) ?? PreviewSource();
            _previewReleased = false;
            _nextLiveRefresh = EditorApplication.timeSinceStartup + .2;
        }

        void ReleasePreview()
        {
            if (_thumbnail.image == _preview.Target) _thumbnail.image = null;
            _preview.Dispose(); _previewReleased = true;
        }

        bool IsCompatible(Texture texture)
        {
            return texture != null && texture.dimension == _property.MaterialProperty.textureDimension;
        }

        void Assign(Texture texture)
        {
            if (!CanAssign || (texture != null && !IsCompatible(texture))) return;
            // Mixed selections must commit even when the first material already has this texture.
            using (var change = ChangeEvent<UnityEngine.Object>.GetPooled(_assignment.value, texture))
            {
                _assignment.showMixedValue = false;
                _assignment.SetValueWithoutNotify(texture);
                change.target = _assignment; _assignment.SendEvent(change);
            }
        }

        bool IsFrameConversion()
        {
            var paths = DragAndDrop.paths;
            if (!_convertArray || paths.Length == 0 || DragAndDrop.objectReferences.OfType<Texture2DArray>().Any()) return false;
            return CanConvertFrames(paths);
        }

        internal static bool CanConvertFrames(string[] paths) => paths != null && paths.Length > 0
            && ((paths.Length == 1 && paths[0].EndsWith(".gif", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(paths[0]))
                || paths.All(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null));

        void InstallDropTarget(VisualElement target)
        {
            target.RegisterCallback<DragUpdatedEvent>(e =>
            {
                bool valid = CanAssign && (DragAndDrop.objectReferences.OfType<Texture>().Any(IsCompatible) || IsFrameConversion());
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                target.EnableInClassList("thry-texture-drag", valid); e.StopPropagation();
            });
            target.RegisterCallback<DragLeaveEvent>(e => target.RemoveFromClassList("thry-texture-drag"));
            target.RegisterCallback<DragExitedEvent>(e => target.RemoveFromClassList("thry-texture-drag"));
            target.RegisterCallback<DragPerformEvent>(e =>
            {
                target.RemoveFromClassList("thry-texture-drag");
                if (!CanAssign) return;
                var texture = DragAndDrop.objectReferences.OfType<Texture>().FirstOrDefault(IsCompatible);
                if (texture != null) { DragAndDrop.AcceptDrag(); Assign(texture); }
                else if (IsFrameConversion())
                {
                    // Keep the established array conversion and frame/FPS update callbacks on the field.
                    using (var forwarded = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = _assignment.worldBound.center }))
                    { forwarded.target = _assignment; _assignment.SendEvent(forwarded); }
                }
                e.StopPropagation();
            });
        }
    }
}
#endif
