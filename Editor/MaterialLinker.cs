using System.Collections.Generic;
using System.Linq;
using Thry.ThryEditor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public class MaterialLinker
    {
        private static Dictionary<(Material,string), List<Material>> linked_materials;

        private static void Load()
        {
            if (linked_materials == null)
            {
                linked_materials = new Dictionary<(Material,string), List<Material>>();
                string raw = FileHelper.ReadFileIntoString(PATH.LINKED_MATERIALS_FILE);
                string[][] parsed = Parser.Deserialize<string[][]>(raw);
                if(parsed!=null)
                    foreach (string[] material_cloud in parsed)
                    {
                        if(material_cloud == null) continue;
                        List<Material> materials = new List<Material>();
                        for (int i = 1; i < material_cloud.Length; i++)
                        {
                            string path = AssetDatabase.GUIDToAssetPath(material_cloud[i]);
                            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (m != null)
                                materials.Add(m);
                        }
                        foreach (Material m in materials)
                            if(linked_materials.ContainsKey((m, material_cloud[0])) == false)
                                linked_materials.Add((m, material_cloud[0]), materials);
                    }
            }
        }

        private static void Save()
        {
            List<string[]> save_structre = new List<string[]>();
            HashSet<(Material,string)> has_already_been_saved = new HashSet<(Material,string)>();
            foreach (KeyValuePair<(Material,string),List<Material>> link in linked_materials)
            {
                if (has_already_been_saved.Contains(link.Key)) continue;
                string[] value = new string[link.Value.Count + 1];
                value[0] = link.Key.Item2;
                int i = 1;
                foreach (Material m in link.Value) {
                    has_already_been_saved.Add((m, link.Key.Item2));
                    value[i++] = UnityHelper.GetGUID(m);
                }
                save_structre.Add(value);
            }
            FileHelper.WriteStringToFile(Parser.Serialize(save_structre, prettyPrint: true),PATH.LINKED_MATERIALS_FILE);
        }

        public static bool IsLinked(MaterialProperty p)
        {
            Load();
            return linked_materials.ContainsKey(((Material)p.targets[0], p.name));
        }

        public static IEnumerable<Material> GetLinked(MaterialProperty p)
        {
            return GetLinked((Material)p.targets[0], p);
        }

        public static IEnumerable<Material> GetLinked(Material m, MaterialProperty p)
        {
            Load();
            if(linked_materials.TryGetValue((m,p.name), out List<Material> links))
                return links.Where(l => l != m && l != null);
            return null;
        }

        public static void Link(Material master, Material add_to, MaterialProperty p)
        {
            Load();
            Debug.Log("link " + master.name + "," + add_to.name);
            bool containes_key1 = linked_materials.ContainsKey((master,p.name));
            bool containes_key2 = linked_materials.ContainsKey((add_to,p.name));

            if(containes_key1 && containes_key2)
            {
                Unlink(add_to, p);
                Link(master, add_to, p);
                return;
            }
            else if (containes_key1)
                AddToListIfMaterialAlreadyLinked(master, add_to, p);
            else if (containes_key2)
                AddToListIfMaterialAlreadyLinked(add_to, master, p);
            else
            {
                List<Material> value = new List<Material>();
                value.Add(master);
                value.Add(add_to);
                linked_materials[(master,p.name)] = value;
                linked_materials[(add_to,p.name)] = value;
            }
        }

        private static void AddToListIfMaterialAlreadyLinked(Material existing, Material add, MaterialProperty p)
        {
            List<Material> value = linked_materials[(existing,p.name)];
            value.Add(add);
            linked_materials[(add,p.name)] = value;
        }

        public static void Unlink(Material m, MaterialProperty p)
        {
            Load();
            List<Material> value = linked_materials[(m,p.name)];
            value.Remove(m);
            linked_materials.Remove((m,p.name));
        }

        public static void UnlinkAll(Material m)
        {
            Load();
            List<(Material, string)> remove_keys = new List<(Material, string)>();
            foreach (KeyValuePair<(Material,string), List<Material>> link_cloud in linked_materials)
            {
                if (link_cloud.Key.Item1 == m)
                {
                    link_cloud.Value.Remove(m);
                    remove_keys.Add(link_cloud.Key);
                }
            }
            foreach ((Material, string) k in remove_keys)
                linked_materials.Remove(k);
            RemoveEmptyLinks();
            Save();
        }

        private static void RemoveEmptyLinks()
        {
            List<(Material, string)> remove_keys = new List<(Material, string)>();
            foreach (KeyValuePair<(Material,string), List<Material>> link_cloud in linked_materials)
            {
                if (link_cloud.Value.Count < 2)
                {
                    link_cloud.Value.Clear();
                    remove_keys.Add(link_cloud.Key);
                }
            }
            foreach ((Material, string) k in remove_keys)
                linked_materials.Remove(k);
        }
    }
}
