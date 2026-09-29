using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Thry.ThryEditor.Helpers
{
    /// <summary>
    /// How a color property stores its value and which controls its picker shows.
    /// </summary>
    public struct ThryColorFormat
    {
        /// <summary>Show the Intensity control and allow values above 1.</summary>
        public bool Hdr;
        /// <summary>Show the alpha control.</summary>
        public bool Alpha;
        /// <summary>
        /// The stored value is linear, so the picker shows and edits its gamma encoding.
        /// Only [HDR] colors without [Gamma] in a Linear project store linear data.
        /// [ThryHDR], [HDR][Gamma] and plain colors store the displayed value directly.
        /// </summary>
        public bool LinearData;

        public static ThryColorFormat ForProperty(ShaderPropertyFlags flags, bool thryHdr, bool alpha = true)
        {
            bool hdrFlag = (flags & ShaderPropertyFlags.HDR) != 0;
            return new ThryColorFormat
            {
                Hdr = hdrFlag || thryHdr,
                Alpha = alpha,
                LinearData = hdrFlag && (flags & ShaderPropertyFlags.Gamma) == 0 && QualitySettings.activeColorSpace == ColorSpace.Linear
            };
        }

        public static ThryColorFormat Ldr(bool alpha = true) => new ThryColorFormat { Alpha = alpha };
    }

    /// <summary>
    /// Color conversions shared by the Thry color picker and color fields.
    /// "Raw" is the stored material value. "Display" is the value as it appears on screen,
    /// which is the same as raw except for linear data. HDR raw values are split into a base
    /// color whose brightest channel is at most 1 and an intensity in stops (raw = base * 2^intensity).
    /// </summary>
    public static class ThryColorMath
    {
        public const float DefaultIntensityRange = 10f;
        /// <summary>Intensity is clamped to this many stops either way, so scaled values stay finite.</summary>
        public const float MaxIntensity = 64f;
        // Values a hair above 1 (float round trips, Unity's picker) are not treated as HDR.
        private const float MinIntensity = 0.005f;

        // ---------- HDR split ----------

        /// <summary>
        /// Stops above 1 of the brightest channel, or 0 when it does not exceed 1. Never negative:
        /// a color darkened with a negative intensity reopens as a darker base at intensity 0.
        /// </summary>
        public static float Intensity(Color raw, ThryColorFormat format)
        {
            if (!format.Hdr) return 0;
            float max = MaxChannel(raw);
            if (!(max > 1f) || !IsFinite(max)) return 0f;
            float stops = Mathf.Log(max, 2f);
            return stops < MinIntensity ? 0f : Mathf.Min(stops, MaxIntensity);
        }

        /// <summary>Raw-space base color (brightest channel at most 1). Alpha is kept.</summary>
        public static Color BaseRaw(Color raw, ThryColorFormat format, out float intensity)
        {
            intensity = Intensity(raw, format);
            if (intensity == 0) return raw;
            float scale = Mathf.Pow(2f, intensity);
            return new Color(raw.r / scale, raw.g / scale, raw.b / scale, raw.a);
        }

        /// <summary>The displayed base color of a stored value. Not clamped. Alpha is kept.</summary>
        public static Color ToDisplay(Color raw, ThryColorFormat format, out float intensity)
        {
            var baseRaw = BaseRaw(raw, format, out intensity);
            return format.LinearData ? EncodeGamma(baseRaw) : baseRaw;
        }

        /// <summary>Builds the stored value from a displayed base color, intensity in stops and alpha.</summary>
        public static Color FromDisplay(Color display, float intensity, float alpha, ThryColorFormat format)
        {
            var baseRaw = format.LinearData ? DecodeGamma(display) : display;
            return ScaleRgb(baseRaw, format.Hdr ? intensity : 0, alpha);
        }

        public static Color ScaleRgb(Color baseRaw, float stops, float alpha)
        {
            if (stops == 0 || !IsFinite(stops)) return new Color(baseRaw.r, baseRaw.g, baseRaw.b, alpha);
            float scale = Mathf.Pow(2f, Mathf.Clamp(stops, -MaxIntensity, MaxIntensity));
            return new Color(baseRaw.r * scale, baseRaw.g * scale, baseRaw.b * scale, alpha);
        }

        /// <summary>The color a swatch should paint for a stored value: the displayed base color, clamped.</summary>
        public static Color Preview(Color raw, ThryColorFormat format)
        {
            var display = ToDisplay(Sanitize(raw), format, out _);
            return Clamp01(display);
        }

        // ---------- Gamma ----------

        /// <summary>Linear to sRGB per channel. Negative and non-finite values become 0; alpha is kept.</summary>
        public static Color EncodeGamma(Color linear)
            => new Color(Mathf.LinearToGammaSpace(Positive(linear.r)), Mathf.LinearToGammaSpace(Positive(linear.g)),
                Mathf.LinearToGammaSpace(Positive(linear.b)), linear.a);

        /// <summary>sRGB to linear per channel. Negative and non-finite values become 0; alpha is kept.</summary>
        public static Color DecodeGamma(Color gamma)
            => new Color(Mathf.GammaToLinearSpace(Positive(gamma.r)), Mathf.GammaToLinearSpace(Positive(gamma.g)),
                Mathf.GammaToLinearSpace(Positive(gamma.b)), gamma.a);

        // ---------- Hex ----------

        public static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(IsFinite(value) ? value : 0) * 255f);

        /// <summary>"RRGGBB", or "RRGGBBAA" with alpha. Uppercase, no '#'.</summary>
        public static string ToHex(Color display, bool alpha = false)
        {
            string hex = ToByte(display.r).ToString("X2") + ToByte(display.g).ToString("X2") + ToByte(display.b).ToString("X2");
            return alpha ? hex + ToByte(display.a).ToString("X2") : hex;
        }

        /// <summary>
        /// Parses RGB, RGBA, RRGGBB or RRGGBBAA, with or without '#' or "0x", ignoring surrounding
        /// whitespace. Alpha is 1 when the text has none; <paramref name="hasAlpha"/> says which.
        /// </summary>
        public static bool TryParseHex(string text, out Color display, out bool hasAlpha)
        {
            display = Color.black; hasAlpha = false;
            if (text == null) return false;
            text = text.Trim();
            if (text.StartsWith("#")) text = text.Substring(1);
            else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            foreach (char c in text) if (!Uri.IsHexDigit(c)) return false;
            if (text.Length == 3 || text.Length == 4)
            {
                var expanded = new char[text.Length * 2];
                for (int i = 0; i < text.Length; i++) expanded[2 * i] = expanded[2 * i + 1] = text[i];
                text = new string(expanded);
            }
            if (text.Length != 6 && text.Length != 8) return false;
            if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) return false;
            hasAlpha = text.Length == 8;
            if (!hasAlpha) value = (value << 8) | 0xFF;
            display = new Color(((value >> 24) & 0xFF) / 255f, ((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
            return true;
        }

        /// <summary>
        /// Whether pasted text should be read as a hex color: "#" or "0x" followed by a code, or a bare 6- or 8-digit
        /// code. Bare 3- and 4-digit codes are left out so a copied number such as "255" or "1000" is not a color.
        /// </summary>
        public static bool IsPastedHex(string text)
        {
            if (!TryParseHex(text, out _, out _)) return false;
            text = text.Trim();
            return text.StartsWith("#") || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.Length == 6 || text.Length == 8;
        }

        // ---------- HSV / HSL (display space, 0-1) ----------

        /// <summary>Hue, saturation and value in 0-1. Hue is in [0, 1).</summary>
        public static Vector3 RgbToHsv(Color display)
        {
            var c = Clamp01(display);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return new Vector3(h >= 1f ? 0f : h, s, v);
        }

        public static Color HsvToRgb(Vector3 hsv, float alpha = 1f)
        {
            var c = Color.HSVToRGB(Repeat01(hsv.x), Mathf.Clamp01(hsv.y), Mathf.Clamp01(hsv.z));
            c.a = alpha; return c;
        }

        public static Vector3 RgbToHsl(Color display)
        {
            var c = Clamp01(display);
            float max = Mathf.Max(c.r, c.g, c.b), min = Mathf.Min(c.r, c.g, c.b);
            float l = (max + min) / 2f, d = max - min;
            if (d <= 0f) return new Vector3(0, 0, l);
            float s = d / (1f - Mathf.Abs(2f * l - 1f));
            return new Vector3(RgbToHsv(c).x, Mathf.Clamp01(s), l);
        }

        public static Color HslToRgb(Vector3 hsl, float alpha = 1f)
        {
            float s = Mathf.Clamp01(hsl.y), l = Mathf.Clamp01(hsl.z);
            float v = l + s * Mathf.Min(l, 1f - l);
            float sv = v <= 0f ? 0f : 2f * (1f - l / v);
            return HsvToRgb(new Vector3(hsl.x, sv, v), alpha);
        }

        public static Vector3 HsvToHsl(Vector3 hsv)
        {
            float v = Mathf.Clamp01(hsv.z), s = Mathf.Clamp01(hsv.y);
            float l = v * (1f - s / 2f);
            float sl = l <= 0f || l >= 1f ? 0f : (v - l) / Mathf.Min(l, 1f - l);
            return new Vector3(hsv.x, Mathf.Clamp01(sl), l);
        }

        public static Vector3 HslToHsv(Vector3 hsl)
        {
            float s = Mathf.Clamp01(hsl.y), l = Mathf.Clamp01(hsl.z);
            float v = l + s * Mathf.Min(l, 1f - l);
            return new Vector3(hsl.x, v <= 0f ? 0f : 2f * (1f - l / v), v);
        }

        /// <summary>
        /// Keeps the previous hue when a color has none (gray, white or black) and the previous
        /// saturation when the value is 0, so dragging through black or white does not lose them.
        /// </summary>
        public static Vector3 KeepHue(Vector3 hsv, Vector3 previous)
        {
            if (hsv.z <= 0f) return new Vector3(previous.x, previous.y, 0f);
            if (hsv.y <= 0f) return new Vector3(previous.x, 0f, hsv.z);
            return hsv;
        }

        // ---------- Wheel geometry (screen space, y down) ----------
        // Red is at the right of the ring and hue increases counterclockwise on screen.
        // The triangle's pure-hue corner points at the hue; white is 120 degrees further
        // counterclockwise and black 240 degrees.

        public static Vector2 HueDirection(float hue)
        {
            float angle = Repeat01(hue) * 2f * Mathf.PI;
            return new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
        }

        public static float HueFromPoint(Vector2 point, Vector2 center)
        {
            var d = point - center;
            if (d.sqrMagnitude <= 0f) return 0f;
            float angle = Mathf.Atan2(-d.y, d.x) / (2f * Mathf.PI);
            return Repeat01(angle);
        }

        public static void TriangleCorners(float hue, Vector2 center, float radius, out Vector2 pure, out Vector2 white, out Vector2 black)
        {
            pure = center + HueDirection(hue) * radius;
            white = center + HueDirection(hue + 1f / 3f) * radius;
            black = center + HueDirection(hue + 2f / 3f) * radius;
        }

        /// <summary>Point inside the triangle for a saturation and value.</summary>
        public static Vector2 TrianglePoint(float saturation, float value, Vector2 pure, Vector2 white, Vector2 black)
        {
            float s = Mathf.Clamp01(saturation), v = Mathf.Clamp01(value);
            return pure * (s * v) + white * ((1f - s) * v) + black * (1f - v);
        }

        /// <summary>
        /// Saturation and value for a point, clamped to the triangle. When the value is 0 the
        /// saturation is undefined and <paramref name="fallbackSaturation"/> is returned.
        /// </summary>
        public static Vector2 TriangleSaturationValue(Vector2 point, Vector2 pure, Vector2 white, Vector2 black, float fallbackSaturation)
        {
            var p = ClosestPointInTriangle(point, pure, white, black);
            Barycentric(p, pure, white, black, out float a, out float b, out _);
            a = Mathf.Max(0, a); b = Mathf.Max(0, b);
            float v = Mathf.Clamp01(a + b);
            float s = v > 1e-5f ? Mathf.Clamp01(a / (a + b)) : Mathf.Clamp01(fallbackSaturation);
            return new Vector2(s, v);
        }

        public static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            Barycentric(point, a, b, c, out float u, out float v, out float w);
            return u >= 0 && v >= 0 && w >= 0;
        }

        public static void Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d11 = Vector2.Dot(v1, v1);
            float d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-12f) { u = 1; v = 0; w = 0; return; }
            v = (d11 * d20 - d01 * d21) / denominator;
            w = (d00 * d21 - d01 * d20) / denominator;
            u = 1f - v - w;
        }

        public static Vector2 ClosestPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            if (InsideTriangle(p, a, b, c)) return p;
            var ab = ClosestPointOnSegment(p, a, b);
            var bc = ClosestPointOnSegment(p, b, c);
            var ca = ClosestPointOnSegment(p, c, a);
            float dab = (p - ab).sqrMagnitude, dbc = (p - bc).sqrMagnitude, dca = (p - ca).sqrMagnitude;
            return dab <= dbc && dab <= dca ? ab : dbc <= dca ? bc : ca;
        }

        public static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float length = ab.sqrMagnitude;
            if (length <= 0f) return a;
            return a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
        }

        /// <summary>Approximate perceived brightness of a displayed color, for choosing marker contrast.</summary>
        public static float Luminance(Color display)
        {
            var c = Clamp01(display);
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        // ---------- Utilities ----------

        public static float MaxChannel(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static Color Clamp01(Color c) => new Color(Mathf.Clamp01(Positive(c.r)), Mathf.Clamp01(Positive(c.g)), Mathf.Clamp01(Positive(c.b)), Mathf.Clamp01(IsFinite(c.a) ? c.a : 1f));
        public static Color Sanitize(Color c) => new Color(Positive(c.r), Positive(c.g), Positive(c.b), IsFinite(c.a) ? c.a : 1f);
        public static float Repeat01(float value) { float r = value - Mathf.Floor(value); return r >= 1f ? 0f : r; }
        /// <summary>Exact per-channel equality (Color's == operator is approximate).</summary>
        public static bool Same(Color a, Color b) => a.r.Equals(b.r) && a.g.Equals(b.g) && a.b.Equals(b.b) && a.a.Equals(b.a);
        private static float Positive(float value) => IsFinite(value) && value > 0f ? value : 0f;
    }


    /// <summary>
    /// The editable state of one picker session. The stored (raw) value is the source of truth:
    /// every edit changes only the channels it is about, so an alpha edit never re-derives RGB, an
    /// edit that leaves the displayed RGB as it was leaves the stored RGB as it was, and opening and
    /// closing the picker without edits leaves the value bit-for-bit unchanged.
    /// </summary>
    public sealed class ThryColorState
    {
        public ThryColorFormat Format { get; }
        public Color Original { get; private set; }
        public Color Raw { get; private set; }
        /// <summary>Displayed base color, always 0-1 per channel. Alpha is the stored alpha.</summary>
        public Color Display { get; private set; }
        /// <summary>Hue, saturation and value of <see cref="Display"/>, keeping hue and saturation through grays and black.</summary>
        public Vector3 Hsv { get; private set; }
        /// <summary>Hue, saturation and lightness of <see cref="Display"/>, keeping hue and saturation through grays, black and white.</summary>
        public Vector3 Hsl { get; private set; }
        /// <summary>Intensity in stops. Always 0 for non-HDR formats.</summary>
        public float Intensity { get; private set; }
        public float Alpha => Raw.a;
        /// <summary>Raw-space base color: Raw = BaseRaw * 2^Intensity.</summary>
        public Color BaseRaw => _baseRaw;
        public bool Changed => !ThryColorMath.Same(Raw, Original);
        /// <summary>Incremented on every change, including loads and reverts, so views can tell when to refresh.</summary>
        public int Version { get; private set; }
        /// <summary>Incremented only by user edits (the Set methods), never by Load, Adopt or Revert.</summary>
        public int Edits { get; private set; }

        private Color _baseRaw;

        public ThryColorState(Color raw, ThryColorFormat format)
        {
            Format = format;
            Load(raw);
        }

        /// <summary>Starts over from a stored value, which also becomes the original.</summary>
        public void Load(Color raw)
        {
            Original = raw;
            Adopt(raw);
        }

        /// <summary>Returns to the original value.</summary>
        public void Revert() => Adopt(Original);

        /// <summary>Takes a stored value as the current value, e.g. after an undo elsewhere. Not an edit.</summary>
        public void Adopt(Color raw)
        {
            _baseRaw = ThryColorMath.BaseRaw(raw, Format, out float intensity);
            Intensity = intensity;
            var display = ThryColorMath.Clamp01(Format.LinearData ? ThryColorMath.EncodeGamma(_baseRaw) : _baseRaw);
            display.a = raw.a;
            Display = display;
            Raw = raw;
            SyncHue(ThryColorMath.RgbToHsv(display));
            Version++;
        }

        public void SetHsv(Vector3 hsv)
        {
            hsv = new Vector3(Finite(hsv.x, Hsv.x), Finite(hsv.y, Hsv.y), Finite(hsv.z, Hsv.z));
            hsv = new Vector3(ThryColorMath.Repeat01(hsv.x), Mathf.Clamp01(hsv.y), Mathf.Clamp01(hsv.z));
            ApplyDisplay(ThryColorMath.HsvToRgb(hsv, Raw.a));
            Hsv = hsv;
            Hsl = KeepHsl(ThryColorMath.HsvToHsl(hsv), Hsl);
            Edited();
        }

        public void SetHsl(Vector3 hsl)
        {
            hsl = new Vector3(Finite(hsl.x, Hsl.x), Finite(hsl.y, Hsl.y), Finite(hsl.z, Hsl.z));
            hsl = new Vector3(ThryColorMath.Repeat01(hsl.x), Mathf.Clamp01(hsl.y), Mathf.Clamp01(hsl.z));
            var hsv = ThryColorMath.HslToHsv(hsl);
            // HSV saturation is undefined only at black; white is saturation 0, which HslToHsv already gives.
            if (hsv.z <= 0f) hsv.y = Hsv.y;
            ApplyDisplay(ThryColorMath.HslToRgb(hsl, Raw.a));
            Hsv = hsv;
            Hsl = hsl;
            Edited();
        }

        /// <summary>Sets the displayed RGB (clamped to 0-1). Alpha and intensity are unchanged.</summary>
        public void SetDisplay(Color display)
        {
            display = ThryColorMath.Clamp01(display);
            display.a = Raw.a;
            ApplyDisplay(display);
            SyncHue(ThryColorMath.RgbToHsv(display));
            Edited();
        }

        /// <summary>
        /// Sets one displayed channel (0 = R, 1 = G, 2 = B). The other channels of the stored
        /// value are copied unchanged, even when they are outside 0-1.
        /// </summary>
        public void SetDisplayChannel(int channel, float value)
        {
            if (channel < 0 || channel > 2 || !ThryColorMath.IsFinite(value)) return;
            value = Mathf.Clamp01(value);
            if (Display[channel].Equals(value)) { Edited(); return; }
            var display = Display; display[channel] = value; Display = display;
            _baseRaw[channel] = Format.LinearData ? Mathf.GammaToLinearSpace(value) : value;
            var raw = Raw;
            raw[channel] = Format.Hdr && Intensity != 0 ? _baseRaw[channel] * Mathf.Pow(2f, Intensity) : _baseRaw[channel];
            Raw = raw;
            SyncHue(ThryColorMath.RgbToHsv(display));
            Edited();
        }

        /// <summary>
        /// Applies hex text. RGB changes; alpha changes only when the text includes it and the
        /// format shows alpha; intensity is kept. Text that matches the current bytes changes
        /// nothing. Returns false for text that is not a color.
        /// </summary>
        public bool SetHex(string text)
        {
            if (!ThryColorMath.TryParseHex(text, out var display, out bool hasAlpha)) return false;
            bool sameRgb = ThryColorMath.ToHex(display) == Hex;
            bool sameAlpha = !hasAlpha || !Format.Alpha || ThryColorMath.ToByte(display.a) == ThryColorMath.ToByte(Raw.a);
            if (sameRgb && sameAlpha) return true;
            if (hasAlpha && Format.Alpha && !sameAlpha) SetAlpha(display.a);
            if (!sameRgb) SetDisplay(display);
            return true;
        }

        public string Hex => ThryColorMath.ToHex(Display);

        public void SetIntensity(float stops)
        {
            if (!Format.Hdr || !ThryColorMath.IsFinite(stops)) return;
            // Intensity only brightens; darkening is the value axis, and a negative split never survives a reopen.
            Intensity = Mathf.Clamp(stops, 0, ThryColorMath.MaxIntensity);
            Raw = ThryColorMath.ScaleRgb(_baseRaw, Intensity, Raw.a);
            Edited();
        }

        public void SetAlpha(float alpha)
        {
            if (!ThryColorMath.IsFinite(alpha)) return;
            var raw = Raw; raw.a = Mathf.Clamp01(alpha); Raw = raw;
            var display = Display; display.a = raw.a; Display = display;
            Edited();
        }

        /// <summary>
        /// Applies a color picked from the screen or a swatch, given as a displayed color.
        /// Intensity is kept unless a finite one is given (HDR formats only). Alpha is kept.
        /// </summary>
        public void SetPicked(Color display, float? intensity = null)
        {
            if (intensity.HasValue && Format.Hdr && ThryColorMath.IsFinite(intensity.Value))
                Intensity = Mathf.Clamp(intensity.Value, 0, ThryColorMath.MaxIntensity);
            display = ThryColorMath.Clamp01(display);
            display.a = Raw.a;
            ApplyDisplay(display, force: intensity.HasValue);
            SyncHue(ThryColorMath.RgbToHsv(display));
            Edited();
        }

        /// <summary>
        /// Applies a whole stored value, as clicking a swatch from Unity's library does. Alpha changes only
        /// when <paramref name="withAlpha"/> is set. Non-finite values are ignored.
        /// </summary>
        public void SetRaw(Color raw, bool withAlpha)
        {
            if (!ThryColorMath.IsFinite(raw.r) || !ThryColorMath.IsFinite(raw.g) || !ThryColorMath.IsFinite(raw.b)) return;
            raw.a = withAlpha && ThryColorMath.IsFinite(raw.a) ? Mathf.Clamp01(raw.a) : Raw.a;
            _baseRaw = ThryColorMath.BaseRaw(raw, Format, out float intensity);
            Intensity = intensity;
            var display = ThryColorMath.Clamp01(Format.LinearData ? ThryColorMath.EncodeGamma(_baseRaw) : _baseRaw);
            display.a = raw.a;
            Display = display;
            Raw = raw;
            SyncHue(ThryColorMath.RgbToHsv(display));
            Edited();
        }

        private void ApplyDisplay(Color display, bool force = false)
        {
            // Same displayed RGB (e.g. a hue change on a gray): keep the stored RGB bit-exact.
            if (!force && display.r.Equals(Display.r) && display.g.Equals(Display.g) && display.b.Equals(Display.b)) return;
            Display = display;
            _baseRaw = Format.LinearData ? ThryColorMath.DecodeGamma(display) : display;
            Raw = ThryColorMath.ScaleRgb(_baseRaw, Format.Hdr ? Intensity : 0f, Raw.a);
        }

        private void SyncHue(Vector3 hsv)
        {
            Hsv = ThryColorMath.KeepHue(hsv, Hsv);
            Hsl = KeepHsl(ThryColorMath.HsvToHsl(Hsv), Hsl);
        }

        private static Vector3 KeepHsl(Vector3 hsl, Vector3 previous)
        {
            // HSL saturation is undefined at black and white; keep the previous one there.
            if (hsl.z <= 0f || hsl.z >= 1f) hsl.y = previous.y;
            return hsl;
        }

        private void Edited() { Edits++; Version++; }
        private static float Finite(float value, float fallback) => ThryColorMath.IsFinite(value) ? value : fallback;
    }
}
