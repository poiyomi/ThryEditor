Shader "Hidden/Thry/TextureInspection"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cube ("Cubemap", Cube) = "" {}
        _Array ("Array", 2DArray) = "" {}
        _Volume ("Volume", 3D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            samplerCUBE _Cube;
            sampler3D _Volume;
            UNITY_DECLARE_TEX2DARRAY(_Array);
            float _TextureKind, _Channel, _Slice, _Depth;
            float3 FaceDirection(float2 uv)
            {
                float2 p = uv * 2 - 1;
                if (_Slice < .5) return float3(1, p.y, -p.x);
                if (_Slice < 1.5) return float3(-1, p.y, p.x);
                if (_Slice < 2.5) return float3(p.x, 1, -p.y);
                if (_Slice < 3.5) return float3(p.x, -1, p.y);
                if (_Slice < 4.5) return float3(p.x, p.y, 1);
                return float3(-p.x, p.y, -1);
            }
            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c;
                if (_TextureKind < .5) c = tex2D(_MainTex, i.uv);
                else if (_TextureKind < 1.5) c = texCUBE(_Cube, FaceDirection(i.uv));
                else if (_TextureKind < 2.5) c = UNITY_SAMPLE_TEX2DARRAY(_Array, float3(i.uv, _Slice));
                else c = tex3D(_Volume, float3(i.uv, (_Slice + .5) / max(1, _Depth)));
                if (_Channel < .5) return c;
                if (_Channel < 1.5) return fixed4(c.r, 0, 0, 1);
                if (_Channel < 2.5) return fixed4(0, c.g, 0, 1);
                if (_Channel < 3.5) return fixed4(0, 0, c.b, 1);
                return fixed4(c.a, c.a, c.a, 1);
            }
            ENDCG
        }
    }
}
