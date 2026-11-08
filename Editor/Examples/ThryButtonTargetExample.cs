using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    // Example of [ThryButton]'s args and IThryButtonTarget: reads properties it is handed without
    // knowing the shader in advance. Opened by a button in Examples/Example3.shader, so no menu
    // entry. The two easy things to get wrong are in DescribeValue and OnInspectorUpdate.
    public class ThryButtonTargetExample : EditorWindow, IThryButtonTarget
    {
        // Serialized so the context survives a domain reload, as the window does.
        [SerializeField] private string[] _propertyNames = new string[0];
        [SerializeField] private Material[] _materials = new Material[0];

        private void OnEnable()
        {
            titleContent = new GUIContent("Thry Button Target Example");
            minSize = new Vector2(420f, 180f);
        }

        public void SetButtonContext(string[] propertyNames, Material[] materials)
        {
            _propertyNames = propertyNames ?? new string[0];
            _materials = materials ?? new Material[0];
            Repaint();
        }

        // Nothing repaints this window when a material changes, so without this the values freeze
        // on the frame it opened. OnInspectorUpdate runs at 10 Hz, focused or not.
        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (_propertyNames.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Open this window from the [ThryButton] at the bottom of the Stencil section " +
                    "in the Thry/Example 3 shader. The button's args decide what is listed here.",
                    MessageType.Info);
                return;
            }

            Material[] live = _materials.Where(m => m != null).ToArray();
            EditorGUILayout.LabelField("Opened for",
                live.Length == 0 ? "nothing" : string.Join(", ", live.Select(m => m.name)));

            EditorGUILayout.Space();

            if (live.Length == 0)
            {
                EditorGUILayout.HelpBox("Every material this window was opened for is gone.", MessageType.Warning);
                return;
            }

            foreach (string name in _propertyNames)
                EditorGUILayout.LabelField(name, DescribeValue(name, live));
        }

        // Read off the Material, not ShaderEditor.Active.PropertyDictionary: the MaterialProperty
        // behind each entry there is only refreshed while the inspector draws
        // (ShaderPart.UpdatedMaterialPropertyReference), so this would report its last frame.
        private static string DescribeValue(string name, Material[] materials)
        {
            Shader shader = materials[0].shader;
            if (shader == null) return "no shader";

            int index = shader.FindPropertyIndex(name);
            if (index < 0) return "not on the current shader";

            ShaderPropertyType type = shader.GetPropertyType(index);
            string value = ReadValue(materials[0], name, type);

            // Mixed rather than silently showing only the first material's value.
            for (int i = 1; i < materials.Length; i++)
            {
                if (!materials[i].HasProperty(name)) return "-";
                if (ReadValue(materials[i], name, type) != value) return "-";
            }

            return value;
        }

        private static string ReadValue(Material material, string name, ShaderPropertyType type)
        {
            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return material.GetFloat(name).ToString();
#if UNITY_2022_1_OR_NEWER
                case ShaderPropertyType.Int:
                    return material.GetInt(name).ToString();
#endif
                case ShaderPropertyType.Color:
                    return material.GetColor(name).ToString();
                case ShaderPropertyType.Vector:
                    return material.GetVector(name).ToString();
                case ShaderPropertyType.Texture:
                    Texture texture = material.GetTexture(name);
                    return texture != null ? texture.name : "none";
                default:
                    return "unsupported type";
            }
        }
    }
}
