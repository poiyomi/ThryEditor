using System;
using System.Collections.Generic;
using System.Text;
using Thry.ThryEditor.Helpers;
using Thry.ThryEditor.Drawers;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor
{
    public static class MaterialTextSerializer
    {
        public const int FORMAT_VERSION = 1;

        [Serializable]
        public class SerializedMaterial
        {
            public int thryTextMaterial;
            public string shader;
            public SerializedProp[] props;
            public SerializedTileLabel[] tileLabels; // optional; custom UV Tile Discard labels
        }

        [Serializable]
        public class SerializedProp
        {
            public string n;
            public string t;
            public float[] v;
            public string l; // optional custom UV Tile Discard label; null/absent for non-tile props
        }

        // Custom UV Tile Discard label, keyed by the tile's canonical property name. Captured separately
        // from props because three of every row's four tiles are [HideInInspector] and aren't serialized
        // as props, yet their labels still need to round-trip.
        [Serializable]
        public class SerializedTileLabel
        {
            public string n;
            public string l;
        }

        public static string Serialize(Material material)
        {
            if (material == null || material.shader == null) return null;

            var shader = material.shader;
            int count = shader.GetPropertyCount();
            var list = new List<SerializedProp>(count);

            for (int i = 0; i < count; i++)
            {
                var flags = shader.GetPropertyFlags(i);
                if ((flags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0) continue;

                string name = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                var entry = TryReadProp(material, name, type);
                if (entry != null) list.Add(entry);
            }

            var labels = CollectTileLabels(material, list);
            ExpandUdimSiblings(material, list);
            return Build(material.shader.name, list, labels);
        }

        public static string Serialize(ShaderPart part, Material material)
        {
            if (part == null || material == null) return null;

            var list = new List<SerializedProp>();
            CollectFromPart(part, material, list, new HashSet<string>());
            var labels = CollectTileLabels(material, list);
            ExpandUdimSiblings(material, list);
            return Build(material.shader.name, list, labels);
        }

        // A UV Tile Discard row serializes only its visible column-0 prop; pull in the three hidden
        // [HideInInspector] sibling tile values too so a paste restores the whole 4-tile row instead of
        // leaving the hidden columns at zero.
        static void ExpandUdimSiblings(Material material, List<SerializedProp> list)
        {
            var existing = new HashSet<string>();
            foreach (var p in list) existing.Add(p.n);

            var additions = new List<SerializedProp>();
            foreach (var p in list)
            {
                var siblings = TileLabelUtility.GetHiddenSiblingPropertyNames(p.n);
                if (siblings == null) continue;
                foreach (var sib in siblings)
                {
                    if (!existing.Add(sib)) continue;
                    if (!material.HasProperty(sib)) continue;
                    additions.Add(new SerializedProp { n = sib, t = "Float", v = new[] { material.GetFloat(sib) } });
                }
            }
            list.AddRange(additions);
        }

        // For every UV Tile Discard row represented in `list` (only the visible column-0 prop is ever
        // serialized), read the labels of all four tile columns from the material. Non-empty labels only.
        static List<SerializedTileLabel> CollectTileLabels(Material material, List<SerializedProp> list)
        {
            var labels = new List<SerializedTileLabel>();
            var seen = new HashSet<string>();
            foreach (var p in list)
            {
                var cols = TileLabelUtility.GetRowColumnCanonicalNames(p.n);
                if (cols == null) continue;
                foreach (string col in cols)
                {
                    if (!seen.Add(col)) continue;
                    string val = material.GetTag(TileLabelUtility.TAG_PREFIX + col, false, string.Empty);
                    if (!string.IsNullOrEmpty(val)) labels.Add(new SerializedTileLabel { n = col, l = val });
                }
            }
            return labels;
        }

        static void CollectFromPart(ShaderPart part, Material material, List<SerializedProp> list, HashSet<string> seen)
        {
            if (part == null) return;

            if (part.MaterialProperty != null)
            {
                string name = part.MaterialProperty.name;
                if (seen.Add(name) && material.HasProperty(name))
                {
                    var type = part.MaterialProperty.GetPropertyType();
                    var entry = TryReadProp(material, name, type);
                    if (entry != null) list.Add(entry);
                }
            }

            if (part is ShaderGroup group && group.Children != null)
            {
                foreach (var child in group.Children)
                    CollectFromPart(child, material, list, seen);
            }
        }

        static SerializedProp TryReadProp(Material m, string name, UnityEngine.Rendering.ShaderPropertyType type)
        {
            if (!m.HasProperty(name)) return null;

            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                    return new SerializedProp { n = name, t = "Float", v = new[] { m.GetFloat(name) } };
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    return new SerializedProp { n = name, t = "Range", v = new[] { m.GetFloat(name) } };
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                {
                    var c = m.GetColor(name);
                    return new SerializedProp { n = name, t = "Color", v = new[] { c.r, c.g, c.b, c.a } };
                }
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                {
                    var v4 = m.GetVector(name);
                    return new SerializedProp { n = name, t = "Vector", v = new[] { v4.x, v4.y, v4.z, v4.w } };
                }
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    return new SerializedProp { n = name, t = "Int", v = new[] { (float)m.GetInteger(name) } };
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                default:
                    return null;
            }
        }

        static string Build(string shaderName, List<SerializedProp> entries, List<SerializedTileLabel> tileLabels)
        {
            var sb = new StringBuilder(256 + entries.Count * 48);
            sb.Append("{\n");
            sb.Append("  \"thryTextMaterial\": ").Append(FORMAT_VERSION).Append(",\n");
            sb.Append("  \"shader\": ").Append(JsonString(shaderName)).Append(",\n");
            sb.Append("  \"props\": [");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\n    ");
                AppendEntry(sb, entries[i]);
            }
            if (entries.Count > 0) sb.Append("\n  ");
            sb.Append("]");
            if (tileLabels != null && tileLabels.Count > 0)
            {
                sb.Append(",\n  \"tileLabels\": [");
                for (int i = 0; i < tileLabels.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\n    {\"n\": ").Append(JsonString(tileLabels[i].n))
                      .Append(", \"l\": ").Append(JsonString(tileLabels[i].l)).Append("}");
                }
                sb.Append("\n  ]");
            }
            sb.Append("\n}");
            return sb.ToString();
        }

        static void AppendEntry(StringBuilder sb, SerializedProp e)
        {
            sb.Append("{\"n\": ").Append(JsonString(e.n))
              .Append(", \"t\": ").Append(JsonString(e.t))
              .Append(", \"v\": [");
            for (int i = 0; i < e.v.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(e.v[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append("]}");
        }

        static string JsonString(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static bool TryDeserialize(string json, out SerializedMaterial result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                result = Parser.Deserialize<SerializedMaterial>(json);
                if (result == null) return false;
                if (result.thryTextMaterial < 1) return false;
                if (result.props == null) result.props = new SerializedProp[0];
                if (result.tileLabels == null) result.tileLabels = new SerializedTileLabel[0];
                return true;
            }
            catch (Exception e)
            {
                ThryLogger.LogWarn($"MaterialTextSerializer: failed to parse JSON — {e.Message}");
                return false;
            }
        }

        public static int ApplyToMaterial(SerializedMaterial data, Material target)
        {
            if (data == null || target == null || data.props == null) return 0;

            // Restore custom UV Tile Discard labels onto the (scratch) material. The caller's subsequent
            // ShaderProperty.CopyFrom then propagates them, full row at a time, to the real targets.
            if (data.tileLabels != null)
            {
                foreach (var tl in data.tileLabels)
                {
                    if (tl == null || string.IsNullOrEmpty(tl.n) || !TileLabelUtility.IsUdimProperty(tl.n)) continue;
                    target.SetOverrideTag(TileLabelUtility.TAG_PREFIX + TileLabelUtility.CanonicalPropertyName(tl.n), tl.l ?? string.Empty);
                }
            }

            int applied = 0;
            foreach (var p in data.props)
            {
                if (p == null || string.IsNullOrEmpty(p.n) || p.v == null) continue;
                if (!target.HasProperty(p.n)) continue;

                switch (p.t)
                {
                    case "Float":
                    case "Range":
                        if (p.v.Length >= 1) { target.SetFloat(p.n, p.v[0]); applied++; }
                        break;
                    case "Int":
                        if (p.v.Length >= 1) { target.SetInteger(p.n, Mathf.RoundToInt(p.v[0])); applied++; }
                        break;
                    case "Color":
                        if (p.v.Length >= 4) { target.SetColor(p.n, new Color(p.v[0], p.v[1], p.v[2], p.v[3])); applied++; }
                        else if (p.v.Length == 3) { target.SetColor(p.n, new Color(p.v[0], p.v[1], p.v[2], 1f)); applied++; }
                        break;
                    case "Vector":
                        if (p.v.Length >= 4) { target.SetVector(p.n, new Vector4(p.v[0], p.v[1], p.v[2], p.v[3])); applied++; }
                        else if (p.v.Length == 3) { target.SetVector(p.n, new Vector4(p.v[0], p.v[1], p.v[2], 0f)); applied++; }
                        else if (p.v.Length == 2) { target.SetVector(p.n, new Vector4(p.v[0], p.v[1], 0f, 0f)); applied++; }
                        break;
                }
            }
            return applied;
        }

        public static readonly HashSet<UnityEngine.Rendering.ShaderPropertyType> SkipTextures = new HashSet<UnityEngine.Rendering.ShaderPropertyType>
        {
            UnityEngine.Rendering.ShaderPropertyType.Texture
        };
    }
}
