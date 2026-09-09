#if UNITY_2021_3_OR_NEWER
using UnityEditor;

namespace Thry.ThryEditor
{
    /// <summary>Cheap invalidation shared by texture fields; importing an existing asset need not dirty its material.</summary>
    [InitializeOnLoad]
    internal static class RetainedTextureRevision
    {
        internal static int Version { get; private set; }

        static RetainedTextureRevision()
        {
            EditorApplication.projectChanged += () => { unchecked { Version++; } };
        }
    }
}
#endif
