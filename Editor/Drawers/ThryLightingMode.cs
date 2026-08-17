using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    // Lighting Type selector for modular shading modes.
    //
    // Replaces the fixed [KeywordEnum(...)] on _LightingMode. Each shading mode is (or will
    // be) its own module that contributes a `_LIGHTINGMODE_<X>` shader_feature keyword; this
    // drawer builds the dropdown from the keywords the *generated shader actually declares*
    // (shader.keywordSpace), so a shader only lists the modes it ships — no phantom entries
    // for modes that were left out, and no mid-line KeywordEnum injection needed.
    //
    // The stored value stays the STABLE master index (below), so every existing
    // `condition_showS:(_LightingMode==N)` / `//ifex _LightingMode!=N` keeps working unchanged
    // and pre-existing materials (which already store the index + keyword) need no migration.
    //
    // MASTER is APPEND-ONLY: index == position, and materials persist that index. Never
    // reorder or remove a row — only append new modes at the end.
    public class ThryLightingModeDrawer : MaterialPropertyDrawer
    {
        static readonly string[] MasterKeyword =
        {
            "_LIGHTINGMODE_TEXTURERAMP",    // 0
            "_LIGHTINGMODE_MULTILAYER_MATH",// 1
            "_LIGHTINGMODE_WRAPPED",        // 2
            "_LIGHTINGMODE_SKIN",           // 3
            "_LIGHTINGMODE_SHADEMAP",       // 4
            "_LIGHTINGMODE_FLAT",           // 5
            "_LIGHTINGMODE_REALISTIC",      // 6
            "_LIGHTINGMODE_CLOTH",          // 7
            "_LIGHTINGMODE_SDF",            // 8
            "_LIGHTINGMODE_PBR",            // 9
        };
        static readonly string[] MasterDisplay =
        {
            "Texture Ramp", "Multilayer Math", "Wrapped", "Skin", "ShadeMap",
            "Flat", "Realistic", "Cloth", "SDF", "PBR",
        };

        // master indices whose keyword is declared by this shader, in master order
        static List<int> AvailableIndices(Shader shader)
        {
            var declared = new HashSet<string>();
            foreach (var k in shader.keywordSpace.keywords)
                declared.Add(k.name);
            var result = new List<int>();
            for (int i = 0; i < MasterKeyword.Length; i++)
                if (declared.Contains(MasterKeyword[i]))
                    result.Add(i);
            return result;
        }

        static void ApplyMode(MaterialProperty prop, int masterIndex)
        {
            prop.SetNumber(masterIndex);
            foreach (var o in prop.targets)
            {
                var m = o as Material;
                if (m == null) continue;
                for (int i = 0; i < MasterKeyword.Length; i++)
                    m.DisableKeyword(MasterKeyword[i]);
                m.EnableKeyword(MasterKeyword[masterIndex]);
            }
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            var shader = (prop.targets.Length > 0 ? prop.targets[0] as Material : null)?.shader;
            if (shader == null)
            {
                EditorGUI.LabelField(position, label.text, "(no shader)");
                return;
            }

            var available = AvailableIndices(shader);
            if (available.Count == 0)
            {
                EditorGUI.LabelField(position, label.text, "(no lighting modes)");
                return;
            }

            var names = new GUIContent[available.Count];
            for (int i = 0; i < available.Count; i++)
                names[i] = new GUIContent(MasterDisplay[available[i]]);

            int current = Mathf.RoundToInt(prop.GetNumber());
            int selected = available.IndexOf(current);
            // Current mode not compiled into this shader (e.g. material moved between shaders):
            // show the first available without silently rewriting the stored value on open.
            bool missing = selected < 0;
            if (missing) selected = 0;

            EditorGUI.showMixedValue = prop.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int chosen = EditorGUI.Popup(position, label, selected, names);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label.text);
                ApplyMode(prop, available[chosen]);
            }
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return base.GetPropertyHeight(prop, label, editor);
        }
    }
}
