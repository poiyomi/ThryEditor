using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    public class ShaderTextureProperty : ShaderProperty
    {
        public bool showFoldoutProperties = false;
        public bool hasFoldoutProperties = false;
        public bool hasScaleOffset = false;
        public string VRAMString = "";
        bool _isVRAMDirty = true;

        public ShaderTextureProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, int xOffset, string optionsRaw, bool hasScaleOffset, bool forceThryUI, int property_index) : base(shaderEditor, materialProperty, displayName, xOffset, optionsRaw, false, property_index)
        {
            _doCustomDrawLogic = forceThryUI;
            this.hasScaleOffset = hasScaleOffset;
            PropertyValueChanged += (PropertyValueEventArgs args) => _isVRAMDirty = true;
        }

        protected override void InitOptions()
        {
            base.InitOptions();
            this.hasFoldoutProperties = hasScaleOffset || DoReferencePropertiesExist;
        }

        void UpdateVRAM()
        {
            if (MaterialProperty.textureValue != null)
            {
                var details = TextureHelper.VRAM.CalcSize(MaterialProperty.textureValue);
                this.VRAMString = $"{TextureHelper.VRAM.ToByteString(details.size)}";
            }
            else
            {
                VRAMString = null;
            }
        }

        protected override void PreDraw()
        {
            DrawingData.CurrentTextureProperty = this;
            this._doCustomDrawLogic = _drawer == null;
            if (this._isVRAMDirty)
            {
                UpdateVRAM();
                _isVRAMDirty = false;
            }
        }

        bool _normalImportPending;

        internal TextureImporter[] NormalMapImportMismatches()
        {
            if (MyMaterialEditor == null || MaterialProperty == null) return new TextureImporter[0];
            string propertyName = MaterialProperty.name;
            return MyMaterialEditor.targets.OfType<Material>()
                .Where(material => material != null && MaterialProperty.targets.Contains(material)
                    && material.shader != null && material.HasProperty(propertyName))
                .Where(material => (material.shader.GetPropertyFlags(material.shader.FindPropertyIndex(propertyName))
                    & ShaderPropertyFlags.Normal) != 0)
                .Select(material => material.GetTexture(propertyName)).OfType<Texture2D>()
                .Select(texture => AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter)
                .Where(importer => importer != null && importer.textureType != TextureImporterType.NormalMap)
                .Distinct().ToArray();
        }

        bool CanFixNormalMapImport => MyMaterialEditor != null && !MyShaderUI.IsLockedMaterial
            && (Options.condition_enable == null || Options.condition_enable.Test());

        internal void FixNormalMapImport()
        {
            if (_normalImportPending || !CanFixNormalMapImport) return;
            var paths = NormalMapImportMismatches().Select(importer => importer.assetPath).ToArray();
            if (paths.Length == 0) return;
            _normalImportPending = true;
            // Reimport after IMGUI finishes and only while the textures are still assigned.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (!CanFixNormalMapImport) return;
                    foreach (var importer in NormalMapImportMismatches().Where(importer => paths.Contains(importer.assetPath)))
                    {
                        Undo.RecordObject(importer, "Fix normal map import");
                        // Setting textureType directly resets other import settings.
                        var settings = new TextureImporterSettings();
                        importer.ReadTextureSettings(settings);
                        settings.textureType = TextureImporterType.NormalMap;
                        importer.SetTextureSettings(settings);
                        importer.SaveAndReimport();
                    }
                }
                finally
                {
                    _normalImportPending = false;
                    _isVRAMDirty = true;
                    if (MyMaterialEditor != null) MyMaterialEditor.Repaint();
                }
            };
        }

        protected override void DrawInternal(GUIContent content, Rect? rect = null, bool useEditorIndent = false, bool isInHeader = false)
        {
            base.DrawInternal(content, rect, useEditorIndent, isInHeader);
            if (rect != null || (MyShader.GetPropertyFlags(ShaderPropertyIndex) & ShaderPropertyFlags.Normal) == 0
                || NormalMapImportMismatches().Length == 0) return;
            bool changed = GUI.changed;
            try
            {
                using (new EditorGUI.DisabledScope(_normalImportPending || !CanFixNormalMapImport))
                {
                    if (GUILib.TextureImportWarningBox("This texture is not marked as a Normal Map in its import settings."))
                        FixNormalMapImport();
                }
            }
            finally { GUI.changed = changed; }
        }

        protected override void DrawDefault()
        {
            Rect pos = GUILib.GetPropertyRect(XOffset, EditorGUIUtility.singleLineHeight);
            GUILib.ConfigTextureProperty(pos, MaterialProperty, Content, MyMaterialEditor, hasFoldoutProperties);
            DrawingData.LastGuiObjectRect = pos;
        }
    }

}