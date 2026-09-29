// Material/Shader Inspector for Unity 2021/2022/6
// Copyright (C) 2019-2026 Thryrallo

using System.Collections.Generic;
using Thry.ThryEditor.Helpers;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor
{
    /// <summary>
    /// Leaves out the ForwardBase variants of locked shaders that have none of LIGHTPROBE_SH, LIGHTMAP_ON and
    /// DYNAMICLIGHTMAP_ON. Unity enables LIGHTPROBE_SH for every renderer that isn't lightmapped, whatever its Light
    /// Probes setting or the scene's ambient mode, and a lightmapped one asks for LIGHTMAP_ON or DYNAMICLIGHTMAP_ON.
    /// The only renderer that ended up on one of these is a lightmapped one on a shader that skips its lightmap
    /// variants (Poiyomi outside the World shaders), which now falls back to the LIGHTPROBE_SH variant instead. Those
    /// shaders never sample lightmaps, so the two only differ in OpenLit's light direction mode. For an avatar this is
    /// half of the Base and Outline programs, the most expensive ones to compile.
    /// </summary>
    public class StripUnusedLightProbeVariants : IPreprocessShaders
    {
        static readonly ShaderKeyword s_lightProbeSH = new ShaderKeyword("LIGHTPROBE_SH");
        static readonly ShaderKeyword s_lightmap = new ShaderKeyword("LIGHTMAP_ON");
        static readonly ShaderKeyword s_dynamicLightmap = new ShaderKeyword("DYNAMICLIGHTMAP_ON");

        // After StripUnlockedShadersFromBuild.
        public int callbackOrder => 5;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (snippet.passType != PassType.ForwardBase || data.Count == 0 || !shader.IsLocked()) return;

            // Only passes that have the keyword: Poiyomi's early-Z pass is ForwardBase without it.
            bool hasLightProbeVariants = false;
            foreach (ShaderCompilerData entry in data)
            {
                if (entry.shaderKeywordSet.IsEnabled(s_lightProbeSH)) { hasLightProbeVariants = true; break; }
            }
            if (!hasLightProbeVariants) return;

            int before = data.Count;
            for (int i = data.Count - 1; i >= 0; i--)
            {
                ShaderKeywordSet keywords = data[i].shaderKeywordSet;
                if (!keywords.IsEnabled(s_lightProbeSH) && !keywords.IsEnabled(s_lightmap) && !keywords.IsEnabled(s_dynamicLightmap))
                    data.RemoveAt(i);
            }
            if (data.Count != before)
                ThryLogger.LogDetail("Stripping", $"{shader.name} {snippet.passName} ({snippet.shaderType}): left out {before - data.Count} of {before} variants no renderer uses.");
        }
    }
}
