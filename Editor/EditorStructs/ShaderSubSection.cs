using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
	public class ShaderSubSection : ShaderGroup
	{
		const int BORDER_WIDTH = 2;
		const int HEADER_HEIGHT = InspectorTheme.SectionHeight;
		const int CHECKBOX_OFFSET = 20;
		const int CONTENT_PADDING = 2;
		const int CONTENT_RIGHT_PADDING = 2;

		public ShaderSubSection(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, int xOffset, string optionsRaw, int propertyIndex) : base(shaderEditor, prop, materialEditor, displayName, xOffset, optionsRaw, propertyIndex)
		{
		}

		protected override void DrawInternal(GUIContent content, Rect? rect = null, bool useEditorIndent = false, bool isInHeader = false)
		{
			if (Options.margin_top > 0)
			{
				GUILayoutUtility.GetRect(0, Options.margin_top);
			}

			ShaderProperty reference = Options.reference_property != null ? MyShaderUI.PropertyDictionary[Options.reference_property] : null;
			int sectionToggleWidth = SectionEditing.IsEditing(this) ? SlidingToggle.ReservedWidth : 0;
			bool has_header = string.IsNullOrWhiteSpace(this.Content.text) == false || reference != null || sectionToggleWidth > 0;

			int headerTextX = 18 + sectionToggleWidth;
			int height = (has_header ? HEADER_HEIGHT : 0) + 4;

			Rect border = EditorGUILayout.BeginVertical();
			float rightEdge = border.x + border.width - GUILib.SectionContentRightPadding - 3;
			border.x = GUILib.GetPropertyX(this.XOffset) - 2;
			border.width = rightEdge - border.x;
			border = new RectOffset(0, 0, -2, -2).Add(border);

			InspectorTheme.DrawSection(this, border, IsExpanded, has_header, true);
			Rect clickCheckRect = GUILayoutUtility.GetRect(0, height);
			if (reference != null)
			{
				EditorGUI.BeginChangeCheck();
				Rect referenceRect = new Rect(border.x + CHECKBOX_OFFSET + sectionToggleWidth, border.y + (HEADER_HEIGHT - 18) / 2, 18, 18);
				using (new SectionEditing.HeaderTintScope(this))
					reference.Draw(referenceRect, new GUIContent(), isInHeader: true, useEditorIndent: true);
				headerTextX = CHECKBOX_OFFSET + 20 + sectionToggleWidth;
				if (EditorGUI.EndChangeCheck() && Options.ref_float_toggles_expand)
				{
					IsExpanded = reference.MaterialProperty.GetNumber() == 1;
				}
			}

			Rect top_border = new Rect(border.x, border.y, border.width - 20, HEADER_HEIGHT);
			if (has_header)
			{
				Rect header_rect = new RectOffset(headerTextX, 0, 0, 0).Remove(top_border);
				using (new SectionEditing.HeaderTintScope(this))
					GUI.Label(header_rect, this.Content, InspectorTheme.Subsection);
			}

			// Draw menu icon
			if (has_header)
			{
				using (new SectionEditing.HeaderTintScope(this, graphics: true))
					DrawMenuIcon(border, Event.current);
			}

			if (sectionToggleWidth > 0)
				SectionEditing.DrawHeaderToggle?.Invoke(this, new Rect(border.x + 18, border.y + (HEADER_HEIGHT - 16) / 2, SlidingToggle.Width, 16));
			FoldoutArrow(top_border, Event.current);
			if (Event.current.type == EventType.MouseDown && clickCheckRect.Contains(Event.current.mousePosition))
			{
				IsExpanded = !IsExpanded;
				Event.current.Use();
			}

			if (IsExpanded)
			{
				GUILib.SectionContentPadding += CONTENT_PADDING;
				GUILib.SectionContentRightPadding += CONTENT_RIGHT_PADDING;
				EditorGUI.BeginDisabledGroup(DoDisableChildren);
				foreach (ShaderPart part in Children)
				{
					part.Draw();
				}
				EditorGUI.EndDisabledGroup();
				GUILib.SectionContentPadding -= CONTENT_PADDING;
				GUILib.SectionContentRightPadding -= CONTENT_RIGHT_PADDING;
			}
			EditorGUILayout.EndVertical();
		}

		private void DrawMenuIcon(Rect border, Event e)
		{
			Rect buttonRect = new Rect(border);
			buttonRect.x = border.x + border.width - 18;
			buttonRect.y = border.y + (HEADER_HEIGHT - 16) / 2;
			buttonRect.width = 16;
			buttonRect.height = 16;

			if (GUILib.Button(buttonRect, Icons.menu))
			{
				ShaderEditor.Input.Use();
				ShowContextMenu(buttonRect);
			}
		}

		private void ShowContextMenu(Rect position)
		{
			ShaderSubSection section = this;
			Material[] materials = ShaderEditor.Active.Materials;
			
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Reset"), false, delegate()
			{
				int undoGroup = Undo.GetCurrentGroup();
				section.CopyFrom(new Material(materials[0].shader), true);
				IEnumerable<Material> linked_materials = MaterialLinker.GetLinked(section.MaterialProperty);
				if (linked_materials != null)
					foreach (Material m in linked_materials)
						section.CopyTo(m, true);
				Undo.SetCurrentGroupName($"Reset {section.Content.text}");
				Undo.CollapseUndoOperations(undoGroup);
			});
			menu.DropDown(position);
		}
	}
}
