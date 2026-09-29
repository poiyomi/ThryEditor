using System;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Per user and shared by all projects, like Unity's own color picker settings.
    internal static class ColorPickerPreferences
    {
        const string Key = "Thry.ColorPicker";

        [Serializable] internal sealed class Data
        {
            public int mode;                    // slider group: 0 RGB, 1 HSV, 2 HSL
            public bool floats;                 // every slider as 0-1 instead of 0-255, degrees and percent
            public bool square;                 // saturation/value square instead of the triangle
        }

        static Data s_Data;

        internal static Data Get()
        {
            if (s_Data != null) return s_Data;
            try { s_Data = JsonUtility.FromJson<Data>(EditorPrefs.GetString(Key, "")); }
            catch (ArgumentException) { s_Data = null; }
            s_Data = s_Data ?? new Data();
            s_Data.mode = Mathf.Clamp(s_Data.mode, 0, 2);
            return s_Data;
        }

        internal static void Save() => EditorPrefs.SetString(Key, JsonUtility.ToJson(Get()));
    }
}
