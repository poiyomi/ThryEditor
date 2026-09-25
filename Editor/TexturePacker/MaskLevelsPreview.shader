Shader "Hidden/Thry/MaskLevelsPreview"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _InputMin, _InputMax, _Gamma, _OutputMin, _OutputMax, _Invert;
            float _Adjusted, _PostInvert;
            int _Channel;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 value = tex2D(_MainTex, i.uv);
                if (_Adjusted > 0.5)
                {
                    float4 range = _InputMax - _InputMin;
                    float4 normalized = saturate((value - _InputMin) / max(range, 0.00001));
                    float4 t = lerp(step(_InputMax, value), normalized, float4(range.x > 0.00001, range.y > 0.00001, range.z > 0.00001, range.w > 0.00001));
                    t = pow(t, _Gamma);
                    if (_PostInvert < 0.5) t = lerp(t, 1 - t, _Invert);
                    value = lerp(_OutputMin, _OutputMax, t);
                    if (_PostInvert > 0.5) value = lerp(value, 1-value, _Invert);
                }
                if (_Channel >= 0) return float4(value[_Channel].xxx, 1);
                return float4(value.rgb, 1);
            }
            ENDCG
        }
    }
}
