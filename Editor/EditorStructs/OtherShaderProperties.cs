using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    public class RenderQueueProperty : ShaderProperty
    {
        public RenderQueueProperty(ShaderEditor shaderEditor) : base(shaderEditor, "RenderQueue", "", "Change the Queue at which the material is rendered.", -1)
        {
            IsAnimatable = false;
            CustomStringTagID = "RenderQueue";
        }

        public override object FetchPropertyValue()
        {
            return MyShaderUI.Materials[0].renderQueue;
        }

        public override object PropertyDefaultValue => ShaderEditor.Active.Shader.renderQueue;
        public override bool IsPropertyValueDefault => MyShaderUI.Materials.All(m => m.renderQueue == ShaderEditor.Active.Shader.renderQueue);

        public override void CopyFrom(Material sourceM, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            foreach (Material m in MyShaderUI.Materials) m.renderQueue = sourceM.renderQueue;
        }
        public override void CopyTo(Material[] targetsM, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            foreach (Material m in targetsM) m.renderQueue = MyShaderUI.Materials[0].renderQueue;
        }

        public override void CopyFrom(ShaderPart srcPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            this.CopyFrom(srcPart.MaterialProperty.targets[0] as Material);
        }
        public override void CopyTo(ShaderPart targetPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            this.CopyTo(targetPart.MaterialProperty.targets.Cast<Material>().ToArray());
        }
    }
    public class VRCFallbackProperty : ShaderProperty
    {
        internal static string[] RetainedNames => s_vRCFallbackOptionsPopup;
        internal static string[] RetainedValues => s_vRCFallbackOptionsValues;
        static string[] s_fallbackShaderTypes = { "Standard", "Toon", "Unlit", "VertexLit", "Particle", "Sprite", "Matcap", "MobileToon", "toonstandard", "toonstandardoutline" };
        static string[] s_fallbackRenderTypes = { "Opaque", "Cutout", "Transparent", "Fade" };
        static string[] s_fallbackRenderTypesValues = { "", "Cutout", "Transparent", "Fade" };
        static string[] s_fallbackCullTypes = { "OneSided", "DoubleSided" };
        static string[] s_fallbackCullTypesValues = { "", "DoubleSided" };
        static string[] s_fallbackNoTypes = { "None", "Hidden" };
        static string[] s_fallbackNoTypesValues = { "", "Hidden" };
        static string[] s_vRCFallbackOptionsPopup = s_fallbackNoTypes.Union(s_fallbackShaderTypes.SelectMany(s => s_fallbackRenderTypes.SelectMany(r => s_fallbackCullTypes.Select(c => r + "/" + c).Select(rc => s + "/" + rc)))).ToArray();
        static string[] s_vRCFallbackOptionsValues = s_fallbackNoTypes.Union(s_fallbackShaderTypes.SelectMany(s => s_fallbackRenderTypesValues.SelectMany(r => s_fallbackCullTypesValues.Select(c => r + c).Select(rc => s + rc)))).ToArray();

        internal static string WithRenderMode(string fallback, string renderType, bool doubleSided)
        {
            string shaderType = s_fallbackShaderTypes.FirstOrDefault(s => s_fallbackRenderTypesValues.Any(r => s_fallbackCullTypesValues.Any(c => s + r + c == fallback)));
            // VRChat does not combine the Toon Standard fallbacks with a render type or culling
            if (shaderType == null || shaderType.StartsWith("toonstandard", System.StringComparison.Ordinal)) return fallback;
            return shaderType + renderType + (doubleSided ? s_fallbackCullTypesValues[1] : s_fallbackCullTypesValues[0]);
        }

        public VRCFallbackProperty(ShaderEditor shaderEditor) : base(shaderEditor, "VRCFallback", "", "Select the shader VRChat should use when your shaders are being hidden.", -1)
        {
            IsAnimatable = false;
            CustomStringTagID = "VRCFallback";
            IsExemptFromLockedDisabling = true;
        }

        public void SetPropertyValue(string value)
        {
            foreach (Material m in MyShaderUI.Materials)
            {
                m.SetOverrideTag("VRCFallback", value);
                EditorUtility.SetDirty(m);
            }
        }

        public override object FetchPropertyValue()
        {
            return MyShaderUI.Materials[0].GetTag("VRCFallback", false, "None");
        }

        public override object PropertyDefaultValue => "None";
        public override bool IsPropertyValueDefault => MyShaderUI.Materials.All(m => m.GetTag("VRCFallback", false, "None") == "None");

        public override void CopyFrom(Material sourceM, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            string value = sourceM.GetTag("VRCFallback", false, "None");
            foreach (Material m in MyShaderUI.Materials) m.SetOverrideTag("VRCFallback", value);
        }
        public override void CopyTo(Material[] targetsM, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            string value = MyShaderUI.Materials[0].GetTag("VRCFallback", false, "None");
            foreach (Material m in targetsM) m.SetOverrideTag("VRCFallback", value);
        }
        public override void CopyFrom(ShaderPart srcPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            this.CopyFrom(srcPart.MaterialProperty.targets[0] as Material, applyDrawers);
        }
        public override void CopyTo(ShaderPart targetPart, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            this.CopyTo(targetPart.MaterialProperty.targets.Cast<Material>().ToArray(), applyDrawers);
        }
    }
    public class InstancingProperty : ShaderProperty
    {
        public InstancingProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, string optionsRaw, int property_index) : base(shaderEditor, materialProperty, displayName, optionsRaw, property_index)
        {
            IsAnimatable = false;
        }

        public override object FetchPropertyValue()
        {
            return MyShaderUI.Materials[0].enableInstancing;
        }
        public override object PropertyDefaultValue => false;
        public override bool IsPropertyValueDefault => MyShaderUI.Materials.All(m => m.enableInstancing == false);
    }
    public class GIProperty : ShaderProperty
    {
        public GIProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, string optionsRaw, int property_index) : base(shaderEditor, materialProperty, displayName, optionsRaw, property_index)
        {
            IsAnimatable = false;
        }

        public static GUIContent[] lightmapEmissiveStrings = { EditorGUIUtility.TrTextContent("Realtime"), EditorGUIUtility.TrTextContent("Baked"), EditorGUIUtility.TrTextContent("None") };
        public static int[] lightmapEmissiveValues = { (int)MaterialGlobalIlluminationFlags.RealtimeEmissive, (int)MaterialGlobalIlluminationFlags.BakedEmissive, (int)MaterialGlobalIlluminationFlags.None };

        public static void FixupEmissiveFlag(Material mat)
        {
            if (mat == null)
                throw new System.ArgumentNullException("mat");
            Color matColor = Color.black;
            if (mat.HasProperty("_EmissionColor") == true)
                matColor = mat.GetColor("_EmissionColor");
            mat.globalIlluminationFlags = FixupEmissiveFlag(matColor, mat.globalIlluminationFlags);
        }

        public static MaterialGlobalIlluminationFlags FixupEmissiveFlag(Color col, MaterialGlobalIlluminationFlags flags)
        {
            if ((flags & MaterialGlobalIlluminationFlags.BakedEmissive) != 0 && col.maxColorComponent == 0.0f) // flag black baked
                flags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            else if (flags != MaterialGlobalIlluminationFlags.EmissiveIsBlack) // clear baked flag on everything else, unless it's explicity disabled
                flags &= MaterialGlobalIlluminationFlags.AnyEmissive;
            return flags;
        }

        public override object FetchPropertyValue()
        {
            return MyShaderUI.Materials[0].globalIlluminationFlags;
        }

        // New materials start as EmissiveIsBlack (shown as None)
        public override object PropertyDefaultValue => MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        public override bool IsPropertyValueDefault => MyShaderUI.Materials.All(m => (m.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) == MaterialGlobalIlluminationFlags.None);
    }
    public class DSGIProperty : ShaderProperty
    {
        public DSGIProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, string optionsRaw, int property_index) : base(shaderEditor, materialProperty, displayName, optionsRaw, property_index)
        {
            IsAnimatable = false;
        }

        public override object FetchPropertyValue()
        {
            return MyShaderUI.Materials[0].doubleSidedGI;
        }
        public override object PropertyDefaultValue => false;
        public override bool IsPropertyValueDefault => MyShaderUI.Materials.All(m => m.doubleSidedGI == false);
    }
}
