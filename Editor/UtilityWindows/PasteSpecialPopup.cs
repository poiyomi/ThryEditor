using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Thry.ThryEditor
{
    public class PasteSpecialPopup : EditorWindow
    {
        class ShaderPartUIAdapter
        {
            public ShaderPart ShaderPart { get; private set; }
            public bool HasChildren => children.Count > 0;
            public bool IsEnabled { get; set; } = true;

            List<ShaderPartUIAdapter> children = new List<ShaderPartUIAdapter>();

            void SetChildrenEnabled(bool enabled)
            {
                if(!HasChildren)
                    return;

                foreach(var child in children)
                    child.IsEnabled = enabled;
            }

            bool IsExpanded
            {
                get => HasChildren && _isExpanded;
                set => _isExpanded = value;
            }

            bool _isExpanded = false;

            private ShaderPartUIAdapter() {}

            public ShaderPartUIAdapter(ShaderPart shaderPart)
            {
                ShaderPart = shaderPart;
                if(shaderPart is ShaderGroup group)
                {
                    foreach(var child in group.Children)
                        children.Add(new ShaderPartUIAdapter(child));
                }
            }

            public VisualElement CreateView()
            {
                var root=new VisualElement();
                var toggle=new Toggle(ShaderPart.Content.text){value=IsEnabled};root.Add(toggle);toggle.RegisterValueChangedCallback(e=>IsEnabled=e.newValue);
                if(HasChildren)
                {
                    var fold=new Foldout{text="Properties",value=IsExpanded};fold.RegisterValueChangedCallback(e=>{if(e.target==fold)IsExpanded=e.newValue;});root.Add(fold);
                    var actions=new VisualElement();actions.AddToClassList("thry-components");fold.Add(actions);
                    var list=new VisualElement();
                    actions.Add(new Button(()=>{SetChildrenEnabled(false);Rebuild();}){text=RetainedText.Get("none","None")});actions.Add(new Button(()=>{SetChildrenEnabled(true);Rebuild();}){text=RetainedText.Get("all","All")});
                    fold.Add(list);
                    void Rebuild(){list.Clear();foreach(var child in children)list.Add(child.CreateView());}Rebuild();
                }
                else if(ShaderPart.MaterialProperty!=null)
                {
                    var property=ShaderPart.MaterialProperty;
                    string value=property.GetPropertyType()==ShaderPropertyType.Texture?property.textureValue?.name??"None":property.GetPropertyType()==ShaderPropertyType.Vector?property.vectorValue.ToString():property.GetPropertyType()==ShaderPropertyType.Color?property.colorValue.ToString():property.GetNumber().ToString();
                    root.Add(new Label(value));
                }
                return root;
            }

            public void AddDisabledShaderPartsToListRecursive(ref List<ShaderPart> disabledParts)
            {
                if(!IsEnabled)
                    disabledParts.Add(ShaderPart);
                
                if(HasChildren)
                    foreach(var child in children)
                        child.AddDisabledShaderPartsToListRecursive(ref disabledParts);
            }
        }
        
        ShaderPartUIAdapter partAdapter;
        
        /// <summary>
        /// OnPasteClicked, comes with a list of shader parts the user left enabled
        /// </summary>
        public event Action<List<ShaderPart>> OnPasteClicked;

        void Awake()
        {
            minSize = new Vector2(480, 400);
            titleContent = new GUIContent("Paste Special");
        }

        public void Init(ShaderPart shaderPart)
        {
            partAdapter = new ShaderPartUIAdapter(shaderPart);
        }

        void OnGUI()
        {
            // The copied part isn't serialized, so after a domain reload there is nothing left to paste
            if(partAdapter?.ShaderPart == null)
                Close();
        }
        
        public void CreateGUI()
        {
            rootVisualElement.Clear();RetainedWindow.Style(rootVisualElement);rootVisualElement.AddToClassList("thry-dialog");RetainedWindow.Shortcuts(rootVisualElement, Close);if(partAdapter==null)return;
            var scroll=new ScrollView();scroll.style.flexGrow=1;scroll.Add(partAdapter.CreateView());rootVisualElement.Add(scroll);
            var actions=new VisualElement();actions.AddToClassList("thry-components");actions.AddToClassList("thry-dialog-actions");rootVisualElement.Add(actions);
            actions.Add(new Button(Close){text=RetainedText.Get("cancel","Cancel")});var paste=new Button(()=>{var disabled=new List<ShaderPart>();partAdapter.AddDisabledShaderPartsToListRecursive(ref disabled);OnPasteClicked?.Invoke(disabled);Close();}){text=RetainedText.Get("paste_selected","Paste Selected")};paste.AddToClassList("thry-primary-action");actions.Add(paste);
        }
    }
}
