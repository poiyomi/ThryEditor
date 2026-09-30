// Material/Shader Inspector for Unity 2017/2018
// Copyright (C) 2019 Thryrallo

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public partial class GradientEditor : EditorWindow
    {
        public class GradientData
        {
            public bool UsePreviewTexture;
            public Texture PreviewTexture;
            public Gradient Gradient;
        }
        public static void Open(GradientData data, MaterialProperty prop, TextureData predefinedTextureSettings, bool force_texture_options = false, bool show_texture_options=true, ColorSpace colorSpace=ColorSpace.Linear)
        {
            GradientEditor window = (GradientEditor)EditorWindow.GetWindow(typeof(GradientEditor));
            // Ending the session can write to prop, so re-read it
            if (window.EndSession())
            {
                MaterialProperty refreshed = MaterialEditor.GetMaterialProperty(prop.targets, prop.name);
                refreshed.applyPropertyCallback = prop.applyPropertyCallback;
                prop = refreshed;
            }
            texture_settings_data = LoadTextureSettings(prop, predefinedTextureSettings, force_texture_options);
            data.Gradient = TextureHelper.GetGradient(prop.textureValue);
            data.UsePreviewTexture = true;
            window.titleContent = new GUIContent("Gradient '" +prop.name +"' of '"+ prop.targets[0].name + "'");
            window._colorSpace = colorSpace;
            window._previous_property_texture = prop.textureValue;
            window._prop = prop;
            window._data = data;
            window._show_texture_options = show_texture_options;
            window.minSize = new Vector2(350, 350);
            window.Show();
            window.CreateGUI();
        }

        private ColorSpace _colorSpace = ColorSpace.Linear;
        private GradientData _data;
        private MaterialProperty _prop;

        private bool _show_texture_options = true;

        private bool _gradient_has_been_edited = false;
        private Texture _previous_property_texture;

        private static TextureData LoadTextureSettings(MaterialProperty prop, TextureData predefinedTextureSettings, bool force_texture_options)
        {
            if (force_texture_options && predefinedTextureSettings != null)
                return predefinedTextureSettings;
            string json_texture_settings = FileHelper.LoadValueFromFile("gradient_texture_options_"+prop.name, PATH.PERSISTENT_DATA);
            if (json_texture_settings != null)
                return Parser.Deserialize<TextureData>(json_texture_settings);
            else if (predefinedTextureSettings != null)
                return predefinedTextureSettings;
            else
                return new TextureData();
        }
        private static TextureData texture_settings_data;
        private TextureData textureSettings
        {
            get
            {
                return texture_settings_data;
            }
        }

        public void OnDestroy()
        {
            EndSession();
        }

        private bool EndSession()
        {
            if (_data == null) return false;
            if (_gradient_has_been_edited)
            {
                if (_data.PreviewTexture != null && _data.PreviewTexture.GetType() == typeof(Texture2D))
                {
                    string file_name = GetGradientSavefileName(_prop.targets[0].name);
                    Texture2D finalGradientTexture = GetFinalGradientTexture();
                    Texture savedAsset = TextureHelper.SaveTextureAsPNG(finalGradientTexture, PATH.TEXTURES_DIR+"/Gradients/" + file_name, textureSettings);
                    FileHelper.SaveValueToFile(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(savedAsset)), Parser.Serialize(_data.Gradient), PATH.GRADIENT_INFO_FILE);
                    ApplyImporterSettings(savedAsset);
                    _prop.textureValue = savedAsset;
                }
            }else
            {
                if (_prop != null) _prop.textureValue = _previous_property_texture;
            }
            _data.UsePreviewTexture = false;
            _data = null;
            _prop = null;
            _gradient_has_been_edited = false;
            ShaderEditor.RepaintActive();
            return true;
        }

        private Texture2D GetFinalGradientTexture()
        {
            if(_colorSpace == ColorSpace.Gamma)
            {
                return TextureHelper.ConvertToGamma(_data.PreviewTexture as Texture2D);
            }
            return _data.PreviewTexture as Texture2D;
        }

        private string GetGradientSavefileName(string material_name)
        {
            // Gradient.GetHashCode repeats across gradients
            string hash = Guid.NewGuid().ToString("N");
            return GetGradientSavefileName(hash, material_name);
        }

        private string GetGradientSavefileName(string hash, string material_name)
        {
            Config config = Config.Instance;
            string ret = config.gradient_name;
            ret = Regex.Replace(ret, "<hash>", hash);
            ret = Regex.Replace(ret, "<material>", material_name);
            return ret;
        }

        private void ApplyImporterSettings(Texture texture)
        {
            TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.sRGBTexture = _colorSpace != ColorSpace.Linear;
            #if VRC_SDK_VRCSDK3
                importer.streamingMipmaps = true;
                importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            #endif
            if(Config.Instance.gradientEditorCompressionOverwrite != TextureImporterFormat.Automatic)
            {
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings()
                {
                    name = "PC",
                    overridden = true,
                    maxTextureSize = 2048,
                    format = Config.Instance.gradientEditorCompressionOverwrite
                });
            }
            importer.SaveAndReimport();
        }

        void OnGUI()
        {
            // The session doesn't survive script reloads
            if (_data == null || _prop == null) Close();
        }

        private void UpdateGradientPreviewTexture()
        {
            _data.PreviewTexture = Converter.GradientToTexture(_data.Gradient, textureSettings.width, textureSettings.height);
            textureSettings.ApplyModes(_data.PreviewTexture);
            _prop.textureValue = _data.PreviewTexture;
            _gradient_has_been_edited = true;
            ShaderEditor.RepaintActive();
        }

    }
}
