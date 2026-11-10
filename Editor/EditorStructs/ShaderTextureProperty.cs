using UnityEditor;

namespace Thry.ThryEditor
{
    public class ShaderTextureProperty : ShaderProperty
    {
        public bool showFoldoutProperties = false;
        public bool hasScaleOffset = false;

        public ShaderTextureProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, string optionsRaw, bool hasScaleOffset, int property_index) : base(shaderEditor, materialProperty, displayName, optionsRaw, property_index)
        {
            this.hasScaleOffset = hasScaleOffset;
        }
    }

}