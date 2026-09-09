#if UNITY_2021_3_OR_NEWER
using System;
using System.Linq;
using Thry.ThryEditor.Helpers;
using Thry.ThryEditor.TexturePacker;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    public partial class ThryRGBAPackerDrawer
    {
        internal static PackedTextureBuildItem PendingBuildTexture(Material material, Shader sourceShader, int propertyIndex)
        {
            string name = sourceShader.GetPropertyName(propertyIndex);
            var pendingSlots = AssignedPreviewSlots(material, name, out var state);
            if (pendingSlots.Length == 0) return null;
            if (state?.inputs == null || state.inputs.Length != 4)
                throw new InvalidOperationException("Pending channel data is incomplete for " + material.name + " / " + name + ". Open the texture card and merge it again.");
            var attribute = sourceShader.GetPropertyAttributes(propertyIndex).Select(value => new DrawerAttribute(value))
                .FirstOrDefault(value => value.Name == "ThryRGBAPacker");
            if (attribute == null || (attribute.Args.Length != 4 && attribute.Args.Length != 6))
                throw new InvalidOperationException("The channel-packer declaration is unavailable for " + material.name + " / " + name + ".");
            var args = attribute.Args;
            var drawer = args.Length == 4 ? new ThryRGBAPackerDrawer(args[0], args[1], args[2], args[3])
                : new ThryRGBAPackerDrawer(args[0], args[1], args[2], args[3], args[4], args[5]);
            var preview = material.GetTexture(pendingSlots[0]) as Texture2D;
            // Interactive previews are deliberately smaller than the authored
            // output. Upload must reconstruct those from their native sources.
            if (state.previewOnly) preview = null;
            var inputs = state.inputs.Select((input, index) =>
            {
                var source = input.source == null ? new PackerSource() : JsonUtility.FromJson<PackerSource>(JsonUtility.ToJson(input.source));
                // Serialized preview provenance can still reference a live studio's
                // generated inputs. This build owns only its regenerated copies.
                source.GradientTexture = null; source.ColorTexture = null;
                // The exact preview can be saved even if an input was removed later.
                // Reconstruction needs each authored asset, never a silent fallback.
                if (source.InputType == InputType.Texture && source.Texture == null)
                {
                    if (!string.IsNullOrEmpty(source.ImageTextureGuid))
                    {
                        source.ResolveImageIdentity();
                        if (source.Texture == null && preview == null)
                            throw new InvalidOperationException("A source texture is missing for " + material.name + " / " + name + " (" + RetainedChannelIds[index] + ").");
                    }
                    else
                    {
                    string guid = material.GetTag(name + "_texPack_" + RetainedChannelIds[index] + "_guid", false, "");
                    if (!string.IsNullOrEmpty(guid))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                        if (asset == null && preview == null) throw new InvalidOperationException("A source texture is missing for " + material.name + " / " + name + " (" + RetainedChannelIds[index] + ").");
                        source.SetInputTexture(asset);
                    }
                    }
                }
                return new InlinePackerChannelConfig { Source = source, Channel = input.channel, Invert = input.invert,
                    Fallback = input.fallback, Remapping = input.remapping };
            }).ToArray();
            var config = drawer.RetainedConfig(inputs);
            if (preview == null) config.RequireResolvedSources();
            if (preview != null) config.FileOutput.FilterMode = preview.filterMode;
            string signature = string.Join("|", RetainedChannelIds.SelectMany(channel => RetainedInputTags.Select(tag =>
                material.GetTag(name + "_texPack_" + channel + "_" + tag, false, ""))));
            return new PackedTextureBuildItem { Material = material, Property = name, Slots = pendingSlots,
                Preview = preview, Config = config, Signature = signature };
        }

        internal static bool HasAssignedPendingPreview(Material material, string name)
            => AssignedPreviewSlots(material, name, out _).Length != 0;

        static string[] AssignedPreviewSlots(Material material, string name, out RetainedPreviewState state)
        {
            state = null;
            string suffix = material.GetTag(ShaderOptimizer.TAG_LOCKED_RENAME_SUFFIX, false, "");
            if (string.IsNullOrEmpty(suffix)) suffix = ShaderOptimizer.GetRenamedPropertySuffix(material);
            var slots = new[] { name, name + "_" + suffix }.Distinct().Where(material.HasProperty)
                .Where(slot => material.GetTexture(slot) is Texture2D texture && texture != null && !AssetDatabase.Contains(texture)).ToArray();
            if (slots.Length == 0) return Array.Empty<string>();
            string json = material.GetTag(name + "_texPack_previewState", false, "");
            if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
            var previewState = JsonUtility.FromJson<RetainedPreviewState>(json);
            state = previewState;
            if (previewState == null) return Array.Empty<string>();
            // Provenance describes authoring settings, not intent to create output.
            // Only persist a generated preview the material actually displays.
            return slots.Where(slot => material.GetTexture(slot) == previewState.texture
                || (!string.IsNullOrEmpty(previewState.previewName) && material.GetTexture(slot).name == previewState.previewName)).ToArray();
        }

        internal static void ClearPendingPreview(MaterialProperty property) => ClearPendingPreview(property, null, false);
        internal static void ClearPendingPreview(MaterialProperty property, Texture expectedTexture) => ClearPendingPreview(property, expectedTexture, true);
        static void ClearPendingPreview(MaterialProperty property, Texture expectedTexture, bool requireAssignment)
        {
            if (property == null) return;
            foreach (var material in property.targets.OfType<Material>())
            {
                if (material == null || !material.HasProperty(property.name)
                    || (requireAssignment && material.GetTexture(property.name) != expectedTexture)) continue;
                string canonicalName = property.name;
                if (ShaderOptimizer.IsShaderLocked(material.shader))
                {
                    string suffix = material.GetTag(ShaderOptimizer.TAG_LOCKED_RENAME_SUFFIX, false, "");
                    if (string.IsNullOrEmpty(suffix)) suffix = ShaderOptimizer.GetRenamedPropertySuffix(material);
                    string ending = "_" + suffix;
                    var sourceShader = ShaderOptimizer.GetOriginalShader(material, false);
                    if (sourceShader != null && canonicalName.EndsWith(ending, StringComparison.Ordinal))
                    {
                        string originalName = canonicalName.Substring(0, canonicalName.Length - ending.Length);
                        if (sourceShader.FindPropertyIndex(originalName) >= 0) canonicalName = originalName;
                    }
                }
                string tag = canonicalName + "_texPack_previewState";
                if (string.IsNullOrEmpty(material.GetTag(tag, false, ""))) continue;
                Undo.RegisterCompleteObjectUndo(material, "Assign texture");
                material.SetOverrideTag(tag, "");
            }
        }
    }
}
#endif
