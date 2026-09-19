using System;
using System.Collections.Generic;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    [Serializable]
    public class TextureData
    {
        public string name = null;
        public string guid = null;
        public int width = 128;
        public int height = 128;

        public char channel = 'r';

        public int ansioLevel = 1;
        public FilterMode filterMode = FilterMode.Bilinear;
        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
        public bool center_position = false;

        public void ApplyModes(Texture texture)
        {
            texture.filterMode = filterMode;
            texture.wrapMode = wrapMode;
            texture.anisoLevel = ansioLevel;
        }
        public void ApplyModes(string path)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = filterMode;
            importer.wrapMode = wrapMode;
            importer.anisoLevel = ansioLevel;
            importer.SaveAndReimport();
        }

        static Dictionary<string, Texture> s_loaded_textures = new Dictionary<string, Texture>();
        public Texture loaded_texture
        {
            get
            {
                if (guid != null)
                {
                    if (!s_loaded_textures.ContainsKey(guid) || s_loaded_textures[guid] == null)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (path != null)
                            s_loaded_textures[guid] = AssetDatabase.LoadAssetAtPath<Texture>(path);
                        else
                            s_loaded_textures[guid] = Texture2D.whiteTexture;
                    }
                    return s_loaded_textures[guid];
                }
                else if (name != null)
                {
                    if (!s_loaded_textures.ContainsKey(name) || s_loaded_textures[name] == null)
                    {
                        string path = FileHelper.FindFile(name, "texture");
                        if (path != null)
                            s_loaded_textures[name] = AssetDatabase.LoadAssetAtPath<Texture>(path);
                        else
                            s_loaded_textures[name] = Texture2D.whiteTexture;
                    }
                    return s_loaded_textures[name];
                }
                return Texture2D.whiteTexture;
            }
        }

        private static TextureData ParseForThryParser(string s)
        {
            s = s.Trim(' ', '"');
            if (s.StartsWith("{") == false)
            {
                return new TextureData()
                {
                    name = s
                };
            }
            return Parser.Deserialize<TextureData>(s);
        }

    }

}
