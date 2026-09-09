#if UNITY_2021_3_OR_NEWER
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.Drawers
{
    public partial class LocalMessageDrawer
    {
        internal VisualElement CreateRetained(string definition, Material[] materials, Action<DefineableAction> perform = null, Func<bool> valid = null)
        {
            Init(definition);
            var root = new VisualElement();
            var text = new Label { enableRichText = true }; text.style.whiteSpace = WhiteSpace.Normal; root.Add(text);
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore }; root.Add(image);
            Action activate = () =>
            {
                if (!root.enabledInHierarchy || (valid != null && !valid()) || _buttonData?.action == null) return;
                if (perform != null) perform(_buttonData.action); else _buttonData.action.Perform(materials);
            };
            root.RegisterCallback<ClickEvent>(e => { if (e.button == 0) activate(); });
            root.RegisterCallback<NavigationSubmitEvent>(e => { activate(); e.StopPropagation(); });
            Action refresh = () =>
            {
                if (valid != null && !valid()) { root.SetEnabled(false); return; }
                text.text = _buttonData?.text ?? ""; root.tooltip = _buttonData?.hover ?? "";
                root.focusable = _buttonData?.action != null && _buttonData.action.type != DefineableActionType.NONE;
                bool centered = _buttonData?.center_position == true;
                text.style.unityTextAlign = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                image.style.display = _buttonData?.texture == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (_buttonData?.texture != null)
                {
                    // Downloads replace the cached texture without replacing ButtonData.
                    // Revisit the image while waiting instead of freezing its placeholder.
                    image.image = _buttonData.texture.loaded_texture;
                    float width = root.contentRect.width;
                    image.style.height = width > 0 ? Mathf.Min(_buttonData.texture.height, width) : _buttonData.texture.height;
                    image.style.alignSelf = centered ? Align.Center : Align.Stretch;
                    if (centered) image.style.width = image.image != null && width > 0 ? Mathf.Min(image.image.width, width) : _buttonData.texture.width;
                    else image.style.width = StyleKeyword.Auto;
                }
            };
            refresh(); root.schedule.Execute(refresh).Every(150);
            return root;
        }
    }
}
#endif
