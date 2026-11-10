using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Helpers
{
    public class TextureHelper
    {
        public static Gradient GetGradient(Texture texture, bool log = true)
        {
            if (texture != null)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                string gradient_data_string = null;
                if (path != null) gradient_data_string = FileHelper.LoadValueFromFile(AssetDatabase.AssetPathToGUID(path), PATH.GRADIENT_INFO_FILE);
                //For Backwards compatibility check old id (name) if guid cant be found
                if (gradient_data_string == null) gradient_data_string = FileHelper.LoadValueFromFile(texture.name, PATH.GRADIENT_INFO_FILE);
                if (gradient_data_string != null)
                {
                    if(log) Debug.Log(texture.name + " Gradient loaded from file.");
                    Gradient g = Parser.Deserialize<Gradient>(gradient_data_string);
                    return g;
                }
                if(log) Debug.Log(texture.name + " Converted into Gradient.");
                var readable = GetReadableTexture(texture);
                try { return Converter.TextureToGradient(readable); }
                finally { UnityEngine.Object.DestroyImmediate(readable); }
            }
            return new Gradient();
        }

        public static Texture SaveTextureAsPNG(Texture2D texture, string path, TextureData settings = null)
            => SaveTextureAsPNGCore(texture, path, settings, FileHelper.WriteBytesToFile, assetPath =>
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                if (settings != null) settings.ApplyModes(assetPath);
                return AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            });

        internal static Texture SaveTextureAsPNGCore(Texture2D texture, string path, TextureData settings,
            Func<byte[], string, bool> write, Func<string, Texture> import)
        {
            if (!path.EndsWith(".png"))
                path += ".png";
            byte[] encoding = texture.EncodeToPNG();
            if (encoding == null || encoding.Length == 0 || !write(encoding, path))
                throw new IOException("Could not write texture at \"" + path + "\". The material was not changed.");
            Texture saved = import(path);
            if (saved == null) throw new IOException("Could not import the saved texture at \"" + path + "\". The material was not changed.");
            Debug.Log("Texture saved at \"" + path + "\".");
            return saved;
        }

        public static Texture2D ConvertToGamma(Texture2D texture)
        {
            Texture2D ret = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, texture.mipmapCount > 0);
            for (int x = 0; x < texture.width; x++)
            {
                for (int y = 0; y < texture.height; y++)
                {
                    Color c = texture.GetPixel(x, y);
                    c.r = Mathf.Pow(c.r, 1/2.2f);
                    c.g = Mathf.Pow(c.g, 1/2.2f);
                    c.b = Mathf.Pow(c.b, 1/2.2f);
                    ret.SetPixel(x, y, c);
                }
            }
            ret.Apply();
            return ret;
        }

        public static Texture2D GetReadableTexture(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temp = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Texture2D ret = null;
            try
            {
                Graphics.Blit(texture, temp); RenderTexture.active = temp;
                ret = new Texture2D(texture.width, texture.height);
                ret.ReadPixels(new Rect(0, 0, temp.width, temp.height), 0, 0); ret.Apply();
                return ret;
            }
            catch { if (ret != null) UnityEngine.Object.DestroyImmediate(ret); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temp); }
        }

        public static Texture2D Resize(Texture2D texture, int width, int height)
        {
            Texture2D ret = new Texture2D(width, height, texture.format, texture.mipmapCount > 0);
            try
            {
                float scaleX = ((float)texture.width) / width;
                float scaleY = ((float)texture.height) / height;
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        ret.SetPixel(x, y, texture.GetPixel((int)(scaleX * x), (int)(scaleY * y)));
                    }
                }
                ret.Apply();
                return ret;
            }
            catch { UnityEngine.Object.DestroyImmediate(ret); throw; }
        }

        public static DateTime GetLastModifiedTime(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (File.Exists(path))
            {
                return File.GetLastWriteTime(path);
            }
            return DateTime.MinValue;
        }

        //===============TGA Loader by aaro4130 https://forum.unity.com/threads/tga-loader-for-unity3d.172291/==============

        public static Texture2D LoadTGA(string TGAFile, bool displayProgressbar = false)
        {
            using (BinaryReader r = new BinaryReader(File.Open(TGAFile, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                byte IDLength = r.ReadByte();
                byte ColorMapType = r.ReadByte();
                byte ImageType = r.ReadByte();
                Int16 CMapStart = r.ReadInt16();
                Int16 CMapLength = r.ReadInt16();
                byte CMapDepth = r.ReadByte();
                Int16 XOffset = r.ReadInt16();
                Int16 YOffset = r.ReadInt16();
                Int16 Width = r.ReadInt16();
                Int16 Height = r.ReadInt16();
                byte PixelDepth = r.ReadByte();
                byte ImageDescriptor = r.ReadByte();
                // 2 = uncompressed truecolor
                if (ImageType != 2 || (PixelDepth != 24 && PixelDepth != 32))
                {
                    string reason = ImageType == 9 || ImageType == 10 || ImageType == 11 ? "is run-length encoded"
                        : ImageType == 3 ? "is grayscale"
                        : ImageType == 1 ? "uses a color map"
                        : ImageType == 0 ? "has no image data"
                        : ImageType == 2 ? "has " + PixelDepth + "-bit pixels"
                        : "has an unknown image type";
                    throw new NotSupportedException(Path.GetFileName(TGAFile) + " can't be read directly because it " + reason + ".");
                }
                r.ReadBytes(IDLength);
                if (ColorMapType == 1) r.ReadBytes((ushort)CMapLength * ((CMapDepth + 7) / 8));
                if (r.BaseStream.Length - r.BaseStream.Position < (PixelDepth == 32 ? 4 : 3) * Width * Height)
                    throw new EndOfStreamException(Path.GetFileName(TGAFile) + " ends before all of its pixels.");
                bool startsAtTop = (ImageDescriptor & 1 << 5) >> 5 == 1;
                bool startsAtRight = (ImageDescriptor & 1 << 4) >> 4 == 1;
                //     MsgBox("Dimensions are "  Width  ","  Height)
                Texture2D b = new Texture2D(Width, Height, TextureFormat.ARGB32, false);
                Color[] colors = new Color[Width * Height];
                int texX = 0;
                int texY = 0;
                int index = 0;
                float red = 0, green = 0, blue = 0, alpha = 0;
                Byte[] bytes = r.ReadBytes((PixelDepth == 32 ? 4 : 3) * Width * Height);

                int byteIndex = 0;
                for (int y = 0; y < b.height; y++)
                {
                    if (displayProgressbar && y % 50 == 0) EditorUtility.DisplayProgressBar("Loading Raw TGA", "Loading " + TGAFile, (float)y / b.height);
                    for (int x = 0; x < b.width; x++)
                    {
                        texX = x;
                        texY = y;
                        if (startsAtRight) texX = b.width - x - 1;
                        if (startsAtTop) texY = b.height - y - 1;
                        index = texX + texY * b.width;

                        blue = Convert.ToSingle(bytes[byteIndex++]);
                        green = Convert.ToSingle(bytes[byteIndex++]);
                        red = Convert.ToSingle(bytes[byteIndex++]);

                        blue = Mathf.Pow(blue / 255, 0.45454545454f);
                        green = Mathf.Pow(green / 255, 0.45454545454f);
                        red = Mathf.Pow(red / 255, 0.45454545454f);

                        // blue /= 255;
                        // green /= 255;
                        // red /= 255;

                        colors[index].r = red;
                        colors[index].g = green;
                        colors[index].b = blue;

                        if (PixelDepth == 32)
                        {
                            alpha = Convert.ToSingle(bytes[byteIndex++]);
                            alpha /= 255;
                            colors[index].a = alpha;
                        }
                        else
                        {
                            colors[index].a = 1;
                        }
                    }
                }
                b.SetPixels(colors);
                b.Apply();
                if (displayProgressbar) EditorUtility.ClearProgressBar();

                return b;
            }
        }

        public class VRAM
        {
            public static string ToByteString(long l)
            {
                if (l < 1000) return l + " B";
                if (l < 1000000) return (l / 1000f).ToString("n2") + " KB";
                if (l < 1000000000) return (l / 1000000f).ToString("n2") + " MB";
                else return (l / 1000000000f).ToString("n2") + " GB";
            }
        }
    }

}
