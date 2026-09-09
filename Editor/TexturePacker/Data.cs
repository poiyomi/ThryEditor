using System;
using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.TexturePacker
{
    public enum TextureChannelIn { R, G, B, A, Max, None }
    public enum TextureChannelOut { R, G, B, A, None }
    public enum BlendMode { Add, Multiply, Max, Min }
    public enum InvertMode { None, Invert}
    public enum SaveType { PNG, JPG, EXR}
    public enum InputType { Texture, Color, Gradient }
    public enum GradientDirection { Horizontal, Vertical }
    public enum KernelPreset { None, Custom, EdgeDetection, Sharpen, GaussianBlur3x3, GaussianBlur5x5 }
    public enum RemapMode { None, RangeToRange }

    public static class SaveTypeExtensions
    {
        public static string GetTypeEnding(this SaveType type)
        {
            switch (type)
            {
                case SaveType.PNG: return ".png";
                case SaveType.JPG: return ".jpg";
                case SaveType.EXR: return ".exr";
                default: return ".png";
            }
        }
    }
    
    public abstract class IPackerUIDragable
    {
        public Vector2 UIPosition;
    }

    [Serializable]
    public class KernelSettings
    {
        public bool SplitVerticalHorizontal = true;
        public float[] X = GetKernelPreset(KernelPreset.None, true);
        public float[] Y = GetKernelPreset(KernelPreset.None, false);
        public int Loops = 1;
        public float Strength = 1;
        public bool TwoPass = false;
        public bool GrayScale = false;
        public bool[] Channels = new bool[4] { true, true, true, true };

        public void LoadPreset(KernelPreset preset)
        {
            Loops = 1;
            Strength = 1;
            TwoPass = false;
            GrayScale = false;
            Channels = new bool[] { true, true, true, true };
            if (preset == KernelPreset.GaussianBlur3x3 || preset == KernelPreset.GaussianBlur5x5) Loops = 10;
            if (preset == KernelPreset.EdgeDetection) TwoPass = true;
            if (preset == KernelPreset.EdgeDetection) Channels = new bool[] { true, true, true, false };
            X = GetKernelPreset(preset, true);
            Y = GetKernelPreset(preset, false);
        }

        public static float[] GetKernelPreset(KernelPreset preset, bool isXKernel)
        {
            // return a 5x5 kernel. always 25 values
            switch (preset)
            {
                case KernelPreset.Sharpen: return new float[] { 0, 0, 0, 0, 0, 0, 0, -0.5f, 0, 0, 0, -0.5f, 3, -0.5f, 0, 0, 0, -0.5f, 0, 0, 0, 0, 0, 0, 0 };
                case KernelPreset.EdgeDetection:
                    if (isXKernel) return new float[] { 0, 0, 0, 0, 0, 0, -1, 0, 1, 0, 0, -2, 0, 2, 0, 0, -1, 0, 1, 0, 0, 0, 0, 0, 0 };
                    else return new float[] { 0, 0, 0, 0, 0, 0, -1, -2, -1, 0, 0, 0, 0, 0, 0, 0, 1, 2, 1, 0, 0, 0, 0, 0, 0 };
                case KernelPreset.GaussianBlur3x3: return new float[] { 0, 0, 0, 0, 0, 0, 0.0625f, 0.125f, 0.0625f, 0, 0, 0.125f, 0.25f, 0.125f, 0, 0, 0.0625f, 0.125f, 0.0625f, 0, 0, 0, 0, 0, 0 };
                case KernelPreset.GaussianBlur5x5: return new float[] { 0.003f, 0.0133f, 0.0219f, 0.0133f, 0.003f, 0.0133f, 0.0596f, 0.0983f, 0.0596f, 0.0133f, 0.0219f, 0.0983f, 0.1621f, 0.0983f, 0.0219f, 0.0133f, 0.0596f, 0.0983f, 0.0596f, 0.0133f, 0.003f, 0.0133f, 0.0219f, 0.0133f, 0.003f };
            }
            return new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        }

        public float[] GetKernel(KernelPreset preset, bool isXKernel)
        {
            if (preset == KernelPreset.Custom)
            {
                return isXKernel ? X : Y;
            }
            return GetKernelPreset(preset, isXKernel);
        }

        public override bool Equals(object obj)
        {
            return obj is KernelSettings settings &&
                   SplitVerticalHorizontal == settings.SplitVerticalHorizontal &&
                   EqualityComparer<float[]>.Default.Equals(X, settings.X) &&
                   EqualityComparer<float[]>.Default.Equals(Y, settings.Y) &&
                   Loops == settings.Loops &&
                   Strength == settings.Strength &&
                   TwoPass == settings.TwoPass &&
                   GrayScale == settings.GrayScale &&
                   EqualityComparer<bool[]>.Default.Equals(Channels, settings.Channels);
        }

        public override int GetHashCode()
        {
#if NET_STANDARD_2_1
            return HashCode.Combine(SplitVerticalHorizontal, X, Y, Loops, Strength, TwoPass, GrayScale, Channels);
#else
            int hash = 17;
            hash = hash * 23 + SplitVerticalHorizontal.GetHashCode();
            hash = hash * 23 + EqualityComparer<float[]>.Default.GetHashCode(X);
            hash = hash * 23 + EqualityComparer<float[]>.Default.GetHashCode(Y);
            hash = hash * 23 + Loops.GetHashCode();
            hash = hash * 23 + Strength.GetHashCode();
            hash = hash * 23 + TwoPass.GetHashCode();
            hash = hash * 23 + GrayScale.GetHashCode();
            hash = hash * 23 + EqualityComparer<bool[]>.Default.GetHashCode(Channels);
            return hash;
#endif
        }
    }

    [Serializable]
    public class FileOutput
    {
        public string SaveFolder;
        public string FileName;
        public SaveType SaveType;
        public ColorSpace ColorSpace;
        public FilterMode FilterMode;
        public bool AlphaIsTransparency;
        public int SaveQuality;
        public Vector2Int Resolution;
        public bool CustomResolution;

        public FileOutput(string saveFolder, string fileName, SaveType saveType, ColorSpace colorSpace, FilterMode filterMode, bool alphaIsTransparency, int saveQuality, Vector2Int resolution)
        {
            SaveFolder = saveFolder;
            FileName = fileName;
            SaveType = saveType;
            ColorSpace = colorSpace;
            FilterMode = filterMode;
            AlphaIsTransparency = alphaIsTransparency;
            SaveQuality = saveQuality;
            Resolution = resolution;
        }

        public FileOutput Copy()
        {
            var copy = new FileOutput(
                SaveFolder,
                FileName,
                SaveType,
                ColorSpace,
                FilterMode,
                AlphaIsTransparency,
                SaveQuality,
                Resolution
            );
            copy.CustomResolution = CustomResolution;
            return copy;
        }
    }

    [Serializable]
    public class ImageAdjust : IPackerUIDragable
    {
        public float Brightness = 1;
        public float Hue = 0;
        public float Saturation = 1;
        public float Rotation = 0;
        public Vector2 Scale = Vector2.one;
        public Vector2 Offset = Vector2Int.zero;
        public bool ChangeCheck = false;

        public override bool Equals(object obj)
        {
            if (obj is ImageAdjust other)
            {
                return Brightness == other.Brightness &&
                       Hue == other.Hue &&
                       Saturation == other.Saturation &&
                       Rotation == other.Rotation &&
                       Scale == other.Scale &&
                       Offset == other.Offset;
            }
            return false;
        }

        public override int GetHashCode()
        {
#if UNITY_2021_2_OR_NEWER
            return HashCode.Combine(Brightness, Hue, Saturation, Rotation, Scale, Offset);
#else
            int hash = 17;
            hash = hash * 23 + Brightness.GetHashCode();
            hash = hash * 23 + Hue.GetHashCode();
            hash = hash * 23 + Saturation.GetHashCode();
            hash = hash * 23 + Rotation.GetHashCode();
            hash = hash * 23 + Scale.GetHashCode();
            hash = hash * 23 + Offset.GetHashCode();
            return hash;
#endif
        }
    }


    [Serializable]
    public struct OutputTarget
    {
        public BlendMode BlendMode;
        public InvertMode Invert;
        public float Fallback;
        
        public OutputTarget(BlendMode blendMode = BlendMode.Max, InvertMode invert = InvertMode.None, float fallback = 0)
        {
            BlendMode = blendMode;
            Invert = invert;
            Fallback = fallback;
        }
    }

    [Serializable, InitializeOnLoad]
    public class PackerSource : IPackerUIDragable
    {
        public FilterMode FilterMode;
        public Color Color;
        public Gradient Gradient;
        public GradientDirection GradientDirection;

        public Texture2D GradientTexture;
        public Texture2D ImageTexture;
        public string ImageTextureGuid;
        public long ImageTextureLocalId;
        public bool MissingImageReference;
        public Texture2D ColorTexture;
        public InputType InputType = InputType.Texture;
        [NonSerialized] public Vector2[] ChannelPositions = new Vector2[5];
        [NonSerialized] public Rect[] ChannelRects = new Rect[5];
        
        public Texture2D Texture
        {
            get
            {
                if (InputType == InputType.Texture) return ImageTexture;
                if (InputType == InputType.Gradient) return GradientTexture;
                if (InputType == InputType.Color) return ColorTexture;
                return null;
            }
        }

        public PackerSource()
        {
        }

        void ReleaseGeneratedTexture(Texture2D texture)
        {
            if (texture == null || texture == ImageTexture || AssetDatabase.Contains(texture)) return;
            RemoveDecodedTexture(texture);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        public void DisposeGeneratedTextures()
        {
            ReleaseGeneratedTexture(GradientTexture);
            ReleaseGeneratedTexture(ColorTexture);
            GradientTexture = null; ColorTexture = null;
        }

        public void SetInputTexture(Texture2D tex)
        {
            ImageTexture = tex;
            MissingImageReference = false;
            CaptureImageIdentity();
            FilterMode = tex != null ? tex.filterMode : FilterMode.Bilinear;
            if (tex != null) InputType = InputType.Texture;
        }

        internal void CaptureImageIdentity()
        {
            ImageTextureGuid = null; ImageTextureLocalId = 0;
            if (ImageTexture != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ImageTexture, out string guid, out long localId))
            { ImageTextureGuid = guid; ImageTextureLocalId = localId; }
        }

        internal void ResolveImageIdentity()
        {
            if (string.IsNullOrEmpty(ImageTextureGuid)) return;
            ImageTexture = null;
            string path = AssetDatabase.GUIDToAssetPath(ImageTextureGuid);
            if (string.IsNullOrEmpty(path)) { MissingImageReference = true; return; }
            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>())
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long localId)
                    && guid == ImageTextureGuid && localId == ImageTextureLocalId) { ImageTexture = candidate; break; }
            MissingImageReference = ImageTexture == null;
        }

        public void FixImageTexture()
        {
            if(ImageTexture == null) return;
            string path = AssetDatabase.GetAssetPath(ImageTexture);
            if (string.IsNullOrEmpty(path))
            {
                ThryLogger.LogWarn("TexturePacker", $"Removing faulty input texture {ImageTexture.name} as it could not be found in the project");
                ImageTexture = null; MissingImageReference = true;
            }
            else if (!AssetDatabase.Contains(ImageTexture)
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ImageTexture, out string guid, out long localId))
            {
                ThryLogger.LogWarn("TexturePacker", $"Removing faulty input texture {path} as it is not a Texture2D");
                ImageTexture = null; MissingImageReference = true;
            }
        }

        public void UpdateGradientTexture(Vector2Int size)
        {
            if (InputType != InputType.Gradient) return;
            if (GradientTexture != null && GradientTexture.width == size.x && GradientTexture.height == size.y) return;
            if (Gradient == null) Gradient = new Gradient();
            ReleaseGeneratedTexture(GradientTexture);
            GradientTexture = Converter.GradientToTexture(Gradient, size.x, size.y, GradientDirection == GradientDirection.Vertical);
        }

        public void UpdateColorTexture()
        {
            if (InputType != InputType.Color) return;
            if (ColorTexture != null && ColorTexture.GetPixel(0,0) == Color) return;
            ReleaseGeneratedTexture(ColorTexture);
            ColorTexture = Converter.ColorToTexture(Color, 16, 16);
        }

        public bool HasBeenModifiedExternally()
        {
            if (InputType != InputType.Texture) return false;
            if (Texture == null) return false;
            return _cachedTextureLastModifiedTime.TryGetValue(Texture, out DateTime cachedLastModified)
                && cachedLastModified != TextureHelper.GetLastModifiedTime(Texture);
        }

        static Dictionary<Texture2D, Texture2D> _cachedUncompressedTextures = new Dictionary<Texture2D, Texture2D>();
        static Dictionary<Texture2D, DateTime> _cachedTextureLastModifiedTime = new Dictionary<Texture2D, DateTime>();
        internal static long DecodedCacheBudgetBytes = 64L * 1024 * 1024;
        static readonly Dictionary<Texture2D, long> _decodedUse = new Dictionary<Texture2D, long>();
        static readonly Dictionary<Texture2D, int> _decodedPins = new Dictionary<Texture2D, int>();
        static long _decodeSequence;
        internal static IDisposable KeepDecodedSources(IEnumerable<PackerSource> sources) => new DecodedLease(sources);
        sealed class DecodedLease : IDisposable
        {
            readonly Texture2D[] _textures;
            public DecodedLease(IEnumerable<PackerSource> sources)
            {
                _textures = sources.Where(source => source.InputType == InputType.Texture && source.ImageTexture != null).Select(source => source.ImageTexture).Distinct().ToArray();
                foreach (var texture in _textures) _decodedPins[texture] = _decodedPins.TryGetValue(texture, out int count) ? count + 1 : 1;
            }
            public void Dispose()
            {
                foreach (var texture in _textures) if (--_decodedPins[texture] == 0) _decodedPins.Remove(texture);
                TrimDecodedCache(null);
            }
        }
        static void TrimDecodedCache(Texture2D current)
        {
            long bytes = _cachedUncompressedTextures.Sum(pair => pair.Value != null && pair.Value != pair.Key && !AssetDatabase.Contains(pair.Value)
                ? (long)pair.Value.width * pair.Value.height * 4 : 0);
            foreach (var texture in _decodedUse.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray())
            {
                if (bytes <= DecodedCacheBudgetBytes) break;
                if (texture == current || _decodedPins.ContainsKey(texture)) continue;
                var decoded = _cachedUncompressedTextures[texture];
                if (decoded != null && decoded != texture && !AssetDatabase.Contains(decoded)) bytes -= (long)decoded.width * decoded.height * 4;
                RemoveDecodedTexture(texture);
            }
        }
        static PackerSource()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearDecodedTextureCache;
            EditorApplication.quitting += ClearDecodedTextureCache;
            EditorApplication.projectChanged += () =>
            {
                foreach (var source in _cachedUncompressedTextures.Keys.Where(source => source == null).ToArray()) RemoveDecodedTexture(source);
            };
        }

        static void RemoveDecodedTexture(Texture2D source)
        {
            Texture2D decoded;
            if (_cachedUncompressedTextures.TryGetValue(source, out decoded) && decoded != null && decoded != source && !AssetDatabase.Contains(decoded))
                UnityEngine.Object.DestroyImmediate(decoded);
            _cachedUncompressedTextures.Remove(source); _cachedTextureLastModifiedTime.Remove(source);
            _decodedUse.Remove(source);
        }

        internal static void ClearDecodedTextureCache()
        {
            foreach (var source in _cachedUncompressedTextures.Keys.ToArray()) RemoveDecodedTexture(source);
        }
        public Texture2D UncompressedTexture
        {
            get
            {
                if (Texture == null) return null;
                if (_cachedUncompressedTextures.ContainsKey(Texture) == false
                    || _cachedUncompressedTextures[Texture] == null
                    || _cachedTextureLastModifiedTime[Texture] != TextureHelper.GetLastModifiedTime(Texture)
                    )
                {
                    string path = AssetDatabase.GetAssetPath(Texture);
                    string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    Texture2D decoded = null;
                    if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
                    {
                        EditorUtility.DisplayProgressBar("Loading Raw PNG", "Loading " + path, 0.5f);
                        try
                        {
                            decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                            if (!decoded.LoadImage(System.IO.File.ReadAllBytes(path))) throw new InvalidOperationException("Could not decode texture " + path);
                            decoded.filterMode = Texture.filterMode;
                        }
                        catch { if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded); throw; }
                        finally { EditorUtility.ClearProgressBar(); }
                    }
                    else if (extension == ".tga")
                    {
                        try { decoded = TextureHelper.LoadTGA(path, true); }
                        finally { EditorUtility.ClearProgressBar(); }
                        if (decoded != null) { decoded.filterMode = Texture.filterMode; decoded.hideFlags = HideFlags.HideAndDontSave; }
                    }
                    else decoded = Texture;
                    RemoveDecodedTexture(Texture);
                    _cachedUncompressedTextures[Texture] = decoded;
                    _cachedTextureLastModifiedTime[Texture] = TextureHelper.GetLastModifiedTime(Texture);

                    if(_cachedUncompressedTextures[Texture] == null)
                    {
                        ThryLogger.LogErr("[TexturePacker]", $"Texture {Texture.name} could not be loaded. Make sure it is a readable texture.");
                    }
                }
                _decodedUse[Texture] = ++_decodeSequence;
                TrimDecodedCache(Texture);
                return _cachedUncompressedTextures[Texture];
            }
        }

        public Texture2D ComputeShaderTexture
        {
            get
            {
                if (Texture == null) return Texture2D.whiteTexture;
                if (InputType == InputType.Texture) return UncompressedTexture;
                return Texture;
            }
        }

        public bool ComputeShaderTextureIsValid
        {
            get
            {
                if (Texture == null) return false;
                if (InputType == InputType.Texture && UncompressedTexture == null) return false;
                return true;
            }
        }

        public void FindMaxSize(ref int width, ref int height)
        {
            if (Texture == null) return;
            width = Mathf.Max(width, UncompressedTexture.width);
            height = Mathf.Max(height, UncompressedTexture.height);
        }
    }

    [Serializable]
    public struct Connection
    {
        public int FromTextureIndex;
        public TextureChannelIn FromChannel;
        public TextureChannelOut ToChannel;
        public RemapMode RemappingMode;
        public Vector4 Remapping;

        public Connection(int fromTex = -1, TextureChannelIn from = TextureChannelIn.None,
                      TextureChannelOut to = TextureChannelOut.None, RemapMode remapMode = RemapMode.None,
                      Vector4 remap = default)
        {
            FromTextureIndex = fromTex;
            FromChannel = from;
            ToChannel = to;
            RemappingMode = remapMode;
            Remapping = remap == default ? new Vector4(0, 1, 0, 1) : remap;
        }
    }

    struct ConnectionBezierPoints
    {
        public Vector3 Start;
        public Vector3 End;
        public Vector3 StartTangent;
        public Vector3 EndTangent;
        public ConnectionBezierPoints(Connection c, PackerSource[] sources, Vector2[] positionsOut)
        {
            Start = sources[c.FromTextureIndex].ChannelPositions[(int)c.FromChannel];
            End = positionsOut[(int)c.ToChannel];
            StartTangent = Start + Vector3.right * 50;
            EndTangent = End + Vector3.left * 50;
        }
    }

    struct InteractionWithConnection
    {
        public int ListIndex;
        public Connection Data;
        public float DistanceX;
    }
}
