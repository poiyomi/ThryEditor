using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Decorators
{
    public class ThryDecalBakeAllDecorator : MaterialPropertyDrawer
    {
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDecorator(this);
            return DecalBakeBridge.BakeAllAvailable ? EditorGUIUtility.singleLineHeight + 6 : 0;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            if (!DecalBakeBridge.BakeAllAvailable) return;
            position = new RectOffset(0, 0, 0, 3).Remove(EditorGUI.IndentedRect(position));
            var material = editor.target as Material;
            using (new EditorGUI.DisabledScope(editor.targets.Length != 1 || !DecalBakeBridge.HasEnabledDecals(material)))
                if (GUI.Button(position, new GUIContent("Bake Active Decals", "Bake the active decals into one new main texture and disable them. Use Undo to restore them.")))
                    EditorApplication.delayCall += () => { if (material != null) DecalBakeBridge.BakeAll(material); };
        }
    }
}
