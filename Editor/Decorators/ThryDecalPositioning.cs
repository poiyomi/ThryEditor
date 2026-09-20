using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Decorators
{
    public class ThryDecalPositioningDecorator : MaterialPropertyDrawer
    {
        string _texturePropertyName;
        string _uvIndexPropertyName;
        string _positionPropertyName;
        string _rotationPropertyName;
        string _scalePropertyName;
        string _offsetPropertyName;
        DecalSceneTool _sceneTool;

        public ThryDecalPositioningDecorator(string textureProp, string uvIndexPropertyName, string positionProp, string rotationProp, string scaleProp, string offsetProp)
        {
            _texturePropertyName = textureProp;
            _uvIndexPropertyName = uvIndexPropertyName;
            _positionPropertyName = positionProp;
            _rotationPropertyName = rotationProp;
            _offsetPropertyName = offsetProp;
            _scalePropertyName = scaleProp;
        }

        void CreateSceneTool()
        {
            DiscardSceneTool();
            _sceneTool = DecalSceneTool.Create(
                Selection.activeTransform.GetComponent<Renderer>(),
                ShaderEditor.Active.Materials[0],
                (int)ShaderEditor.Active.PropertyDictionary[_uvIndexPropertyName].MaterialProperty.GetNumber(),
                ShaderEditor.Active.PropertyDictionary[_positionPropertyName].MaterialProperty,
                ShaderEditor.Active.PropertyDictionary[_rotationPropertyName].MaterialProperty,
                ShaderEditor.Active.PropertyDictionary[_scalePropertyName].MaterialProperty,
                ShaderEditor.Active.PropertyDictionary[_offsetPropertyName].MaterialProperty);
        }

        void DiscardSceneTool(bool discardChanges = false)
        {
            if (_sceneTool != null)
            {
                _sceneTool.Deactivate(discardChanges);
                _sceneTool = null;
            }
        }

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            position = new RectOffset(0, 0, 0, 3).Remove(EditorGUI.IndentedRect(position));
            var bakePosition = position;
            position.height = EditorGUIUtility.singleLineHeight + 3;
            bakePosition.y += position.height + 3;
            bakePosition.height = EditorGUIUtility.singleLineHeight + 3;
            if (DecalBakeBridge.Available && _texturePropertyName.StartsWith("_DecalTexture", System.StringComparison.Ordinal))
            {
                using (new EditorGUI.DisabledScope(editor.targets.Length != 1))
                    if (GUI.Button(bakePosition, new GUIContent("Bake to Main Texture", "Preview and bake this decal using the original image files.")))
                    {
                        DiscardSceneTool(_sceneTool != null && _sceneTool.GetMode() == DecalSceneTool.Mode.Raycast);
                        var material = editor.target as Material;
                        EditorApplication.delayCall += () => { if (material != null) DecalBakeBridge.Open(material, _texturePropertyName); };
                    }
            }
            bool isInScene = Selection.activeTransform != null && Selection.activeTransform.GetComponent<Renderer>() != null;
            if (isInScene)
            {
                position.width /= 2;
                ButtonRaycast(position);
                position.x += position.width;
                ButtonSceneTools(position);
                if (_sceneTool != null)
                {
                    _sceneTool.SetMaterialProperties(
                        ShaderEditor.Active.PropertyDictionary[_positionPropertyName].MaterialProperty,
                        ShaderEditor.Active.PropertyDictionary[_rotationPropertyName].MaterialProperty,
                        ShaderEditor.Active.PropertyDictionary[_scalePropertyName].MaterialProperty,
                        ShaderEditor.Active.PropertyDictionary[_offsetPropertyName].MaterialProperty);
                    if(Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
                        DiscardSceneTool(true);
                }
            }
        }

        void ButtonRaycast(Rect r)
        {
            if (GUI.Button(r, "Raycast"))
            {
                if (_sceneTool != null && _sceneTool.GetMode() == DecalSceneTool.Mode.Raycast)
                {
                    DiscardSceneTool(true);
                }
                else
                {
                    CreateSceneTool();
                    _sceneTool.StartRaycastMode();
                }
            }
            if (_sceneTool != null && _sceneTool.GetMode() == DecalSceneTool.Mode.Raycast)
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), 0, 3);
        }

        void ButtonSceneTools(Rect r)
        {
            if (GUI.Button(r, "Scene Tools"))
            {
                if (_sceneTool != null && _sceneTool.GetMode() == DecalSceneTool.Mode.Handles)
                {
                    DiscardSceneTool();
                }
                else
                {
                    CreateSceneTool();
                    _sceneTool.StartHandleMode();
                }
            }
            if (_sceneTool != null && _sceneTool.GetMode() == DecalSceneTool.Mode.Handles)
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), 0, 3);
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDecorator(this);
            return (EditorGUIUtility.singleLineHeight + 6) * (DecalBakeBridge.Available && _texturePropertyName.StartsWith("_DecalTexture", System.StringComparison.Ordinal) ? 2 : 1);
        }
    }

}
