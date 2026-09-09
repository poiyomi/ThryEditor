#if UNITY_2021_3_OR_NEWER
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    internal static class RetainedUiState
    {
        static string Preference(string scope, string key) => "Thry.Retained.Foldout." + Hash128.Compute(Application.dataPath + "|" + scope + "|" + key);
        internal static bool Get(string scope, string key, bool defaultValue = false) => EditorPrefs.GetBool(Preference(scope, key), defaultValue);
        internal static void Set(string scope, string key, bool value) => EditorPrefs.SetBool(Preference(scope, key), value);
        internal static void Bind(Foldout foldout, string scope, string key, bool defaultValue = false)
        {
            foldout.SetValueWithoutNotify(Get(scope, key, defaultValue));
            foldout.RegisterValueChangedCallback(e => { if (e.target == foldout) Set(scope, key, e.newValue); });
        }
    }

    internal static class RetainedText
    {
        internal static string Get(string key, string fallback)
        {
            var locale = EditorLocale.editor;
            string id = "ui_" + key;
            return locale.Get(id, Config.Instance.locale, locale.Get(key, Config.Instance.locale, fallback));
        }

        internal static string Get(ShaderEditor shader, string key, string fallback)
        {
            string language = shader?.Locale == null ? Config.Instance.locale
                : shader.Locale.LanguageNames[shader.Locale.LanguageIndex];
            string shared = EditorLocale.editor.Get("ui_" + key, language, fallback);
            return shader?.Locale == null ? shared : shader.Locale.Get("ui." + key, shared);
        }
    }
}
#endif
