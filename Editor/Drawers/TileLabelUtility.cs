// Powers right-click rename of UV Tile Discard / Face Discard tile buttons.
// Labels are stored as material override tags so the underlying `_UDIMDiscardRow*` shader
// properties stay untouched. Only `_UDIM(Face)?DiscardRow\d_\d` properties opt in; every
// other usage of the host drawers is unaffected.
//
// Original Concept created by an anonymous user (refused credit). Implemented officially by BluWizard LABS.

using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Thry.ThryEditor.Helpers;

namespace Thry.ThryEditor.Drawers
{
    internal static class TileLabelUtility
    {
        internal const string TAG_PREFIX = "thry_tile_label_";
        internal const string ROW_TOOLTIP = "Right-click any tile button to rename it";

        // Poiyomi's lock-in renames animated properties to `<name>_<suffix>`. Stripping the suffix
        // back to the canonical name keeps the tag key stable across lock/unlock cycles regardless
        // of which rename-suffix the user chose. Read-only — we never write a non-canonical tag.
        static readonly Regex CANONICAL_UDIM_NAME = new Regex(@"^(_UDIM(?:Face)?DiscardRow\d_\d)(?:_.+)?$", RegexOptions.Compiled);

        internal static bool IsUdimProperty(string propertyName)
        {
            return !string.IsNullOrEmpty(propertyName) && CANONICAL_UDIM_NAME.IsMatch(propertyName);
        }

        internal static string CanonicalPropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return propertyName;
            Match m = CANONICAL_UDIM_NAME.Match(propertyName);
            return m.Success ? m.Groups[1].Value : propertyName;
        }

        // Returns the user-set label, or null if none exists / property is not UDIM / mat is null.
        // Falls back to a tag accidentally saved under the runtime (suffixed) name from before
        // canonicalization, in case a material was tagged while locked.
        internal static string GetTileLabel(Material mat, string propertyName)
        {
            if (mat == null || !IsUdimProperty(propertyName)) return null;
            string canonical = CanonicalPropertyName(propertyName);
            string tag = mat.GetTag(TAG_PREFIX + canonical, false, string.Empty);
            if (!string.IsNullOrEmpty(tag)) return tag;
            if (canonical != propertyName)
            {
                tag = mat.GetTag(TAG_PREFIX + propertyName, false, string.Empty);
                if (!string.IsNullOrEmpty(tag)) return tag;
            }
            return null;
        }

        // Drop into the per-button render loop. Intercepts right-click on `buttonRect` and pops the
        // Rename / Reset context menu. Safe to call every frame; only acts on MouseDown/ContextClick.
        internal static void HandleRightClick(Rect buttonRect, Object[] targets, string propertyName, string defaultLabel)
        {
            if (!IsUdimProperty(propertyName)) return;
            Event evt = Event.current;
            if (evt == null) return;

            if (evt.type == EventType.MouseDown && evt.button == 1 && buttonRect.Contains(evt.mousePosition))
            {
                Vector2 screenPos = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                ShowContextMenu(targets, propertyName, defaultLabel, screenPos);
                evt.Use();
            }
            else if (evt.type == EventType.ContextClick && buttonRect.Contains(evt.mousePosition))
            {
                // Swallow so the host inspector's own context menu (if any) doesn't double-fire.
                evt.Use();
            }
        }

