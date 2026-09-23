using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
	public class ThryMultiFloatButtonsDrawer : MaterialPropertyDrawer
	{
		string[] _labels = new string[4];
		string[] _otherProperties = new string[3];
		MaterialProperty[] _otherMaterialProps = new MaterialProperty[3];

		static string FormatUdimLabel(string raw)
		{
			if (string.IsNullOrEmpty(raw)) return raw;

			if (!(raw[0] == 'u' || raw[0] == 'U')) return raw;

			int vIndex = -1;
			for (int i = 1; i < raw.Length; i++)
			{
				char c = raw[i];
				if (c == 'v' || c == 'V')
				{
					vIndex = i;
					break;
				}
			}

			if (vIndex <= 1 || vIndex >= raw.Length - 1) return raw;

			string aStr = raw.Substring(1, vIndex - 1);
			string bStr = raw.Substring(vIndex + 1);

			if (!int.TryParse(aStr, out int a)) return raw;
			if (!int.TryParse(bStr, out int b)) return raw;

			return $"{a}, {b}";
		}

		public ThryMultiFloatButtonsDrawer(string label0, string label1, string label2, string label3, string prop1, string prop2, string prop3)
		{
			_labels[0] = label0;
			_labels[1] = label1;
			_labels[2] = label2;
			_labels[3] = label3;
			_otherProperties[0] = prop1;
			_otherProperties[1] = prop2;
			_otherProperties[2] = prop3;
		}

		public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
		{
			for (int i = 0; i < _otherProperties.Length; i++)
			{
				if (!ShaderEditor.Active.PropertyDictionary.TryGetValue(_otherProperties[i], out var sProp))
				{
					_otherMaterialProps[i] = null;
					continue;
				}
				sProp.UpdatedMaterialPropertyReference();
				_otherMaterialProps[i] = sProp.MaterialProperty;
			}

			Rect labelR = new Rect(position);
			labelR.width = EditorGUIUtility.labelWidth;
			GUIContent labelContent = label != null ? new GUIContent(label.text, TileLabelUtility.ROW_TOOLTIP) : new GUIContent(string.Empty, TileLabelUtility.ROW_TOOLTIP);
			Rect fieldR = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), labelContent);

			float spacing = 4f;
			float buttonWidth = (fieldR.width - spacing * 3) / 4;

			MaterialProperty[] props = new MaterialProperty[4];
			string[] propNames = new string[4];
			props[0] = prop;
			propNames[0] = prop.name;
			for (int i = 0; i < 3; i++)
			{
				props[i + 1] = _otherMaterialProps[i];
				propNames[i + 1] = _otherProperties[i];
			}

			Material refMat = editor.target as Material;

			bool anyChanged = false;
			using (new GUILib.IndentOverrideScope(0))
			{
				for (int i = 0; i < 4; i++)
				{
					if (props[i] == null) continue;

					Rect buttonRect = new Rect(fieldR.x + i * (buttonWidth + spacing), fieldR.y, buttonWidth, fieldR.height);

					// Right-click rename on UDIM tile buttons. No-op on non-UDIM properties.
					string defaultLabel = FormatUdimLabel(_labels[i]);
					TileLabelUtility.HandleRightClick(buttonRect, editor.targets, propNames[i], defaultLabel);

					string tagLabel = TileLabelUtility.GetTileLabel(refMat, propNames[i]);
					string buttonText = !string.IsNullOrEmpty(tagLabel) ? tagLabel : defaultLabel;

					using (new GUILib.AnimationScope(editor, props[i]))
					{
						bool on = props[i].floatValue > 0.5f;
						EditorGUI.showMixedValue = props[i].hasMixedValue;
						bool newOn = GUI.Toggle(buttonRect, on, buttonText, "Button");
						EditorGUI.showMixedValue = false;

						if (newOn != on)
						{
							props[i].floatValue = newOn ? 1f : 0f;
							anyChanged = true;
							if (TileLabelUtility.IsUdimProperty(prop.name) && ShaderEditor.Active.IsInAnimationMode
								&& ShaderEditor.Active.PropertyDictionary.TryGetValue(propNames[i], out var animatedTile) && !animatedTile.IsAnimated)
								animatedTile.SetAnimated(true, false);

							// Force other properties to invalidate their cached default value check
							// so the main property's IsPropertyValueDefault will be recalculated
							if (i > 0 && ShaderEditor.Active.PropertyDictionary.TryGetValue(_otherProperties[i - 1], out var changedProp))
							{
								changedProp.CheckForValueChange();
							}
						}
					}
				}
			}

			if (anyChanged && !TileLabelUtility.IsUdimProperty(prop.name) && ShaderEditor.Active.IsInAnimationMode && !ShaderEditor.Active.CurrentProperty.IsAnimated)
				ShaderEditor.Active.CurrentProperty.SetAnimated(true, false);

			// The UV grid owns animation per tile. A legacy redraw must not copy
			// column zero's state over the other three tiles.
			if (TileLabelUtility.IsUdimProperty(prop.name)) return;

			bool animated = ShaderEditor.Active.CurrentProperty.IsAnimated;
			bool renamed = ShaderEditor.Active.CurrentProperty.IsRenaming;
			for (int i = 0; i < _otherProperties.Length; i++)
			{
				if (ShaderEditor.Active.PropertyDictionary.TryGetValue(_otherProperties[i], out var sProp))
					sProp.SetAnimated(animated, renamed);
			}
		}

		public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
		{
			ShaderProperty.RegisterDrawer(this);

			// Register additional properties for default value checking (for the * indicator feature)
			// Must be done here (not in OnGUI) because Content is accessed before OnGUI runs
			if (ShaderEditor.Active?.PropertyDictionary != null)
			{
				if (ShaderEditor.Active.PropertyDictionary.TryGetValue(prop.name, out var mainShaderProp))
				{
					mainShaderProp.AdditionalDefaultCheckProperties = _otherProperties;
				}
			}
			return base.GetPropertyHeight(prop, label, editor);
		}
	}
}

