using UnityEngine;

namespace Thry.ThryEditor
{
    // Lets a ButtonData tell the window it opens what it was opened for.
    //
    // Names, not MaterialProperty refs: those go stale when the inspector rebuilds. Resolve against
    // the materials passed here, not ShaderEditor.Active.PropertyDictionary - the MaterialProperty
    // behind each entry there is only refreshed while the inspector draws
    // (ShaderPart.UpdatedMaterialPropertyReference). Example in Editor/Examples.
    public interface IThryButtonTarget
    {
        void SetButtonContext(string[] propertyNames, Material[] materials);
    }
}
