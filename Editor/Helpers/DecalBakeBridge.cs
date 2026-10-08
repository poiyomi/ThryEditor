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
        static readonly MethodInfo BakeMethod = Baker?.GetMethod("Bake");
        static readonly MethodInfo BakeAllMethod = Baker?.GetMethod("BakeAll");
        static readonly MethodInfo HasDecalsMethod = Baker?.GetMethod("HasEnabledDecals");
        internal static bool Available => BakeMethod != null;
        internal static bool BakeAllAvailable => BakeAllMethod != null && HasDecalsMethod != null;
        internal static bool HasEnabledDecals(Material material) => BakeAllAvailable && (bool)HasDecalsMethod.Invoke(null, new object[] { material });
        internal static void BakeAll(Material material)
        {
            try { BakeAllMethod?.Invoke(null, new object[] { material }); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }
        internal static void Bake(Material material, string textureProperty)
        {
            try { BakeMethod?.Invoke(null, new object[] { material, textureProperty }); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }
    }
}
