#if UNITY_2021_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Property count catches runtime shader replacement; import versions also cover
    // same-sized schemas whose attributes, names, defaults, or section order changed.
    internal struct RetainedShaderSchema : IEquatable<RetainedShaderSchema>
    {
        private int _dirtyCount, _propertyCount, _importVersion;
        internal static RetainedShaderSchema Read(Shader shader) => new RetainedShaderSchema
        {
            _dirtyCount=EditorUtility.GetDirtyCount(shader), _propertyCount=shader.GetPropertyCount(),
            _importVersion=RetainedShaderSchemaImports.Version(shader)
        };
        public bool Equals(RetainedShaderSchema other) => _dirtyCount==other._dirtyCount && _propertyCount==other._propertyCount && _importVersion==other._importVersion;
    }

    internal sealed class RetainedShaderSchemaImports : AssetPostprocessor
    {
        private static readonly Dictionary<int,int> Versions = new Dictionary<int,int>();
        internal static int Version(Shader shader) { Versions.TryGetValue(shader.GetInstanceID(),out var value); return value; }
        private static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
        {
            foreach(var path in imported)
            {
                if(!path.EndsWith(".shader",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".shadergraph",StringComparison.OrdinalIgnoreCase)) continue;
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                if(shader!=null) Versions[shader.GetInstanceID()]=Version(shader)+1;
            }
        }
    }
}
#endif
