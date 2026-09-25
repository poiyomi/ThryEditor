Shader "Hidden/Thry/MaskBake"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _InputMin, _InputMax, _Midpoint, _OutputMin, _OutputMax, _Invert, _Normalize, _BoundsMin, _BoundsMax;
            int _Channels;
            float _DecodeSRGB;
            float Adjust(float value, int c)
            {
                float low = _InputMin[c], high = _InputMax[c];
                if (_Normalize[c] > .5 && _BoundsMax[c] > _BoundsMin[c])
                {
                    low = lerp(_BoundsMin[c], _BoundsMax[c], low);
                    high = lerp(_BoundsMin[c], _BoundsMax[c], high);
                }
                if (low == 0 && high == 1 && _Midpoint[c] == .5 && _OutputMin[c] == 0 && _OutputMax[c] == 1 && _Invert[c] < .5)
                    return value;
                float range = high - low;
                float t = range > .00001 ? saturate((value - low) / range) : step(high, value);
                if (_Midpoint[c] != .5) t = pow(t, log(.5) / log(clamp(_Midpoint[c], .01, .99)));
                if (_Invert[c] > .5) t = 1 - t;
                return lerp(_OutputMin[c], _OutputMax[c], t);
            }
            float4 frag(v2f_img i) : SV_Target
            {
                float4 value = tex2D(_MainTex, i.uv);
                if (_DecodeSRGB > .5) value.rgb = GammaToLinearSpace(value.rgb);
                if ((_Channels & 1) != 0) value.r = Adjust(value.r, 0);
                if ((_Channels & 2) != 0) value.g = Adjust(value.g, 1);
                if ((_Channels & 4) != 0) value.b = Adjust(value.b, 2);
                if ((_Channels & 8) != 0) value.a = Adjust(value.a, 3);
                return value;
            }
            ENDCG
        }
    }
}
