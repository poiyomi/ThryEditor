using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    // Inspect the actual shader, including optimized shaders: a disabled material
    // effect can still leave an executable GrabPass in an unlocked shader.
    internal static class GrabPassQueueWarning
    {
        internal const string Message = "This material captures the screen before the sky clears the background. Leftover colors can make your GrabPass look extremely bright, especially with bloom. To give the sky a chance to render, set the render queue to 2501 or higher.";
        static readonly Dictionary<Shader, bool> Cache = new Dictionary<Shader, bool>();
        // Skip comments and strings before looking for the ShaderLab command.
        static readonly Regex Tokens = new Regex(@"//[^\r\n]*|/\*[\s\S]*?\*/|""(?:\\.|[^""\\])*""|(?<grab>\bGrabPass\s*\{)");

        internal static void Invalidate() => Cache.Clear();

        internal static bool CapturesBeforeSkybox(Material material)
        {
            if (material == null || material.shader == null || material.renderQueue > 2500
                || GraphicsSettings.currentRenderPipeline != null) return false;
            var shader = material.shader;
            if (!Cache.TryGetValue(shader, out bool hasGrab))
            {
                string path = AssetDatabase.GetAssetPath(shader);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".shader", System.StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    foreach (Match match in Tokens.Matches(File.ReadAllText(path)))
                        if (match.Groups["grab"].Success) { hasGrab = true; break; }
                }
                Cache[shader] = hasGrab;
            }
            return hasGrab;
        }

        internal static bool HasAffected(Material[] materials) => materials != null && materials.Any(CapturesBeforeSkybox);
    }

    internal sealed class GrabPassQueueCacheInvalidation : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(path => path.EndsWith(".shader", System.StringComparison.OrdinalIgnoreCase)))
                GrabPassQueueWarning.Invalidate();
        }
    }
}
