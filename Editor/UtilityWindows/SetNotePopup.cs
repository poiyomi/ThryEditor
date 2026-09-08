using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
#endif

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

        void OnGUI()
        {
#if UNITY_2021_3_OR_NEWER
            if(rootVisualElement.childCount>0)return;
#endif
            if(ShaderPart == null)
            {
                Close();
                return;
            }

            GUI.SetNextControlName(nameof(TextFieldContent));
            TextFieldContent = EditorGUILayout.TextField(TextFieldContent);
            EditorGUI.FocusTextInControl(nameof(TextFieldContent));
            
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("Cancel", GUILayout.Height(30))) 
            {
                Close();
            }

            if(Event.current.isKey)
            {
                if(Event.current.keyCode == KeyCode.Return)
                    UpdateNoteAndClose(true);
                else if(Event.current.keyCode == KeyCode.Escape)
                    Close();
            }

            if(GUILayout.Button("Ok", GUILayout.Height(30)))
                UpdateNoteAndClose(false);
            
            EditorGUILayout.EndHorizontal();
        }

        void UpdateNoteAndClose(bool enterPressed)
        {
            if(enterPressed)
                Event.current.Use();
            
            ShaderPart.Note = TextFieldContent;
            Close();
        }
#if UNITY_2021_3_OR_NEWER
        public void CreateGUI()
        {
            minSize=new Vector2(320,150);RetainedWindow.Style(rootVisualElement);
            var text=new TextField {multiline=true,value=TextFieldContent};text.style.flexGrow=1;rootVisualElement.Add(text);
            text.RegisterValueChangedCallback(e=>TextFieldContent=e.newValue);
            var actions=new VisualElement();actions.AddToClassList("thry-components");rootVisualElement.Add(actions);
            actions.Add(new Button(Close){text="Cancel"});actions.Add(new Button(()=>UpdateNoteAndClose(false)){text="Save"});
            rootVisualElement.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape)Close();if(e.keyCode==KeyCode.Return&&(e.ctrlKey||e.commandKey))UpdateNoteAndClose(false);});
            text.schedule.Execute(text.Focus);
        }
#endif
    }
}
