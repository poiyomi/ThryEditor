using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.TexturePacker
{
    [Serializable]
    public class TexturePackerConfig
    {
        public int Version;
        public PackerSource[] Sources;
        public OutputTarget[] Targets;
        public List<Connection> Connections;
        public FileOutput FileOutput;
        public ImageAdjust ImageAdjust;

        public KernelPreset KernelPreset;
        public KernelSettings KernelSettings;
        public Vector2 ScrollPosition;

        public string Serialize()
        {
            var saved = JsonUtility.FromJson<TexturePackerConfig>(JsonUtility.ToJson(this));
            saved.Version = 2;
            foreach (var source in saved.Sources)
            {
                if (source.ImageTexture != null) source.CaptureImageIdentity();
                source.ImageTexture = null;
                source.ColorTexture = null; source.GradientTexture = null;
            }
            return "ThryTexturePackerConfig:" + JsonUtility.ToJson(saved);
        }

        [Serializable] sealed class LegacyReference { public int instanceID; }
        [Serializable] sealed class LegacySource { public LegacyReference ImageTexture; }
        [Serializable] sealed class LegacySources { public LegacySource[] Sources; }

        public static TexturePackerConfig Deserialize(string json)
        {
            if (json.StartsWith("ThryTexturePackerConfig:"))
            {
                string payload = json.Substring("ThryTexturePackerConfig:".Length);
                var config = JsonUtility.FromJson<TexturePackerConfig>(payload);
                var legacy = config.Version < 2 ? JsonUtility.FromJson<LegacySources>(payload) : null;
                for (int i = 0; i < config.Sources.Length; i++)
                {
                    var source = config.Sources[i];
                    if (config.Version < 2 && source.ImageTexture != null) source.CaptureImageIdentity();
                    if (!string.IsNullOrEmpty(source.ImageTextureGuid)) source.ResolveImageIdentity();
                    else if (legacy?.Sources != null && i < legacy.Sources.Length && legacy.Sources[i]?.ImageTexture?.instanceID != 0
                        && legacy.Sources[i]?.ImageTexture != null && source.ImageTexture == null) source.MissingImageReference = true;
                }
                return config;
            }
            return null;
        }

        public static TexturePackerConfig GetNewConfig()
        {
            TexturePackerConfig config = new TexturePackerConfig();
            config.Version = 2;
            config.Sources = new PackerSource[]
            {
                new PackerSource(),
                new PackerSource(),
                new PackerSource(),
                new PackerSource(),
            };
            config.Targets = new OutputTarget[]
        {
                new OutputTarget(fallback: 0),
                new OutputTarget(fallback: 0),
                new OutputTarget(fallback: 0),
                new OutputTarget(fallback: 1),
        };
            config.Connections = new List<Connection>();
            config.FileOutput = new FileOutput(
                saveFolder: "Assets/Textures/Packed",
                fileName: "output",
                saveType: SaveType.PNG,
                colorSpace: ColorSpace.Linear,
                filterMode: FilterMode.Bilinear,
                alphaIsTransparency: true,
                saveQuality: 75,
                resolution: new Vector2Int(16, 16)
            );
            config.ImageAdjust = new ImageAdjust();
            config.KernelPreset = KernelPreset.None;
            config.KernelSettings = null;
            config.ScrollPosition = NodeGUI.DefaultScrollPosition;
            return config;
        }

        public static bool TryGetFromTexture(Texture2D tex, out TexturePackerConfig config)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(path))
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    string json = importer.userData;
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            config = TexturePackerConfig.Deserialize(json);
                            if (config.Sources.Length > 0)
                            {
                                return true;
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            config = null;
            return false;
        }

        public void Fix()
        {
            foreach (var src in Sources)
            {
                src.FixImageTexture();
                src.UpdateGradientTexture(FileOutput.Resolution);
                src.UpdateColorTexture();
            }
        }

        public void RequireResolvedSources()
        {
            foreach (var source in Sources)
                if (source.InputType == InputType.Texture && source.ImageTexture == null && !string.IsNullOrEmpty(source.ImageTextureGuid)) source.ResolveImageIdentity();
            var missing = Sources.Select((source, index) => new { source, index })
                .Where(item => item.source.InputType == InputType.Texture && item.source.MissingImageReference)
                .Select(item => (item.index + 1).ToString()).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("Source " + string.Join(", ", missing)
                + " could not be restored. Choose its texture again or explicitly clear that source before saving.");
        }

        public void SaveToImporter(TextureImporter importer)
        {
            if (importer == null || this == null) return;
            importer.userData = this.Serialize();
            s_textureImporterList[importer.assetPath.Replace("Assets/", "")] = importer;
        }

        private static SortedList<string, TextureImporter> s_textureImporterList = new SortedList<string, TextureImporter>();
        private static string[] s_importerGuids = null;
        private static bool s_isLoadingImportersDone = false;
        private const int LOADING_BATCH_SIZE = 50;
        private static int s_currentLoadingIndex = 0;
        public static IList<TextureImporter> AssetImporters => s_textureImporterList.Values;
        public static IList<string> AssetNames => s_textureImporterList.Keys;

        public static bool AreImportersLoaded()
        {
            return s_isLoadingImportersDone;
        }

        public static void InvalidateImporterCache()
        {
            s_importerGuids = null;
            s_currentLoadingIndex = 0;
            s_isLoadingImportersDone = false;
            s_textureImporterList.Clear();
        }

        public static int GetImporterLoadingProgress(out int total)
        {
            total = s_importerGuids == null ? 0 : s_importerGuids.Length;
            return Mathf.Min(s_currentLoadingIndex, total);
        }

        public static void LoadImportersBatch()
        {
            if(!s_isLoadingImportersDone)
            {
                if (s_importerGuids == null)
                {
                    s_textureImporterList.Clear();
                    s_importerGuids = AssetDatabase.FindAssets("t:Texture2D");
                    s_currentLoadingIndex = 0;
                }

                for (int i = s_currentLoadingIndex; i < Mathf.Min(s_currentLoadingIndex + LOADING_BATCH_SIZE, s_importerGuids.Length); i++)
                {
                    string guid = s_importerGuids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        if (importer.userData.StartsWith("ThryTexturePackerConfig:"))
                        {
                            s_textureImporterList.Add(path.Replace("Assets/", ""), importer);
                        }
                    }
                }
                s_currentLoadingIndex += LOADING_BATCH_SIZE;
                if(s_currentLoadingIndex >= s_importerGuids.Length)
                {
                    s_isLoadingImportersDone = true;
                }
            }
        }
    }
}
