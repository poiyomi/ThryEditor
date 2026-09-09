#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Thry.ThryEditor.Drawers;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.TexturePacker
{
    internal sealed class PackedTextureBuildItem
    {
        internal Material Material;
        internal string Property;
        internal string[] Slots;
        internal Texture2D Preview;
        internal TexturePackerConfig Config;
        internal string Signature;
    }

    public static class PackedTextureBuildPreparation
    {
        public const string OutputDirectory = "Assets/Textures/Packed";

        public static int Prepare(IEnumerable<Material> materials) => Prepare(materials, OutputDirectory, null);

        internal static int Prepare(IEnumerable<Material> materials, string outputDirectory,
            Func<Texture2D, string, TexturePackerConfig, Texture> export)
            => Prepare(materials, outputDirectory, export, material => AssetDatabase.SaveAssetIfDirty(material));

        internal static int Prepare(IEnumerable<Material> materials, string outputDirectory,
            Func<Texture2D, string, TexturePackerConfig, Texture> export, Action<Material> saveMaterial)
        {
            var pending = new List<PackedTextureBuildItem>();
            foreach (var material in materials.Where(material => material != null).Distinct())
            {
                var shader = material.shader;
                if (shader == null) continue;
                if (ShaderOptimizer.IsShaderLocked(shader))
                {
                    shader = ShaderOptimizer.GetOriginalShader(material, false);
                    if (shader == null && HasAssignedPendingPreview(material))
                        throw new InvalidOperationException("Upload stopped: material '" + material.name + "' has a pending packed texture but its original shader is missing. Restore the original shader and merge the texture before uploading.");
                }
                if (shader == null || !shader.name.TrimStart('.').StartsWith("poiyomi/", StringComparison.OrdinalIgnoreCase)) continue;
                for (int index = 0; index < shader.GetPropertyCount(); index++)
                {
                    if (shader.GetPropertyType(index) != ShaderPropertyType.Texture) continue;
                    string property = shader.GetPropertyName(index);
                    if (string.IsNullOrEmpty(material.GetTag(property + "_texPack_previewState", false, ""))) continue;
                    PackedTextureBuildItem item;
                    try { item = ThryRGBAPackerDrawer.PendingBuildTexture(material, shader, index); }
                    catch (Exception exception) { throw Failure(material, property, exception); }
                    if (item == null) continue;
                    AssertWritable(material, item.Slots);
                    pending.Add(item);
                }
            }
            if (pending.Count == 0) return 0;
            string directory = AssetDirectory(outputDirectory);
            EnsureDirectory(directory);
            var saved = new Dictionary<PackedTextureBuildItem, Texture>();
            var createdPaths = new HashSet<string>();
            bool cleanupSafe = true;
            var existingPaths = new HashSet<string>(AssetDatabase.FindAssets("", new[] { directory }).Select(AssetDatabase.GUIDToAssetPath));
            try
            {
            foreach (var item in pending)
            {
                Texture2D generated = null;
                try
                {
                    var texture = item.Preview;
                    if (texture == null) texture = generated = Packer.Pack(item.Config);
                    else if (!texture.isReadable) texture = generated = TextureHelper.GetReadableTexture(texture);
                    string key = export == null ? PackedTextureBuildCache.Key(texture, item.Config) : null;
                    var reused = key == null ? null : PackedTextureBuildCache.Find(key, directory);
                    if (reused != null) { saved[item] = reused; continue; }
                    string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + PackedTextureExport.Filename(item.Material, item.Property));
                    while (File.Exists(path) || File.Exists(path + ".meta"))
                        path = directory + "/" + Guid.NewGuid().ToString("N") + ".png";
                    createdPaths.Add(path);
                    var asset = export == null ? PackedTextureExport.Save(texture, path, item.Config) : export(texture, path, item.Config);
                    if (asset == null || !AssetDatabase.Contains(asset)) throw new IOException("The exported texture was not imported as an asset at " + path + ".");
                    string importedPath = AssetDatabase.GetAssetPath(asset);
                    if (importedPath.StartsWith(directory + "/", StringComparison.Ordinal) && !existingPaths.Contains(importedPath)) createdPaths.Add(importedPath);
                    saved[item] = asset;
                    if (key != null) PackedTextureBuildCache.Remember(key, asset);
                }
                catch (Exception exception) { throw Failure(item.Material, item.Property, exception); }
                finally
                {
                    if (generated != null) UnityEngine.Object.DestroyImmediate(generated);
                    foreach (var source in item.Config.Sources) source.DisposeGeneratedTextures();
                }
            }
            // Revalidate the entire set after imports before changing any owner.
            foreach (var item in pending) AssertWritable(item.Material, item.Slots);
            var owners = pending.Select(item => item.Material).Distinct().ToArray();
            var snapshots = owners.ToDictionary(material => material, material => EditorJsonUtility.ToJson(material));
            Undo.RegisterCompleteObjectUndo(owners, "Merge textures before upload");
            try
            {
                foreach (var item in pending)
                {
                    foreach (string slot in item.Slots) item.Material.SetTexture(slot, saved[item]);
                    if (item.Slots.Any(slot => item.Material.GetTexture(slot) != saved[item]))
                        throw Failure(item.Material, item.Property, new InvalidOperationException("The material rejected the saved texture assignment."));
                }
                foreach (var item in pending)
                {
                    item.Material.SetOverrideTag(item.Property + "_texPack_savedInputs", item.Signature);
                    item.Material.SetOverrideTag(item.Property + "_texPack_previewState", "");
                    EditorUtility.SetDirty(item.Material);
                }
                foreach (var material in owners) if (AssetDatabase.Contains(material)) saveMaterial(material);
            }
            catch
            {
                // Preserve texture references, variant overrides and provenance if
                // an owner rejects assignment or its material asset cannot save.
                foreach (var material in owners)
                {
                    try
                    {
                        EditorJsonUtility.FromJsonOverwrite(snapshots[material], material);
                        EditorUtility.SetDirty(material);
                        if (AssetDatabase.Contains(material)) saveMaterial(material);
                    }
                    catch (Exception rollback)
                    {
                        cleanupSafe = false;
                        Debug.LogError("Could not restore material asset '" + material.name + "' after upload preparation failed: " + rollback.Message, material);
                    }
                }
                throw;
            }
            return pending.Count;
            }
            catch
            {
                // These paths were new and owned by this attempt. Existing assets
                // and their import settings are never candidates for cleanup.
                if (!cleanupSafe)
                    Debug.LogWarning("Packed textures were retained in '" + directory + "' because a material could not be restored. Removing them could break its saved texture references.");
                else foreach (string path in createdPaths)
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(path);
                        if (File.Exists(path)) File.Delete(path);
                        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                    }
                    catch (Exception cleanup) { Debug.LogWarning("Could not remove unused packed texture '" + path + "': " + cleanup.Message); }
                }
                throw;
            }
        }

        static Exception Failure(Material material, string property, Exception cause) => new InvalidOperationException(
            "Could not merge the pending texture on material '" + material.name + "', property '" + property + "'. Upload stopped. " + cause.Message, cause);

        static bool HasAssignedPendingPreview(Material material)
        {
            using (var serialized = new SerializedObject(material))
            {
                var tags = serialized.FindProperty("stringTagMap");
                if (tags != null)
                    for (int index = 0; index < tags.arraySize; index++)
                    {
                        string name = tags.GetArrayElementAtIndex(index).displayName;
                        const string markerSuffix = "_texPack_previewState";
                        if (name.EndsWith(markerSuffix, StringComparison.Ordinal)
                            && ThryRGBAPackerDrawer.HasAssignedPendingPreview(material, name.Substring(0, name.Length - markerSuffix.Length))) return true;
                    }
            }
            return false;
        }

        static void AssertWritable(Material material, IEnumerable<string> slots)
        {
#if UNITY_2022_1_OR_NEWER
            if (slots.Any(material.IsPropertyLockedByAncestor))
                throw new InvalidOperationException("Upload stopped: a pending texture on material '" + material.name + "' is locked by its parent material.");
#endif
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path)) return; // Temporary materials produced by the avatar build pipeline.
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsSubAsset(material)
                || !AssetDatabase.IsOpenForEdit(material) || (File.Exists(path) && new FileInfo(path).IsReadOnly))
                throw new InvalidOperationException("Upload stopped: pending texture material '" + material.name + "' is read-only (" + path + "). Use an editable material asset and retry.");
        }

        static string AssetDirectory(string directory)
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string absolute = Path.GetFullPath(Path.IsPathRooted(directory) ? directory : Path.Combine(project, directory)).Replace('\\', '/').TrimEnd('/');
            string assets = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (absolute.Equals(assets, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (!absolute.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase)) throw new IOException("Automatic texture output must be inside Assets.");
            return "Assets" + absolute.Substring(assets.Length);
        }

        static void EnsureDirectory(string directory)
        {
            string parent = "Assets";
            foreach (string segment in directory.Split('/').Skip(1))
            {
                string next = parent + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, segment)))
                    throw new IOException("Could not create the automatic texture output folder " + next + ".");
                parent = next;
            }
        }

        public static Material[] CollectMaterials(IEnumerable<GameObject> roots, IEnumerable<RuntimeAnimatorController> additionalControllers = null)
        {
            var materials = new HashSet<Material>(); var clips = new HashSet<AnimationClip>();
            foreach (var root in roots.Where(root => root != null).Distinct())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials) if (material != null) materials.Add(material);
                    if (renderer is ParticleSystemRenderer particles && particles.trailMaterial != null) materials.Add(particles.trailMaterial);
                }
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true)) if (terrain.materialTemplate != null) materials.Add(terrain.materialTemplate);
                foreach (var skybox in root.GetComponentsInChildren<Skybox>(true)) if (skybox.material != null) materials.Add(skybox.material);
                foreach (var animator in root.GetComponentsInChildren<Animator>(true)) AddClips(animator.runtimeAnimatorController, clips);
                foreach (var animation in root.GetComponentsInChildren<Animation>(true))
                {
                    if (animation.clip != null) clips.Add(animation.clip);
                    foreach (AnimationState state in animation) if (state.clip != null) clips.Add(state.clip);
                }
            }
            if (additionalControllers != null) foreach (var controller in additionalControllers) AddClips(controller, clips);
            foreach (var clip in clips)
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.type == null || !typeof(Renderer).IsAssignableFrom(binding.type) || !binding.propertyName.StartsWith("m_Materials", StringComparison.Ordinal)) continue;
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding)) if (key.value is Material material) materials.Add(material);
                }
            return materials.ToArray();
        }

        static void AddClips(RuntimeAnimatorController controller, HashSet<AnimationClip> clips)
        {
            if (controller == null) return;
            // Unity's effective animationClips includes overrides; do not include
            // replaced clips that cannot be used by this upload target.
            foreach (var clip in controller.animationClips) if (clip != null) clips.Add(clip);
        }
    }
}
#endif
