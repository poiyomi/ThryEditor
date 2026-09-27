using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Poi Mesh Tools can write UV channels that exist only in its preview and build meshes.
    // Thry is usable without Poi.Tools, so this optional integration stays behind reflection.
    internal static class MeshToolsBridge
    {
        static readonly Type SceneMesh = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Poi.Tools.PoiMeshToolsSceneMesh")).FirstOrDefault(t => t != null);
        static readonly MethodInfo WritesUVMethod = SceneMesh?.GetMethod("WritesUV");
        static readonly MethodInfo BakeMethod = SceneMesh?.GetMethod("Bake");

        internal static bool WritesUV(Renderer renderer, int channel)
        {
            if (WritesUVMethod == null || renderer == null) return false;
            try { return (bool)WritesUVMethod.Invoke(null, new object[] { renderer, channel }); }
            catch (TargetInvocationException) { return false; }
        }

        // The mesh Poi Mesh Tools builds for this renderer, posed like BakeMesh, or null. The caller destroys it.
        internal static Mesh Bake(Renderer renderer)
        {
            if (BakeMethod == null || renderer == null) return null;
            try { return (Mesh)BakeMethod.Invoke(null, new object[] { renderer }); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
