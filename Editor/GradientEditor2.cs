using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public partial class GradientEditor2 : EditorWindow
    {
        private Gradient _gradient;
        private bool _allowSizeSelection;
        private Vector2Int _textureSizeMin;
        private Vector2Int _textureSizeMax;
        private Vector2Int _textureSize;
        private object _gradientEditor;
        private object _gradientLibary;
        private TextureData _outputSettings;
        private bool _fixedOutput;
        private Action<Gradient, TextureData, int> _onTextureApply;
        private int _outputDirection;
        private Func<bool> _canApply;

        internal static GradientEditor2 OpenTexture(Gradient gradient, TextureData settings, bool fixedOutput,
            Action<Gradient, TextureData, int> apply, Func<bool> canApply, int direction = 0)
        {
            var window = CreateInstance<GradientEditor2>();
            window._gradient = CopyGradient(gradient);
            window._outputSettings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(settings));
            window._textureSize = new Vector2Int(settings.width, settings.height);
            window._textureSizeMin = Vector2Int.one; window._textureSizeMax = new Vector2Int(8192, 8192);
            window._allowSizeSelection = !fixedOutput; window._fixedOutput = fixedOutput;
            window._onTextureApply = apply; window._canApply = canApply;
            window._outputDirection = Mathf.Clamp(direction, 0, 2);
            window.titleContent = new GUIContent("Create gradient texture");
            window.position = new Rect(100, 100, 420, fixedOutput ? 390 : 470);
            window.ShowUtility(); window.CreateGUI();
            return window;
        }

        static MethodInfo s_gradientEditorGUIMethodInfo = null;
        static MethodInfo GradientEditorGUI {
            get
            {
                if(s_gradientEditorGUIMethodInfo == null)
                {
                    Type gradient_editor_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GradientEditor");
                    s_gradientEditorGUIMethodInfo = gradient_editor_type.GetMethod("OnGUI");
                }
                return s_gradientEditorGUIMethodInfo;
            }
        }

        static MethodInfo s_presetLibraryOnGUIMethodInfo = null;
        internal static MethodInfo PresetLibraryOnGUI
        {
            get
            {
                if (s_presetLibraryOnGUIMethodInfo == null)
                {
                    Type gradient_preset_libary_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GradientPresetLibrary");
                    Type preset_libary_editor_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PresetLibraryEditor`1");
                    Type gradient_preset_libary_editor_type = preset_libary_editor_type.MakeGenericType(gradient_preset_libary_type);
                    s_presetLibraryOnGUIMethodInfo = gradient_preset_libary_editor_type.GetMethod("OnGUI");
                }
                return s_presetLibraryOnGUIMethodInfo;
            }
        }

        static object GetGradientEditor(Gradient gradient)
        {
            Type gradientEditorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GradientEditor");
            var gradientEditor = Activator.CreateInstance(gradientEditorType);
            SetGradient(gradientEditor, gradient);
            return gradientEditor;
        }

        static void SetGradient(object gradientEditor, Gradient gradient)
        {
            Type gradientEditorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GradientEditor");
            var gradientEditorInit = gradientEditorType.GetMethod("Init");

            // Not HDR: the PNG clamps channels to 0-1
            gradientEditorInit.Invoke(gradientEditor, new object[] { gradient, 0, false, ColorSpace.Linear });
        }

        /// <summary>Saves a preset library editor's state to EditorPrefs and unloads its libraries, as Unity's gradient picker does on close.</summary>
        internal static void ReleaseGradientLibrary(object library)
        {
            if (library == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var state = library.GetType().GetField("m_State", flags)?.GetValue(library);
                state?.GetType().GetMethod("TransferEditorPrefsState", flags)?.Invoke(state, new object[] { false });
                library.GetType().GetMethod("UnloadUsedLibraries", flags, null, Type.EmptyTypes, null)?.Invoke(library, null);
            }
            catch (TargetInvocationException) { }
            catch (ArgumentException) { }
        }

        public static object GetGradientLibary(Action<int, object> presetSelectedCallback)
        {
            Type presetLibraryEditorState_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PresetLibraryEditorState");
            var preset_libary_editor_state = Activator.CreateInstance(presetLibraryEditorState_type, "Gradient");
            MethodInfo transfer_editor_prefs_state = presetLibraryEditorState_type.GetMethod("TransferEditorPrefsState");
            transfer_editor_prefs_state.Invoke(preset_libary_editor_state, new object[] { true });

            Type scriptable_save_load_helper_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ScriptableObjectSaveLoadHelper`1");
            Type gradient_preset_libary_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GradientPresetLibrary");
            Type preset_libary_editor_type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PresetLibraryEditor`1");
            Type save_load_helper_type = scriptable_save_load_helper_type.MakeGenericType(gradient_preset_libary_type);
            Type gradient_preset_libary_editor_type = preset_libary_editor_type.MakeGenericType(gradient_preset_libary_type);

            object saveLoadHelper = Activator.CreateInstance(save_load_helper_type, "gradients", SaveType.Text);

            var preset_libary_editor = Activator.CreateInstance(gradient_preset_libary_editor_type, saveLoadHelper, preset_libary_editor_state, presetSelectedCallback);
            PropertyInfo show_header = gradient_preset_libary_editor_type.GetProperty("showHeader");
            show_header.SetValue(preset_libary_editor, true, null);
            PropertyInfo minMaxPreviewHeight = gradient_preset_libary_editor_type.GetProperty("minMaxPreviewHeight");
            minMaxPreviewHeight.SetValue(preset_libary_editor, new Vector2(24f, 24f), null);

            return preset_libary_editor;
        }

        private void OnDestroy()
        {
            ReleaseGradientLibrary(_gradientLibary);
            _gradientLibary = null;
        }

        private static Gradient CopyGradient(Gradient source)
        {
            var copy = new Gradient();
            if (source != null) { copy.SetKeys(source.colorKeys, source.alphaKeys); copy.mode = source.mode; }
            return copy;
        }

        void Apply()
        {
            if (_gradient == null || (_canApply != null && !_canApply())) return;
            // Delegate bindings do not survive a script reload. Keep the draft
            // visible, but never create an unassigned texture from a stale tool.
            if (_onTextureApply == null) return;
            var settings = JsonUtility.FromJson<TextureData>(JsonUtility.ToJson(_outputSettings));
            settings.width = _textureSize.x; settings.height = _textureSize.y;
            _onTextureApply(CopyGradient(_gradient), settings, _outputDirection);
        }

        internal static Texture2D CreateTexture(Gradient gradient, int width, int height, int direction)
        {
            width = Mathf.Clamp(width, 1, 8192); height = Mathf.Clamp(height, 1, 8192);
            bool vertical = direction != 0;
            int length = vertical ? height : width;
            var ramp = new Color[length];
            for (int i = 0; i < length; i++)
            {
                float position = length > 1 ? (float)i / (length - 1) : 0;
                if (direction == 2 && length > 1) position = 1 - position;
                ramp[i] = gradient.Evaluate(position);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA64, false);
            try
            {
                var row = vertical ? new Color[width] : ramp;
                for (int y = 0; y < height; y++)
                {
                    if (vertical) for (int x = 0; x < width; x++) row[x] = ramp[y];
                    texture.SetPixels(0, y, width, 1, row);
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear; texture.Apply();
                return texture;
            }
            catch { DestroyImmediate(texture); throw; }
        }
    }
}
