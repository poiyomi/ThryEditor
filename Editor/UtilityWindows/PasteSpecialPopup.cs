using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
#endif

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

            public void DrawUI()
            {
                if(ShaderPart == null)
                    return;

                using(new EditorGUILayout.VerticalScope(Styles.padding2pxHorizontal1pxVertical))
                {
                    if(!HasChildren)
                    {
                        float halfViewWidth = EditorGUIUtility.currentViewWidth * 0.5f; 
                        EditorGUILayout.BeginHorizontal();
                        IsEnabled = EditorGUILayout.ToggleLeft(ShaderPart.Content, IsEnabled, Styles.upperLeft_richText_wordWrap);
                        DrawShaderProperty(ShaderPart.MaterialProperty, GUILayout.Width(halfViewWidth));
                        EditorGUILayout.EndHorizontal();
                        return;
                    }

                    EditorGUILayout.BeginHorizontal();
                    var rect = EditorGUILayout.GetControlRect();

                    var foldoutRect = new Rect(rect.x, rect.y, rect.width, rect.height);
                    var toggleRect = new Rect(rect.x + 16f, rect.y, 14f, rect.height);
                    var labelRect = new Rect(rect.x + 32f, rect.y, rect.width - 32f, rect.height);
                    EditorGUI.LabelField(rect, GUIContent.none, Styles.flatHeader);
                    
                    IsEnabled = EditorGUI.Toggle(toggleRect, GUIContent.none, IsEnabled);
                    IsExpanded = EditorGUI.Foldout(foldoutRect, IsExpanded, string.Empty, true);
                    EditorGUI.LabelField(labelRect, ShaderPart.Content);
                    if(GUILayout.Button("None", GUILayout.MaxWidth(40f)))
                        SetChildrenEnabled(false);
                    if(GUILayout.Button("All", GUILayout.MaxWidth(40f)))
                        SetChildrenEnabled(true);
                    EditorGUILayout.EndHorizontal();
                    if(IsExpanded)
                    {
                        using (new GUILib.IndentScope(1))
                        {
                            foreach(var child in children)
                                child.DrawUI();
                        }
                    }
                }
            }
#if UNITY_2021_3_OR_NEWER
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
                    string value=property.type==MaterialProperty.PropType.Texture?property.textureValue?.name??"None":property.type==MaterialProperty.PropType.Vector?property.vectorValue.ToString():property.type==MaterialProperty.PropType.Color?property.colorValue.ToString():property.GetNumber().ToString();
                    root.Add(new Label(value));
                }
                return root;
            }
#endif

            public void AddDisabledShaderPartsToListRecursive(ref List<ShaderPart> disabledParts)
            {
                if(!IsEnabled)
                    disabledParts.Add(ShaderPart);
                
                if(HasChildren)
                    foreach(var child in children)
                        child.AddDisabledShaderPartsToListRecursive(ref disabledParts);
            }
        }
        
        Vector2 scrollPosition = Vector2.zero;
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
#if UNITY_2021_3_OR_NEWER
            if(rootVisualElement.childCount>0)return;
#endif
            if(partAdapter?.ShaderPart == null)
            {
                Close();
                return;
            }

            using(var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                partAdapter.DrawUI();
                scrollPosition = scroll.scrollPosition;
            }

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("Cancel", GUILayout.Height(30))) 
            {
                Close();
            }

            if(GUILayout.Button("Paste Selected", GUILayout.Height(30)))
            {
                List<ShaderPart> disabledParts = new List<ShaderPart>();
                partAdapter.AddDisabledShaderPartsToListRecursive(ref disabledParts);
                OnPasteClicked?.Invoke(disabledParts);

                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
        
#if UNITY_2021_3_OR_NEWER
        public void CreateGUI()
        {
            rootVisualElement.Clear();RetainedWindow.Style(rootVisualElement);rootVisualElement.AddToClassList("thry-dialog");RetainedWindow.Shortcuts(rootVisualElement, Close);if(partAdapter==null)return;
            var scroll=new ScrollView();scroll.style.flexGrow=1;scroll.Add(partAdapter.CreateView());rootVisualElement.Add(scroll);
            var actions=new VisualElement();actions.AddToClassList("thry-components");actions.AddToClassList("thry-dialog-actions");rootVisualElement.Add(actions);
            actions.Add(new Button(Close){text=RetainedText.Get("cancel","Cancel")});var paste=new Button(()=>{var disabled=new List<ShaderPart>();partAdapter.AddDisabledShaderPartsToListRecursive(ref disabled);OnPasteClicked?.Invoke(disabled);Close();}){text=RetainedText.Get("paste_selected","Paste Selected")};paste.AddToClassList("thry-primary-action");actions.Add(paste);
        }
#endif
        static void DrawShaderProperty(MaterialProperty prop, GUILayoutOption propertyWidth)
        {
            using(new EditorGUI.DisabledScope(true))
            {
                switch(prop.GetPropertyType())
                {
                    case ShaderPropertyType.Color:
                        EditorGUILayout.ColorField(prop.colorValue, propertyWidth);
                        break;
                    case ShaderPropertyType.Vector:
                        EditorGUILayout.Vector4Field(GUIContent.none, prop.vectorValue, propertyWidth);
                        break;
#if UNITY_2021_1_OR_NEWER
                    case ShaderPropertyType.Int:
                        EditorGUILayout.IntField(prop.intValue, propertyWidth);
                        break;
#endif
                    case ShaderPropertyType.Range:
                        EditorGUILayout.Slider(GUIContent.none, prop.floatValue, prop.rangeLimits.x, prop.rangeLimits.y,
                            propertyWidth);
                        break;
                    case ShaderPropertyType.Float:
                        EditorGUILayout.FloatField(prop.floatValue, propertyWidth);
                        break;
                    case ShaderPropertyType.Texture:
                        EditorGUILayout.ObjectField(prop.textureValue, typeof(Texture), true, propertyWidth);
                        break;
                    default:
                        break;
                }
            }
        }        
    }
}
