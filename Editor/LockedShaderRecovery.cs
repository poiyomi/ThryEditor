// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Puts locked materials back to a working state when the shader they were locked to is gone.
    ///
    /// Recovery is an unlock. Everything needed is on the material itself and therefore survives an
    /// export: the original shader is named by an override tag, textures the optimizer stripped were
    /// stashed as _stripped_tex_ tags, and the pre-lock keyword set is in OriginalKeywords. So the
    /// material returns to exactly the state it had before locking, and can simply be locked again -
    /// which regenerates the cache entry.
    /// </summary>
    public static class LockedShaderRecovery
    {
        const string SessionKeyDidFullScan = "Thry.LockedShaderRecovery.DidFullScan";

        #region Detection

        /// <summary>
        /// True when this material was locked but the shader it was locked to no longer exists.
        /// </summary>
        public static bool IsOrphanedLockedMaterial(Material material)
        {
            if (material == null) return false;

            // A present, working shader shouldn't be our problem, including a locked one.
            if (!material.shader.IsBroken()) return false;

            // Distinguishes between "Locked, shader vanished" vs. "shader is missing entirely", which
            // is not ours to touch.
            return ShaderOptimizer.IsMaterialLocked(material);
        }

        /// <summary>
        /// Splits orphaned materials into the ones that can be put back and the ones that cannot,
        /// which is decided by whether the shader they were locked from is still in the project.
        /// </summary>
        public static void Partition(IEnumerable<Material> materials, out List<Material> recoverable, out List<Material> unrecoverable)
        {
            recoverable = new List<Material>();
            unrecoverable = new List<Material>();

            foreach (Material m in materials)
            {
                if (!IsOrphanedLockedMaterial(m)) continue;

                Shader original = ShaderOptimizer.GetOriginalShader(m, false);
                if (original == null || original.IsBroken()) unrecoverable.Add(m);
                else recoverable.Add(m);
            }
        }

        #endregion

        #region Recovery

        // <summary>
        /// Unlocks every orphaned material in the given set. Returns how many were put back.
        /// Materials whose original shader is also missing are reported rather than touched - there is
        /// nothing to put them back to, and guessing would be worse than saying so.
        /// </summary>
        public static int Recover(IEnumerable<Material> materials, bool showProgress = false)
        {
            List<Material> recoverable;
            List<Material> unrecoverable;
            Partition(materials, out recoverable, out unrecoverable);

            foreach (Material m in unrecoverable)
            {
                ThryLogger.LogErr($"Material \"{m.name}\" ({AssetDatabase.GetAssetPath(m)}) was locked, but neither it's locked shader "
                    + $"nor the shader it was locked from (\"{m.GetTag(ShaderOptimizer.TAG_ORIGINAL_SHADER, false, "unknown")}\") is in this project. "
                    + "Install the required shaders and the material should recover on its own.");
            }

            if (recoverable.Count == 0) return 0;

            ThryLogger.Log($"{recoverable.Count} locked material{(recoverable.Count == 1 ? "" : "s")} had no shader - "
                + $"most likely {LockedShaderCache.CacheRoot} was not carried across. Unlock it so they render normally again; "
                + "lock them when you are ready and the cache will be rebuilt.");
            
            ShaderOptimizer.UnlockMaterials(recoverable, showProgress ? ShaderOptimizer.ProgressBar.Uncancellable : ShaderOptimizer.ProgressBar.None);
            return recoverable.Count;
        }

        // Scans every material in the project. Only worth doing when something suggests a problem.
        public static int RecoverAll(bool showProgress = true)
        {
            IEnumerable<Material> all = AssetDatabase.FindAssets("t:Material")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m != null);

            return Recover(all, showProgress);
        }

        #endregion

        #region Automatic Trigger

        class Postprocessor : AssetPostprocessor
        {
            static readonly List<string> s_importedMaterialPaths = new List<string>();
            static bool s_needsFullScan;
            static bool s_queued;

            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                foreach (string path in importedAssets)
                    if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        s_importedMaterialPaths.Add(path);

                // Losing a cached shader does not necessarily re-import the materials that used it, so
                // that case has to be caught from the deletion instead.
                foreach (string path in deletedAssets)
                {
                    if (!LockedShaderCache.IsInCache(path)) continue;
                    s_needsFullScan = true;
                    break;
                }

                if (s_importedMaterialPaths.Count == 0 && !s_needsFullScan) return;
                if (s_queued) return;
                s_queued = true;

                // Let the import finish before touching anything.
                EditorApplication.delayCall += Run;
            }

            static void Run()
            {
                EditorApplication.delayCall -= Run;
                s_queued = false;

                try
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        // Try again once the editor is idle rather than giving up.
                        s_queued = true;
                        EditorApplication.delayCall += Run;
                        return;
                    }

                    bool fullScan = s_needsFullScan;
                    string[] paths = s_importedMaterialPaths.ToArray();
                    s_importedMaterialPaths.Clear();
                    s_needsFullScan = false;

                    if (fullScan)
                    {
                        // Once per session at most; a project missing it's cache would otherwise re-scan
                        // every time anything at all is imported.
                        if (SessionState.GetBool(SessionKeyDidFullScan, false)) return;
                        SessionState.SetBool(SessionKeyDidFullScan, true);
                        RecoverAll(showProgress: true);
                        return;
                    }

                    IEnumerable<Material> materials = paths
                        .Select(AssetDatabase.LoadAssetAtPath<Material>)
                        .Where(m => m != null);

                    Recover(materials);
                }
                catch (Exception e)
                {
                    // Never let recovery break an import.
                    Debug.LogException(e);
                }
            }
        }

        #endregion

        #region Menu

        [MenuItem("Poi/Thry/ThryEditor/Recover Locked Materials With Missing Shaders", priority = 50)]
        static void MenuRecoverAll()
        {
            int recovered = RecoverAll();
            EditorUtility.DisplayDialog("Recover Locked Materials",
                recovered == 0
                    ? "No locked materials are missing their shader."
                    : $"Unlocked {recovered} material{(recovered == 1 ? "" : "s")} whose locked shader was missing.\n\n"
                      + "They render normally again and can be locked whenever you are ready.",
                "OK");
        }

        #endregion
    } 
}
