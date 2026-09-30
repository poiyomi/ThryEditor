using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Helpers
{
    public static class StencilPropertyHelper
    {
        public static int GetValueFromProperty(string propertyName, int defaultValue)
        {
            var materialProperty = GetRefreshedMaterialProperty(propertyName);
            if (materialProperty == null) return defaultValue;
            return Mathf.Clamp((int)materialProperty.GetNumber(), StencilOperationsHelper.ByteMin, StencilOperationsHelper.ByteMax);
        }

        // Clamped on the way out to match GetValueFromProperty, which clamps on the way in.
        public static void SaveValueToProperty(string propertyName, int value)
        {
            value = Mathf.Clamp(value, StencilOperationsHelper.ByteMin, StencilOperationsHelper.ByteMax);
            var property = GetRefreshedShaderProperty(propertyName);
            if (property?.MaterialProperty == null) return;

            property.FloatValue = value;
            // FloatValue does not raise the value-changed event, so the cached IsPropertyValueDefault
            // on this property and its sections would keep showing a stale non-default star. Same
            // reason ThryMultiFloatButtonsDrawer calls this after writing a property other than its own.
            property.CheckForValueChange();
            MarkMaterialsDirty();
        }

        // MaterialProperty caches the value it read
        public static void RefreshProperty(string propertyName)
        {
            var property = GetRefreshedShaderProperty(propertyName);
            MaterialProperty[] editorProps = ShaderEditor.Active?.Properties;
            if (property?.MaterialProperty == null || editorProps == null) return;
            int index = property.ThryPropertyIndex;
            if (index < 0 || index >= editorProps.Length) return;
            editorProps[index] = MaterialEditor.GetMaterialProperty(property.MaterialProperty.targets, propertyName);
            property.UpdatedMaterialPropertyReference();
            property.CheckForValueChange();
        }

        public static MaterialProperty GetProperty(string propertyName)
        {
            return GetRefreshedMaterialProperty(propertyName);
        }

        public static bool HasMixedValue(string propertyName)
        {
            return GetProperty(propertyName)?.hasMixedValue ?? false;
        }

        private static MaterialProperty GetRefreshedMaterialProperty(string propertyName)
        {
            return GetRefreshedShaderProperty(propertyName)?.MaterialProperty;
        }

        // Material property references go stale when the inspector rebuilds, so re-resolve before use.
        private static ShaderProperty GetRefreshedShaderProperty(string propertyName)
        {
            var propertyDict = ShaderEditor.Active?.PropertyDictionary;
            if (propertyDict == null) return null;
            if (!propertyDict.TryGetValue(propertyName, out var property) || property == null) return null;
            property.UpdatedMaterialPropertyReference();
            return property;
        }

        private static void MarkMaterialsDirty()
        {
            if (ShaderEditor.Active?.Materials == null) return;
            foreach (Material mat in ShaderEditor.Active.Materials)
            {
                if (mat != null) EditorUtility.SetDirty(mat);
            }
        }
    }
}
