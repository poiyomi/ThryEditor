using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Thry.ThryEditor.TexturePacker;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Stored on the new texture importer so repeat bakes can read the original file.
    [Serializable]
    internal sealed class MaskBakeRecipe
    {
        public string kind;
        public int version;
        public string sourceGuid;
        public List<MaskBakeStage> stages = new List<MaskBakeStage>();
    }

    [Serializable]
    internal sealed class MaskBakeStage
    {
        public int channels;
        public Vector4[] values;
    }

    internal static class MaskLevelsBaker
    {
        internal static string UnavailableReason(Material[] materials, string property)
        {
            if (materials.Length == 0) return "Select a material to bake.";
            if (AnimationMode.InAnimationMode()) return "Exit animation editing before baking.";
            foreach (var material in materials)
            {
                if (ShaderOptimizer.IsShaderLocked(material.shader)) return "Unlock the material before baking.";
                var texture = material.GetTexture(property) as Texture2D;
                if (texture == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                    return "Assign a saved 2D texture to bake.";
                // Scalar legacy Invert remains in the shader, so it may stay animated.
                foreach (var name in new[] { property }.Concat(Enumerable.Range(0, 7)
                    .Select(i => MaskLevelsData.Name(property, i)).Where(material.HasProperty)))
                {
                    string tag = material.GetTag(name + ShaderOptimizer.AnimatedTagSuffix, false, "");
                    if (!string.IsNullOrEmpty(tag) && tag != "0") return "Disable animation on the texture and adjustments before baking.";
                }
            }
            return null;
        }

        internal static MaskBakeStage Capture(Material material, string property, int channels)
        {
            var stage = new MaskBakeStage { channels = channels, values = (Vector4[])MaskLevelsData.Defaults.Clone() };
            for (int i = 0; i < stage.values.Length; i++)
            {
                string name = MaskLevelsData.Name(property, i);
                if (material.HasProperty(name)) stage.values[i] = material.GetVector(name);
            }
            if (stage.values[6] != Vector4.zero)
                MaskLevelsData.Measure(material.GetTexture(property), out stage.values[7], out stage.values[8]);
            return stage;
        }

        internal static void Reset(Material material, string property, int channels)
        {
            for (int i = 0; i < MaskLevelsData.Suffixes.Length; i++)
            {
                string name = MaskLevelsData.Name(property, i);
                if (!material.HasProperty(name)) continue;
                var value = material.GetVector(name);
                for (int c = 0; c < 4; c++) if ((channels & (1 << c)) != 0) value[c] = MaskLevelsData.Defaults[i][c];
                material.SetVector(name, value);
            }
        }

        internal static Dictionary<Material, Texture2D> Export(Material[] materials, string property, int channels)
        {
            string reason = UnavailableReason(materials, property);
            if (reason != null) throw new InvalidOperationException(reason);
            var result = new Dictionary<Material, Texture2D>();
            var shared = new Dictionary<string, Texture2D>();
            try
            {
                foreach (var material in materials)
                {
                    var source = (Texture2D)material.GetTexture(property);
                    var stage = Capture(material, property, channels);
                    string key = AssetDatabase.GetAssetPath(source) + JsonUtility.ToJson(stage);
                    if (!shared.TryGetValue(key, out var baked))
                    {
                        baked = ExportTexture(source, stage);
                        shared.Add(key, baked);
                    }
                    result.Add(material, baked);
                }
                return result;
            }
            catch
            {
                foreach (var texture in shared.Values) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(texture));
                throw;
            }
        }

        internal static Texture2D ExportTexture(Texture2D source, MaskBakeStage stage)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            if (sourceImporter == null) throw new InvalidOperationException("Bake requires an imported texture file.");
            var recipe = ReadRecipe(sourceImporter.userData);
            if (recipe == null)
                recipe = new MaskBakeRecipe { kind = "PoiyomiMaskBake", version = 1, sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath) };
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(recipe.sourceGuid));
            if (original == null) throw new InvalidOperationException("The original source texture is missing.");
            recipe.stages.Add(stage);
            var packerSource = new PackerSource();
            packerSource.SetInputTexture(original);
            using (PackerSource.KeepDecodedSources(new[] { packerSource }))
            {
                var decoded = packerSource.UncompressedTexture;
                if (decoded == null) throw new InvalidOperationException("The original texture could not be decoded.");
                var originalImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(original));
                bool decodeSrgb = decoded != original && originalImporter.sRGBTexture && QualitySettings.activeColorSpace == ColorSpace.Linear;
                // HDR sources need an HDR output even when untouched channels carry the range.
                bool hdr = original.graphicsFormat.ToString().Contains("SFloat") || original.graphicsFormat.ToString().Contains("UFloat");
                var image = Render(decoded, recipe.stages, decodeSrgb, hdr);
                try
                {
                    string folder = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
                    if (!folder.StartsWith("Assets/", StringComparison.Ordinal) && folder != "Assets") folder = "Assets";
                    string extension = hdr ? ".exr" : ".png";
                    string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "_baked" + extension);
                    // Never replace a source or previous bake, even if an external process creates the path.
                    if (File.Exists(path)) throw new IOException("The bake destination already exists: " + path);
                    var bytes = hdr ? image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP) : image.EncodeToPNG();
                    AtomicTextureExport.WriteNew(path, bytes, p =>
                    {
                        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
                        var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                        if (importer == null) return null;
                        importer.textureType = TextureImporterType.Default;
                        importer.sRGBTexture = false;
                        importer.alphaIsTransparency = false;
                        importer.alphaSource = TextureImporterAlphaSource.FromInput;
                        importer.npotScale = TextureImporterNPOTScale.None;
                        importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(image.width, image.height)), 32, 16384);
                        importer.filterMode = sourceImporter.filterMode;
                        importer.wrapModeU = sourceImporter.wrapModeU;
                        importer.wrapModeV = sourceImporter.wrapModeV;
                        importer.anisoLevel = sourceImporter.anisoLevel;
                        importer.mipmapEnabled = sourceImporter.mipmapEnabled;
                        importer.streamingMipmaps = sourceImporter.streamingMipmaps;
                        importer.mipMapBias = sourceImporter.mipMapBias;
                        // Fixed red-only masks can use one stored channel. Packed and mixed-use
                        // textures retain RGBA, including non-mask data.
                        if (stage.channels == 1 && !hdr)
                        {
                            var settings = new TextureImporterSettings();
                            importer.ReadTextureSettings(settings);
                            settings.textureType = TextureImporterType.SingleChannel;
                            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                            importer.SetTextureSettings(settings);
                            var platform = importer.GetDefaultPlatformTextureSettings();
                            platform.format = TextureImporterFormat.R8;
                            importer.SetPlatformTextureSettings(platform);
                        }
                        else
                        {
                            importer.textureCompression = sourceImporter.textureCompression;
                            importer.compressionQuality = sourceImporter.compressionQuality;
                            importer.crunchedCompression = !hdr && sourceImporter.crunchedCompression;
                        }
                        importer.userData = JsonUtility.ToJson(recipe);
                        importer.SaveAndReimport();
                        if (AssetDatabase.LoadAssetAtPath<Texture2D>(p) == null)
                            throw new IOException("The baked texture could not be loaded: " + p);
                        return importer;
                    });
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
        }

        static MaskBakeRecipe ReadRecipe(string json)
        {
            if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{")) return null;
            MaskBakeRecipe recipe;
            try { recipe = JsonUtility.FromJson<MaskBakeRecipe>(json); }
            catch (ArgumentException) { return null; }
            if (recipe == null || recipe.kind != "PoiyomiMaskBake") return null;
            if (recipe.version != 1 || recipe.stages == null || string.IsNullOrEmpty(recipe.sourceGuid))
                throw new InvalidOperationException("This texture's bake history cannot be read.");
            return recipe;
        }

        internal static Texture2D Render(Texture source, List<MaskBakeStage> stages, bool decodeSrgb, bool hdr)
        {
            var shader = Shader.Find("Hidden/Thry/MaskBake");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The mask bake shader is unavailable.");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var previous = RenderTexture.active;
            RenderTexture current = null;
            Texture2D image = null;
            try
            {
                Texture input = source;
                foreach (var stage in stages)
                {
                    var v = stage.values;
                    if (v == null || v.Length != 9) throw new InvalidOperationException("The mask bake history contains an invalid adjustment.");
                    for (int i = 0; i < 9; i++) material.SetVector("_" + MaskLevelsData.Suffixes[i], v[i]);
                    material.SetInt("_Channels", stage.channels);
                    material.SetFloat("_DecodeSRGB", decodeSrgb ? 1 : 0);
                    var next = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    next.filterMode = FilterMode.Point;
                    try { Graphics.Blit(input, next, material); }
                    catch { RenderTexture.ReleaseTemporary(next); throw; }
                    if (current != null) RenderTexture.ReleaseTemporary(current);
                    current = next; input = next; decodeSrgb = false;
                }
                RenderTexture.active = current;
                image = new Texture2D(source.width, source.height, hdr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, true);
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                image.Apply(false);
                return image;
            }
            catch { if (image != null) UnityEngine.Object.DestroyImmediate(image); throw; }
            finally
            {
                RenderTexture.active = previous;
                if (current != null) RenderTexture.ReleaseTemporary(current);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
