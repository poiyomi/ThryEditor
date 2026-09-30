using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Thry.ThryEditor.Helpers
{
    public class Converter
    {

        public static Color StringToColor(string s)
        {
            s = s.Trim(new char[] { '(', ')' });
            string[] split = s.Split(",".ToCharArray());
            float[] rgba = new float[4] { 1, 1, 1, 1 };
            for (int i = 0; i < split.Length; i++) if (string.IsNullOrWhiteSpace(split[i]) == false) rgba[i] = float.Parse(split[i], CultureInfo.InvariantCulture);
            return new Color(rgba[0], rgba[1], rgba[2], rgba[3]);

        }

        public static Vector4 StringToVector(string s)
        {
            s = s.Trim(new char[] { '(', ')' });
            string[] split = s.Split(",".ToCharArray());
            float[] xyzw = new float[4];
            for (int i = 0; i < 4 && i < split.Length; i++) if (string.IsNullOrWhiteSpace(split[i]) == false) xyzw[i] = float.Parse(split[i], CultureInfo.InvariantCulture); else xyzw[i] = 0;
            return new Vector4(xyzw[0], xyzw[1], xyzw[2], xyzw[3]);
        }

        public static string ArrayToString(object[] a)
        {
            string ret = "";
            foreach (object o in a)
                ret += o.ToString() + ",";
            return ret.TrimEnd(new char[] { ',' });
        }

        public static string ArrayToString(Array a)
        {
            string ret = "";
            foreach (object o in a)
                ret += o.ToString() + ",";
            return ret.TrimEnd(new char[] { ',' });
        }

        //--Start--Gradient
        public static Gradient TextureToGradient(Texture2D texture)
        {
            var resized = Gradient_Resize(texture);
            try
            {
                Color[] values = Gradient_Sample(resized);
                var gradient = ConstructGradient(GradientChanges(values), values);
                // Opacity needs its own change samples; RGB may be constant while
                // the texture still contains an authored opacity ramp.
                var alphaValues = values.Select(value => new Color(value.a, value.a, value.a, 1)).ToArray();
                var alphaChanges = GradientChanges(alphaValues);
                var alphas = alphaChanges.Take(6).Select(change => new GradientAlphaKey(change[1].r, change[0].a)).ToList();
                alphas.Add(new GradientAlphaKey(values[0].a, 0));
                alphas.Add(new GradientAlphaKey(values[values.Length - 1].a, 1));
                gradient.alphaKeys = alphas.ToArray();
                return gradient;
            }
            finally { UnityEngine.Object.DestroyImmediate(resized); }
        }

        static List<Color[]> GradientChanges(Color[] values)
        {
            Color[] delta = CalcDelta(values);
            delta[0] = delta[1];
            Color[] delta_delta = CalcDelta(delta);
            List<Color[]> changes = DeltaDeltaToChanges(delta_delta, values);
            changes = RemoveChangesUnderDistanceThreshold(changes);
            SortChanges(changes);
            return changes;
        }

        private static Texture2D Gradient_Resize(Texture2D texture)
        {
            return TextureHelper.Resize(texture, 512, 512);
        }

        private static Color[] Gradient_Sample(Texture2D texture)
        {
            texture.wrapMode = TextureWrapMode.Clamp;
            int length = texture.width;
            Color[] ar = new Color[length];
            for (int i = 0; i < length; i++)
            {
                ar[i] = texture.GetPixel(i, i);
            }
            return ar;
        }

        private static Color[] CalcDelta(Color[] values)
        {
            Color[] delta = new Color[values.Length];
            delta[0] = new Color(0, 0, 0);
            for (int i = 1; i < values.Length; i++)
            {
                delta[i] = ColorSubtract(values[i - 1], values[i]);
            }
            return delta;
        }

        private static List<Color[]> DeltaDeltaToChanges(Color[] deltadelta, Color[] values)
        {
            List<Color[]> changes = new List<Color[]>();
            for (int i = 0; i < deltadelta.Length; i++)
            {
                if (deltadelta[i].r != 0 || deltadelta[i].g != 0 || deltadelta[i].b != 0)
                {
                    deltadelta[i].a = i / 512.0f;
                    Color[] new_change = new Color[2];
                    new_change[0] = deltadelta[i];
                    new_change[1] = values[i];
                    changes.Add(new_change);
                }
            }
            return changes;
        }

        const float GRADIENT_DISTANCE_THRESHOLD = 0.05f;
        private static List<Color[]> RemoveChangesUnderDistanceThreshold(List<Color[]> changes)
        {
            List<Color[]> new_changes = new List<Color[]>();
            if (changes.Count == 0) return new_changes;
            new_changes.Add(changes[0]);
            for (int i = 1; i < changes.Count; i++)
            {

                if (changes[i][0].a - new_changes[new_changes.Count - 1][0].a < GRADIENT_DISTANCE_THRESHOLD)
                {
                    if (ColorValueForDelta(new_changes[new_changes.Count - 1][0]) < ColorValueForDelta(changes[i][0]))
                    {
                        new_changes.RemoveAt(new_changes.Count - 1);
                        new_changes.Add(changes[i]);
                    }
                }
                else
                {
                    new_changes.Add(changes[i]);
                }
            }
            return new_changes;
        }

        private static void SortChanges(List<Color[]> changes)
        {
            changes.Sort(delegate (Color[] x, Color[] y)
            {
                float sizeX = ColorValueForDelta(x[0]);
                float sizeY = ColorValueForDelta(y[0]);
                if (sizeX < sizeY) return 1;
                else if (sizeY < sizeX) return -1;
                return 0;
            });
        }

        private static Gradient ConstructGradient(List<Color[]> changes, Color[] values)
        {
            List<GradientAlphaKey> alphas = new List<GradientAlphaKey>();
            List<GradientColorKey> colors = new List<GradientColorKey>();
            for (int i = 0; i < 6 && i < changes.Count; i++)
            {
                colors.Add(new GradientColorKey(changes[i][1], changes[i][0].a));
                //Debug.Log("key " + changes[i][0].a);
            }
            colors.Add(new GradientColorKey(values[0], 0));
            colors.Add(new GradientColorKey(values[values.Length - 1], 1));
            alphas.Add(new GradientAlphaKey(1, 0));
            alphas.Add(new GradientAlphaKey(1, 1));
            Gradient gradient = new Gradient();
            gradient.SetKeys(colors.ToArray(), alphas.ToArray());
            return gradient;
        }

        private static float ColorValueForDelta(Color col)
        {
            return Mathf.Abs(col.r) + Mathf.Abs(col.g) + Mathf.Abs(col.b);
        }

        private static Color ColorSubtract(Color col1, Color col2)
        {
            return new Color(col1.r - col2.r, col1.g - col2.g, col1.b - col2.b);
        }

        public static Texture2D ColorToTexture(Color color, int width, int height, bool linear = false)
        {
            width = Mathf.Max(0, Mathf.Min(8192, width));
            height = Mathf.Max(0, Mathf.Min(8192, height));
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true, linear);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            return texture;
        }

        public static Texture2D GradientToTexture(Gradient gradient, int width, int height, bool vertical = false, bool linear = false)
        {
            width = Mathf.Max(0, Mathf.Min(8192, width));
            height = Mathf.Max(0, Mathf.Min(8192, height));
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA64, false, linear);
            Color col;
            if (vertical)
            {
                for (int y = 0; y < height; y++)
                {
                    col = gradient.Evaluate((float)y / height);
                    for (int x = 0; x < width; x++) texture.SetPixel(x, y, col);
                }
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    col = gradient.Evaluate((float)x / width);
                    for (int y = 0; y < height; y++) texture.SetPixel(x, y, col);
                }
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply();
            return texture;
        }

        //--End--Gradient

        public static Texture2D CurveToTexture(AnimationCurve curve, TextureData texture_settings, bool vertical = false)
        {
            Texture2D texture = new Texture2D(texture_settings.width, texture_settings.height);
            int samples = vertical ? texture_settings.height : texture_settings.width;
            int repeats = vertical ? texture_settings.width : texture_settings.height;
            for (int i = 0; i < samples; i++)
            {
                Color color = new Color();
                float value = curve.Evaluate((float)i / Mathf.Max(1, samples - 1));
                value = Mathf.Clamp01(value);
                if (texture_settings.channel == 'r')
                    color.r = value;
                else if (texture_settings.channel == 'g')
                    color.g = value;
                else if (texture_settings.channel == 'b')
                    color.b = value;
                else if (texture_settings.channel == 'a')
                    color.a = value;
                if (texture_settings.channel != 'a')
                    color.a = 1;
                for (int j = 0; j < repeats; j++)
                    texture.SetPixel(vertical ? j : i, vertical ? i : j, color);
            }
            texture.Apply();
            texture_settings.ApplyModes(texture);
            return texture;
        }

        //==============Texture Array=================

        [MenuItem("Assets/Thry/Flipbooks/Images 2 TextureArray", false, 303)]
        static void SelectionImagesToTextureArray()
        {
            string[] paths = Selection.assetGUIDs.Select(g => AssetDatabase.GUIDToAssetPath(g)).ToArray();
            // Only here: drops must keep the material selected
            Texture2DArray texture2DArray = PathsToTexture2DArray(paths);
            if (texture2DArray != null)
                Selection.activeObject = texture2DArray;
        }

        [MenuItem("Assets/Thry/Flipbooks/Images 2 TextureArray", true)]
        static bool SelectionImagesToTextureArrayValidator()
        {
            if (Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0)
            {
                return Selection.assetGUIDs.All(g => AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(g)) == typeof(Texture2D));
            }
            return false;
        }

        public static Texture2DArray PathsToTexture2DArray(string[] paths)
        {
            return PathsToTexture2DArray(paths, out _);
        }

        public static Texture2DArray PathsToTexture2DArray(string[] paths, out float fps)
        {
            fps = 0;
            if (paths.Length == 0)
                return null;
            if (paths[0].EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                return Converter.GifToTextureArray(paths[0], out fps);
            }
            else
            {
                Texture2D[] textures = paths.Where(p => AssetDatabase.GetMainAssetTypeAtPath(p)?.IsAssignableFrom(typeof(Texture2D)) == true)
                    .Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).Where(t => t != null).ToArray();
                if (textures.Length == 0)
                {
                    EditorUtility.DisplayDialog("Texture Array", "Only textures can be combined into a texture array.", "OK");
                    return null;
                }
                Array.Sort(textures, (UnityEngine.Object one, UnityEngine.Object two) => EditorUtility.NaturalCompare(one.name, two.name));
                bool linear = !GraphicsFormatUtility.IsSRGBFormat(textures[0].graphicsFormat);

                string assetPath = AssetDatabase.GetAssetPath(textures[0]);
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath.Remove(assetPath.LastIndexOf('/')) + "/Texture2DArray.asset");

                Texture2DArray texture2DArray;
                if (CanCopyFramesDirectly(textures))
                {
                    texture2DArray = Textre2DArrayToAsset(textures, linear);
                }
                else
                {
                    Texture2D[] frames = new Texture2D[textures.Length];
                    try
                    {
                        TextureFormat format = ConvertedFrameFormat(textures[0].format);
                        for (int i = 0; i < textures.Length; i++)
                        {
                            frames[i] = ReadableFrame(textures[i], textures[0].width, textures[0].height, format);
                            if (frames[i].format != format)
                            {
                                EditorUtility.DisplayDialog("Texture Array", $"These images can't be converted to {format}. Change the compression of the first image and try again.", "OK");
                                return null;
                            }
                        }
                        frames[0].anisoLevel = textures[0].anisoLevel;
                        frames[0].wrapModeU = textures[0].wrapModeU;
                        frames[0].wrapModeV = textures[0].wrapModeV;
                        texture2DArray = Textre2DArrayToAsset(frames, linear);
                    }
                    finally
                    {
                        foreach (Texture2D frame in frames)
                            if (frame != null) UnityEngine.Object.DestroyImmediate(frame);
                    }
                }

                AssetDatabase.CreateAsset(texture2DArray, assetPath);
                AssetDatabase.SaveAssets();
                return texture2DArray;
            }
        }

        // Needs CPU data: a GPU-only copy is lost when saved
        static bool CanCopyFramesDirectly(Texture2D[] textures)
        {
            Texture2D first = textures[0];
            int fullMipCount = 1;
            for (int size = Mathf.Max(first.width, first.height); size > 1; size >>= 1) fullMipCount++;
            return textures.All(t => t.isReadable && t.width == first.width && t.height == first.height
                && t.format == first.format && t.mipmapCount == fullMipCount) && !GraphicsFormatUtility.IsCrunchFormat(first.format);
        }

        static TextureFormat ConvertedFrameFormat(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1Crunched: return TextureFormat.DXT1;
                case TextureFormat.DXT5Crunched: return TextureFormat.DXT5;
                case TextureFormat.ETC_RGB4Crunched: return TextureFormat.ETC_RGB4;
                case TextureFormat.ETC2_RGBA8Crunched: return TextureFormat.ETC2_RGBA8;
            }
            return GraphicsFormatUtility.IsCompressedFormat(format) ? format : TextureFormat.RGBA32;
        }

        static Texture2D ReadableFrame(Texture2D source, int width, int height, TextureFormat format)
        {
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            RenderTexture previous = RenderTexture.active;
            RenderTexture temp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Texture2D frame = new Texture2D(width, height, TextureFormat.RGBA32, true);
            try
            {
                Graphics.Blit(source, temp);
                RenderTexture.active = temp;
                frame.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                frame.Apply(true);
                if (format != TextureFormat.RGBA32)
                    EditorUtility.CompressTexture(frame, format, TextureCompressionQuality.Normal);
                return frame;
            }
            catch { UnityEngine.Object.DestroyImmediate(frame); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temp); }
        }

        [MenuItem("Assets/Thry/Flipbooks/Gif 2 TextureArray", false, 303)]
        static void SelectionGifToTextureArray()
        {
            GifToTextureArray(AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]));
        }

        [MenuItem("Assets/Thry/Flipbooks/Gif 2 TextureArray", true)]
        static bool SelectionGifToTextureArrayValidator()
        {
            if (Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0)
            {
                return AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]).EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public static Texture2DArray GifToTextureArray(string path)
        {
            return GifToTextureArray(path, out _);
        }

        public static Texture2DArray GifToTextureArray(string path, out float fps)
        {
            fps = 0;
            List<Texture2D> array = GetGifFrames(path, out fps);
            if (array == null) return null;
            if (array.Count == 0)
            {
                Debug.LogError("Failed to decode GIF or GIF is empty.");
                return null;
            }
            Texture2DArray arrayTexture;
            try { arrayTexture = Textre2DArrayToAsset(array.ToArray()); }
            finally { foreach (Texture2D frame in array) UnityEngine.Object.DestroyImmediate(frame); }
            string assetPath = System.IO.Path.ChangeExtension(path, ".asset");
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal) || assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(arrayTexture, assetPath);
            AssetDatabase.SaveAssets();
            return arrayTexture;
        }

        public static List<Texture2D> GetGifFrames(string path)
        {
            return GetGifFrames(path, out _);
        }

        public static List<Texture2D> GetGifFrames(string path, out float fps)
        {
            fps = 0;
            var gifFrames = new List<Texture2D>();
            var decoder = new GifDecoder();
            var frames = decoder.Decode(path);

            if (frames == null || frames.Count == 0)
                return gifFrames;

            // Calculate average FPS from frame delays (delay is in centiseconds)
            int totalDelay = 0;
            foreach (var frame in frames)
                totalDelay += frame.Delay > 0 ? frame.Delay : 10; // Default 10cs (100ms) if not specified
            float avgDelaySeconds = (totalDelay / (float)frames.Count) / 100f;
            fps = avgDelaySeconds > 0 ? 1f / avgDelaySeconds : 10f;

            int targetWidth = Mathf.ClosestPowerOfTwo(decoder.Width - 1);
            int targetHeight = Mathf.ClosestPowerOfTwo(decoder.Height - 1);
            bool hasAlpha = false;

            for (int i = 0; i < frames.Count; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("From GIF", $"Processing frame {i + 1}/{frames.Count}", (float)i / frames.Count))
                {
                    EditorUtility.ClearProgressBar();
                    foreach (Texture2D decoded in gifFrames) UnityEngine.Object.DestroyImmediate(decoded);
                    return null;
                }

                var frame = frames[i];
                var srcTexture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false);

                // Flip Y and copy pixels
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        var pixel = frame.Pixels[(frame.Height - 1 - y) * frame.Width + x];
                        srcTexture.SetPixel(x, y, pixel);
                        if (pixel.a < 255)
                            hasAlpha = true;
                    }
                }
                srcTexture.Apply();

                // Resize to power of two
                Texture2D resized = TextureHelper.Resize(srcTexture, targetWidth, targetHeight);
                UnityEngine.Object.DestroyImmediate(srcTexture);

                gifFrames.Add(resized);
            }

            EditorUtility.ClearProgressBar();

            // Compress textures
            for (int i = 0; i < gifFrames.Count; i++)
            {
                EditorUtility.CompressTexture(gifFrames[i], hasAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1, TextureCompressionQuality.Normal);
                gifFrames[i].Apply(true, false);
            }

            return gifFrames;
        }

        private static Texture2DArray Textre2DArrayToAsset(Texture2D[] array, bool linear = false)
        {
            Texture2DArray texture2DArray = new Texture2DArray(array[0].width, array[0].height, array.Length, array[0].format, true, linear);

            for (int i = 0; i < array.Length; i++)
            {
                for (int m = 0; m < array[i].mipmapCount; m++)
                {
                    UnityEngine.Graphics.CopyTexture(array[i], 0, m, texture2DArray, i, m);
                }
            }

            texture2DArray.anisoLevel = array[0].anisoLevel;
            texture2DArray.wrapModeU = array[0].wrapModeU;
            texture2DArray.wrapModeV = array[0].wrapModeV;

            texture2DArray.Apply(false, true);

            return texture2DArray;
        }
    }

}
