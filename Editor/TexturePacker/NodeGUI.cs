using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.TexturePacker
{
    public partial class NodeGUI : EditorWindow
    {
        static NodeGUI s_instance;
        const int MIN_WIDTH = 850;
        const int MIN_HEIGHT = 810;

        const string CHANNEL_PREVIEW_SHADER = "Hidden/Thry/ChannelPreview";

        [SerializeField] TexturePackerConfig _config;
        TextureImporter _associatedImporter;
        Texture2D _outputTexture;

        bool[] _channel_export = new bool[4] { true, true, true, true };

        public Action<Texture2D> OnSave;
        public Action<Texture2D, TexturePackerConfig> OnChange;

        static Material s_channelPreviewMaterial;
        static Material ChannelPreviewMaterial
        {
            get
            {
                if (s_channelPreviewMaterial == null)
                {
                    s_channelPreviewMaterial = new Material(Shader.Find(CHANNEL_PREVIEW_SHADER));
                }
                return s_channelPreviewMaterial;
            }
        }

        [MenuItem("Assets/Thry/Textures/Open in Texture Packer")]
        public static void OpenTexturePackerWithOneTexture()
        {
            Open(Selection.activeObject as Texture2D);
        }

        [MenuItem("Assets/Thry/Textures/Open in Texture Packer", true)]
        public static bool OpenTexturePackerWithOneTextureValidate()
        {
            return Selection.activeObject is Texture2D;
        }

        public static NodeGUI Open()
        {
            var config = TexturePackerConfig.GetNewConfig();
            config.FileOutput.AlphaIsTransparency = false;
            return ShowWindow().InitilizeWithData(config);
        }

        public static NodeGUI Open(Texture2D tex)
        {
            if (TexturePackerConfig.TryGetFromTexture(tex, out TexturePackerConfig config))
            {
                return ShowWindow().InitilizeWithData(config);
            }
            return ShowWindow().InitilizeWithOneTexture(tex);
        }

        public static NodeGUI Open(TexturePackerConfig config)
        {
            return ShowWindow().InitilizeWithData(config);
        }

        NodeGUI InitilizeWithData(TexturePackerConfig config, TextureImporter importer = null)
        {
            // The window owns generated sources in its draft; callers retain
            // ownership of their configuration and any existing source textures.
            var draft = JsonUtility.FromJson<TexturePackerConfig>(JsonUtility.ToJson(config));
            foreach (var source in draft.Sources) { source.GradientTexture = null; source.ColorTexture = null; }
            draft.Fix();
            _graph?.Dispose(); _graph = null;
            _retainedPreview = null;
            DisposeGeneratedSources();
            _config = draft;
            _associatedImporter = importer;
            Packer.DeterminePathAndFileNameIfEmpty(_config);
            Packer.DetermineOutputResolution(_config);
            TryStudioAction(Pack);
            CreateGUI();
            return this;
        }

        NodeGUI InitilizeWithOneTexture(Texture2D texture)
        {
            var config = TexturePackerConfig.GetNewConfig();
            config.Sources[0].SetInputTexture(texture);
            config.Sources[1].SetInputTexture(texture);
            config.Sources[2].SetInputTexture(texture);
            config.Sources[3].SetInputTexture(texture);
            // Add connections
            config.Connections.Add(new Connection(0, TextureChannelIn.R, TextureChannelOut.R));
            config.Connections.Add(new Connection(1, TextureChannelIn.G, TextureChannelOut.G));
            config.Connections.Add(new Connection(2, TextureChannelIn.B, TextureChannelOut.B));
            config.Connections.Add(new Connection(3, TextureChannelIn.A, TextureChannelOut.A));
            Packer.DeterminePathAndFileNameIfEmpty(config, true);
            Packer.DetermineImportSettings(config);
            return InitilizeWithData(config);
        }

        static NodeGUI ShowWindow()
        {
            s_instance = (NodeGUI)GetWindow(typeof(NodeGUI));
            s_instance.minSize = new Vector2(MIN_WIDTH, MIN_HEIGHT);
            s_instance.titleContent = new GUIContent("Thry Texture Packer");
            s_instance.OnSave = null; // clear save callback
            s_instance.OnChange = null; // clear save callback
            return s_instance;
        }

        public static Vector2 DefaultScrollPosition = new Vector2(0, 115);
        private void OnGUI()
        {
            if (rootVisualElement.childCount == 0) CreateGUI();
        }

        void ShowLoadPreviousProjectDropdown(Rect anchor)
        {
            if (!TexturePackerConfig.AreImportersLoaded())
            {
                // Avoid performance regression loophole: DO NOT scan every Texture2D in the project
                // on every OnGUI frame via LoadImportersBatch() + Repaint(), just to populate a
                // "Load previous project" dropdown the user almost never opens. In a project with
                // thousands of AssetImporter.GetAtPath() calls per second, it makes Unity's importer
                // lag for god knows how long. This is especially evident on weaker PCs!
                try
                {
                    while (!TexturePackerConfig.AreImportersLoaded())
                    {
                        int loaded = TexturePackerConfig.GetImporterLoadingProgress(out int total);
                        float progress = total == 0 ? 0f : (float)loaded / total;
                        if (EditorUtility.DisplayCancelableProgressBar("Thry Texture Packer", total == 0 ? "Scanning project for previous packer configs…" : $"Scanning textures ({loaded}/{total})", progress)) break;
                        TexturePackerConfig.LoadImportersBatch();
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            GenericMenu menu = new GenericMenu();
            IList<string> names = TexturePackerConfig.AssetNames;
            IList<TextureImporter> importers = TexturePackerConfig.AssetImporters;
            if (names.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No previous packer configs found"));
            }
            else
            {
                for (int i = 0; i < names.Count; i++)
                {
                    int captured = i;
                    bool isCurrent = importers[i] == _associatedImporter;
                    menu.AddItem(new GUIContent(names[i]), isCurrent, () =>
                    {
                        TextureImporter importer = TexturePackerConfig.AssetImporters[captured];
                        TexturePackerConfig newConfig = TexturePackerConfig.Deserialize(importer.userData);
                        try
                        {
                            InitilizeWithData(newConfig, importer);
                        }
                        catch (Exception e)
                        {
                            ThryLogger.LogErr("TexturePacker", $"Could not correctly load config from {TexturePackerConfig.AssetNames[captured]}: {e.Message}");
                        }
                    });
                }
            }
            menu.DropDown(anchor);
        }

        void Pack()
        {
            Texture2D oldOutput = _outputTexture;
            _outputTexture = Packer.Pack(_config);
            if (oldOutput != null)
                UnityEngine.Object.DestroyImmediate(oldOutput);

            _retainedError = null;
            UpdateRetainedPreview();

            if (OnChange != null) OnChange(_outputTexture, _config);
        }

        void DisposeGeneratedSources()
        {
            if (_config?.Sources == null) return;
            foreach (var source in _config.Sources) source?.DisposeGeneratedTextures();
        }

        void OnDisable() { ReleaseOwnedTextures(); }
        void OnDestroy() { ReleaseOwnedTextures(); }

        void ReleaseOwnedTextures()
        {
            Undo.undoRedoPerformed -= RestoreStudioGraph;
            _graph?.Dispose(); _graph = null;
            _pendingPack?.Pause();
            if (_retainedChannelPreview != null) { _retainedChannelPreview.Release(); DestroyImmediate(_retainedChannelPreview); }
            if (_outputTexture != null)
                UnityEngine.Object.DestroyImmediate(_outputTexture);
            _outputTexture = null;
            DisposeGeneratedSources();
            if (s_channelPreviewMaterial != null) UnityEngine.Object.DestroyImmediate(s_channelPreviewMaterial);
            s_channelPreviewMaterial = null;
        }

        void ExportChannels(bool exportAsBlackAndWhite)
        {
            Pack();
            Packer.DeterminePathAndFileNameIfEmpty(_config);
            Packer.ExportChannels(_outputTexture, _config, _channel_export, exportAsBlackAndWhite);
        }

        public class TextureChangeHandler : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                if (importedAssets.Length > 0 || deletedAssets.Length > 0 || movedAssets.Length > 0)
                {
                    TexturePackerConfig.InvalidateImporterCache();
                }

                if (s_instance == null || s_instance._config?.Sources == null) return;

                string[] active_textures = s_instance._config.Sources
                    .Where(source => source.InputType == InputType.Texture && source.Texture != null)
                    .Select(source => AssetDatabase.GetAssetPath(source.Texture))
                    .ToArray();

                if (importedAssets.Any(path => active_textures.Contains(path, StringComparer.OrdinalIgnoreCase)))
                {
                    ThryLogger.Log("TexturePacker", "Detected external texture change, repacking texture.");
                    s_instance.TryStudioAction(s_instance.Pack);
                    s_instance.Repaint();
                }
            }
        }
            
    }
}
