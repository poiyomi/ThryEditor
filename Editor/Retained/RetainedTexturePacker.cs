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
        UnityEngine.UIElements.Label _retainedPackerMessage;
        void RunRetainedPackerAction(Action action)
        {
            try { action(); if (_retainedPackerMessage != null) _retainedPackerMessage.style.display = UnityEngine.UIElements.DisplayStyle.None; }
            catch (ExitGUIException) { throw; }
            catch (Exception exception)
            {
                if (_retainedPackerMessage == null) throw;
                _retainedPackerMessage.text = exception.Message;
                _retainedPackerMessage.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
            }
        }
        readonly Dictionary<Material, string> _retainedSavedInputs = new Dictionary<Material, string>();
        // Undo references these generated objects and their input snapshots. They
        // must outlive inspector detachment; Unity's Undo owns their lifetime.
        readonly Dictionary<Texture2D, InlinePackerChannelConfig[]> _retainedPreviewInputs = new Dictionary<Texture2D, InlinePackerChannelConfig[]>();
        readonly Dictionary<Texture2D, Texture> _retainedPreviewOrigins = new Dictionary<Texture2D, Texture>();
        [Serializable] sealed class RetainedPreviewState
        {
            public Texture2D texture;
            public string previewName;
            public bool previewOnly;
            public Texture origin;
            public string originGuid;
            public long originLocalId;
            public RetainedChannelState[] inputs;
        }
        [Serializable] sealed class RetainedChannelState
        {
            public PackerSource source;
            public TextureChannelIn channel;
            public bool invert;
            public float fallback;
            public Vector4 remapping;
        }
        string PreviewStateTag => _retainedProperty.MaterialProperty.name + "_texPack_previewState";
        RetainedPreviewState PendingState(Material material)
        {
            string json = material.GetTag(PreviewStateTag, false, "");
            if (string.IsNullOrEmpty(json)) return null;
            RetainedPreviewState state;
            try { state = JsonUtility.FromJson<RetainedPreviewState>(json); }
            catch (ArgumentException) { return null; }
            if (state?.inputs == null || state.inputs.Length != 4 || state.inputs.Any(input => input == null)) return null;
            var current = material.GetTexture(_retainedProperty.MaterialProperty.name);
            if (current != null && (AssetDatabase.Contains(current) || (state.texture != current && state.previewName != current.name))) return null;
            return state;
        }

        static Texture PreviewOrigin(RetainedPreviewState state)
        {
            if (state.origin != null) return state.origin;
            if (string.IsNullOrEmpty(state.originGuid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(state.originGuid);
            if (string.IsNullOrEmpty(path)) return null;
            if (state.originLocalId == 0) return AssetDatabase.LoadAssetAtPath<Texture>(path);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture>().FirstOrDefault(texture =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId)
                && guid == state.originGuid && localId == state.originLocalId);
        }

        InlinePackerChannelConfig[] PendingInputs(Material material, RetainedPreviewState state) => state.inputs.Select((input, index) =>
        {
            var source = input.source == null ? new PackerSource() : JsonUtility.FromJson<PackerSource>(JsonUtility.ToJson(input.source));
            source.GradientTexture = null; source.ColorTexture = null;
            if (source.InputType == InputType.Texture && source.ImageTexture == null)
            {
                if (!string.IsNullOrEmpty(source.ImageTextureGuid)) source.ResolveImageIdentity();
                else
                {
                    string guid = material.GetTag(_retainedProperty.MaterialProperty.name + "_texPack_" + RetainedChannelIds[index] + "_guid", false, "");
                    if (!string.IsNullOrEmpty(guid))
                    {
                        source.SetInputTexture(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid)));
                        if (source.ImageTexture == null) source.MissingImageReference = true;
                    }
                }
            }
            return new InlinePackerChannelConfig { Source = source, Channel = input.channel, Invert = input.invert, Fallback = input.fallback, Remapping = input.remapping };
        }).ToArray();

        bool HasRetainedPreview(Texture2D texture)
        {
            if (texture == null) return false;
            var material = RetainedTargets().FirstOrDefault(owner => owner.GetTexture(_retainedProperty.MaterialProperty.name) == texture);
            var state = material == null ? null : PendingState(material);
            if (state == null) return false;
            _retainedPreviewOrigins[texture] = PreviewOrigin(state);
            _retainedPreviewInputs[texture] = PendingInputs(material, state);
            return true;
        }
        void RememberRetainedPreview(Material material, Texture2D texture, Texture origin, InlinePackerChannelConfig[] inputs)
        {
            _retainedPreviewInputs[texture] = inputs.Select(CopyChannel).ToArray();
            _retainedPreviewOrigins[texture] = origin;
            string originGuid = ""; long originLocalId = 0;
            if (origin != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(origin, out originGuid, out originLocalId);
            // One material-owned snapshot survives inspector recreation and follows
            // Undo/Redo. Replacing it never accumulates global cache or session keys.
            material.SetOverrideTag(PreviewStateTag, JsonUtility.ToJson(new RetainedPreviewState
            {
                texture = texture, previewName = texture.name, previewOnly = true, origin = origin,
                originGuid = originGuid, originLocalId = originLocalId,
                inputs = inputs.Select(CopyChannel).Select(input => new RetainedChannelState { source = input.Source, channel = input.Channel,
                    invert = input.Invert, fallback = input.Fallback, remapping = input.Remapping }).ToArray()
            }));
        }
        internal Func<Texture2D, string, Texture> SavePackedTexture = (texture, path) => TextureHelper.SaveTextureAsPNG(texture, path);
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
                _retainedSavedInputs[material] = material.GetTag(SavedInputsTag, false, RetainedInputSignature(material));
            }
        }

        string RetainedInputSignature(Material material) => string.Join("|", RetainedChannelIds.SelectMany(channel =>
            RetainedInputTags.Select(tag => material.GetTag(_retainedProperty.MaterialProperty.name + "_texPack_" + channel + "_" + tag, false, ""))));
        string SavedInputsTag => _retainedProperty.MaterialProperty.name + "_texPack_savedInputs";
        void PreserveSavedInputs(Material material)
        {
            if (string.IsNullOrEmpty(material.GetTag(SavedInputsTag, false, "")))
                material.SetOverrideTag(SavedInputsTag, RetainedInputSignature(material));
        }

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
            var pending = PendingState(material);
            if (pending != null) return PendingInputs(material, pending);
            var texture = material.GetTexture(_retainedProperty.MaterialProperty.name) as Texture2D;
            InlinePackerChannelConfig[] inputs;
            if (HasRetainedPreview(texture)) return _retainedPreviewInputs[texture].Select(CopyChannel).ToArray();
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

        sealed class PreparedChannelEdit
        {
            internal Texture Origin;
            internal InlinePackerChannelConfig[] Inputs;
            internal TexturePackerConfig Config;
            internal Texture2D Preview;
            internal bool Assigned;
        }

        void ChangeRetainedChannel(int index, Action<InlinePackerChannelConfig> mutation)
        {
            if (!_retainedModel.CanEdit(_retainedProperty) || AnimationMode.InAnimationMode()) return;
            _retainedModel.Refresh();
            var prepared = new Dictionary<Material, PreparedChannelEdit>();
            try
            {
                // Prepare every owner's independent sources before recording Undo or changing
                // any tags. A missing source on a later owner must leave the entire selection intact.
                foreach (var material in RetainedTargets())
                {
                    var pending = PendingState(material);
                    var inputs = RetainedInputs(material); mutation(inputs[index]);
                    var edit = new PreparedChannelEdit {
                        Origin = pending != null ? PreviewOrigin(pending) : material.GetTexture(_retainedProperty.MaterialProperty.name),
                        Inputs = inputs, Config = RetainedConfig(inputs)
                    };
                    prepared.Add(material, edit);
                    edit.Config.RequireResolvedSources();
                    Packer.DetermineOutputResolution(edit.Config);
                    float scale = Mathf.Min(1, 512f / Mathf.Max(edit.Config.FileOutput.Resolution.x, edit.Config.FileOutput.Resolution.y));
                    edit.Config.FileOutput.Resolution = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(edit.Config.FileOutput.Resolution.x * scale)),
                        Mathf.Max(1, Mathf.RoundToInt(edit.Config.FileOutput.Resolution.y * scale)));
                    edit.Config.FileOutput.CustomResolution = true;
                }
                foreach (var edit in prepared.Values)
                {
                    edit.Preview = Packer.Pack(edit.Config);
                    edit.Preview.hideFlags = HideFlags.DontSave;
                    edit.Preview.name = "Thry packed preview " + Guid.NewGuid().ToString("N");
                }
                _retainedModel.Edit(_retainedProperty, property =>
                {
                    var material = property.targets.OfType<Material>().FirstOrDefault();
                    if (material == null || !prepared.TryGetValue(material, out var edit)) return;
                    Undo.RegisterCreatedObjectUndo(edit.Preview, "Edit texture channels");
                    // Complete snapshots preserve the separate tag map alongside native property Undo.
                    Undo.RegisterCompleteObjectUndo(material, "Edit texture channels");
                    RememberRetainedPreview(material, edit.Preview, edit.Origin, edit.Inputs);
                    PreserveSavedInputs(material);
                    SaveRetainedChannel(material, index, edit.Inputs[index]);
                    property.textureValue = edit.Preview;
                    edit.Assigned = true;
                }, true);
            }
            finally
            {
                foreach (var edit in prepared.Values)
                {
                    foreach (var source in edit.Config.Sources) source.DisposeGeneratedTextures();
                    if (!edit.Assigned && edit.Preview != null) UnityEngine.Object.DestroyImmediate(edit.Preview);
                }
            }
            RefreshRetainedPacker();
        }

        void RefreshRetainedPacker()
        {
            _retainedModel.Shader.ActivateRetained(); _prop = _retainedProperty.MaterialProperty;
            var targets = RetainedTargets(); if (targets.Length == 0) return;
            foreach (var preview in _retainedPreviewInputs.Keys.Where(texture => texture == null).ToArray())
            { _retainedPreviewInputs.Remove(preview); _retainedPreviewOrigins.Remove(preview); }
            foreach (var material in _retainedDisplayInputs.Keys.Where(material => material == null || !targets.Contains(material)).ToArray())
            {
                _retainedDisplayInputs.Remove(material); _retainedDisplaySignatures.Remove(material);
                _retainedObservedVersions.Remove(material); _retainedSavedInputs.Remove(material);
            }
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
            _current._hasTextureChanged = targets.Any(material => PendingState(material) != null);
            _current._hasConfigChanged = _current._hasTextureChanged || targets.Any(material =>
                material.GetTag(SavedInputsTag, false, _retainedSavedInputs.TryGetValue(material, out var baseline) ? baseline : "") != RetainedInputSignature(material));
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
                if (material == null || !targets.Contains(material)) return;
                var pending = PendingState(material);
                if (pending != null)
                {
                    Undo.RegisterCompleteObjectUndo(material, "Revert texture preview");
                    property.textureValue = PreviewOrigin(pending); material.SetOverrideTag(PreviewStateTag, "");
                }
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

        static string UnusedRetainedMergePath(string requested)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(requested);
            while (System.IO.File.Exists(path) || System.IO.File.Exists(path + ".meta"))
                path = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(requested, null)
                    + "_" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(requested));
            return path;
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
            var saved = new Dictionary<Material, Texture>();
            var stagedPaths = new List<string>();
            // Export the whole selection before mutating any material. A failed
            // later export must not leave an earlier owner falsely marked saved.
            try
            {
            foreach (var material in directories.Keys)
            {
                var config = RetainedConfig(RetainedInputs(material));
                Texture2D texture = null;
                try
                {
                    config.RequireResolvedSources();
                    texture = Packer.Pack(config);
                    string path = UnusedRetainedMergePath(directories[material] + "/" + PackedTextureExport.Filename(material, _retainedProperty.MaterialProperty.name));
                    stagedPaths.Add(path);
                    saved[material] = PackedTextureExport.Save(texture, path, config, SavePackedTexture);
                }
                finally
                {
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                    foreach (var source in config.Sources) source.DisposeGeneratedTextures();
                }
            }
            }
            catch
            {
                foreach (string path in stagedPaths)
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(path);
                        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                        if (System.IO.File.Exists(path + ".meta")) System.IO.File.Delete(path + ".meta");
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogWarning("Could not remove temporary packed texture '" + path + "': " + cleanupError.Message);
                    }
                }
                throw;
            }
            _retainedModel.Edit(_retainedProperty, property =>
            {
                var material = property.targets.OfType<Material>().FirstOrDefault();
                if (material == null || !saved.ContainsKey(material)) return;
                Undo.RegisterCompleteObjectUndo(material, "Save texture channels");
                property.textureValue = saved[material];
                material.SetOverrideTag(SavedInputsTag, RetainedInputSignature(material));
                material.SetOverrideTag(PreviewStateTag, "");
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
                PreserveSavedInputs(material);
                material.SetOverrideTag(PreviewStateTag, "");
                float fallback = Parser.ParseFloat(GetDefaultFallback(material, property.name));
                for (int i = 0; i < 4; i++) SaveRetainedChannel(material, i, new InlinePackerChannelConfig { Fallback = fallback });
                property.textureValue = null;
            }, true);
            RefreshRetainedPacker();
        }
    }
}
#endif
