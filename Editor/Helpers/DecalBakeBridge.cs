using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Thry.ThryEditor
{
    // Thry is usable without Poi.Tools. Keep the optional integration across the assembly boundary here.
    internal static class DecalBakeBridge
    {
        static readonly Type Baker = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Poi.Tools.PoiDecalBaker")).FirstOrDefault(t => t != null);
        static readonly MethodInfo OpenMethod = Baker?.GetMethod("Open");
        internal static bool Available => OpenMethod != null;
        internal static void Open(Material material, string textureProperty)
        {
            try { OpenMethod?.Invoke(null, new object[] { material, textureProperty }); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }
    }
}
