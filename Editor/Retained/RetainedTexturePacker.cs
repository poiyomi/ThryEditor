#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Thry.ThryEditor.Helpers;
using Thry.ThryEditor.TexturePacker;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    public partial class ThryRGBAPackerDrawer
    {
        RetainedMaterialModel _retainedModel;
        ShaderTextureProperty _retainedProperty;
        readonly Dictionary<Material, Texture> _retainedOriginals = new Dictionary<Material, Texture>();
        readonly Dictionary<Material, string> _retainedSavedInputs = new Dictionary<Material, string>();
        // Undo references these generated objects and their input snapshots. They
        // must outlive inspector detachment; Unity's Undo owns their lifetime.
        readonly Dictionary<Texture2D, InlinePackerChannelConfig[]> _retainedPreviewInputs = new Dictionary<Texture2D, InlinePackerChannelConfig[]>();
        readonly Dictionary<Texture2D, Texture> _retainedPreviewOrigins = new Dictionary<Texture2D, Texture>();
        readonly Dictionary<Material, InlinePackerChannelConfig[]> _retainedDisplayInputs = new Dictionary<Material, InlinePackerChannelConfig[]>();
        readonly Dictionary<Material, string> _retainedDisplaySignatures = new Dictionary<Material, string>();
        readonly Dictionary<Material, int> _retainedObservedVersions = new Dictionary<Material, int>();
        static readonly string[] RetainedChannelIds = { "r", "g", "b", "a" };
        static readonly string[] RetainedInputTags = { "guid", "fallback", "inverted", "channel", "srcRange" };

        Material[] RetainedTargets() => _retainedProperty.MaterialProperty.targets.OfType<Material>()
            .Where(material => material != null && _retainedModel.Shader.Materials.Contains(material)
                && material.HasProperty(_retainedProperty.MaterialProperty.name)).Distinct().ToArray();

        void InitializeRetainedPacker(RetainedMaterialModel model, ShaderTextureProperty property)
        {
            _retainedModel = model; _retainedProperty = property;
            foreach (var material in RetainedTargets())
            {
                _retainedOriginals[material] = material.GetTexture(property.MaterialProperty.name);
                _retainedSavedInputs[material] = RetainedInputSignature(material);
            }
        }

        string RetainedInputSignature(Material material) => string.Join("|", RetainedChannelIds.SelectMany(channel =>
            RetainedInputTags.Select(tag => material.GetTag(_retainedProperty.MaterialProperty.name + "_texPack_" + channel + "_" + tag, false, ""))));

        static InlinePackerChannelConfig CopyChannel(InlinePackerChannelConfig input)
        {
            // Input objects are mutable; sharing them with a studio draft or another
            // material would make Cancel and unrelated channel edits change state.
            var source = JsonUtility.FromJson<PackerSource>(JsonUtility.ToJson(input.Source));
            source.GradientTexture = null; source.ColorTexture = null;
            return new InlinePackerChannelConfig { Source = source, Channel = input.Channel,
                Invert = input.Invert, Fallback = input.Fallback, Remapping = input.Remapping };
        }

        InlinePackerChannelConfig[] RetainedInputs(Material material)
        {
            var texture = material.GetTexture(_retainedProperty.MaterialProperty.name) as Texture2D;
            InlinePackerChannelConfig[] inputs;
            if (texture != null && _retainedPreviewInputs.TryGetValue(texture, out inputs)) return inputs.Select(CopyChannel).ToArray();
            string signature;
            if (_retainedDisplayInputs.TryGetValue(material, out inputs) && _retainedDisplaySignatures.TryGetValue(material, out signature)
                && signature == RetainedInputSignature(material)) return inputs.Select(CopyChannel).ToArray();
            return RetainedChannelIds.Select(channel => LoadForChannel(material, _retainedProperty.MaterialProperty.name, channel)).ToArray();
        }

        TexturePackerConfig RetainedConfig(InlinePackerChannelConfig[] inputs)
        {
            var original = _current;
            try
            {
                _current = new ThryRGBAPackerData { _input_r = inputs[0], _input_g = inputs[1], _input_b = inputs[2], _input_a = inputs[3] };
                return GetConfig();
            }
            finally { _current = original; }
        }

        internal TexturePackerConfig RetainedStudioConfig()
        {
            var draft = JsonUtility.FromJson<TexturePackerConfig>(JsonUtility.ToJson(GetConfig()));
            foreach (var source in draft.Sources) { source.GradientTexture = null; source.ColorTexture = null; }
            return draft;
        }

        void SaveRetainedChannel(Material material, int index, InlinePackerChannelConfig input)
        {
            string prefix = _retainedProperty.MaterialProperty.name + "_texPack_" + RetainedChannelIds[index] + "_";
            material.SetOverrideTag(prefix + "guid", input.Source.Texture == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(input.Source.Texture)));
            material.SetOverrideTag(prefix + "fallback", input.Fallback.ToString(CultureInfo.InvariantCulture));
            material.SetOverrideTag(prefix + "inverted", input.Invert.ToString());
            material.SetOverrideTag(prefix + "channel", ((int)input.Channel).ToString(CultureInfo.InvariantCulture));
            material.SetOverrideTag(prefix + "srcRange", "(" + string.Join(",", Enumerable.Range(0, 4).Select(i => input.Remapping[i].ToString(CultureInfo.InvariantCulture))) + ")");
        }

        void ChangeRetainedChannel(int index, Action<InlinePackerChannelConfig> mutation)
        {
            if (!_retainedModel.CanEdit(_retainedProperty)) return;
            var targets = new HashSet<Material>(RetainedTargets());
            _retainedModel.Edit(_retainedProperty, property =>
            {
                var material = property.targets.OfType<Material>().FirstOrDefault();
                if (material == null || !targets.Contains(material)) return;
                var original = material.GetTexture(property.name);
                if (!(original is Texture2D oldPreview) || !_retainedPreviewInputs.ContainsKey(oldPreview)) _retainedOriginals[material] = original;
                var inputs = RetainedInputs(material); mutation(inputs[index]);
                var texture = Packer.Pack(RetainedConfig(inputs));
                texture.hideFlags = HideFlags.DontSave;
                Undo.RegisterCreatedObjectUndo(texture, "Edit texture channels");
                // MaterialProperty assignment records its own native property change.
                // Keep a complete snapshot before tag writes so the separate tag map
                // is restored too, after generated-object registration flushes records.
                Undo.RegisterCompleteObjectUndo(material, "Edit texture channels");
                _retainedPreviewInputs[texture] = inputs.Select(CopyChannel).ToArray();
                _retainedPreviewOrigins[texture] = original is Texture2D previous && _retainedPreviewOrigins.ContainsKey(previous)
                    ? _retainedPreviewOrigins[previous] : original;
                SaveRetainedChannel(material, index, inputs[index]);
                property.textureValue = texture;
            }, true);
            RefreshRetainedPacker();
        }

        void RefreshRetainedPacker()
        {
            _retainedModel.Shader.ActivateRetained(); _prop = _retainedProperty.MaterialProperty;
            var targets = RetainedTargets(); if (targets.Length == 0) return;
            foreach (var material in targets)
            {
                _retainedDisplayInputs[material] = RetainedInputs(material);
                _retainedDisplaySignatures[material] = RetainedInputSignature(material);
                _retainedObservedVersions[material] = EditorUtility.GetDirtyCount(material);
            }
            var restored = _retainedDisplayInputs[targets[0]];
            var current = new[] { _current._input_r, _current._input_g, _current._input_b, _current._input_a };
            for (int index = 0; index < current.Length; index++)
            {
                current[index].Source = restored[index].Source; current[index].Channel = restored[index].Channel;
                current[index].Invert = restored[index].Invert; current[index].Fallback = restored[index].Fallback; current[index].Remapping = restored[index].Remapping;
            }
            _current._isInit = true;
            _current._packedTexture = _prop.textureValue as Texture2D;
            _current._hasTextureChanged = targets.Any(material => material.GetTexture(_prop.name) is Texture2D texture && _retainedPreviewInputs.ContainsKey(texture));
            _current._hasConfigChanged = targets.Any(material => !_retainedSavedInputs.ContainsKey(material) || _retainedSavedInputs[material] != RetainedInputSignature(material));
        }

        void RefreshRetainedPackerIfChanged()
        {
            if (RetainedTargets().Any(material => !_retainedObservedVersions.ContainsKey(material)
                || _retainedObservedVersions[material] != EditorUtility.GetDirtyCount(material))) RefreshRetainedPacker();
        }

        bool RetainedChannelMixed(int index, Func<InlinePackerChannelConfig, object> value) => RetainedTargets()
            .Where(material => _retainedDisplayInputs.ContainsKey(material)).Select(material => value(_retainedDisplayInputs[material][index])).Distinct().Skip(1).Any();

        void RevertRetainedPacker()
        {
            var targets = new HashSet<Material>(RetainedTargets());
            _retainedModel.Edit(_retainedProperty, property =>
            {
                var material = property.targets.OfType<Material>().FirstOrDefault();
                Texture original;
                if (material == null || !targets.Contains(material)) return;
                var preview = material.GetTexture(property.name) as Texture2D;
                if (preview != null && _retainedPreviewOrigins.TryGetValue(preview, out original)) property.textureValue = original;
                else if (_retainedOriginals.TryGetValue(material, out original)) property.textureValue = original;
            }, true);
            RefreshRetainedPacker();
        }

        static string RetainedAssetDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return "Assets/Textures/Packed";
            string absolute = System.IO.Path.GetFullPath(directory).Replace('\\', '/').TrimEnd('/');
            string assets = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (absolute.Equals(assets, StringComparison.OrdinalIgnoreCase)) return "Assets";
            return absolute.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) ? "Assets" + absolute.Substring(assets.Length) : null;
        }

        void MergeRetainedPacker()
        {
            if (!_retainedModel.CanEdit(_retainedProperty) || !_current._hasConfigChanged) return;
            string prompted = null;
            if (Config.Instance.inlinePackerSaveLocation == TextureSaveLocation.prompt)
            {
                prompted = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
                if (string.IsNullOrEmpty(prompted)) return;
            }
            var directories = new Dictionary<Material, string>();
            foreach (var material in RetainedTargets())
            {
                string directory;
                switch (Config.Instance.inlinePackerSaveLocation)
                {
                    case TextureSaveLocation.material: directory = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)); break;
                    case TextureSaveLocation.texture:
                        var source = RetainedInputs(material).Select(input => input.Source.Texture).FirstOrDefault(texture => texture != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)));
                        directory = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(source != null ? (UnityEngine.Object)source : material)); break;
                    case TextureSaveLocation.custom: directory = Config.Instance.inlinePackerSaveLocationCustom; break;
                    case TextureSaveLocation.prompt: directory = prompted; break;
                    default: directory = "Assets/Textures/Packed"; break;
                }
                directory = RetainedAssetDirectory(directory);
                if (directory == null)
                {
                    EditorUtility.DisplayDialog("Save packed texture", "Choose a folder inside this project's Assets folder.", "OK"); return;
                }
                directories[material] = directory;
            }
            _retainedModel.Edit(_retainedProperty, property =>
            {
                var material = property.targets.OfType<Material>().FirstOrDefault();
                if (material == null || !directories.ContainsKey(material)) return;
                var config = RetainedConfig(RetainedInputs(material));
                var texture = Packer.Pack(config);
                try
                {
                    string filename = material.name + property.name;
                    foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) filename = filename.Replace(invalid, '_');
                    string path = AssetDatabase.GenerateUniqueAssetPath(directories[material] + "/" + filename + ".png");
                    var asset = TextureHelper.SaveTextureAsPNG(texture, path);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        importer.streamingMipmaps = true; importer.crunchedCompression = Config.Instance.inlinePackerChrunchCompression;
                        importer.sRGBTexture = _colorSpace == ColorSpace.Gamma; importer.filterMode = config.FileOutput.FilterMode;
                        importer.alphaIsTransparency = _alphaIsTransparency; importer.SaveAndReimport();
                    }
                    property.textureValue = asset;
                    _retainedSavedInputs[material] = RetainedInputSignature(material);
                }
                finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
            }, true);
            RefreshRetainedPacker();
        }

        void ClearRetainedPacker()
        {
            var targets = new HashSet<Material>(RetainedTargets());
            _retainedModel.Edit(_retainedProperty, property =>
            {
                var material = property.targets.OfType<Material>().FirstOrDefault();
                if (material == null || !targets.Contains(material)) return;
                Undo.RegisterCompleteObjectUndo(material, "Clear texture channels");
                float fallback = Parser.ParseFloat(GetDefaultFallback(material, property.name));
                for (int i = 0; i < 4; i++) SaveRetainedChannel(material, i, new InlinePackerChannelConfig { Fallback = fallback });
                property.textureValue = null;
            }, true);
            RefreshRetainedPacker();
        }
    }
}
#endif
