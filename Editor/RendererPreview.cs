using System;
using UnityEngine;

namespace Thry.ThryEditor
{
    public static class RendererPreview
    {
        public static Func<Renderer, bool> OwnsPropertyBlock;
        private static bool _failed;

        // A block written only by a scene preview holds no material values, so the inspector reads the material as usual.
        internal static bool HasPropertyBlock(Renderer renderer)
        {
            if (renderer == null || !renderer.HasPropertyBlock()) return false;
            var owns = OwnsPropertyBlock;
            if (owns == null) return true;
            try { return !owns(renderer); }
            catch (Exception e)
            {
                if (!_failed) { _failed = true; Debug.LogException(e); }
                return true;
            }
        }
    }
}
