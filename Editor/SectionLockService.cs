// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System;
using System.Collections.Generic;
using System.IO;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3
using VRC.SDKBase.Editor.BuildPipeline;
#endif

namespace Thry.ThryEditor
{
    /// <summary>
    /// Moves the materials being edited onto section shaders while Config.sectionLockWhileEditing is on. A material
    /// only moves when an edit would make the original shader compile a new variant anyway (its keywords changed),
    /// and after that it is checked again shortly after every change. Selecting a material or dragging a slider
    /// changes nothing.
    /// </summary>
    [InitializeOnLoad]
    public static class SectionLockService
    {
        // How long a material has to stay unchanged before it is checked, so a burst of edits costs one check. A
        // keyword change is checked right away. The section shader has no keywords of its own, so the old one keeps
        // rendering unchanged meanwhile.
        const double SettleSeconds = 0.1;
        // A ChangedAt that is always past the settle time. now - SettleSeconds can round to just under it.
        const double ApplyNow = double.NegativeInfinity;
        // Longest a build can keep the service paused if its end is never reported.
        public const double BuildSafetySeconds = 60;
        // How long after a swap the new shader is watched for compile errors.
        const double ErrorWatchSeconds = 60;

        class TrackedMaterial
        {
            public int DirtyCount = -1;
            public string Keywords;
            public double ChangedAt;
            public double SwappedAt = -1;
            public bool Pending;
        }

        static readonly Dictionary<Material, TrackedMaterial> s_tracked = new Dictionary<Material, TrackedMaterial>();
        static readonly List<Material> s_buffer = new List<Material>();
        static bool s_wasEnabled;
        static bool s_trackedAfterReload;
        static double s_suspendedUntil;

        static SectionLockService()
        {
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += OnUndoRedo;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static bool Enabled => Config.Instance.sectionLockWhileEditing;

        /// <summary>
        /// Turns the option on or off and saves it. Turning it off puts every material back on its original shader
        /// on the next editor update.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            if (Config.Instance.sectionLockWhileEditing == enabled) return;
            Config.Instance.sectionLockWhileEditing = enabled;
            Config.Instance.Save();
        }

        /// <summary>Whether the inspector toolbar shows the switch for this shader: only the lock can use it.</summary>
        public static bool AppliesTo(Shader shader)
        {
            return shader != null && ShaderOptimizer.IsShaderUsingThryOptimizer(shader);
        }

        /// <summary>Tooltip for the inspector toolbar switch, with its current state.</summary>
        public static string ToolbarTooltip => (Enabled
                ? RetainedText.Get("section_lock_on", "Strip Disabled Sections While Editing: On")
                : RetainedText.Get("section_lock_off", "Strip Disabled Sections While Editing: Off"))
            + "\n\n" + RetainedText.Get("sectionLockWhileEditing_tooltip", "While you edit a material, it uses a copy of its shader without the sections you have turned off, so switching sections compiles faster. The material file keeps the normal shader, and uploads lock as usual.");

        /// <summary>True when section shaders may be put on materials right now.</summary>
        public static bool IsActive => Enabled && EditorApplication.timeSinceStartup >= s_suspendedUntil
            && !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer;

        /// <summary>Stops section shaders from being applied, e.g. while a build runs. Ends by itself.</summary>
        public static void Suspend(double seconds)
        {
            s_suspendedUntil = EditorApplication.timeSinceStartup + seconds;
        }

        public static void Resume()
        {
            s_suspendedUntil = 0;
        }

        /// <summary>Makes every section-locked material get checked again, e.g. after its original shader changed.</summary>
        public static void RecheckAll()
        {
            TrackLoadedSectionLocked();
            double now = EditorApplication.timeSinceStartup;
            foreach (KeyValuePair<Material, TrackedMaterial> pair in s_tracked)
            {
                if (!SectionLock.IsSectionLocked(pair.Key)) continue;
                pair.Value.Pending = true;
                pair.Value.ChangedAt = now;
            }
        }

