using UnityEngine;
using UnityEngine.UIElements;

namespace Thry.ThryEditor.ShaderTranslations
{

    public class ConditionalTranslationBlockListItem : BindableElement
    {

        public ConditionalTranslationBlockListItem()
        {
            var uxml = Resources.Load<VisualTreeAsset>("Shader Translator/TranslatorConditionalListItem");
            uxml.CloneTree(this);
        }
    }
}