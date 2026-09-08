#if UNITY_2021_3_OR_NEWER
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.Drawers
{
    public partial class LocalMessageDrawer
    {
        internal VisualElement CreateRetained(string definition, Material[] materials)
        {
            Init(definition);
            var root = new VisualElement();
            var text = new Label(); text.style.whiteSpace = WhiteSpace.Normal; root.Add(text);
            var image = new Image { scaleMode = ScaleMode.ScaleToFit }; root.Add(image);
            ButtonData displayed = null;
            root.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) _buttonData?.action?.Perform(materials); });
            root.schedule.Execute(() =>
            {
                if (_buttonData == null || _buttonData == displayed) return;
                displayed = _buttonData;
                text.text = _buttonData.text; root.tooltip = _buttonData.hover;
                text.style.unityTextAlign = _buttonData.center_position ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                image.style.display = _buttonData.texture == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (_buttonData.texture != null) { image.image = _buttonData.texture.loaded_texture; image.style.height = _buttonData.texture.height; }
            }).Every(150);
            return root;
        }
    }
}
#endif
