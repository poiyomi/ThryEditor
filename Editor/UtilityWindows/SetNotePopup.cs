using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public class SetNotePopup : EditorWindow
    {
        ShaderPart ShaderPart { get; set; }
        string TextFieldContent { get; set; }
        
        void Awake()
        {
            titleContent = new GUIContent("Set Note");
        }

        public void Init(ShaderPart shaderPart, Rect? rectOverride = null)
        {
            ShaderPart = shaderPart;
            TextFieldContent = shaderPart.Note;
            if(rectOverride != null)
                position = rectOverride.Value;
        }

        void UpdateNoteAndClose()
        {
            ShaderPart.Note = TextFieldContent;
            Close();
        }
        public void CreateGUI()
        {
            minSize=new Vector2(320,150);rootVisualElement.Clear();RetainedWindow.Style(rootVisualElement);rootVisualElement.AddToClassList("thry-dialog");
            var text=new TextField {multiline=true,value=TextFieldContent};text.style.flexGrow=1;rootVisualElement.Add(text);
            text.RegisterValueChangedCallback(e=>TextFieldContent=e.newValue);
            var actions=new VisualElement();actions.AddToClassList("thry-components");actions.AddToClassList("thry-dialog-actions");rootVisualElement.Add(actions);
            actions.Add(new Button(Close){text=RetainedText.Get("cancel","Cancel")});var save=new Button(UpdateNoteAndClose){text=RetainedText.Get("save","Save")};save.AddToClassList("thry-primary-action");actions.Add(save);
            RetainedWindow.Shortcuts(rootVisualElement, Close, UpdateNoteAndClose, true);
            text.schedule.Execute(text.Focus);
        }
    }
}