        // A domain reload empties s_tracked, but section-locked materials stay on their section shaders. They are
        // tracked again and checked, so changes made from outside the inspector still reach their shader.
        static void TrackLoadedSectionLocked()
        {
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!SectionLock.IsSectionLocked(material) || s_tracked.ContainsKey(material)) continue;
                s_tracked[material] = new TrackedMaterial
                {
                    DirtyCount = EditorUtility.GetDirtyCount(material),
                    Keywords = KeywordState(material),
                    Pending = true,
                };
                SectionLock.Prepare(SectionLock.GetSourceShader(material));
            }
        }

        static string KeywordState(Material material)
        {
            string[] keywords = material.shaderKeywords;
            Array.Sort(keywords, StringComparer.Ordinal);
            return string.Join(" ", keywords);
        }

        static void Update()
        {
            if (!Enabled)
            {
                if (s_wasEnabled)
                {
                    s_wasEnabled = false;
                    s_tracked.Clear();
                    // The section shaders themselves stay until the editor closes, since undo steps may still
                    // point at them.
                    SectionLock.RevertAllLoaded();
                }
                return;
            }
            s_wasEnabled = true;

            if (!IsActive || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!s_trackedAfterReload)
            {
                s_trackedAfterReload = true;
                TrackLoadedSectionLocked();
            }

            double now = EditorApplication.timeSinceStartup;
            Material[] inspected = ShaderEditor.Active?.Materials;
            if (inspected != null)
            {
                foreach (Material material in inspected)
                {
                    if (material == null || s_tracked.ContainsKey(material)) continue;
                    // The first look only records the state, so selecting a material changes nothing. One that is
                    // already on a section shader is checked once, in case it changed while nobody watched it.
                    s_tracked[material] = new TrackedMaterial
                    {
                        DirtyCount = EditorUtility.GetDirtyCount(material),
                        Keywords = KeywordState(material),
                        Pending = SectionLock.IsSectionLocked(material),
                    };
                    if (SectionLock.CanSectionLock(material)) SectionLock.Prepare(SectionLock.GetSourceShader(material));
                }
            }

            s_buffer.Clear();
            foreach (KeyValuePair<Material, TrackedMaterial> pair in s_tracked)
            {
                Material material = pair.Key;
                TrackedMaterial tracked = pair.Value;
                // Nothing left to watch: gone, or on its original shader and no longer in the inspector.
                if (material == null || (!tracked.Pending && !SectionLock.IsSectionLocked(material)
                    && (inspected == null || Array.IndexOf(inspected, material) < 0)))
                {
                    s_buffer.Add(material);
                    continue;
                }
                int dirtyCount = EditorUtility.GetDirtyCount(material);
                if (dirtyCount == tracked.DirtyCount) continue;
                tracked.DirtyCount = dirtyCount;

                if (SectionLock.IsSectionLocked(material))
                {
                    tracked.Pending = true;
                    // Switching a section or option changes keywords in a single edit, and needs a new shader anyway.
                    string current = KeywordState(material);
                    tracked.ChangedAt = current == tracked.Keywords ? now : ApplyNow;
                    tracked.Keywords = current;
                    continue;
                }
                if (SectionLock.RepairIfBroken(material)) continue;
                // On another shader now, e.g. after a full lock or a shader switch.
                if (!material.shader.IsBroken() && !string.IsNullOrEmpty(material.GetTag(SectionLock.TAG_SECTION_SOURCE, false, string.Empty)))
                    material.SetOverrideTag(SectionLock.TAG_SECTION_SOURCE, string.Empty);

                // Still on the original shader: only move when the original would compile a new variant anyway.
                string keywords = KeywordState(material);
                if (keywords == tracked.Keywords) continue;
                tracked.Keywords = keywords;
                tracked.Pending = true;
                tracked.ChangedAt = ApplyNow;
            }
            foreach (Material material in s_buffer) s_tracked.Remove(material);

            // Never swap in the middle of a drag or while text is being typed.
            if (GUIUtility.hotControl != 0 || EditorGUIUtility.editingTextField || AnimationMode.InAnimationMode()) return;

            bool generatedThisFrame = false;
            foreach (KeyValuePair<Material, TrackedMaterial> pair in s_tracked)
            {
                Material material = pair.Key;
                TrackedMaterial tracked = pair.Value;

                // A baked shader that fails to compile falls back to one that bakes nothing, and one that still fails
                // puts the material back on its original for the rest of the session. Only recent swaps are watched.
                if (tracked.SwappedAt >= 0 && now - tracked.SwappedAt < ErrorWatchSeconds
                    && SectionLock.IsSectionLocked(material) && ShaderUtil.ShaderHasError(material.shader))
                {
                    tracked.SwappedAt = -1;
                    if (SectionLock.IsBakingDisabled(material))
                    {
                        ThryLogger.LogWarn("SectionLock", $"{material.name}: the section shader did not compile, so this material stays on its original shader.");
                        SectionLock.Block(material);
                        bool clean = !EditorUtility.IsDirty(material);
                        SectionLock.Revert(material);
                        if (clean) EditorUtility.ClearDirty(material);
                        tracked.DirtyCount = EditorUtility.GetDirtyCount(material);
                        tracked.Pending = false;
                        continue;
                    }
                    ThryLogger.LogDetail("SectionLock", $"{material.name}: the section shader had compile errors, switching to one without baked values.");
                    SectionLock.DisableBaking(material);
                    tracked.Pending = true;
                    tracked.ChangedAt = 0;
                }

                if (!tracked.Pending || now - tracked.ChangedAt < SettleSeconds) continue;
                // Making a shader takes about 150 ms. With many materials edited at once, one per frame.
                if (generatedThisFrame) continue;
                // The original is still being read on another thread. Checked again once it's done.
                if (!SectionLock.IsPrepared(material)) continue;
                tracked.Pending = false;

                bool wasClean = !EditorUtility.IsDirty(material);
                SectionLock.Result result = SectionLock.Apply(material);
                // The swap itself changes the dirty count. It isn't an edit.
                if (wasClean && result != SectionLock.Result.Unchanged) EditorUtility.ClearDirty(material);
                tracked.DirtyCount = EditorUtility.GetDirtyCount(material);
                tracked.Keywords = KeywordState(material);

                if (result == SectionLock.Result.Generated) generatedThisFrame = true;
                if (result == SectionLock.Result.Generated || result == SectionLock.Result.Reused) tracked.SwappedAt = now;
                if (result == SectionLock.Result.Generated || result == SectionLock.Result.Reused)
                {
                    SectionLock.Timings t = SectionLock.LastTimings;
                    ThryLogger.LogDetail("SectionLock", $"{material.name}: {result}, {t.RemovedBlocks} blocks removed, {t.BakedValues} values baked, "
                        + $"key {t.KeyMs:0}ms, generate {t.GenerateMs:0}ms, create {t.CreateMs:0}ms, swap {t.SwapMs:0}ms");
                }
            }
        }

        // An undo can put a material back on a section shader that no longer exists. Runs whether or not the option
        // is on, since undo steps from while it was on remain.
        static void OnUndoRedo()
        {
            HashSet<Material> materials = new HashSet<Material>(s_tracked.Keys);
            if (ShaderEditor.Active?.Materials != null) materials.UnionWith(ShaderEditor.Active.Materials);
            foreach (UnityEngine.Object selected in Selection.objects)
                if (selected is Material material) materials.Add(material);

            foreach (Material material in materials)
            {
                if (material == null) continue;
                // With the option off, an undo must not leave a material on a section shader either.
                if (material.shader.IsBroken() || (!Enabled && SectionLock.IsSectionLocked(material))) SectionLock.Revert(material);
            }
            if (Enabled) RecheckAll();
        }

        static void OnSelectionChanged()
        {
            foreach (UnityEngine.Object selected in Selection.objects)
                if (selected is Material material) SectionLock.RepairIfBroken(material);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Play mode tools (NDMF, avatar optimizers, emulators) copy materials and read their shaders, and a
            // section shader has no file. Play mode gets the original shaders.
            if (change == PlayModeStateChange.ExitingEditMode) SectionLock.RevertAllLoaded();
        }
    }

    /// <summary>
    /// Section shaders only exist in memory, so a material must never be written while it uses one. The original
    /// shader goes back on for the save and the section shader returns right after, which keeps the .mat on disk
    /// exactly as it would be without the section lock.
    /// </summary>
    public class SectionLockSaveGuard : AssetModificationProcessor
    {
        struct Restore
        {
            public Material Material;
            public Shader SectionShader;
            public Shader Source;
        }

        static string[] OnWillSaveAssets(string[] paths)
        {
            List<string> save = new List<string>(paths.Length);
            List<Restore> restore = null;
            foreach (string path in paths)
            {
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    save.Add(path);
                    continue;
                }
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (SectionLock.IsSectionLocked(material))
                {
                    Shader sectionShader = material.shader;
                    Shader source = SectionLock.GetSourceShader(material);
                    if (!SectionLock.Revert(material))
                    {
                        // Writing it now would save a material without a shader.
                        ThryLogger.LogErr($"Did not save \"{material.name}\": its original shader could not be found.");
                        continue;
                    }
                    if (restore == null) restore = new List<Restore>();
                    restore.Add(new Restore { Material = material, SectionShader = sectionShader, Source = source });
                }
                else if (material != null && material.shader.IsBroken())
                {
                    // Lost its section shader, e.g. through an undo. The original goes into the file.
                    SectionLock.Revert(material);
                }
                else if (material != null && !string.IsNullOrEmpty(material.GetTag(SectionLock.TAG_SECTION_SOURCE, false, string.Empty)))
                {
                    // Left over from a shader switch that didn't go through the section lock.
                    material.SetOverrideTag(SectionLock.TAG_SECTION_SOURCE, string.Empty);
                }
                save.Add(path);
            }

            if (restore != null)
            {
                EditorApplication.delayCall += () =>
                {
                    // Turned off, entering play mode or building in the meantime: the material stays on its original.
                    if (!SectionLockService.IsActive) return;
                    foreach (Restore entry in restore)
                    {
                        if (entry.Material == null) continue;
                        // The file matches the original shader; putting the section shader back isn't a change.
                        bool wasClean = !EditorUtility.IsDirty(entry.Material);
                        SectionLock.Restore(entry.Material, entry.SectionShader, entry.Source);
                        if (wasClean) EditorUtility.ClearDirty(entry.Material);
                    }
                };
            }
            return save.ToArray();
        }
    }

    /// <summary>
    /// Builds must only see original shaders: section shaders are not assets, and tools that copy materials early in
    /// a build (NDMF, VRCFury) would carry them into the copies. Runs before anything else.
    /// </summary>
    public class SectionLockBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Update also pauses while BuildPipeline.isBuildingPlayer; this only covers the gap around it.
            SectionLockService.Suspend(SectionLockService.BuildSafetySeconds);
            SectionLock.RevertAllLoaded();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            SectionLockService.Resume();
        }
    }

