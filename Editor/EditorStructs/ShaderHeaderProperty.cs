using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    public class ShaderHeaderProperty : ShaderPart
    {
        public ShaderHeaderProperty(ShaderEditor shaderEditor, MaterialProperty materialProperty, string displayName, string optionsRaw, int propertyIndex) : base(shaderEditor, materialProperty, displayName, optionsRaw, propertyIndex)
        {
            // guid is defined as <guid:x*>
            if (displayName.Contains("<guid="))
            {
                int start = displayName.IndexOf("<guid=");
                int end = displayName.IndexOf(">", start);
                string guid = displayName.Substring(start + 6, end - start - 6);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string replacement = "";
                if (path != null && System.IO.File.Exists(path))
                {
                    replacement = System.IO.File.ReadAllText(path);
                }
                Content.text = displayName.Replace($"<guid={guid}>", replacement);
            }

            // Use the inspector theme's text color, including for existing shader labels.
            Content.text = Regex.Replace(Content.text, @"</?color\b[^>]*>", "", RegexOptions.IgnoreCase);
        }

        public override void CopyFrom(Material src, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            throw new NotImplementedException();
        }

        public override void CopyFrom(ShaderPart src, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            throw new NotImplementedException();
        }

        public override void CopyTo(Material[] targets, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            throw new NotImplementedException();
        }

        public override void CopyTo(ShaderPart target, bool applyDrawers = true, bool deepCopy = true, bool copyReferenceProperties = true, HashSet<ShaderPropertyType> skipPropertyTypes = null, HashSet<string> skipPropertyNames = null)
        {
            throw new NotImplementedException();
        }
    }

}
