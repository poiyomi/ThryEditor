using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Tiled quad in front of the Scene view camera, reading the stencil buffer as it stands at the
    // quad's own render queue - the state at a chosen point in the frame, not the end of it.
    public class StencilDebugOverlay : EditorWindow
    {
        const string ShaderName = "Hidden/Thry/StencilDebugOverlay";
        const string AtlasPath = "StencilOverlay/number_atlas_1024";

        const float QuadDistance = 2f;
        static readonly Vector3 s_quadScale = new Vector3(10f, 10f, 1f);

        const float DefaultTiling = 0.7f;
        const float DefaultOpacity = 0.35f;
        const float DefaultRotation = 22.5f;

        // Log slider: a step is a constant ~1.74x in tile count, not a fixed count.
        const float TilingStep = 0.1f;
        const float RotationStep = 22.5f;
        const float RotationRange = 180f;

        // Slider maps onto 2^1 = 2 up to 2^9 = 512 tiles.
        const float MinTileExponent = 1f;
        const float MaxTileExponent = 9f;

        const float ControlButtonWidth = 20f;
        const float QueueFieldWidth = 75f;
        const int MaxQueue = 5000;
        const int ShaderDefaultQueue = -1;

        static readonly string[] s_renderQueueNames =
            { "From Shader", "Background", "Geometry", "AlphaTest", "Transparent", "Overlay" };
        static readonly int[] s_renderQueueValues =
            { ShaderDefaultQueue, 1000, 2000, 2450, 3000, 4000 };

        // Not from a static initialiser: EditorGUIUtility and EditorStyles are not ready during a
        // domain reload. Built on first draw.
        static GUIContent s_tilingDown, s_tilingUp, s_opacityOff, s_opacityFull, s_rotateLeft, s_rotateRight;
        static GUIContent s_resetTiling, s_resetOpacity, s_resetRotation;
        static GUIStyle s_subscriptLeft, s_subscriptRight;

        // Rebuilt after every domain reload, so deliberately not serialized.
        private GameObject _quad;
        private Material _quadMaterial;
        private bool _creationAttempted;

        // Serialized so a script recompile does not reset the controls under an open window.
        [SerializeField] private bool _quadEnabled = true;
        [SerializeField] private float _tilingSlider = DefaultTiling;
        [SerializeField] private float _opacity = DefaultOpacity;
        [SerializeField] private float _rotation = DefaultRotation;
        [SerializeField] private bool _showNumbers = true;
        [SerializeField] private int _renderQueue = ShaderDefaultQueue;

        private void OnEnable()
        {
            titleContent = new GUIContent("Stencil Debug Overlay");
            minSize = new Vector2(400f, 200f);
            // No quad here: OnEnable fires during domain reload, before Shader.Find can resolve.
            // EnsureQuad defers it to the first draw.
            SceneView.duringSceneGui += UpdateQuadTransform;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= UpdateQuadTransform;
            _creationAttempted = false;
            if (_quad != null)
            {
                DestroyImmediate(_quad);
                _quad = null;
            }
            if (_quadMaterial != null)
            {
                DestroyImmediate(_quadMaterial);
                _quadMaterial = null;
            }

            // Destroying the quad does not dirty the Scene view, so its last frame would linger.
            SceneView.RepaintAll();
        }

        private void EnsureQuad()
        {
            if (_creationAttempted) return;
            // One shot, so a failure logs once instead of every repaint. Reopen to retry.
            _creationAttempted = true;
            CreateQuad();
            ApplyControls();
        }

        private void CreateQuad()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                ThryLogger.LogErr("StencilDebugOverlay", $"Shader '{ShaderName}' not found.");
                return;
            }

            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "SceneViewStencilDebugQuad";
            _quad.hideFlags = HideFlags.HideAndDontSave;

            // Drop the collider CreatePrimitive fits, or it blocks raycasts in front of the camera.
            Collider quadCollider = _quad.GetComponent<Collider>();
            if (quadCollider != null)
                DestroyImmediate(quadCollider);

            Renderer quadRenderer = _quad.GetComponent<Renderer>();
            if (quadRenderer == null)
            {
                ThryLogger.LogErr("StencilDebugOverlay", "The created quad has no Renderer.");
                DestroyImmediate(_quad);
                _quad = null;
                return;
            }

            _quadMaterial = new Material(shader);

            Texture2D atlas = Resources.Load<Texture2D>(AtlasPath);
            if (atlas != null)
                _quadMaterial.SetTexture("_NumberAtlas", atlas);
            else
                ThryLogger.LogWarn("StencilDebugOverlay", $"'{AtlasPath}' is missing from Resources, so the overlay draws without digits.");

            quadRenderer.material = _quadMaterial;
        }

        private void UpdateQuadTransform(SceneView sceneView)
        {
            EnsureQuad();
            Camera cam = sceneView.camera;
            if (cam == null || _quad == null) return;
            _quad.transform.position = cam.transform.position + cam.transform.forward * QuadDistance;
            _quad.transform.rotation = cam.transform.rotation;
            _quad.transform.localScale = s_quadScale;
        }

        private void ApplyControls()
        {
            if (_quad != null) _quad.SetActive(_quadEnabled);
            if (_quadMaterial == null) return;

            float tileCount = Mathf.Round(Mathf.Pow(2f, Mathf.Lerp(MinTileExponent, MaxTileExponent, _tilingSlider)));
            _quadMaterial.SetFloat("_TileCount", tileCount);
            _quadMaterial.SetFloat("_BgOpacity", _opacity);
            _quadMaterial.SetFloat("_NumberRotation", _rotation);
            _quadMaterial.SetFloat("_ShowNumbers", _showNumbers ? 1f : 0f);
            _quadMaterial.renderQueue = _renderQueue;

            // Nothing else dirties the Scene view when a control changes.
            SceneView.RepaintAll();
        }

        private static void EnsureStyles()
        {
            if (s_rotateLeft != null) return;

            s_tilingDown = new GUIContent("−", EditorLocale.editor.Get("stencil_overlay_tiling_down"));
            s_tilingUp = new GUIContent("+", EditorLocale.editor.Get("stencil_overlay_tiling_up"));
            s_opacityOff = new GUIContent("₀", EditorLocale.editor.Get("stencil_overlay_opacity_min"));
            s_opacityFull = new GUIContent("₁₀₀", EditorLocale.editor.Get("stencil_overlay_opacity_max"));
            s_rotateLeft = IconOrText("ArrowNavigationLeft", "◄", EditorLocale.editor.Get("stencil_overlay_rotate_left").ReplaceVariables(RotationStep));
            s_rotateRight = IconOrText("ArrowNavigationRight", "►", EditorLocale.editor.Get("stencil_overlay_rotate_right").ReplaceVariables(RotationStep));

            string resetTo = EditorLocale.editor.Get("stencil_overlay_reset_to");
            s_resetTiling = IconOrText("Refresh", "↺", resetTo.ReplaceVariables(DefaultTiling));
            s_resetOpacity = IconOrText("Refresh", "↺", resetTo.ReplaceVariables(DefaultOpacity));
            s_resetRotation = IconOrText("Refresh", "↺", resetTo.ReplaceVariables(DefaultRotation + "°"));

            // Subscript digits sit below the baseline, so nudge them up to re-centre.
            s_subscriptLeft = new GUIStyle(EditorStyles.miniButtonLeft) { contentOffset = new Vector2(0f, -3f) };
            s_subscriptRight = new GUIStyle(EditorStyles.miniButtonRight) { contentOffset = new Vector2(0f, -3f) };
        }

        // Text fallback: a built-in icon name in one editor version may not exist in the next.
        private static GUIContent IconOrText(string iconName, string fallbackText, string tooltip)
        {
            Texture image = EditorGUIUtility.IconContent(iconName)?.image;
            return image != null ? new GUIContent(image, tooltip) : new GUIContent(fallbackText, tooltip);
        }

        private static bool MiniButton(GUIContent content, GUIStyle style)
        {
            return GUILayout.Button(content, style, GUILayout.Width(ControlButtonWidth));
        }

        private static float WrapRotation(float degrees)
        {
            return Mathf.Repeat(degrees + RotationRange, RotationRange * 2f) - RotationRange;
        }

        private void OnGUI()
        {
            EnsureQuad();
            EnsureStyles();

            // One check for the whole window: button clicks raise GUI.changed like the sliders do.
            EditorGUI.BeginChangeCheck();

            _quadEnabled = EditorGUILayout.Toggle(EditorLocale.editor.Get("stencil_overlay_show"), _quadEnabled);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _tilingSlider = EditorGUILayout.Slider(EditorLocale.editor.Get("stencil_overlay_tiling"), _tilingSlider, 0f, 1f);
            if (MiniButton(s_tilingDown, EditorStyles.miniButtonLeft)) _tilingSlider = Mathf.Clamp01(_tilingSlider - TilingStep);
            if (MiniButton(s_resetTiling, EditorStyles.miniButtonMid)) _tilingSlider = DefaultTiling;
            if (MiniButton(s_tilingUp, EditorStyles.miniButtonRight)) _tilingSlider = Mathf.Clamp01(_tilingSlider + TilingStep);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _opacity = EditorGUILayout.Slider(EditorLocale.editor.Get("stencil_overlay_opacity"), _opacity, 0f, 1f);
            if (MiniButton(s_opacityOff, s_subscriptLeft)) _opacity = 0f;
            if (MiniButton(s_resetOpacity, EditorStyles.miniButtonMid)) _opacity = DefaultOpacity;
            if (MiniButton(s_opacityFull, s_subscriptRight)) _opacity = 1f;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _rotation = EditorGUILayout.Slider(EditorLocale.editor.Get("stencil_overlay_rotation"), _rotation, -RotationRange, RotationRange);
            if (MiniButton(s_rotateLeft, EditorStyles.miniButtonLeft)) _rotation = WrapRotation(_rotation - RotationStep);
            if (MiniButton(s_resetRotation, EditorStyles.miniButtonMid)) _rotation = DefaultRotation;
            if (MiniButton(s_rotateRight, EditorStyles.miniButtonRight)) _rotation = WrapRotation(_rotation + RotationStep);
            EditorGUILayout.EndHorizontal();

            _showNumbers = EditorGUILayout.Toggle(EditorLocale.editor.Get("stencil_overlay_show_numbers"), _showNumbers);

            DrawRenderQueue();

            EditorGUILayout.Space();

            if (GUILayout.Button(EditorLocale.editor.Get("stencil_overlay_reset")))
            {
                _tilingSlider = DefaultTiling;
                _opacity = DefaultOpacity;
                _rotation = DefaultRotation;
                _showNumbers = true;
                _renderQueue = ShaderDefaultQueue;
            }

            if (EditorGUI.EndChangeCheck()) ApplyControls();
        }

        private void DrawRenderQueue()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(EditorLocale.editor.Get("stencil_overlay_queue"),
                EditorLocale.editor.Get("stencil_overlay_queue_tooltip")),
                GUILayout.Width(EditorGUIUtility.labelWidth));

            // A custom queue adds a display-only trailing entry, so an index past the presets must
            // not be written back.
            int selectedIndex;
            string[] queueNames = RenderQueueHelper.GetDropdownNames(_renderQueue, s_renderQueueNames, s_renderQueueValues, out selectedIndex);

            int newIndex = EditorGUILayout.Popup(selectedIndex, queueNames);
            if (newIndex < s_renderQueueValues.Length)
                _renderQueue = s_renderQueueValues[newIndex];

            // -1 is a write-only sentinel the material resolves to the shader's Queue tag, so the
            // int field has to show the material's value instead.
            int shownQueue = _renderQueue;
            if (shownQueue == ShaderDefaultQueue && _quadMaterial != null)
                shownQueue = _quadMaterial.renderQueue;

            int typedQueue = EditorGUILayout.IntField(shownQueue, GUILayout.Width(QueueFieldWidth));
            if (typedQueue != shownQueue)
                _renderQueue = Mathf.Clamp(typedQueue, ShaderDefaultQueue, MaxQueue);

            EditorGUILayout.EndHorizontal();
        }
    }
}
