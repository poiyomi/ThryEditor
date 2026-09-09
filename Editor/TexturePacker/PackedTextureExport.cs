#if UNITY_2021_3_OR_NEWER
using System;
using System.IO;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.TexturePacker
{
    internal static class PackedTextureExport
    {
        internal static Texture Save(Texture2D texture, string path, TexturePackerConfig config,
            Func<Texture2D, string, Texture> writer = null)
        {
            var asset = writer == null ? TextureHelper.SaveTextureAsPNG(texture, path) : writer(texture, path);
            if (asset == null) throw new IOException("No texture was imported at " + path + ". Materials still contain their unsaved previews.");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.streamingMipmaps = true;
                importer.crunchedCompression = Config.Instance.inlinePackerChrunchCompression;
                importer.sRGBTexture = config.FileOutput.ColorSpace == ColorSpace.Gamma;
                importer.filterMode = config.FileOutput.FilterMode;
                importer.alphaIsTransparency = config.FileOutput.AlphaIsTransparency;
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height)), 32, 16384);
                importer.SaveAndReimport();
                asset = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (asset == null) throw new IOException("The saved texture could not be reloaded at " + path + ".");
            }
            return asset;
        }

        internal static string Filename(Material material, string property)
        {
            string name = material.name + property;
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return name + ".png";
        }
    }
}
#endif
