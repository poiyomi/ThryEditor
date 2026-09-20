using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public class ShaderSubSection : ShaderGroup
    {
        public ShaderSubSection(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, int xOffset, string optionsRaw, int propertyIndex)
            : base(shaderEditor, prop, materialEditor, displayName, xOffset, optionsRaw, propertyIndex) { }

        protected override void DrawInternal(GUIContent content, Rect? rect = null, bool useEditorIndent = false, bool isInHeader = false) =>
            DrawSection(true);
    }
}
