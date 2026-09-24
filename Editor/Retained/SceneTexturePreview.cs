using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

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
        sealed class Entry
        {
            public Material Source, Preview;
            public string Property, Name;
            public int Channel;
            public Texture LastTexture;
            public bool NormalMap;
            public double NextNormalCheck;
        }

        static readonly Dictionary<Material, Entry> Entries = new Dictionary<Material, Entry>();
        static readonly List<Material> InvalidSources = new List<Material>();
        static int _drawCount;
        static double _lastRepaint;
        static StageHandle _stage;
        public static bool Active => Entries.Count > 0 && _view != null && _commands != null;
        public static int Count => Entries.Count;
        public static int DrawCount => _drawCount;

        public static bool IsActive(Material material, string property) => Active && material != null
            && Entries.TryGetValue(material, out var entry) && entry.Property == property;

        public static int GetChannel(Material material, string property) => IsActive(material, property) ? Entries[material].Channel : 0;

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

        static int MaterialUV(Material material, string property) => material.HasProperty(property + "UV")
            ? Mathf.RoundToInt(material.GetFloat(property + "UV")) : 0;

        public static bool CanPreview(Material material, string property) => UnavailableReason(material, property) == null;

        public static string UnavailableReason(Material material, string property)
        {
            if (material == null) return "The material is no longer available.";
            if (string.IsNullOrEmpty(property) || !material.HasProperty(property)) return material.name + ": texture slot is unavailable.";
            var texture = material.GetTexture(property);
            if (texture == null || texture.dimension != TextureDimension.Tex2D) return material.name + ": assign a 2D texture to preview this slot.";
            int uv = MaterialUV(material, property);
            return uv < 0 || uv > 3 ? material.name + ": this texture mapping is not supported by the preview yet." : null;
        }

        public static void Toggle(Material material, string property, string caption, int channel = 0)
            => Toggle(new[] { material }, property, caption, channel);

        public static void Toggle(Material[] materials, string property, string caption, int channel = 0)
        {
            if (materials == null || materials.Length == 0) return;
            var owners = materials.Distinct().ToArray();
            if (owners.All(m => IsActive(m, property)))
            {
                foreach (var material in owners) Remove(material);
                return;
            }
            // Validate the whole selection before replacing any active entry.
            foreach (var material in owners)
            {
                var reason = UnavailableReason(material, property);
                if (reason == null) continue;
                var view = _view != null ? _view : SceneView.lastActiveSceneView;
                if (view != null) view.ShowNotification(new GUIContent(reason));
                return;
            }
            if (!EnsureSession()) return;
            foreach (var material in owners) Add(material, property, channel);
            RefreshRenderers(); _view.Focus(); _view.Repaint();
        }

        public static void SetChannel(Material material, string property, int channel)
        {
            if (!IsActive(material, property)) return;
            Entries[material].Channel = Mathf.Clamp(channel, 0, 5);
            _view.Repaint();
        }

        public static bool Start(Material material, string property, string caption = null, int channel = 0)
        {
            if (!CanPreview(material, property) || !EnsureSession()) return false;
            Add(material, property, channel);
            RefreshRenderers(); _view.Focus(); _view.Repaint();
            return true;
        }

        static bool EnsureSession()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                Debug.LogWarning("Scene texture inspection currently supports the Built-in Render Pipeline.");
                return false;
            }
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported) return false;
            if (_commands != null && (!Active || _stage != StageUtility.GetCurrentStageHandle())) Stop();
            if (_commands == null)
            {
                _view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                _stage = StageUtility.GetCurrentStageHandle();
                _commands = new CommandBuffer { name = "Thry Scene Texture Inspection" };
            }
            return true;
        }

        static void Add(Material material, string property, int channel)
        {
            ClearCommands();
            if (!Entries.TryGetValue(material, out var entry))
            {
                entry = new Entry { Source = material, Name = material.name,
                    Preview = new Material(Shader.Find(ShaderName)) { hideFlags = HideFlags.HideAndDontSave, name = "Scene texture inspection (temporary)" } };
                Entries.Add(material, entry);
            }
            entry.Property = property; entry.Channel = Mathf.Clamp(channel, 0, 5);
            entry.LastTexture = null; entry.NextNormalCheck = 0;
        }

        static void ClearCommands()
        {
            Detach(); _commands?.Clear(); _drawCount = 0;
        }

        static void Remove(Material material)
        {
            if (!Entries.TryGetValue(material, out var entry)) return;
            ClearCommands();
            Entries.Remove(material);
            if (entry.Preview != null) UnityEngine.Object.DestroyImmediate(entry.Preview);
            if (Entries.Count == 0) Stop();
            else if (_view != null) _view.Repaint();
        }

        public static void Stop()
        {
            ClearCommands();
            _commands?.Release(); _commands = null;
            foreach (var entry in Entries.Values)
                if (entry.Preview != null) UnityEngine.Object.DestroyImmediate(entry.Preview);
            Entries.Clear(); Renderers.Clear();
            _overlay?.RemoveFromHierarchy(); _overlay = null; _overlayCount = null;
            if (_view != null) _view.Repaint();
            _view = null;
        }

        static void PruneInvalidSources()
        {
            InvalidSources.Clear();
            foreach (var pair in Entries)
            {
                if (CanPreview(pair.Value.Source, pair.Value.Property)) continue;
                if (_view != null) _view.ShowNotification(new GUIContent("Stopped texture preview for " + pair.Value.Name + ". "
                    + UnavailableReason(pair.Value.Source, pair.Value.Property)));
                InvalidSources.Add(pair.Key);
            }
            foreach (var material in InvalidSources) Remove(material);
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
            if (!Active || EditorApplication.isPlayingOrWillChangePlaymode
                || _stage != StageUtility.GetCurrentStageHandle())
            { Stop(); return; }
            PruneInvalidSources();
            if (!Active) return;
            if (EditorApplication.timeSinceStartup - _lastRepaint < 1.0 / 30) return;
            _lastRepaint = EditorApplication.timeSinceStartup;
            _view.Repaint();
        }

        static void Detach()
        {
            if (_attachedCamera != null && _commands != null) _attachedCamera.RemoveCommandBuffer(DrawEvent, _commands);
            _attachedCamera = null;
        }

        static void Prepare(Entry entry)
        {
            int uv = MaterialUV(entry.Source, entry.Property);
            var texture = entry.Source.GetTexture(entry.Property);
            if (texture != entry.LastTexture || EditorApplication.timeSinceStartup >= entry.NextNormalCheck)
            {
                entry.LastTexture = texture; entry.NormalMap = RetainedTexturePreview.IsNormalMap(texture);
                entry.NextNormalCheck = EditorApplication.timeSinceStartup + 1;
            }
            entry.Preview.SetTexture("_ThryInspectTex", texture);
            var scale = entry.Source.GetTextureScale(entry.Property); var offset = entry.Source.GetTextureOffset(entry.Property);
            entry.Preview.SetVector("_ThryInspectST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            var pan = entry.Source.HasProperty(entry.Property + "Pan") ? entry.Source.GetVector(entry.Property + "Pan") : Vector4.zero;
            entry.Preview.SetVector("_ThryInspectPan", pan);
            entry.Preview.SetFloat("_ThryInspectUV", uv);
            entry.Preview.SetVector("_ThryInspectUVTiling", Vector(entry.Source, "_UVSettingsTiling" + uv, new Vector4(1, 1, 0, 0)));
            entry.Preview.SetVector("_ThryInspectUVOffset", Vector(entry.Source, "_UVSettingsOffset" + uv, Vector4.zero));
            entry.Preview.SetVector("_ThryInspectUVPan", Vector(entry.Source, "_UVSettingsPan" + uv, Vector4.zero));
            entry.Preview.SetFloat("_ThryInspectUVAngle", Number(entry.Source, "_UVSettingsAngle" + uv));
            entry.Preview.SetFloat("_ThryInspectUVRotate", Number(entry.Source, "_UVSettingsRotate" + uv));
            entry.Preview.SetFloat("_ThryInspectShiftBackface", Number(entry.Source, "_UVSettingsShiftBackfaceUV"));
            entry.Preview.SetFloat("_ThryInspectTimeSource", Number(entry.Source, "_PoiTimeSource"));
            entry.Preview.SetFloat("_ThryInspectStochastic", Number(entry.Source, entry.Property + "Stochastic"));
            entry.Preview.SetFloat("_StochasticMode", entry.Source.HasProperty("_StochasticMode") ? Number(entry.Source, "_StochasticMode") : 2);
            entry.Preview.SetFloat("_StochasticDeliotHeitzDensity", Number(entry.Source, "_StochasticDeliotHeitzDensity"));
            entry.Preview.SetFloat("_StochasticHexGridDensity", Number(entry.Source, "_StochasticHexGridDensity"));
            entry.Preview.SetFloat("_StochasticHexRotationStrength", Number(entry.Source, "_StochasticHexRotationStrength"));
            entry.Preview.SetFloat("_StochasticHexFallOffContrast", Number(entry.Source, "_StochasticHexFallOffContrast"));
            entry.Preview.SetFloat("_StochasticHexFallOffPower", Number(entry.Source, "_StochasticHexFallOffPower"));
            entry.Preview.SetFloat("_ThryInspectNormal", entry.NormalMap ? 1 : 0);
            entry.Preview.SetFloat("_ThryInspectChannel", entry.Channel);
            entry.Preview.SetInt("_ThryInspectCull", entry.Source.HasProperty("_Cull") ? entry.Source.GetInt("_Cull") : (int)CullMode.Back);
        }

        static void BeforeCamera(Camera camera)
        {
            if (!Active || camera != _view.camera || camera.cameraType != CameraType.SceneView) return;
            if (_stage != StageUtility.GetCurrentStageHandle()) { Stop(); return; }
            ClearCommands();
            PruneInvalidSources();
            if (!Active) return;
            _commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            foreach (var entry in Entries.Values) Prepare(entry);
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
                    if (materials[slot] == null || mesh.subMeshCount == 0 || !Entries.TryGetValue(materials[slot], out var entry)) continue;
                    _commands.DrawRenderer(renderer, entry.Preview, Mathf.Min(slot, mesh.subMeshCount - 1), 0);
                    _drawCount++;
                }
            }
            // Keep this attached to its owning Scene camera until the next rebuild or Stop.
            // Removing it in onPostRender can precede execution of the late camera event.
            camera.AddCommandBuffer(DrawEvent, _commands); _attachedCamera = camera;
        }

        static VisualElement _overlay;
        static Label _overlayCount;

        static void UpdateOverlay()
        {
            if (_overlay == null || _overlay.parent != _view.rootVisualElement)
            {
                _overlay?.RemoveFromHierarchy();
                _overlay = new VisualElement { name = "thry-scene-texture-overlay" };
                RetainedWindow.Style(_overlay);
                _overlay.AddToClassList("thry-scene-texture-overlay");
                var icon = new Image { image = EditorGUIUtility.IconContent("scenevis_visible_hover").image,
                    scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("thry-scene-texture-overlay-icon"); _overlay.Add(icon);
                var title = new Label("Texture preview"); title.AddToClassList("thry-scene-texture-overlay-title"); _overlay.Add(title);
                _overlayCount = new Label(); _overlayCount.AddToClassList("thry-scene-texture-overlay-count"); _overlay.Add(_overlayCount);
                var divider = new VisualElement(); divider.AddToClassList("thry-scene-texture-overlay-divider"); _overlay.Add(divider);
                var clear = new Button(Stop) { text = "Clear all", name = "thry-scene-texture-clear", tooltip = "Stop all texture previews (Escape)" };
                _overlay.Add(clear);
                var shortcut = new Label("Esc"); shortcut.AddToClassList("thry-scene-texture-overlay-shortcut"); _overlay.Add(shortcut);
                _overlay.RegisterCallback<KeyDownEvent>(e => {
                    if (e.keyCode != KeyCode.Escape) return;
                    Stop(); e.StopPropagation();
                });
                _overlay.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                _overlay.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                _overlay.RegisterCallback<WheelEvent>(e => e.StopPropagation());
                _view.rootVisualElement.Add(_overlay);
            }
            _overlay.EnableInClassList("thry-light", !EditorGUIUtility.isProSkin);
            _overlay.EnableInClassList("thry-dark", EditorGUIUtility.isProSkin);
            string count = Count.ToString();
            if (_overlayCount.text != count)
            {
                _overlayCount.text = count;
                _overlayCount.tooltip = Count == 1 ? "1 material previewing" : count + " materials previewing";
            }
        }

        static void DrawGUI(SceneView view)
        {
            if (!Active || view != _view) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Stop(); e.Use(); return; }
            UpdateOverlay();
        }

        static Vector4 Vector(Material source, string property, Vector4 fallback) => source.HasProperty(property) ? source.GetVector(property) : fallback;
        static float Number(Material source, string property) => source.HasProperty(property) ? source.GetFloat(property) : 0;
    }
}
