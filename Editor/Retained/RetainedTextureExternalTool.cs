#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    // External tools keep their authored immediate-mode interface. The adapter
    // owns only the embedded instance and the lifetime of its assignment callback.
    internal sealed class RetainedExternalTextureTool : VisualElement
    {
        readonly RetainedMaterialModel _model;
        readonly ShaderTextureProperty _property;
        readonly Type _type;
        readonly MethodInfo _onGui;
        readonly EventInfo _generated;
        readonly Button _button;
        readonly IMGUIContainer _content;
        readonly EventHandler _handler;
        ScriptableObject _tool;
        bool _expanded;

        internal RetainedExternalTextureTool(RetainedMaterialModel model, ShaderTextureProperty property, DrawerAttribute attribute)
        {
            _model = model; _property = property;
            name = "external-texture-tool-" + property.MaterialProperty.name;
            string title = attribute.Args.Length > 0 ? attribute.Args[0] : "Texture tool";
            string typeName = attribute.Args.Length > 1 ? attribute.Args[1] : "";
            _type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(typeName, false)).FirstOrDefault(type => type != null);
            if (_type != null && typeof(ScriptableObject).IsAssignableFrom(_type) && !_type.IsAbstract)
            {
                _onGui = _type.GetMethod("OnGUI", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                _generated = _type.GetEvent("TextureGenerated", BindingFlags.Public | BindingFlags.Instance);
            }
            _button = new Button(Toggle) { text = title, name = "open-external-texture-tool" }; Add(_button);
            _content = new IMGUIContainer(() =>
            {
                if (_tool == null || !_expanded || panel == null) return;
                _model.Shader.ActivateRetained(); _model.Shader.CurrentProperty = _property;
                using (new EditorGUI.DisabledScope(!CanApply()))
                {
                    try { _onGui.Invoke(_tool, null); }
                    catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
                }
            });
            _content.style.display = DisplayStyle.None; Add(_content);
            _handler = OnTextureGenerated;
            RegisterCallback<DetachFromPanelEvent>(evt => { if (evt.target == this) Release(); });
            Synchronize();
        }

        bool Available => _onGui != null && (_generated == null || _generated.EventHandlerType == typeof(EventHandler));
        bool CanApply() => panel != null && RetainedMaterialModel.HasValidTargets(_model.Editor) && _model.CanEdit(_property);

        internal void Synchronize()
        {
            _button.SetEnabled(Available && _model.CanEdit(_property));
            _button.tooltip = Available ? "Open the texture tool" : "This texture tool is not installed or does not provide a compatible interface.";
            _content.SetEnabled(_model.CanEdit(_property));
        }

        internal void Toggle()
        {
            if (!Available || !CanApply()) return;
            if (_tool == null)
            {
                _tool = ScriptableObject.CreateInstance(_type);
                _tool.hideFlags = HideFlags.HideAndDontSave;
                _generated?.AddEventHandler(_tool, _handler);
            }
            _expanded = !_expanded;
            _content.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OnTextureGenerated(object sender, EventArgs args)
        {
            if (!CanApply() || _tool == null || args == null) return;
            var field = args.GetType().GetField("generated_texture", BindingFlags.Public | BindingFlags.Instance);
            var texture = field?.GetValue(args) as Texture;
            if (texture == null) return;
            var dimension = _property.MaterialProperty.textureDimension;
            if (dimension != UnityEngine.Rendering.TextureDimension.Any && texture.dimension != dimension) return;
            _model.Edit(_property, property => property.textureValue = texture);
        }

        void Release()
        {
            _expanded = false; _content.style.display = DisplayStyle.None;
            if (_tool != null)
            {
                _generated?.RemoveEventHandler(_tool, _handler);
                UnityEngine.Object.DestroyImmediate(_tool);
            }
            _tool = null;
        }
    }
}
#endif
