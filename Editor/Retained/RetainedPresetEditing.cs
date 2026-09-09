#if UNITY_2021_3_OR_NEWER
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public partial class Presets
    {
        internal static VisualElement CreateEditorControls(RetainedMaterialModel model, RetainedFields fields)
        {
            var root = new VisualElement();
            var shader = model.Shader;
            if (shader.IsPresetEditor)
            {
                root.Add(new HelpBox(RetainedText.Get("preset_material_notify", "Editing a preset material. Use property actions to choose which settings it contains."), HelpBoxMessageType.Info));
                var sectioned = new Toggle(RetainedText.Get("preset_section_preset", "Section preset")) { value = IsMaterialSectionedPreset(shader.Materials[0]) };
                root.Add(sectioned);
                sectioned.RegisterValueChangedCallback(e => { model.Mutate("Preset type", m => SetMaterialSectionedPreset(m, e.newValue)); shader.Reload(); model.Notify(); });
                if (!sectioned.value)
                {
                    var name = new TextField(RetainedText.Get("preset_name", "Preset name")) { value = shader.Materials[0].GetTag(TAG_MATERIAL_PRESET_NAME, false, ""), isDelayed = true };
                    root.Add(name);
                    name.RegisterValueChangedCallback(e =>
                    {
                        InitializeDataStructures();
                        model.Mutate("Preset name", m =>
                        {
                            m.SetOverrideTag(TAG_MATERIAL_PRESET_NAME, e.newValue);
                            FullPresets.AddOrUpdate(e.newValue, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m)));
                        });
                        Save();
                    });
                }
            }
            var applied = new VisualElement(); applied.AddToClassList("thry-components"); root.Add(applied);
            var revert = new Button(() => { Revert(shader); model.Notify(); });
            var dismiss = new Button(() => { Dismiss(shader); model.Notify(); }) { text = RetainedText.Get("keep_changes", "Keep changes") };
            applied.Add(revert); applied.Add(dismiss);
            fields.Track(applied, () =>
            {
                AppliedPreset preset;
                bool pending = s_appliedPresets.TryGetValue(shader.Materials[0], out preset);
                applied.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
                if (pending) revert.text = RetainedText.Get("preset_revert", "Revert ") + preset.name;
            });
            return root;
        }
    }
}
#endif
