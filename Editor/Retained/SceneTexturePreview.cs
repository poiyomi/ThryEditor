using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    /// <summary>Camera-owned inspection drawing. Never substitutes renderer materials or writes source assets.</summary>
    [InitializeOnLoad]
    public static class SceneTexturePreview
    {
        const CameraEvent DrawEvent = CameraEvent.AfterForwardAlpha;
        public const string ShaderName = "Hidden/Thry/SceneTextureInspection";
        static readonly List<Renderer> Renderers = new List<Renderer>();
        static SceneView _view;
        static Camera _attachedCamera;
        static CommandBuffer _commands;
        static Material _preview;
        static Material _source;
        static string _property;
        static int _drawCount, _channel;
        static Texture _lastTexture;
        static bool _normalMap;
        static double _nextNormalCheck;
        static double _lastRepaint;
        static StageHandle _stage;
        public static bool Active => _source != null && _view != null && _preview != null;
        public static Material Source => _source;
        public static string Property => _property;
        public static int DrawCount => _drawCount;

        static SceneTexturePreview()
        {
            Camera.onPreCull += BeforeCamera;
            SceneView.duringSceneGui += DrawGUI;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            UnityEditor.Compilation.CompilationPipeline.compilationStarted += _ => Stop();
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
            EditorApplication.hierarchyChanged += RefreshRenderers;
            EditorApplication.update += Tick;
        }

        public static bool CanPreview(Material material, string property)
        {
            return !string.IsNullOrEmpty(property) && material != null && material.HasProperty(property)
                && material.GetTexture(property) != null
                && material.GetTexture(property).dimension == TextureDimension.Tex2D;
        }

        public static void Toggle(Material material, string property, string caption, int channel = 0)
        {
            if (Active && _source == material && _property == property) Stop();
            else Start(material, property, caption, channel);
        }

        public static void SetChannel(Material material, string property, int channel)
        {
            if (!Active || _source != material || _property != property) return;
            _channel = Mathf.Clamp(channel, 0, 5);
            _view.Repaint();
        }

        public static bool Start(Material material, string property, string caption = null, int channel = 0)
        {
            Stop();
            if (!CanPreview(material, property) || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                Debug.LogWarning("Scene texture inspection currently supports the Built-in Render Pipeline.");
                return false;
            }
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported) return false;
            _view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            _source = material; _property = property;
            _channel = Mathf.Clamp(channel, 0, 5);
            _stage = StageUtility.GetCurrentStageHandle();
            _preview = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "Scene texture inspection (temporary)" };
            _commands = new CommandBuffer { name = "Thry Scene Texture Inspection" };
            RefreshRenderers(); _view.Focus(); _view.Repaint();
            return true;
        }

        public static void Stop()
        {
            Detach();
            _commands?.Release(); _commands = null;
            if (_preview != null) UnityEngine.Object.DestroyImmediate(_preview);
            _preview = null; _source = null; _property = null; _lastTexture = null; _nextNormalCheck = 0;
            Renderers.Clear(); _drawCount = 0;
            if (_view != null) _view.Repaint();
            _view = null;
        }

        static void RefreshRenderers()
        {
            Renderers.Clear();
            if (!Active) return;
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    && !EditorUtility.IsPersistent(renderer)
                    && StageUtility.GetStageHandle(renderer.gameObject) == StageUtility.GetCurrentStageHandle())
                    Renderers.Add(renderer);
        }

        static void Tick()
        {
            if (_commands == null) return;
            if (!Active || !CanPreview(_source, _property) || EditorApplication.isPlayingOrWillChangePlaymode
                || _stage != StageUtility.GetCurrentStageHandle())
            { Stop(); return; }
            if (EditorApplication.timeSinceStartup - _lastRepaint < 1.0 / 30) return;
            _lastRepaint = EditorApplication.timeSinceStartup;
            _view.Repaint();
        }

        static void Detach()
        {
            if (_attachedCamera != null && _commands != null) _attachedCamera.RemoveCommandBuffer(DrawEvent, _commands);
            _attachedCamera = null;
        }

        static int MaterialUV => _source != null && _source.HasProperty(_property + "UV") ? Mathf.RoundToInt(_source.GetFloat(_property + "UV")) : 0;

        static void BeforeCamera(Camera camera)
        {
            if (!Active || camera != _view.camera || camera.cameraType != CameraType.SceneView) return;
            Detach(); _commands.Clear(); _drawCount = 0;
            _commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            if (!CanPreview(_source, _property)) return;
            int uv = MaterialUV;
            // Special projections must not silently fall back to a different mapping.
            if (uv < 0 || uv > 3)
            {
                _view.ShowNotification(new GUIContent("This texture mapping is not supported by the preview yet."));
                Stop(); return;
            }
            var texture = _source.GetTexture(_property);
            if (texture != _lastTexture || EditorApplication.timeSinceStartup >= _nextNormalCheck)
            {
                _lastTexture = texture; _normalMap = RetainedTexturePreview.IsNormalMap(texture);
                _nextNormalCheck = EditorApplication.timeSinceStartup + 1;
            }
            _preview.SetTexture("_ThryInspectTex", texture);
            var scale = _source.GetTextureScale(_property); var offset = _source.GetTextureOffset(_property);
            _preview.SetVector("_ThryInspectST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            var pan = _source.HasProperty(_property + "Pan") ? _source.GetVector(_property + "Pan") : Vector4.zero;
            _preview.SetVector("_ThryInspectPan", pan);
            _preview.SetFloat("_ThryInspectUV", uv);
            _preview.SetVector("_ThryInspectUVTiling", Vector("_UVSettingsTiling" + uv, new Vector4(1, 1, 0, 0)));
            _preview.SetVector("_ThryInspectUVOffset", Vector("_UVSettingsOffset" + uv, Vector4.zero));
            _preview.SetVector("_ThryInspectUVPan", Vector("_UVSettingsPan" + uv, Vector4.zero));
            _preview.SetFloat("_ThryInspectUVAngle", Number("_UVSettingsAngle" + uv));
            _preview.SetFloat("_ThryInspectUVRotate", Number("_UVSettingsRotate" + uv));
            _preview.SetFloat("_ThryInspectShiftBackface", Number("_UVSettingsShiftBackfaceUV"));
            _preview.SetFloat("_ThryInspectTimeSource", Number("_PoiTimeSource"));
            _preview.SetFloat("_ThryInspectNormal", _normalMap ? 1 : 0);
            _preview.SetFloat("_ThryInspectChannel", _channel);
            _preview.SetInt("_ThryInspectCull", _source.HasProperty("_Cull") ? _source.GetInt("_Cull") : (int)CullMode.Back);
            foreach (var renderer in Renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                    || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                    || SceneVisibilityManager.instance.IsHidden(renderer.gameObject)
                    || StageUtility.GetStageHandle(renderer.gameObject) != StageUtility.GetCurrentStageHandle()) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    if (materials[slot] != _source || mesh.subMeshCount == 0) continue;
                    _commands.DrawRenderer(renderer, _preview, Mathf.Min(slot, mesh.subMeshCount - 1), 0);
                    _drawCount++;
                }
            }
            // Keep this attached to its owning Scene camera until the next rebuild or Stop.
            // Removing it in onPostRender can precede execution of the late camera event.
            camera.AddCommandBuffer(DrawEvent, _commands); _attachedCamera = camera;
        }

        static void DrawGUI(SceneView view)
        {
            if (!Active || view != _view) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Stop(); e.Use(); return; }
        }

        static Vector4 Vector(string property, Vector4 fallback) => _source.HasProperty(property) ? _source.GetVector(property) : fallback;
        static float Number(string property) => _source.HasProperty(property) ? _source.GetFloat(property) : 0;
    }
}
