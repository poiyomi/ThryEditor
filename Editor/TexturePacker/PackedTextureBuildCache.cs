#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.TexturePacker
{
    // Repeated avatar clones can share an unchanged generated asset. Validate
    // Unity's dependency hash so user edits and importer changes invalidate it.
    internal static class PackedTextureBuildCache
    {
        struct Entry { internal string Path; internal Hash128 Revision; }
        static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        internal static string Key(Texture2D texture, TexturePackerConfig config)
        {
            using (var hash = SHA256.Create())
            {
                string pixels = Convert.ToBase64String(hash.ComputeHash(texture.EncodeToPNG()));
                var output = config.FileOutput;
                return pixels + "|" + output.ColorSpace + "|" + output.FilterMode + "|" + output.AlphaIsTransparency
                    + "|" + Config.Instance.inlinePackerChrunchCompression;
            }
        }
        internal static Texture Find(string key, string directory)
        {
            if (!Entries.TryGetValue(key, out var entry)) return null;
            if (!entry.Path.StartsWith(directory + "/", StringComparison.Ordinal)
                || AssetDatabase.GetAssetDependencyHash(entry.Path) != entry.Revision)
            { Entries.Remove(key); return null; }
            return AssetDatabase.LoadAssetAtPath<Texture>(entry.Path);
        }
        internal static void Remember(string key, Texture texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (Entries.Count >= 128) Entries.Clear();
            Entries[key] = new Entry { Path = path, Revision = AssetDatabase.GetAssetDependencyHash(path) };
        }
    }
}
#endif
