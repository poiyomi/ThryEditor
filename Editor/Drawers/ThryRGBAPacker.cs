using System.Linq;
using Thry.ThryEditor.TexturePacker;
using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
    public partial class ThryRGBAPackerDrawer
    {
        // TODO : Load lacale by property name in the future: propname_r, propname_g, propname_b, propname_a
        class ThryRGBAPackerData
        {
            public InlinePackerChannelConfig _input_r;
            public InlinePackerChannelConfig _input_g;
            public InlinePackerChannelConfig _input_b;
            public InlinePackerChannelConfig _input_a;

            public bool _isInit;
            public bool _hasConfigChanged;
            public bool _hasTextureChanged;
        }

        MaterialProperty _prop;
        ThryRGBAPackerData _current;

        string _label1;
        string _label2;
        string _label3;
        string _label4;
        bool _firstTextureIsRGB;
        ColorSpace _colorSpace = ColorSpace.Gamma;
        bool _alphaIsTransparency = true;

        // for locale changing
        // i tried using an array to save the default labels, but the data just got lost somewhere. not sure why
        string _defaultLabel1;
        string _defaultLabel2;
        string _defaultLabel3;
        string _defaultLabel4;
        int _reloadCount = -1;
        static int _reloadCountStatic;

        public static void Reload()
        {
            _reloadCountStatic++;
        }

        void LoadLabels()
        {
            if (_reloadCount == _reloadCountStatic) return;
            // using the string itself as a key for reuse in other places. this might cause issues, if it does in the future 
            // we can add the class name as a prefix to the key
            _label1 = ShaderEditor.Active.Locale.Get(_defaultLabel1, _defaultLabel1);
            _label2 = ShaderEditor.Active.Locale.Get(_defaultLabel2, _defaultLabel2);
            _label3 = ShaderEditor.Active.Locale.Get(_defaultLabel3, _defaultLabel3);
            _label4 = ShaderEditor.Active.Locale.Get(_defaultLabel4, _defaultLabel4);
            _reloadCount = _reloadCountStatic;
        }

        // end locale changing

        public ThryRGBAPackerDrawer(string label1, string label2, string label3, string label4, string colorspace, string alphaIsTransparency)
        {
            _label1 = string.IsNullOrWhiteSpace(label1) ? null : label1;
            _label2 = string.IsNullOrWhiteSpace(label2) ? null : label2;
            _label3 = string.IsNullOrWhiteSpace(label3) ? null : label3;
            _label4 = string.IsNullOrWhiteSpace(label4) ? null : label4;
            _defaultLabel1 = _label1;
            _defaultLabel2 = _label2;
            _defaultLabel3 = _label3;
            _defaultLabel4 = _label4;
            _colorSpace = (colorspace == "linear" || colorspace == "Linear") ? ColorSpace.Linear : ColorSpace.Gamma;
            _alphaIsTransparency = alphaIsTransparency == "true" || alphaIsTransparency == "True";
        }

        public ThryRGBAPackerDrawer(string label1, string label2, string colorspace, string alphaIsTransparency) : this(label1, label2, null, null, colorspace, alphaIsTransparency)
        {
            _firstTextureIsRGB = true;
        }

        void Init()
        {
            if (_current._isInit) return;
            _current._input_r = LoadForChannel(ShaderEditor.Active.Materials[0], _prop.name, "r");
            _current._input_g = LoadForChannel(ShaderEditor.Active.Materials[0], _prop.name, "g");
            _current._input_b = LoadForChannel(ShaderEditor.Active.Materials[0], _prop.name, "b");
            _current._input_a = LoadForChannel(ShaderEditor.Active.Materials[0], _prop.name, "a");
            _current._isInit = true;
        }

        // Default fallback based on the shader property's default texture
        // "black" -> 0, "gray"/"grey" -> 0.5, everything else -> 1
        string GetDefaultFallback(Material m, string id)
        {
            string defaultFallback = "1";
            int propIdx = m.shader.FindPropertyIndex(id);
            if (propIdx >= 0)
            {
                string defTexName = m.shader.GetPropertyTextureDefaultName(propIdx)?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(defTexName))
                {
                    if (defTexName.Contains("black")) defaultFallback = "0";
                    else if (defTexName.Contains("gray") || defTexName.Contains("grey")) defaultFallback = "0.5";
                }
            }
            return defaultFallback;
        }

        InlinePackerChannelConfig LoadForChannel(Material m, string id, string channel)
        {
            InlinePackerChannelConfig packerChannelConfig = new InlinePackerChannelConfig();
            string defaultFallback = GetDefaultFallback(m, id);
            packerChannelConfig.Fallback = Parser.ParseFloat(m.GetTag(id + "_texPack_" + channel + "_fallback", false, defaultFallback));
            packerChannelConfig.Invert = bool.Parse(m.GetTag(id + "_texPack_" + channel + "_inverted", false, "false"));
            packerChannelConfig.Channel = (TexturePacker.TextureChannelIn)int.Parse(m.GetTag(id + "_texPack_" + channel + "_channel", false, "4"));
            packerChannelConfig.Remapping = Parser.ParseVector4(m.GetTag(id + "_texPack_" + channel + "_srcRange", false, "(0,1,0,1)"));
            string guid = m.GetTag(id + "_texPack_" + channel + "_guid", false, "");
            if (string.IsNullOrEmpty(guid) == false)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p != null)
                    packerChannelConfig.Source.SetInputTexture(AssetDatabase.LoadAssetAtPath<Texture2D>(p));
            }
            _ = packerChannelConfig.Source.ComputeShaderTexture; // to cache uncompressed texture
            return packerChannelConfig;
        }

        TexturePackerConfig GetConfig()
        {
            TexturePackerConfig config = TexturePackerConfig.GetNewConfig();
            config.Sources = GetTextureSources();
            config.Targets = GetOutputConfigs();
            config.Connections = GetConnections().ToList();
            config.FileOutput.FilterMode = GetFiltermode();
            config.FileOutput.ColorSpace = _colorSpace;
            config.FileOutput.AlphaIsTransparency = _alphaIsTransparency;
            return config;
        }

        TexturePacker.PackerSource[] GetTextureSources()
        {
            // build sources array
            return new TexturePacker.PackerSource[4]{
                _current._input_r.Source, _current._input_g.Source, _current._input_b.Source, _current._input_a.Source };
        }

        TexturePacker.OutputTarget[] GetOutputConfigs()
        {
            // Build OutputConfig Array
            TexturePacker.OutputTarget[] outputConfigs = new TexturePacker.OutputTarget[4];

            if (_firstTextureIsRGB)
            {
                outputConfigs[0] = _current._input_r.ToOutputConfig();
                outputConfigs[1] = _current._input_r.ToOutputConfig();
                outputConfigs[2] = _current._input_r.ToOutputConfig();
                outputConfigs[3] = _current._input_g.ToOutputConfig();
            }
            else
            {
                outputConfigs[0] = _current._input_r.ToOutputConfig();
                outputConfigs[1] = _current._input_g.ToOutputConfig();
                outputConfigs[2] = _current._input_b.ToOutputConfig();
                outputConfigs[3] = _current._input_a.ToOutputConfig();
            }
            return outputConfigs;
        }

        Connection[] GetConnections()
        {
            // Build connections array
            Connection[] connections = new Connection[4];
            if (_firstTextureIsRGB)
            {
                connections[0] = new Connection(0, TextureChannelIn.R, TextureChannelOut.R, _current._input_r.RemappingMode, _current._input_r.Remapping);
                connections[1] = new Connection(0, TextureChannelIn.G, TextureChannelOut.G, _current._input_r.RemappingMode, _current._input_r.Remapping);
                connections[2] = new Connection(0, TextureChannelIn.B, TextureChannelOut.B, _current._input_r.RemappingMode, _current._input_r.Remapping);
                connections[3] = new Connection(1, _current._input_g.Channel, TextureChannelOut.A, _current._input_g.RemappingMode, _current._input_g.Remapping);
            }
            else
            {
                connections[0] = new Connection(0, _current._input_r.Channel, TextureChannelOut.R, _current._input_r.RemappingMode, _current._input_r.Remapping);
                connections[1] = new Connection(1, _current._input_g.Channel, TextureChannelOut.G, _current._input_g.RemappingMode, _current._input_g.Remapping);
                connections[2] = new Connection(2, _current._input_b.Channel, TextureChannelOut.B, _current._input_b.RemappingMode, _current._input_b.Remapping);
                connections[3] = new Connection(3, _current._input_a.Channel, TextureChannelOut.A, _current._input_a.RemappingMode, _current._input_a.Remapping);
            }
            return connections;
        }

        FilterMode GetFiltermode()
        {
            if (_current._input_r.GetTexture() != null) return _current._input_r.GetTexture().filterMode;
            if (_current._input_g.GetTexture() != null) return _current._input_g.GetTexture().filterMode;
            if (_current._input_b.GetTexture() != null) return _current._input_b.GetTexture().filterMode;
            if (_current._input_a.GetTexture() != null) return _current._input_a.GetTexture().filterMode;
            return FilterMode.Bilinear;
        }

        readonly static Vector4 s_RemappingDefault = new Vector4(0, 1, 0, 1);

        class InlinePackerChannelConfig
        {
            public PackerSource Source = new PackerSource();
            public bool Invert;
            public float Fallback;
            public TextureChannelIn Channel = TextureChannelIn.Max;
            public Vector4 Remapping = s_RemappingDefault;
            public RemapMode RemappingMode => Remapping == s_RemappingDefault ? RemapMode.None : RemapMode.RangeToRange;

            public OutputTarget ToOutputConfig()
            {
                return new OutputTarget(BlendMode.Add, Invert ? InvertMode.Invert : InvertMode.None, Fallback);
            }

            public Texture2D GetTexture()
            {
                if (Source == null) return null;
                return Source.Texture;
            }
        }
    }

}
