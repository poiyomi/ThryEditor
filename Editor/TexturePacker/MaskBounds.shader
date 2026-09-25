Shader "Hidden/Thry/MaskBounds"
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
            float4 _SourceSize;
            float _Maximum;
            float4 samplePixel(float2 pixel)
            {
                float2 uv = (min(pixel, _SourceSize.xy - 1) + 0.5) * _SourceSize.zw;
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            float4 frag(v2f_img i) : SV_Target
            {
                float2 pixel = floor(i.uv * ceil(_SourceSize.xy * 0.5)) * 2;
                float4 a = samplePixel(pixel);
                float4 b = samplePixel(pixel + float2(1,0));
                float4 c = samplePixel(pixel + float2(0,1));
                float4 d = samplePixel(pixel + 1);
                return _Maximum > 0.5 ? max(max(a,b),max(c,d)) : min(min(a,b),min(c,d));
            }
            ENDCG
        }
    }
}