#if VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3
    public class SectionLockVRChatBuildGuard : IVRCSDKBuildRequestedCallback, IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            // Avatar builds report their end. Others resume after a short safety time.
            SectionLockService.Suspend(SectionLockService.BuildSafetySeconds);
            SectionLock.RevertAllLoaded();
            return true;
        }

        public void OnPostprocessAvatar()
        {
            SectionLockService.Resume();
        }
    }
#endif

    /// <summary>
    /// Repairs materials written while on a section shader, which happens when something saves them without
    /// AssetDatabase.SaveAssets (SaveAssetIfDirty, CreateAsset of a copy). Rechecks section-locked materials when a
    /// shader is reimported, since that can make the section shaders made from it out of date.
    /// </summary>
    class SectionLockAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            List<string> materialPaths = null;
            HashSet<string> shaderPaths = null;
            foreach (string path in imported)
            {
                if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    // Reading the file is much cheaper than loading a material with thousands of properties.
                    if (!FileMentionsSectionLock(path)) continue;
                    if (materialPaths == null) materialPaths = new List<string>();
                    materialPaths.Add(path);
                }
                else if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                {
                    if (shaderPaths == null) shaderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    shaderPaths.Add(path);
                }
            }
            if (shaderPaths != null) RevertMaterialsOf(shaderPaths);
            if (materialPaths == null) return;

            // Saving from inside an import callback can start another import, so the repair waits a tick.
            EditorApplication.delayCall += () =>
            {
                foreach (string path in materialPaths)
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) continue;
                    if (SectionLock.IsSectionLocked(material)) SaveWithOriginalShader(material);
                    else SectionLock.RepairIfBroken(material);
                }
            };
        }

        // An original shader whose properties changed no longer matches the section shaders made from it, and the
        // inspector rebuilds from the new properties. The materials go back to the original now and get a new section
        // shader on their next check.
        static void RevertMaterialsOf(HashSet<string> shaderPaths)
        {
            bool reverted = false;
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!SectionLock.IsSectionLocked(material)) continue;
                Shader source = SectionLock.GetSourceShader(material);
                if (source == null || !shaderPaths.Contains(AssetDatabase.GetAssetPath(source))) continue;
                bool clean = !EditorUtility.IsDirty(material);
                if (SectionLock.Revert(material) && clean) EditorUtility.ClearDirty(material);
                reverted = true;
            }
            if (reverted && SectionLockService.Enabled) SectionLockService.RecheckAll();
        }

        static bool FileMentionsSectionLock(string path)
        {
            try
            {
                return File.ReadAllText(path).Contains(SectionLock.TAG_SECTION_SOURCE);
            }
            catch (IOException)
            {
                return false;
            }
        }

        // The file was written while the material was on its section shader (a copy made with CreateAsset, or
        // SaveAssetIfDirty), so it has no shader on disk. Writes it again with the original.
        static void SaveWithOriginalShader(Material material)
        {
            Shader sectionShader = material.shader;
            Shader source = SectionLock.GetSourceShader(material);
            if (!SectionLock.Revert(material)) return;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            if (!SectionLockService.IsActive) return;
            SectionLock.Restore(material, sectionShader, source);
            EditorUtility.ClearDirty(material);
        }
    }
}
