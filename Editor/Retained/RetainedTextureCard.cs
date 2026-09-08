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
        readonly VisualElement _channels;
        Texture _texture;
        bool _mixed, _initialized;
        int _channel;
        Material _channelMaterial;
        RenderTexture _channelTexture;

        internal RetainedTextureCard(RetainedMaterialModel model, ShaderTextureProperty property, ObjectField assignment, bool convertArray)
        {
            _model = model; _property = property; _assignment = assignment; _convertArray = convertArray;
            name = "texture-card-" + property.MaterialProperty.name;
            AddToClassList("thry-texture-card");
            var summary = new VisualElement(); summary.AddToClassList("thry-texture-summary"); Add(summary);
            var frame = new VisualElement { name = "texture-drop-target", focusable = true, tabIndex = 0, tooltip = "Drop a texture to assign it. Click to locate the assigned asset in the Project window." };
            frame.AddToClassList("thry-texture-preview"); summary.Add(frame);
            _thumbnail = new Image { name = "texture-thumbnail", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _thumbnail.style.flexGrow = 1; frame.Add(_thumbnail);
            _placeholder = new Label { pickingMode = PickingMode.Ignore }; _placeholder.AddToClassList("thry-texture-placeholder"); frame.Add(_placeholder);
            frame.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) { SelectAsset(); e.StopPropagation(); } });
            frame.RegisterCallback<NavigationSubmitEvent>(e => { SelectAsset(); e.StopPropagation(); });
            InstallDropTarget(frame);
            _clear = ActionButton(() => Assign(null));
            _clear.name = "clear-texture"; _clear.tooltip = "Clear texture";
            _clear.AddToClassList("thry-texture-clear");
            var clearIcon = new Image { image = Resources.Load<Texture2D>("ThryToolbar/texture-clear"), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            clearIcon.AddToClassList("thry-texture-clear-icon"); _clear.Add(clearIcon);
            _clear.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());

            var info = new VisualElement(); info.AddToClassList("thry-texture-info"); summary.Add(info);
            var title = new VisualElement(); title.AddToClassList("thry-texture-title"); info.Add(title);
            title.Add(_clear);
            _name = new Label(); _name.AddToClassList("thry-texture-name"); title.Add(_name);
            _description = new Label(); _description.AddToClassList("thry-muted"); info.Add(_description);
            _channels = new VisualElement(); _channels.AddToClassList("thry-texture-channels"); info.Add(_channels);
            string[] names = { "RGB", "R", "G", "B", "A" };
            string[] hints = { "Full texture", "Red channel", "Green channel", "Blue channel", "Alpha channel" };
            for (int i = 0; i < names.Length; i++)
            {
                int channel = i;
                var button = ActionButton(() => SetChannel(channel));
                button.name = "texture-channel-" + names[i]; button.text = names[i]; button.tooltip = hints[i];
                _channels.Add(button);
            }
            RegisterCallback<DetachFromPanelEvent>(e => ReleasePreview());
            RegisterCallback<AttachToPanelEvent>(e => { if (_initialized) UpdatePreview(); });
        }

        static Button ActionButton(Action action)
        {
            var button = new Button(action);
            button.RegisterCallback<NavigationSubmitEvent>(e => { e.PreventDefault(); e.StopImmediatePropagation(); action(); }, TrickleDown.TrickleDown);
            return button;
        }

        bool CanAssign => enabledInHierarchy && _assignment.enabledInHierarchy && _model.CanEdit(_property);
        bool CanInspectChannels => _texture != null && !_mixed && _texture.dimension == TextureDimension.Tex2D && (_texture is Texture2D || _texture is RenderTexture);

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
            _channels.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
            _clear.SetEnabled(CanAssign && (texture != null || mixed));
            if (changed)
            {
                ReleasePreview(); _channel = 0;
                _thumbnail.style.display = assigned ? DisplayStyle.Flex : DisplayStyle.None;
                _placeholder.style.display = assigned ? DisplayStyle.None : DisplayStyle.Flex;
                _placeholder.text = mixed ? "Mixed" : "Drop\ntexture";
                _thumbnail.image = assigned ? PreviewSource() : null;
                _name.text = mixed ? "Multiple textures" : assigned ? texture.name : "No texture assigned";
                if (assigned)
                {
                    var memory = Helpers.TextureHelper.VRAM.CalcSize(texture);
                    _description.text = string.Join(" · ", new[] { texture.width + " × " + texture.height, memory.format, Helpers.TextureHelper.VRAM.ToByteString(memory.size) }.Where(s => !string.IsNullOrEmpty(s)));
                }
                else _description.text = "Drop a texture here or use the field above.";
            }
            if (changed || (_texture is RenderTexture && _channel > 0)) UpdatePreview();
        }

        Texture PreviewSource()
        {
            if (_texture == null || _mixed) return null;
            if (CanInspectChannels) return _texture;
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
            int index = 0;
            foreach (var button in _channels.Children().OfType<Button>())
            {
                button.EnableInClassList("thry-selected", index == _channel);
                button.SetEnabled(_texture != null && !_mixed && (index == 0 || CanInspectChannels)); index++;
            }
            _thumbnail.tooltip = CanInspectChannels ? (_channel == 4 ? "Alpha · black is transparent, white is opaque" : _channel == 0 ? "Original texture" : new[] { "", "Red channel", "Green channel", "Blue channel" }[_channel])
                : "Asset preview · channel inspection is available for 2D textures.";
            if (_channel == 0 || !CanInspectChannels)
            {
                ReleasePreview(); _thumbnail.image = PreviewSource(); return;
            }
            if (_channelMaterial == null)
            {
                var shader = Shader.Find("Hidden/Thry/ChannelPreview");
                if (shader == null) { _thumbnail.tooltip = "Channel preview shader is unavailable."; _thumbnail.image = PreviewSource(); return; }
                _channelMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            float scale = Mathf.Min(1f, 128f / Mathf.Max(_texture.width, _texture.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(_texture.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(_texture.height * scale));
            if (_channelTexture == null || _channelTexture.width != width || _channelTexture.height != height)
            {
                ReleaseTarget();
                _channelTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave, name = "Thry texture channel preview" };
                _channelTexture.Create();
            }
            var active = RenderTexture.active;
            try { _channelMaterial.SetFloat("_Channel", _channel - 1); Graphics.Blit(_texture, _channelTexture, _channelMaterial); }
            finally { RenderTexture.active = active; }
            _thumbnail.image = _channelTexture;
        }

        void ReleaseTarget()
        {
            if (_channelTexture == null) return;
            if (_thumbnail.image == _channelTexture) _thumbnail.image = null;
            _channelTexture.Release(); UnityEngine.Object.DestroyImmediate(_channelTexture); _channelTexture = null;
        }

        void ReleasePreview()
        {
            ReleaseTarget();
            if (_channelMaterial != null) { UnityEngine.Object.DestroyImmediate(_channelMaterial); _channelMaterial = null; }
        }

        bool IsCompatible(Texture texture)
        {
            return texture != null && texture.dimension == _property.MaterialProperty.textureDimension && _assignment.objectType.IsInstanceOfType(texture);
        }

        void Assign(Texture texture)
        {
            if (!CanAssign) return;
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
            return (paths.Length == 1 && paths[0].EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                || paths.All(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null);
        }

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
