using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Editor preferences are intentionally separate from serialized material values.
    internal static class RetainedSearchPreferences
    {
        [Serializable] sealed class State
        {
            public List<string> favorites = new List<string>();
        }
        static readonly Dictionary<string, State> Cache = new Dictionary<string, State>();
        sealed class InspectorState { internal Shader Shader; internal State State; internal string Key; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShaderEditor, InspectorState> Inspectors
            = new System.Runtime.CompilerServices.ConditionalWeakTable<ShaderEditor, InspectorState>();
        internal static int Revision { get; private set; }
        static string Key(ShaderEditor shader)
        {
            var material = shader.Materials[0];
            var original = material.IsLocked() ? ShaderOptimizer.GetOriginalShader(material, false) : SectionLock.GetSourceShader(material.shader);
            return "Thry.Search." + Application.dataPath + "." + (original != null ? original.name : material.shader.name);
        }
        static State Read(ShaderEditor shader)
        {
            var inspector = Inspectors.GetValue(shader, _ => new InspectorState());
            if (inspector.Shader == shader.Materials[0].shader && inspector.State != null) return inspector.State;
            string key = Key(shader); State state;
            if (!Cache.TryGetValue(key, out state))
            {
                try { state = JsonUtility.FromJson<State>(EditorPrefs.GetString(key, "")); }
                catch (ArgumentException) { state = null; }
                state = state ?? new State(); Cache[key] = state;
            }
            inspector.Shader = shader.Materials[0].shader; inspector.State = state; inspector.Key = key; return state;
        }
        static void Write(ShaderEditor shader) { var state = Read(shader); EditorPrefs.SetString(Inspectors.GetValue(shader, _ => new InspectorState()).Key, JsonUtility.ToJson(state)); Revision++; }
        internal static bool IsFavorite(ShaderProperty property) => Read(property.MyShaderUI).favorites.Contains(property.MaterialProperty.name);
        internal static void ToggleFavorite(ShaderProperty property)
        {
            var favorites = Read(property.MyShaderUI).favorites; string name = property.MaterialProperty.name;
            if (!favorites.Remove(name)) favorites.Add(name);
            Write(property.MyShaderUI);
        }
    }
}
