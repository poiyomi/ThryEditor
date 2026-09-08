// Material/Shader Inspector for Unity 2017/2018
// Copyright (C) 2019 Thryrallo

using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
	public class Styles
	{
		private static GUIStyle _masterLabel;
		public static GUIStyle masterLabel
		{
			get
			{
				if (_masterLabel == null)
					_masterLabel = new GUIStyle(EditorStyles.label) { richText = true, fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
				return _masterLabel;
			}
		}

		private static GUIStyle _editorHeaderLabel;
		public static GUIStyle editorHeaderLabel
		{
			get
			{
				if (_editorHeaderLabel == null)
					_editorHeaderLabel = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
				return _editorHeaderLabel;
			}
		}

		private static GUIStyle _dropdownHeader;
		public static GUIStyle dropdownHeader
		{
			get
			{
				if (_dropdownHeader == null)
				{
					_dropdownHeader = new GUIStyle(new GUIStyle("ShurikenModuleTitle"))
					{
						font = new GUIStyle(EditorStyles.label).font,
						fontSize = GUI.skin.font.fontSize,
						border = new RectOffset(15, 7, 4, 4),
						fixedHeight = 22,
						contentOffset = new Vector2(20f, -2f)
					};
				}
				return _dropdownHeader;
			}
		}

		public static GUIStyle flatHeader
		{
			get => InspectorTheme.Header;
		}

		public static Color AnimatedColor => EditorGUIUtility.isProSkin ? new Color(0.3f, 1f, 0.3f) : new Color(0f, 0.5f, 0f);
		public static Color AnimatedRenamedColor => EditorGUIUtility.isProSkin ? new Color(1f, 0.85f, 0.2f) : new Color(0.55f, 0.4f, 0f);

		private static GUIStyle _animatedIndicatorStyle;
		public static GUIStyle animatedIndicatorStyle
		{
			get
			{
				if (_animatedIndicatorStyle == null)
				{
					_animatedIndicatorStyle = new GUIStyle()
					{
						normal = new GUIStyleState() { textColor = AnimatedColor },
						alignment = TextAnchor.MiddleRight
					};
				}
				return _animatedIndicatorStyle;
			}
		}

		private static GUIStyle _animatedRenamedIndicatorStyle;
		public static GUIStyle animatedRenamedIndicatorStyle
		{
			get
			{
				if (_animatedRenamedIndicatorStyle == null)
				{
					_animatedRenamedIndicatorStyle = new GUIStyle()
					{
						normal = new GUIStyleState() { textColor = AnimatedRenamedColor },
						alignment = TextAnchor.MiddleRight
					};
				}
				return _animatedRenamedIndicatorStyle;
			}
		}

		private static GUIStyle _headerAnimatedDotStyle;
		public static GUIStyle headerAnimatedDotStyle
		{
			get
			{
				if (_headerAnimatedDotStyle == null)
				{
					_headerAnimatedDotStyle = new GUIStyle()
					{
						fontSize = 9,
						normal = new GUIStyleState() { textColor = AnimatedColor },
						alignment = TextAnchor.MiddleLeft
					};
				}
				return _headerAnimatedDotStyle;
			}
		}

		private static GUIStyle _headerAnimatedRenamedDotStyle;
		public static GUIStyle headerAnimatedRenamedDotStyle
		{
			get
			{
				if (_headerAnimatedRenamedDotStyle == null)
				{
					_headerAnimatedRenamedDotStyle = new GUIStyle()
					{
						fontSize = 9,
						normal = new GUIStyleState() { textColor = AnimatedRenamedColor },
						alignment = TextAnchor.MiddleLeft
					};
				}
				return _headerAnimatedRenamedDotStyle;
			}
		}

		private static GUIStyle _presetIndicatorStyle;
		public static GUIStyle presetIndicatorStyle
		{
			get
			{
				if (_presetIndicatorStyle == null)
				{
					_presetIndicatorStyle = new GUIStyle()
					{
						normal = new GUIStyleState() { textColor = EditorGUIUtility.isProSkin ? new Color(0f, 1f, 1f) : new Color(0f, 0.5f, 0.71f) },
						alignment = TextAnchor.MiddleRight
					};
				}
				return _presetIndicatorStyle;
			}
		}

		private static GUIStyle _madeByLabel;
		public static GUIStyle madeByLabel
		{
			get
			{
				if (_madeByLabel == null)
					_madeByLabel = new GUIStyle(EditorStyles.label) { fontSize = 10 };
				return _madeByLabel;
			}
		}

		private static GUIStyle _notification;
		public static GUIStyle notification
		{
			get
			{
				if (_notification == null)
					_notification = new GUIStyle(GUI.skin.box) { fontSize = 12, wordWrap = true, normal = new GUIStyleState() { textColor = Color.red } };
				return _notification;
			}
		}

		private static GUIStyle _label_property_note;
		public static GUIStyle label_property_note
		{
			get
			{
				if (_label_property_note == null)
				{
					_label_property_note = new GUIStyle(EditorStyles.label)
					{
						alignment = TextAnchor.MiddleRight,
						padding = new RectOffset(0, 0, 0, 0),
						normal = new GUIStyleState { textColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.6f) : new Color(0f, 0f, 0f, 1f) }
					};
				}
				return _label_property_note;
			}
		}

		// Built from boldLabel, not label + FontStyle.Bold: the editor font has no bold face for
		// Unity to synthesise from, so setting fontStyle on a label style silently does nothing.
		// Same reason ThryHeaderLabel, ThryHeader and ThryRichLabel all derive from boldLabel.
		private static GUIStyle _stencilBitDigit;
		public static GUIStyle stencilBitDigit
		{
			get
			{
				if (_stencilBitDigit == null)
					_stencilBitDigit = MakeBitLabelStyle(9);
				return _stencilBitDigit;
			}
		}

		private static GUIStyle _stencilBitSymbol;
		public static GUIStyle stencilBitSymbol
		{
			get
			{
				if (_stencilBitSymbol == null)
					_stencilBitSymbol = MakeBitLabelStyle(12);
				return _stencilBitSymbol;
			}
		}

		private static GUIStyle MakeBitLabelStyle(int fontSize)
		{
			GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = fontSize,
				wordWrap = false,
			};
			// boldLabel carries a content offset for its normal use as a row label. Left in place it
			// shifts every glyph off the centre of its cell.
			style.contentOffset = Vector2.zero;
			style.padding = new RectOffset(0, 0, 0, 0);
			style.margin = new RectOffset(0, 0, 0, 0);
			return style;
		}

		private static GUIStyle _stencilRowLabel;
		public static GUIStyle stencilRowLabel
		{
			get
			{
				if (_stencilRowLabel == null)
				{
					_stencilRowLabel = new GUIStyle(EditorStyles.label)
					{
						padding = new RectOffset(0, 0, 0, 0),
						margin = new RectOffset(0, 0, 0, 0),
						alignment = TextAnchor.MiddleLeft
					};
					// Row labels and decimal readouts are not interactive — suppress the
					// implicit hover tint EditorStyles.label carries.
					_stencilRowLabel.hover.textColor = _stencilRowLabel.normal.textColor;
				}
				return _stencilRowLabel;
			}
		}

		private static GUIStyle _stencilBitWeight;
		public static GUIStyle stencilBitWeight
		{
			get
			{
				if (_stencilBitWeight == null)
				{
					_stencilBitWeight = new GUIStyle(EditorStyles.label)
					{
						alignment = TextAnchor.UpperCenter,
						fontSize = 8,
					};
					_stencilBitWeight.normal.textColor = Colors.stencilBitWeightText;
					// Not interactive either - see stencilRowLabel.
					_stencilBitWeight.hover.textColor = _stencilBitWeight.normal.textColor;
				}
				return _stencilBitWeight;
			}
		}

		public static readonly GUIStyle vectorPropertyStyle = new GUIStyle() { padding = new RectOffset(0, 0, 2, 2) };
		public static readonly GUIStyle orangeStyle = new GUIStyle() { normal = new GUIStyleState() { textColor = new Color(0.9f, 0.5f, 0) } };
		public static readonly GUIStyle cyanStyle = new GUIStyle() { normal = new GUIStyleState() { textColor = Color.cyan } };
		public static readonly GUIStyle redStyle = new GUIStyle() { normal = new GUIStyleState() { textColor = Color.red } };
		public static readonly GUIStyle greenStyle = new GUIStyle() { normal = new GUIStyleState() { textColor = new Color(0, 0.5f, 0) } };

		private static GUIStyle _upperRight;
		public static GUIStyle upperRight
		{
			get
			{
				if (_upperRight == null)
					_upperRight = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.UpperRight };
				return _upperRight;
			}
		}

		private static GUIStyle _upperLeft_richText;
		public static GUIStyle upperLeft_richText
		{
			get
			{
				if (_upperLeft_richText == null)
					_upperLeft_richText = new GUIStyle(EditorStyles.label) { richText = true };
				return _upperLeft_richText;
			}
		}

		private static GUIStyle _upperLeft_richText_wordWrap;
		public static GUIStyle upperLeft_richText_wordWrap
		{
			get
			{
				if (_upperLeft_richText_wordWrap == null)
					_upperLeft_richText_wordWrap = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
				return _upperLeft_richText_wordWrap;
			}
		}

		private static GUIStyle _middleCenter_richText_wordWrap;
		public static GUIStyle middleCenter_richText_wordWrap
		{
			get
			{
				if (_middleCenter_richText_wordWrap == null)
					_middleCenter_richText_wordWrap = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter };
				return _middleCenter_richText_wordWrap;
			}
		}

		public static readonly GUIStyle padding2pxHorizontal1pxVertical = new GUIStyle() { padding = new RectOffset(2, 2, 1, 1) };

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

	public class Colors
	{
		public static readonly Color foreground = EditorGUIUtility.isProSkin ? new Color(0.8f, 0.8f, 0.8f) : Color.black;
		public static readonly Color backgroundDark = EditorGUIUtility.isProSkin ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.70f, 0.70f, 0.70f);
		public static readonly Color backgroundLight = EditorGUIUtility.isProSkin ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);

		// Drawer colors - centralized for consistency
		public static readonly Color graphBackground = new Color(0.16f, 0.16f, 0.16f, 1f);
		public static readonly Color graphGutter = new Color(0.1f, 0.1f, 0.1f, 1f);
		public static readonly Color handleOrange = new Color(1f, 0.6f, 0.2f, 1f);
		public static readonly Color handleBlue = new Color(0.4f, 0.8f, 1f, 1f);
		public static readonly Color borderDark = new Color(0, 0, 0, 0.6f);
		public static readonly Color referenceLine = new Color(0.3f, 0.3f, 0.3f, 0.5f);
		// Tint behind an expanded foldout body. Same value SmallTextureProperty uses for its border.
		public static readonly Color expandedFoldoutBackground = new Color(0, 0, 0, 0.1f);

		// Stencil calculator colors
		public static readonly Color stencilBufferValue = new Color(1f, 0.88f, 0.2f);
		public static readonly Color stencilReference = new Color(0.4f, 0.75f, 1f);
		public static readonly Color stencilMask = new Color(1f, 0.5f, 0.8f);
		public static readonly Color stencilOutput = new Color(0.45f, 1f, 0.7f);
		public static readonly Color stencilPass = new Color(0.35f, 0.9f, 0.35f);
		public static readonly Color stencilFail = new Color(1f, 0.55f, 0.2f);
		public static readonly Color stencilBlockedSymbol = new Color(0.75f, 0.2f, 0.2f, 1f);
		public static readonly Color stencilLedOff = EditorGUIUtility.isProSkin ? new Color(0.38f, 0.38f, 0.38f) : new Color(0.72f, 0.72f, 0.72f);
		public static readonly Color stencilLedMasked = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.82f, 0.82f, 0.82f);
		public static readonly Color stencilLedHover = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.25f) : new Color(0f, 0f, 0f, 0.12f);
		public static readonly Color stencilBitTextMasked = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.25f);
		public static readonly Color stencilSymbolHover = EditorGUIUtility.isProSkin ? Color.white : Color.black;
		public static readonly Color stencilDivider = EditorGUIUtility.isProSkin ? Color.gray : new Color(0.42f, 0.42f, 0.42f);
		public static readonly Color stencilBitWeightText = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f);
		public static readonly Color stencilDim = EditorGUIUtility.isProSkin ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.62f, 0.62f, 0.62f);
		// Zebra striping behind every other bit row. Reads on both skins, so it is not theme-split.
		public static readonly Color stencilRowStripe = new Color(0, 0, 0, 0.1f);
	}

	public class Icons
	{
		public static readonly GUIStyle help = CreateIconStyle(EditorGUIUtility.IconContent("_Help@2x"));
		public static readonly GUIStyle menu = CreateIconStyle(EditorGUIUtility.IconContent("_Menu"));
		public static readonly GUIStyle settings = CreateIconStyle(LoadTextureByGUID(RESOURCE_GUID.ICON_SETTINGS));
		public static readonly GUIStyle search = CreateIconStyle(EditorGUIUtility.IconContent("Search Icon"));
		public static readonly GUIStyle presets = CreateIconStyle(EditorGUIUtility.IconContent("Preset.Context"));
		public static readonly GUIStyle add = CreateIconStyle(EditorGUIUtility.IconContent("PrefabOverlayAdded Icon"));
		public static readonly GUIStyle remove = CreateIconStyle(EditorGUIUtility.IconContent("PrefabOverlayRemoved Icon"));
		public static readonly GUIStyle refresh = CreateIconStyle(EditorGUIUtility.IconContent("d_Refresh"));
		public static readonly GUIStyle shaders = CreateIconStyle(EditorGUIUtility.IconContent("d_ShaderVariantCollection Icon"));
		public static readonly GUIStyle tools = CreateIconStyle(LoadTextureByGUID(RESOURCE_GUID.ICON_TOOLS));
		public static readonly GUIStyle linked = CreateIconStyle(LoadTextureByGUID(EditorGUIUtility.isProSkin ? RESOURCE_GUID.ICON_LINK : RESOURCE_GUID.ICON_LINK_INV));
		public static readonly GUIStyle thryIcon = CreateIconStyle(LoadTextureByGUID(EditorGUIUtility.isProSkin ? RESOURCE_GUID.ICON_THRY : RESOURCE_GUID.ICON_THRY_INV));
		public static readonly GUIStyle github = CreateIconStyle(LoadTextureByGUID(EditorGUIUtility.isProSkin ? RESOURCE_GUID.ICON_GITHUB : RESOURCE_GUID.ICON_GITHUB_INV));
		public static readonly GUIStyle video = CreateVideoIconStyle();
		public static readonly GUIStyle globallinked = CreateIconStyle(LoadTextureByGUID(EditorGUIUtility.isProSkin ? RESOURCE_GUID.ICON_GLOBALLINK : RESOURCE_GUID.ICON_GLOBALLINK_INV));
		public static readonly GUIStyle globallinked_active = CreateIconStyle(LoadTextureByGUID(RESOURCE_GUID.ICON_GLOBALLINK_ACTIVE));

		static GUIStyle CreateVideoIconStyle()
		{
			// Try multiple icon names for cross-version compatibility
			string[] candidates = { "PlayButton", "d_PlayButton", "Animation.Play", "d_Animation.Play", "d_Video Player" };
			foreach (var name in candidates)
			{
				var content = EditorGUIUtility.IconContent(name);
				if (content?.image != null)
					return CreateIconStyle(content);
			}
			// Generate a fallback texture so the button is always visible
			var fallback = new Texture2D(16, 16) { hideFlags = HideFlags.HideAndDontSave };
			var pixels = new Color[16 * 16];
			for (int y = 0; y < 16; y++)
				for (int x = 0; x < 16; x++)
				{
					// Simple play triangle
					float ny = (y - 8f) / 8f; // -1 to 1
					float maxX = (1f - Mathf.Abs(ny)) * 10f + 3f;
					pixels[y * 16 + x] = x >= 3 && x <= maxX ? Color.white : Color.clear;
				}
			fallback.SetPixels(pixels);
			fallback.Apply();
			return CreateIconStyle(fallback);
		}

		static GUIStyle CreateIconStyle(GUIContent content)
		{
			return CreateIconStyle(content.image as Texture2D);
		}

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
