using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
	// [Helpbox(messageType)] - Standard helpbox
	// [Helpbox(messageType, minLines)] - Minimum height in lines
	// [Helpbox(messageType, minLines, iconType)] - 0=help (default), 1=thryEditor_Help, 2=thryEditor_Warning, 3=thryEditor_Danger, 4=thryEditor_Pass, 5=thryEditor_Fail; falls back to help
	// Without an iconType, Warning and Error boxes use the warning and danger icons.
	public class HelpboxDrawer : MaterialPropertyDrawer
	{
		readonly MessageType type;
		readonly int minLines;
		readonly int iconType;
		readonly bool hasIconType;
		Texture2D _customIcon;

		static readonly string[] _iconResourceNames =
		{
			"help",               // 0 - legacy fallback
			"thryEditor_Help",    // 1
			"thryEditor_Warning", // 2
			"thryEditor_Danger",  // 3
			"thryEditor_Pass",    // 4
			"thryEditor_Fail"     // 5
		};

		public HelpboxDrawer()
		{
			type = MessageType.Info;
			minLines = 0;
		}

		public HelpboxDrawer(float f)
		{
			type = (MessageType)(int)f;
			minLines = 0;
		}

		public HelpboxDrawer(float messageType, float minLines)
		{
			type = (MessageType)(int)messageType;
			this.minLines = (int)minLines;
		}

		public HelpboxDrawer(float messageType, float minLines, float iconType)
		{
			type = (MessageType)(int)messageType;
			this.minLines = (int)minLines;
			this.iconType = (int)iconType;
			hasIconType = true;
		}

	public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
	{
		if (_customIcon == null)
		{
			int idx;
			if (hasIconType) idx = (iconType >= 0 && iconType < _iconResourceNames.Length) ? iconType : 0;
			else idx = type == MessageType.Warning ? 2 : type == MessageType.Error ? 3 : 0;
			_customIcon = Resources.Load<Texture2D>(_iconResourceNames[idx]);
			if (_customIcon == null && idx != 0)
			{
				_customIcon = Resources.Load<Texture2D>(_iconResourceNames[0]);
			}
		}

		PropertyOptions options = ShaderEditor.Active?.CurrentProperty?.Options;

		int xOffset = ShaderEditor.Active?.CurrentProperty?.XOffset ?? 0;
		float leftX = GUILib.GetPropertyX(xOffset);
		float availableWidth = EditorGUIUtility.currentViewWidth - leftX - GUILib.SectionContentRightPadding - 1;
		GUIContent content = new GUIContent(label.text);

		// Calculate height using the actual text style and width (accounting for icon + padding)
		GUIStyle textStyle = new GUIStyle(GUI.skin.label);
		textStyle.wordWrap = true;
		textStyle.alignment = TextAnchor.MiddleLeft;
		float textWidth = availableWidth - 56;
		float textHeight = textStyle.CalcHeight(content, textWidth);
		float height = Mathf.Max(textHeight + 8, 40); // 4px padding top+bottom, min 40 for icon

		if (minLines > 0)
		{
			float lineHeight = textStyle.CalcHeight(new GUIContent("A"), textWidth);
			float minHeight = lineHeight * minLines + 8;
			height = Mathf.Max(height, minHeight);
		}

		Rect r = EditorGUILayout.GetControlRect(false, height);
		float rightEdge = r.x + r.width - GUILib.SectionContentRightPadding - 1;
		r.x = leftX;
		r.width = rightEdge - leftX;

		GUI.Box(r, GUIContent.none, GUI.skin.button);

		if (_customIcon != null)
		{
			Rect iconRect = new Rect(r.x + 5, r.y + (r.height - 32) * 0.5f, 32, 32);
			GUI.DrawTexture(iconRect, _customIcon);
		}

		Rect textRect = new Rect(r.x + 42, r.y + 4, r.width - 56, r.height - 8);
		GUI.Label(textRect, label.text, textStyle);
		
		if (options?.onClick != null && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
		{
			options.onClick.Perform(ShaderEditor.Active.Materials);
			Event.current.Use();
		}
		
		GUILayout.Space(4);
	}

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDrawer(this);
            return 0;
        }
    }

}