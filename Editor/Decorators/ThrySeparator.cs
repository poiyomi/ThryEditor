using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Decorators
{
    public class ThrySeparatorDecorator : MaterialPropertyDrawer
    {
        readonly Color _color;
        readonly float _thickness;
        readonly float _paddingTop;
        readonly float _paddingBottom;

        public ThrySeparatorDecorator() : this(1, 0, 0)
        {
        }
        
        public ThrySeparatorDecorator(float thickness) : this(thickness, 5, 5)
        {
        }
        
        public ThrySeparatorDecorator(float thickness, float padding) : this(thickness, padding, padding)
        {
        }
        
        public ThrySeparatorDecorator(float thickness, float paddingTop, float paddingBottom)
        {
            this._color = Colors.backgroundDark;
            this._thickness = thickness;
            this._paddingTop = paddingTop;
            this._paddingBottom = paddingBottom;
        }
        
        public ThrySeparatorDecorator(string c) : this(c, 1, 0, 0)
        {
        }
        
        public ThrySeparatorDecorator(string c, float thickness) : this(c, thickness, 5, 5)
        {
        }
        
        public ThrySeparatorDecorator(string c, float thickness, float padding) : this(c, thickness, padding, padding)
        {
        }
        
        public ThrySeparatorDecorator(string c, float thickness, float paddingTop, float paddingBottom)
        {
            if (!c.StartsWith("#"))
                c = "#" + c;
            ColorUtility.TryParseHtmlString(c, out _color);
            this._thickness = thickness;
            this._paddingTop = paddingTop;
            this._paddingBottom = paddingBottom;
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            ShaderProperty.RegisterDecorator(this);
            return _thickness + _paddingTop + _paddingBottom;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            Rect inner = EditorGUI.IndentedRect(position);
            inner.y += _paddingTop;
            inner.height = _thickness;

            // Adjust side padding symmetrically relative to indented rect
            const float expandLeft = 3f;
            const float expandRight = 3f;
            inner.x -= expandLeft;
            inner.width += (expandLeft + expandRight);

            EditorGUI.DrawRect(inner, _color);
        }
    }

    // Legacy misspelling of [ThrySeparator]. Kept so shaders that still use the misspelled attribute keep working.
    // Please use the correct spelling for fresh code. -BluWizard10
    public class ThrySeperatorDecorator : ThrySeparatorDecorator
    {
        public ThrySeperatorDecorator() : base()
        {
        }

        public ThrySeperatorDecorator(float thickness) : base(thickness)
        {
        }

        public ThrySeperatorDecorator(float thickness, float padding) : base(thickness, padding)
        {
        }

        public ThrySeperatorDecorator(float thickness, float paddingTop, float paddingBottom) : base(thickness, paddingTop, paddingBottom)
        {
        }

        public ThrySeperatorDecorator(string c) : base(c)
        {
        }

        public ThrySeperatorDecorator(string c, float thickness) : base(c, thickness)
        {
        }

        public ThrySeperatorDecorator(string c, float thickness, float padding) : base(c, thickness, padding)
        {
        }

        public ThrySeperatorDecorator(string c, float thickness, float paddingTop, float paddingBottom) : base(c, thickness, paddingTop, paddingBottom)
        {
        }
    }
}