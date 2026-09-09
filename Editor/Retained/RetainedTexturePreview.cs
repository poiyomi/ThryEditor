#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Thry.ThryEditor
{
    /// <summary>Owns only the small inspection target. Never reads back or modifies the assigned texture.</summary>
    internal sealed class RetainedTexturePreview : IDisposable
    {
        Material _material;
        RenderTexture _target;
        internal RenderTexture Target => _target;
        internal int RenderCount { get; private set; }

        internal static bool Supports(Texture texture)
        {
            if (texture == null) return false;
            switch (texture.dimension)
            {
                case TextureDimension.Tex2D: case TextureDimension.Cube: case TextureDimension.Tex3D: return true;
                case TextureDimension.Tex2DArray: return SystemInfo.supports2DArrayTextures;
                default: return false;
            }
        }

        internal static int SliceCount(Texture texture)
        {
            var array = texture as Texture2DArray;
            if (array != null) return array.depth;
            var volume = texture as Texture3D;
            if (volume != null) return volume.depth;
            var render = texture as RenderTexture;
            return render == null ? 1 : Mathf.Max(1, render.volumeDepth);
        }

        internal static long EstimateMemory(Texture texture)
        {
            if (texture == null) return 0;
            if (texture.graphicsFormat == GraphicsFormat.None || texture.dimension == TextureDimension.CubeArray)
                return UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            long bytes = 0;
            int width = texture.width, height = texture.height;
            int depth = texture.dimension == TextureDimension.Tex3D ? SliceCount(texture) : 1;
            int surfaces = texture.dimension == TextureDimension.Cube ? 6 : texture.dimension == TextureDimension.Tex2DArray ? SliceCount(texture) : 1;
            for (int mip = 0; mip < Mathf.Max(1, texture.mipmapCount); mip++)
            {
                bytes += (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, texture.graphicsFormat) * depth * surfaces;
                width = Mathf.Max(1, width / 2); height = Mathf.Max(1, height / 2); depth = Mathf.Max(1, depth / 2);
            }
            var render = texture as RenderTexture;
            if (render != null)
            {
                bytes += (long)render.width * render.height * SliceCount(render) * (render.depth / 8) * (render.dimension == TextureDimension.Cube ? 6 : 1);
                bytes *= Mathf.Max(1, render.antiAliasing);
            }
            return bytes;
        }

        internal static string Format(Texture texture)
        {
            var twoD = texture as Texture2D; if (twoD != null) return twoD.format.ToString();
            var array = texture as Texture2DArray; if (array != null) return array.format.ToString();
            var cube = texture as Cubemap; if (cube != null) return cube.format.ToString();
            var volume = texture as Texture3D; if (volume != null) return volume.format.ToString();
            var render = texture as RenderTexture; return render != null ? render.format.ToString() : texture.graphicsFormat.ToString();
        }

        internal Texture Render(Texture source, int channel, int slice)
        {
            if (!Supports(source)) return null;
            var shader = Shader.Find("Hidden/Thry/TextureInspection");
            if (shader == null || !shader.isSupported) return null;
            if (_material == null) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            float scale = Mathf.Min(1, 128f / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            if (_target == null || _target.width != width || _target.height != height)
            {
                ReleaseTarget();
                _target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                { hideFlags = HideFlags.HideAndDontSave, name = "Thry texture inspection preview", filterMode = FilterMode.Bilinear };
                _target.Create();
            }
            int kind = source.dimension == TextureDimension.Cube ? 1 : source.dimension == TextureDimension.Tex2DArray ? 2 : source.dimension == TextureDimension.Tex3D ? 3 : 0;
            _material.SetFloat("_TextureKind", kind);
            _material.SetFloat("_Channel", channel);
            _material.SetFloat("_Slice", kind == 1 ? Mathf.Clamp(slice, 0, 5) : Mathf.Clamp(slice, 0, SliceCount(source) - 1));
            _material.SetFloat("_Depth", SliceCount(source));
            _material.SetTexture("_Cube", kind == 1 ? source : null);
            _material.SetTexture("_Array", kind == 2 ? source : null);
            _material.SetTexture("_Volume", kind == 3 ? source : null);
            var previous = RenderTexture.active;
            try { Graphics.Blit(kind == 0 ? source : null, _target, _material); RenderCount++; }
            finally { RenderTexture.active = previous; }
            return _target;
        }

        void ReleaseTarget()
        {
            if (_target == null) return;
            _target.Release(); UnityEngine.Object.DestroyImmediate(_target); _target = null;
        }

        public void Dispose()
        {
            ReleaseTarget();
            if (_material != null) UnityEngine.Object.DestroyImmediate(_material);
            _material = null;
        }
    }
}
#endif
