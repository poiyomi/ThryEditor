using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    public class ShaderHeader : ShaderGroup
    {
        public ShaderHeader(ShaderEditor shaderEditor, MaterialProperty prop, MaterialEditor materialEditor, string displayName, string optionsRaw, int propertyIndex) : base(shaderEditor, prop, materialEditor, displayName, optionsRaw, propertyIndex)
        {
        }

        internal static GenericMenu RetainedHeaderMenu(ShaderGroup property, Material[] materials, System.Action<GenericMenu> addSearch = null)
        {
            var menu = new GenericMenu();
            bool canEdit = !property.MyShaderUI.IsLockedMaterial;
            bool canPaste = canEdit && Mediator.copy_part != null;
            void AddItem(string text, bool enabled, GenericMenu.MenuFunction action, string icon = null)
            {
                if (enabled) menu.AddItem(new GUIContent(text, icon), false, action);
                else menu.AddDisabledItem(new GUIContent(text, icon));
            }
            AddItem("Reset", canEdit, delegate ()
            {
                ThryLogger.LogDetail("ShaderHeader", $"Resetting '{property.Content.text}' of {ShaderEditor.Active.Materials[0].name}");
                int undoGroup = Undo.GetCurrentGroup();

                var defaults = new Material(SectionLock.GetSourceShader(materials[0].shader) ?? materials[0].shader);
                try { property.CopyFrom(defaults, true); }
                finally { Object.DestroyImmediate(defaults); }
                GlobalLinker.OnSectionChanged(property);

                Undo.SetCurrentGroupName($"Reset {property.Content.text} of {ShaderEditor.Active.Materials[0].name}");
                Undo.CollapseUndoOperations(undoGroup);
            }, MenuIconReset);
            AddItem("Copy", true, delegate ()
            {
                ThryLogger.LogDetail("ShaderHeader", $"Copying '{property.Content.text}' of {ShaderEditor.Active.Materials[0].name}");
                Mediator.copy_part = property;
            }, MenuIconCopy);
            AddItem("Paste", canPaste, delegate ()
            {
                if (Mediator.copy_part != null)
                {
                    ThryLogger.LogDetail("ShaderHeader", $"Pasting '{property.Content.text}' of {ShaderEditor.Active.Materials[0].name}");
                    int undoGroup = Undo.GetCurrentGroup();

                    property.CopyFrom(Mediator.copy_part);
                    GlobalLinker.OnSectionChanged(property);

                    Undo.SetCurrentGroupName($"Paste {property.Content.text} of {ShaderEditor.Active.Materials[0].name}");
                    Undo.CollapseUndoOperations(undoGroup);
                }
            }, MenuIconPaste);
            AddItem("Paste without Textures", canPaste, delegate ()
            {
                if (Mediator.copy_part != null)
                {
                    ThryLogger.LogDetail("ShaderHeader", $"Pasting* '{property.Content.text}' of {ShaderEditor.Active.Materials[0].name}");
                    int undoGroup = Undo.GetCurrentGroup();

                    var propsToIgnore = new HashSet<ShaderPropertyType> { ShaderPropertyType.Texture };
                    property.CopyFrom(Mediator.copy_part, skipPropertyTypes: propsToIgnore);
                    GlobalLinker.OnSectionChanged(property);

                    Undo.SetCurrentGroupName($"Paste* {property.Content.text} of {ShaderEditor.Active.Materials[0].name}");
                    Undo.CollapseUndoOperations(undoGroup);
                }
            });
            AddItem("Paste Special...", canPaste, () =>
            {
                if(Mediator.copy_part == null) return;
                
                ThryLogger.LogDetail("ShaderHeader", $"Pasting** '{property.Content.text}' of {ShaderEditor.Active.Materials[0].name}");
                var popup = ScriptableObject.CreateInstance<PasteSpecialPopup>();
                popup.Init(Mediator.copy_part);
                popup.ShowUtility();
                
                popup.OnPasteClicked += (disabledPartsList) =>
                {
                    HashSet<string> ignoreProperties = new HashSet<string>(disabledPartsList.Select(p => p.MaterialProperty.name));
                    if (Mediator.copy_part != null)
                    {
                        int undoGroup = Undo.GetCurrentGroup();

                        property.CopyFrom(Mediator.copy_part, skipPropertyNames: ignoreProperties);
                        GlobalLinker.OnSectionChanged(property);

                        Undo.SetCurrentGroupName($"Paste** {property.Content.text} of {ShaderEditor.Active.Materials[0].name}");
                        Undo.CollapseUndoOperations(undoGroup);
                    }
                };
                
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy as Text"), false, () =>
            {
                string json = MaterialTextSerializer.Serialize(property, materials[0]);
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError("Failed to serialize section to text");
                    return;
                }
                EditorGUIUtility.systemCopyBuffer = json;
                ThryLogger.LogDetail("ShaderHeader", $"Copied section '{property.Content.text}' of {materials[0].name} as text ({json.Length} chars)");
            });
            AddItem("Paste from Text", canEdit, () =>
            {
                string clip = EditorGUIUtility.systemCopyBuffer;
                if (!MaterialTextSerializer.TryDeserialize(clip, out var data))
                {
                    Debug.LogError("Paste from Text: clipboard does not contain a valid Thry material text payload.");
                    return;
                }

                Shader shader = SectionLock.GetSourceShader(materials[0].shader) ?? materials[0].shader;
                if (!string.IsNullOrEmpty(data.shader) && data.shader != shader.name) ThryLogger.LogWarn("ShaderHeader", $"Pasting from shader '{data.shader}' onto '{shader.name}'. Properties that don't exist on the target shader will be skipped.");

                int undoGroup = Undo.GetCurrentGroup();
                Undo.RecordObjects(materials, $"Paste from Text {property.Content.text}");

                var scratch = new Material(shader);
                var appliedNames = new HashSet<string>();
                int applied = MaterialTextSerializer.ApplyToMaterial(data, scratch, appliedNames);
                var skipNames = MaterialTextSerializer.GetUnpastedPropertyNames(property.MyShaderUI, appliedNames);

                MaterialTextSerializer.KeepAnimatedTags(property.MyShaderUI, appliedNames,
                    () => property.CopyFrom(scratch, skipPropertyTypes: MaterialTextSerializer.SkipTextures, skipPropertyNames: skipNames));
                GlobalLinker.OnSectionChanged(property);

                Object.DestroyImmediate(scratch);

                Undo.SetCurrentGroupName($"Paste from Text {property.Content.text} of {materials[0].name} ({applied} properties)");
                Undo.CollapseUndoOperations(undoGroup);

                ThryLogger.LogDetail("ShaderHeader", $"Pasted {applied} properties from text onto '{property.Content.text}'");
            });
            menu.AddSeparator("");
            property.AddPresetMenu(menu);
            menu.AddSeparator("");
            addSearch?.Invoke(menu);
            SectionExtensions.ExtendMenu(property.MaterialProperty?.name, materials, menu);
            if(Config.Instance.showNotes)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Set Note"), false, () =>
                {
                    var popup = ScriptableObject.CreateInstance<SetNotePopup>();
                    popup.Init(property, new Rect());
                    popup.ShowUtility();
                });
                //menu.AddItem(new GUIContent("Clear Note"), false, () => { Note = null; }); // Too easy to missclick when there's no undo?
            }

            return menu;
        }
    }

}
