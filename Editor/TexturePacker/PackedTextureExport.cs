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
                // The file keeps the sources' full resolution. Max Size follows the size the sources are used at, so
                // sources imported smaller than their files don't come out larger once merged.
                int size = Mathf.Max(texture.width, texture.height), used = 0;
                TextureImporter first = null;
                foreach (PackerSource source in config.Sources)
                {
                    if (source.InputType != InputType.Texture || source.Texture == null) continue;
                    if (!(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source.Texture)) is TextureImporter sourceImporter)) continue;
                    used = Mathf.Max(used, Mathf.Max(source.Texture.width, source.Texture.height));
                    if (first == null) first = sourceImporter;
                }
                if (used > 0 && !config.FileOutput.CustomResolution) size = Mathf.Min(size, used);
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(size), 32, 16384);
                // The first source also decides mipmaps, wrapping and compression, as it does color space and filtering
                if (first != null)
                {
                    importer.mipmapEnabled = first.mipmapEnabled;
                    importer.wrapMode = first.wrapMode;
                    importer.textureCompression = first.textureCompression;
                }
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
