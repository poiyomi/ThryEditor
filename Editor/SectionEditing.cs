using System;

namespace Thry.ThryEditor
{
    /// <summary>Optional package integration. Thry does not reference a shader generator.</summary>
    public static class SectionEditing
    {
        public static Func<ShaderPart, bool> IsEditingPart;
        public static Func<ShaderPart, bool> HidePart;
        public static Func<ShaderGroup, bool> IsExcluded;
        public static Func<ShaderEditor, UnityEngine.UIElements.VisualElement> CreateToolbar;
        public static Action<ShaderGroup, bool> SetIncluded;

        internal static bool IsEditing(ShaderPart part) => IsEditingPart?.Invoke(part) == true;
        internal static bool IsHidden(ShaderPart part) => HidePart?.Invoke(part) == true;
    }
}
