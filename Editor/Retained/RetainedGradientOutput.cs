#if UNITY_2021_3_OR_NEWER
using System;
using System.IO;
using Thry.ThryEditor.Helpers;
using Thry.ThryEditor.TexturePacker;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    internal static class RetainedGradientOutput
    {
        [Serializable] internal sealed class Metadata
        {
            public TextureData settings;
            public int direction;
        }
        const string MetadataPrefix = "gradient_texture_asset_";
        internal static Metadata Load(Texture texture)
        {
            string guid = texture == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
            if (string.IsNullOrEmpty(guid)) return null;
            string json = FileHelper.LoadValueFromFile(MetadataPrefix + guid, PATH.GRADIENT_INFO_FILE);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<Metadata>(json); }
            catch (ArgumentException) { return null; }
        }
        internal static Texture Save(Gradient gradient, TextureData settings, int direction, bool gamma, string path)
        {
            Texture2D texture = GradientEditor2.CreateTexture(gradient, settings.width, settings.height, direction);
            try
            {
                if (gamma)
                {
                    var converted = TextureHelper.ConvertToGamma(texture);
                    UnityEngine.Object.DestroyImmediate(texture); texture = converted;
                }
                AtomicTextureExport.Write(path, texture.EncodeToPNG(), assetPath =>
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null) throw new IOException("Could not import gradient texture at " + assetPath + ".");
                    importer.filterMode = settings.filterMode; importer.wrapMode = settings.wrapMode; importer.anisoLevel = settings.ansioLevel;
                    importer.sRGBTexture = gamma;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height)), 32, 16384);
                    var format = Config.Instance.gradientEditorCompressionOverwrite;
                    if (format != TextureImporterFormat.Automatic)
                        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "PC", overridden = true,
                            maxTextureSize = importer.maxTextureSize, format = format });
                    else importer.ClearPlatformTextureSettings("PC");
                    importer.SaveAndReimport();
                    return importer;
                });
                var asset = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (asset == null) throw new IOException("Could not reload gradient texture at " + path + ".");
                string guid = AssetDatabase.AssetPathToGUID(path);
                FileHelper.SaveValueToFile(guid, Parser.Serialize(gradient), PATH.GRADIENT_INFO_FILE);
                FileHelper.SaveValueToFile(MetadataPrefix + guid, JsonUtility.ToJson(new Metadata { settings = settings, direction = direction }), PATH.GRADIENT_INFO_FILE);
                return asset;
            }
            finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
#endif
