using System.Collections.Generic;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    public class CurveDrawer : MaterialPropertyDrawer
    {
        public AnimationCurve curve;
        public EditorWindow window;
        public Texture2D texture;
        public bool saved = true;
        public TextureData imageData;

        // Unity shares one drawer instance across every material using the shader, so the curve being
        // edited has to be tracked per material or material B is shown (and saves) material A's curve.
        private static readonly Dictionary<string, AnimationCurve> s_curvesByMaterialProperty = new Dictionary<string, AnimationCurve>();
        private string _curveKey;
        private Object[] _pendingTargets;
        private string _pendingPropertyName;

        public CurveDrawer()
        {
            curve = new AnimationCurve();
        }

        private void Init()
        {
            if (imageData == null)
            {
                if (ShaderEditor.Active.CurrentProperty.Options.texture == null)
                    imageData = new TextureData();
                else
                    imageData = ShaderEditor.Active.CurrentProperty.Options.texture;
            }
        }

        private static string GetCurveKey(MaterialProperty prop)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prop.targets[0]));
            if (string.IsNullOrEmpty(guid)) guid = prop.targets[0].GetObjectId().ToString();
            return guid + "_" + prop.name;
        }

        private void SelectCurveFor(MaterialProperty prop)
        {
            string key = GetCurveKey(prop);
            if (key == _curveKey) return;
            // A pending edit belongs to the previous material
            if (!saved) SavePending();
            _curveKey = key;
            if (!s_curvesByMaterialProperty.TryGetValue(key, out curve))
            {
                curve = new AnimationCurve();
                s_curvesByMaterialProperty[key] = curve;
            }
            saved = true;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {
            Init();
            SelectCurveFor(prop);
            Rect border_position = new Rect(position.x + EditorGUIUtility.labelWidth - GUILib.VALUE_FIELD_LABEL_OFFSET, position.y, position.width - EditorGUIUtility.labelWidth + GUILib.VALUE_FIELD_LABEL_OFFSET - GUILib.GetSmallTextureVRAMWidth(prop), position.height);

            EditorGUI.BeginChangeCheck();
            curve = EditorGUI.CurveField(border_position, curve);
            if (EditorGUI.EndChangeCheck())
            {
                s_curvesByMaterialProperty[_curveKey] = curve;
                UpdateCurveTexture(prop);
            }

            GUILib.SmallTextureProperty(position, prop, label, editor, DrawingData.CurrentTextureProperty.hasFoldoutProperties);

            CheckWindowForCurveEditor();

            if (window == null && !saved)
                Save(prop, _curveKey);
        }

        private void UpdateCurveTexture(MaterialProperty prop)
        {
            texture = Converter.CurveToTexture(curve, imageData);
            prop.textureValue = texture;
            saved = false;
            _pendingTargets = prop.targets;
            _pendingPropertyName = prop.name;
        }

        private void CheckWindowForCurveEditor()
        {
            string windowName = "";
            if (EditorWindow.focusedWindow != null)
                windowName = EditorWindow.focusedWindow.titleContent.text;
            bool isCurveEditor = windowName == "Curve";
            if (isCurveEditor)
                window = EditorWindow.focusedWindow;
        }

        private void Save(MaterialProperty prop, string curveKey)
        {
            // Named per material and property. AnimationCurve.GetHashCode is the native handle of the
            // shared instance, so using it as the file name made every material overwrite the same PNG.
            Texture saved_texture = TextureHelper.SaveTextureAsPNG(texture, PATH.TEXTURES_DIR + "/curves/" + curveKey + ".png", null);
            prop.textureValue = saved_texture;
            saved = true;
        }

        private void SavePending()
        {
            saved = true;
            if (_pendingTargets == null || texture == null) return;
            try
            {
                Texture saved_texture = TextureHelper.SaveTextureAsPNG(texture, PATH.TEXTURES_DIR + "/curves/" + _curveKey + ".png", null);
                foreach (Object target in _pendingTargets)
                {
                    Material material = target as Material;
                    if (material == null || !material.HasProperty(_pendingPropertyName) || material.GetTexture(_pendingPropertyName) != texture) continue;
                    Undo.RecordObject(material, "Save Curve");
                    material.SetTexture(_pendingPropertyName, saved_texture);
                    EditorUtility.SetDirty(material);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            _pendingTargets = null;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return base.GetPropertyHeight(prop, label, editor);
        }
    }

}
