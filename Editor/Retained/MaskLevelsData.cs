using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    internal static class MaskLevelsData
    {
        internal static readonly string[] Suffixes = { "InputMin", "InputMax", "Midpoint", "OutputMin", "OutputMax", "Invert", "Normalize", "BoundsMin", "BoundsMax" };
        internal static readonly Vector4[] Defaults = { Vector4.zero, Vector4.one, Vector4.one * .5f, Vector4.zero, Vector4.one, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.one };
        sealed class Bounds { internal uint Version; internal Vector4 Min, Max; }
        static readonly Dictionary<Texture, Bounds> Cache = new Dictionary<Texture, Bounds>();
        static MaskLevelsData() { EditorApplication.projectChanged += Cache.Clear; AssemblyReloadEvents.beforeAssemblyReload += Cache.Clear; }
        internal static string Name(string texture, int index) => texture + "ML" + Suffixes[index];
        internal static void Measure(Texture texture, out Vector4 minimum, out Vector4 maximum)
        {
            if(texture == null) { minimum = maximum = Vector4.one; return; }
            if(Cache.TryGetValue(texture,out var bounds) && bounds.Version == texture.updateCount)
            { minimum=bounds.Min; maximum=bounds.Max; return; }
            var shader = Shader.Find("Hidden/Thry/MaskBounds");
            if(shader == null || !shader.isSupported) throw new InvalidOperationException("The mask bounds shader is unavailable.");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                minimum=Reduce(texture,material,false); maximum=Reduce(texture,material,true);
                // Keep only a small set of tiny results; never retain decoded pixel arrays.
                if(Cache.Count >= 128) Cache.Clear();
                Cache[texture]=new Bounds { Version=texture.updateCount, Min=minimum, Max=maximum };
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }
        static Vector4 Reduce(Texture texture, Material material, bool maximum)
        {
            var previous=RenderTexture.active; RenderTexture current=null; Texture2D read=null;
            try
            {
                Texture input=texture; int width=texture.width,height=texture.height;
                material.SetFloat("_Maximum",maximum?1:0);
                do
                {
                    int w=Mathf.Max(1,(width+1)/2),h=Mathf.Max(1,(height+1)/2);
                    var next=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                    next.filterMode=FilterMode.Point;
                    material.SetVector("_SourceSize",new Vector4(width,height,1f/width,1f/height));
                    try { Graphics.Blit(input,next,material); }
                    catch { RenderTexture.ReleaseTemporary(next); throw; }
                    if(current!=null) RenderTexture.ReleaseTemporary(current);
                    current=next; input=next; width=w; height=h;
                } while(width>1||height>1);
                RenderTexture.active=current;
                read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
                read.ReadPixels(new Rect(0,0,1,1),0,0,false);
                return (Vector4)read.GetPixel(0,0);
            }
            finally { RenderTexture.active=previous; if(current!=null)RenderTexture.ReleaseTemporary(current); if(read!=null)UnityEngine.Object.DestroyImmediate(read); }
        }
        internal static bool SynchronizeBounds(Material material, string texture)
        {
            if(!material.HasProperty(Name(texture,6))) return false;
            string animated=material.GetTag(Name(texture,6)+ShaderOptimizer.AnimatedTagSuffix,false,"");
            if(material.GetVector(Name(texture,6))==Vector4.zero && (string.IsNullOrEmpty(animated)||animated=="0")) return false;
            Measure(material.GetTexture(texture),out var min,out var max);
            if(material.GetVector(Name(texture,7))==min && material.GetVector(Name(texture,8))==max) return false;
            material.SetVector(Name(texture,7),min); material.SetVector(Name(texture,8),max); EditorUtility.SetDirty(material); return true;
        }
        internal static void PrepareForLock(Material material)
        {
            var shader=material.shader;
            for(int i=0;i<shader.GetPropertyCount();i++)
            {
                if(shader.GetPropertyType(i)!=UnityEngine.Rendering.ShaderPropertyType.Texture)continue;
                string name=shader.GetPropertyName(i);
                if(material.HasProperty(Name(name,6)))SynchronizeBounds(material,name);
            }
        }
    }

    internal sealed class MaskLevelsPreview : IDisposable
    {
        Material material;
        internal RenderTexture Before, After;
        Texture lastTexture; uint lastVersion; int lastChannel=int.MinValue;
        static RenderTexture Target() { var t=new RenderTexture(192,192,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) {hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};t.Create();return t; }
        internal void Render(Texture texture, Vector4[] values, int channel, bool legacyInvert = false)
        {
            if(texture==null)texture=Texture2D.whiteTexture;
            if(material==null)material=new Material(Shader.Find("Hidden/Thry/MaskLevelsPreview")){hideFlags=HideFlags.HideAndDontSave};
            if(Before==null)Before=Target(); if(After==null)After=Target();
            bool lost=!Before.IsCreated(); if(lost)Before.Create(); if(!After.IsCreated())After.Create();
            var min=values[0];var max=values[1];var gamma=Vector4.zero;
            for(int c=0;c<4;c++)
            {
                if(values[6][c]>.5f && values[8][c]>values[7][c]) {min[c]=Mathf.Lerp(values[7][c],values[8][c],min[c]);max[c]=Mathf.Lerp(values[7][c],values[8][c],max[c]);}
                gamma[c]=Mathf.Log(.5f)/Mathf.Log(Mathf.Clamp(values[2][c],.01f,.99f));
            }
            material.SetVector("_InputMin",min);material.SetVector("_InputMax",max);material.SetVector("_Gamma",gamma);
            material.SetFloat("_PostInvert",legacyInvert?1:0);
            material.SetVector("_OutputMin",values[3]);material.SetVector("_OutputMax",values[4]);material.SetVector("_Invert",values[5]);material.SetInt("_Channel",channel);
            var previous=RenderTexture.active;
            try
            {
                if(lost || lastTexture!=texture || lastVersion!=texture.updateCount || lastChannel!=channel)
                { material.SetFloat("_Adjusted",0);Graphics.Blit(texture,Before,material);lastTexture=texture;lastVersion=texture.updateCount;lastChannel=channel; }
                material.SetFloat("_Adjusted",1);Graphics.Blit(texture,After,material);
            }
            finally {RenderTexture.active=previous;}
        }
        public void Dispose()
        {
            if(material!=null)UnityEngine.Object.DestroyImmediate(material);
            if(Before!=null){Before.Release();UnityEngine.Object.DestroyImmediate(Before);}
            if(After!=null){After.Release();UnityEngine.Object.DestroyImmediate(After);}
            lastTexture=null;lastChannel=int.MinValue;
        }
    }
}
