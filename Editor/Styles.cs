// Material/Shader Inspector for Unity 2017/2018
// Copyright (C) 2019 Thryrallo

using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
	public class Styles
	{
		// Monokai markings, with the same light-skin counterparts as the toolbar.
		public static Color AnimatedColor => InspectorTheme.Skin(0xa6e22e, 0x638f14);
		public static Color AnimatedRenamedColor => InspectorTheme.Skin(0xe6db74, 0x82721e);

		// Variant Stuff
		public static readonly GUIContent revertContent = EditorGUIUtility.TrTextContent("Revert");
		public static readonly GUIContent revertAllContent = EditorGUIUtility.TrTextContent("Revert all Overrides");
		public static readonly GUIContent lockContent = EditorGUIUtility.TrTextContent("Lock in children");
		public static readonly GUIContent lockOriginContent = EditorGUIUtility.TrTextContent("See lock origin");
		public static string revertMultiText = L10n.Tr("Revert on {0} Material(s)");
		public static string applyToMaterialText = L10n.Tr("Apply to Material '{0}'");
		public static string applyToVariantText = L10n.Tr("Apply as Override in Variant '{0}'");
		public static readonly GUIContent resetContent = EditorGUIUtility.TrTextContent("Reset");
	}

	public class Icons
	{
		public static readonly GUIStyle thryIcon = CreateIconStyle(LoadTextureByGUID(EditorGUIUtility.isProSkin ? RESOURCE_GUID.ICON_THRY : RESOURCE_GUID.ICON_THRY_INV));

		static GUIStyle CreateIconStyle(Texture2D texture)
		{
			return new GUIStyle()
			{
				stretchWidth = true,
				stretchHeight = true,
				fixedHeight = 0,
				fixedWidth = 0,
				normal = new GUIStyleState()
				{
					background = texture
				}
			};
		}

		private static Texture2D LoadTextureByGUID(string guid)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			if (path == null) return Texture2D.whiteTexture;
			return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
		}
	}
}
