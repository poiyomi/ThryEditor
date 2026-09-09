using System;
using System.IO;
using UnityEditor;

namespace Thry.ThryEditor.TexturePacker
{
    internal static class AtomicTextureExport
    {
        internal static TextureImporter Write(string path, byte[] bytes, Func<string, TextureImporter> import,
            Action<string, byte[]> write = null)
        {
            if (bytes == null || bytes.Length == 0) throw new IOException("Texture encoding produced no image data.");
            string token = Guid.NewGuid().ToString("N");
            string pending = path + ".pending." + token + "~", backup = path + ".backup." + token + "~";
            bool existed = File.Exists(path), replaced = false;
            byte[] oldMeta = File.Exists(path + ".meta") ? File.ReadAllBytes(path + ".meta") : null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                (write ?? File.WriteAllBytes)(pending, bytes);
                if (!File.Exists(pending) || new FileInfo(pending).Length != bytes.Length)
                    throw new IOException("The complete texture could not be written to " + path + ".");
                if (existed) File.Replace(pending, path, backup);
                else File.Move(pending, path);
                replaced = true;
                var importer = import(path);
                if (importer == null) throw new IOException("The texture could not be imported at " + path + ".");
                if (File.Exists(backup)) File.Delete(backup);
                return importer;
            }
            catch (Exception failure)
            {
                if (replaced)
                {
                    try
                    {
                        if (existed)
                        {
                            File.Copy(backup, path, true);
                            if (oldMeta != null) File.WriteAllBytes(path + ".meta", oldMeta);
                            else if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        }
                        else
                        {
                            AssetDatabase.DeleteAsset(path);
                            if (File.Exists(path)) File.Delete(path);
                            if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                        }
                    }
                    catch (Exception restoration)
                    {
                        // Keep the backup available for manual recovery if the filesystem itself refuses restoration.
                        throw new IOException("Texture export failed and restoration was blocked. Previous image backup: " + backup,
                            new AggregateException(failure, restoration));
                    }
                }
                if (File.Exists(backup)) File.Delete(backup);
                throw;
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
