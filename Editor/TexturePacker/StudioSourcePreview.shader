Shader "Hidden/Thry/StudioSourcePreview"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
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
            fixed4 frag(v2f_img input) : SV_Target
            {
                return fixed4(tex2D(_MainTex, input.uv).rgb, 1);
            }
            ENDCG
        }
    }
}