        static void ShowContextMenu(Object[] targets, string propertyName, string defaultLabel, Vector2 screenPos)
        {
            Object[] capturedTargets = new Object[targets != null ? targets.Length : 0];
            if (targets != null) System.Array.Copy(targets, capturedTargets, targets.Length);
            string canonical = CanonicalPropertyName(propertyName);
            string runtimeName = canonical != propertyName ? propertyName : null;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename..."), false, () =>
            {
                TileLabelRenamePopup.Show(capturedTargets, canonical, defaultLabel, screenPos);
            });
            menu.AddItem(new GUIContent("Reset to default label"), false, () =>
            {
                ApplyTagToTargets(capturedTargets, canonical, string.Empty);
                // Clear any stale tag saved under the runtime (suffixed) name from before normalisation.
                if (runtimeName != null)
                    ApplyTagToTargets(capturedTargets, runtimeName, string.Empty);
            });
            menu.ShowAsContext();
        }

        internal static void ApplyTagToTargets(Object[] targets, string canonicalPropertyName, string value)
        {
            if (targets == null || targets.Length == 0 || string.IsNullOrEmpty(canonicalPropertyName)) return;

            Undo.RegisterCompleteObjectUndo(targets, "Rename UV Tile Label");
            string tagKey = TAG_PREFIX + canonicalPropertyName;
            foreach (var t in targets)
            {
                if (t is Material mat)
                {
                    mat.SetOverrideTag(tagKey, value ?? string.Empty);
                    EditorUtility.SetDirty(mat);

                    // Unity interns material tag-value strings in a shared, case-insensitive table.
                    // If the same letters were already cached under a different capitalization (e.g.
                    // "hair"), setting "Hair" reads back as the cached "hair". Read it straight back,
                    // detect the mismatch, and explain - the label still works, only its displayed
                    // capitalization differs, and it can't be corrected until the cache clears.
                    if (!string.IsNullOrEmpty(value))
                    {
                        string stored = mat.GetTag(tagKey, false, string.Empty);
                        if (!string.Equals(stored, value, System.StringComparison.Ordinal) && string.Equals(stored, value, System.StringComparison.OrdinalIgnoreCase))
                        {
                            ThryLogger.LogWarn("Unity Bug detected! The capitalization of your custom UV Tile Label may be temporarily broken. See Log Message for more details.",
                                $"Label \"{value}\" was stored as \"{stored}\" on material \"{mat.name}\". " +
                                "This is a known Unity Bug: tag-value strings are interned case-insensitively, so once these " +
                                "letters exist in another capitalization Unity reuses that cached casing. The label still works " +
                                "correctly but only its displayed capitalization is affected. " +
                                "Restarting Unity Editor should clear this issue.");
                        }
                    }
                }
            }
        }
        // Splits a UDIM tile property into its row prefix so the whole 4-tile row can be addressed.
        static readonly Regex UDIM_ROW_SPLIT = new Regex(@"^(_UDIM(?:Face)?DiscardRow\d)_\d$", RegexOptions.Compiled);
        // Matches only the visible column-0 tile of a row (the one carrying the drawer).
        static readonly Regex UDIM_MAIN_COLUMN = new Regex(@"^(_UDIM(?:Face)?DiscardRow\d)_0$", RegexOptions.Compiled);
        // If `propertyName` is the visible column-0 of a UV Tile Discard row, returns the property names
        // of its three hidden sibling columns (…_1, …_2, …_3); otherwise null. The siblings are
        // [HideInInspector], so they aren't part of a section's copyable children — copying the visible
        // column has to pull them along for the on/off values to travel. Canonical (unlocked) names;
        // gating on column-0 also prevents the siblings from recursively re-copying the row.
        internal static string[] GetHiddenSiblingPropertyNames(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return null;
            Match m = UDIM_MAIN_COLUMN.Match(CanonicalPropertyName(propertyName));
            if (!m.Success) return null;
            string row = m.Groups[1].Value;
            return new[] { row + "_1", row + "_2", row + "_3" };
        }

        // The canonical names of all four tile columns in the row that `propertyName` belongs to,
        // or null if it isn't a UDIM tile property. Lets a copy of the single visible row property
        // (column 0) also carry the labels of its three hidden sibling columns, since those siblings
        // are [HideInInspector] and never travel through the normal section copy on their own.
        internal static string[] GetRowColumnCanonicalNames(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return null;
            Match m = UDIM_ROW_SPLIT.Match(CanonicalPropertyName(propertyName));
            if (!m.Success) return null;
            string row = m.Groups[1].Value;
            return new[] { row + "_0", row + "_1", row + "_2", row + "_3" };
        }
        // Copy helpers mirror ShaderOptimizer.CopyAnimatedTag so the custom tile label travels with the
        // property during section/material Copy & Paste. Each no-ops on non-UDIM properties. Tags are
        // canonical-name keyed so they line up even when source/target are locked with different suffixes.
        internal static void CopyTileLabelTag(Material source, MaterialProperty target)
        {
            if (source == null || target == null) return;
            string[] cols = GetRowColumnCanonicalNames(target.name);
            if (cols == null) return;
            CopyRowTags(source, cols, target.targets);
        }
        internal static void CopyTileLabelTag(MaterialProperty source, MaterialProperty target)
        {
            if (source == null || target == null) return;
            string[] cols = GetRowColumnCanonicalNames(source.name);
            if (cols == null) return;
            CopyRowTags(source.targets[0] as Material, cols, target.targets);
        }
        internal static void CopyTileLabelTag(MaterialProperty source, Material[] targets)
        {
            if (source == null || targets == null) return;
            string[] cols = GetRowColumnCanonicalNames(source.name);
            if (cols == null) return;
            CopyRowTags(source.targets[0] as Material, cols, targets);
        }
        static void CopyRowTags(Material source, string[] canonicalColumns, Object[] targets)
        {
            if (source == null || canonicalColumns == null || targets == null) return;
            foreach (string col in canonicalColumns)
            {
                string key = TAG_PREFIX + col;
                string val = source.GetTag(key, false, string.Empty);
                foreach (var t in targets)
                {
                    if (t is Material m) m.SetOverrideTag(key, val);
                }
            }
        }

        internal class TileLabelRenamePopup : EditorWindow
        {
            Object[] _targets;
            string _canonicalPropertyName;
            string _value;
            bool _focusGrabbed;

            public static void Show(Object[] targets, string canonicalPropertyName, string defaultLabel, Vector2 screenPos)
            {
                var win = CreateInstance<TileLabelRenamePopup>();
                win._targets = targets;
                win._canonicalPropertyName = canonicalPropertyName;
                Material firstMat = (targets != null && targets.Length > 0) ? targets[0] as Material : null;
                string current = firstMat != null ? firstMat.GetTag(TAG_PREFIX + canonicalPropertyName, false, string.Empty) : string.Empty;
                win._value = string.IsNullOrEmpty(current) ? (defaultLabel ?? string.Empty) : current;
                win.titleContent = new GUIContent("Rename tile label");
                win.position = new Rect(screenPos.x, screenPos.y, 260f, 80f);
                win.ShowPopup();
                win.Focus();
            }

            void OnGUI()
            {
#if UNITY_2021_3_OR_NEWER
                if (rootVisualElement.childCount > 0) return;
#endif
                Event e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    Close();
                    e.Use();
                    return;
                }
                bool submitOnEnter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

                GUILayout.Space(6);

                GUI.SetNextControlName("LabelField");
                _value = EditorGUILayout.TextField("Label", _value);
                if (!_focusGrabbed)
                {
                    EditorGUI.FocusTextInControl("LabelField");
                    _focusGrabbed = true;
                }

                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                bool cancel = GUILayout.Button("Cancel", GUILayout.Width(80));
                bool ok = GUILayout.Button("OK", GUILayout.Width(80));
                GUILayout.EndHorizontal();

                if (cancel)
                {
                    Close();
                    return;
                }
                if (ok || submitOnEnter)
                {
                    ApplyTagToTargets(_targets, _canonicalPropertyName, _value);
                    Close();
                }
            }

#if UNITY_2021_3_OR_NEWER
            public void CreateGUI()
            {
                var root = rootVisualElement; root.Clear(); RetainedWindow.Style(root);
                var field = new UnityEngine.UIElements.TextField("Label") { value = _value };
                root.Add(field);
                var actions = new UnityEngine.UIElements.VisualElement(); actions.AddToClassList("thry-components"); root.Add(actions);
                System.Action apply = () => { ApplyTagToTargets(_targets, _canonicalPropertyName, field.value); Close(); };
                actions.Add(new UnityEngine.UIElements.Button(apply) { text = "Save" });
                actions.Add(new UnityEngine.UIElements.Button(Close) { text = "Cancel" });
                root.RegisterCallback<UnityEngine.UIElements.KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
                    else if (e.keyCode == KeyCode.Return) { apply(); e.StopPropagation(); }
                });
                field.Focus();
            }
#endif
            void OnLostFocus()
            {
                Close();
            }
        }
    }
}
