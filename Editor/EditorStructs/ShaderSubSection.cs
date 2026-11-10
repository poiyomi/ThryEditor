using UnityEditor;

namespace Thry.ThryEditor
{
    public class ShaderSubSection : ShaderGroup
    {
        public ShaderSubSection(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, string optionsRaw, int propertyIndex)
            : base(shaderEditor, prop, materialEditor, displayName, optionsRaw, propertyIndex) { }
    }
}
