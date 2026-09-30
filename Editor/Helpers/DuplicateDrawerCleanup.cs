using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;


namespace Thry.ThryEditor.Helpers
{
    // Detects and removes duplicate copies of specified drawer scripts that were moved
    // from Poiyomi Shaders into this ThryEditor fork. If a user updates ThryEditor
    // without updating Poiyomi (which is pretty common), the old Poiyomi-shipped
    // versions of these scripts will collide (same GUIDs) with the new canonical
    // copies in this repo.
    [InitializeOnLoad]
    internal static class DuplicateDrawerCleanup
    {
        static readonly (string name, string guid)[] MovedDrawers = new[]
        {
            ("ThryMultiFloatButtonsDrawer", "f3df644effce7f34282210a275f7f442"),
            ("ThryMultiFloatHeaderDrawer", "55b996104b3677141bd2bdd661afd304"),
            ("ButtonVector", "384236c783ed181459334431beb0f971"),
            ("InvertedSliderDrawer", "575bd1334554af5468820b6d6334a891"),
        };

        // Relative to the Poiyomi root
        static readonly string[] OldPoiyomiFolders = { "Scripts/Editor", "Scripts/ThryStuff" };
        const string DrawerNamespace = "namespace Thry.ThryEditor.Drawers";

        const string SelfFileName = nameof(DuplicateDrawerCleanup) + ".cs";

        static DuplicateDrawerCleanup()
        {
            EditorApplication.delayCall += Scan;
        }

        static void Scan()
        {
            string drawersDir = ResolveCanonicalDrawersDir();
            if (drawersDir == null) return;

            var removed = new List<string>();
            var failed = new List<string>();

            foreach (var (name, _) in MovedDrawers)
            {
                string canonicalPath = (drawersDir + "/" + name + ".cs");

                string[] guids = AssetDatabase.FindAssets(name + " t:MonoScript");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    string normalized = path.Replace('\\', '/');
                    if (string.Equals(normalized, canonicalPath, StringComparison.OrdinalIgnoreCase)) continue;

                    if (Path.GetFileNameWithoutExtension(normalized) != name) continue;
                    // Other packages can ship scripts with these names
                    if (!IsOldPoiyomiCopy(normalized)) continue;

                    if (AssetDatabase.MoveAssetToTrash(path)) removed.Add(path);
                    else failed.Add(path);
                }
            }

            if (removed.Count > 0)
            {
                ThryLogger.Log($"Removed {removed.Count} duplicate drawer script(s) left over from an older Poiyomi Shaders version. Updating Poiyomi Shaders is recommended.\n - " + string.Join("\n - ", removed));
                AssetDatabase.Refresh();
            }

            if (failed.Count > 0)
            {
                ThryLogger.LogErr("Failed to remove the following duplicate drawer script(s). Please delete them manually in order to continue using this project:\n - " + string.Join("\n - ", failed));
            }
        }

        static bool IsOldPoiyomiCopy(string path)
        {
            string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir)) return false;

            string folder = OldPoiyomiFolders.FirstOrDefault(f => dir.EndsWith("/" + f, StringComparison.OrdinalIgnoreCase));
            if (folder == null) return false;

            // e.g. Assets/_PoiyomiShaders or Packages/com.poiyomi.toon
            string root = dir.Substring(0, dir.Length - folder.Length - 1);
            if (Path.GetFileName(root).IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) < 0) return false;

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            return script != null && script.text.Contains(DrawerNamespace);
        }

        static string ResolveCanonicalDrawersDir()
        {
            string[] selfGuids = AssetDatabase.FindAssets(nameof(DuplicateDrawerCleanup) + " t:MonoScript");
            foreach (string selfGuid in selfGuids)
            {
                string selfPath = AssetDatabase.GUIDToAssetPath(selfGuid);
                if (string.IsNullOrEmpty(selfPath)) continue;

                string normalized = selfPath.Replace('\\', '/');
                if (!normalized.EndsWith("/" + SelfFileName, StringComparison.OrdinalIgnoreCase)) continue;

                string helpersDir = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(helpersDir)) continue;

                string editorDir = Path.GetDirectoryName(helpersDir)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(editorDir)) continue;

                return editorDir + "/Drawers";
            }
            return null;
        }
    }
}
