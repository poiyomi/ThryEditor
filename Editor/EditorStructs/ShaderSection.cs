using UnityEditor;

namespace Thry.ThryEditor
{
    public class ShaderSection : ShaderGroup
    {
        public ShaderSection(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, string optionsRaw, int propertyIndex)
            : base(shaderEditor, prop, materialEditor, displayName, optionsRaw, propertyIndex) { }
    }
}
