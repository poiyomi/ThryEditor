Shader "Hidden/Thry/SceneTextureInspection"
{
    Properties
    {
        _ThryInspectTex ("Texture", 2D) = "white" {}
        _ThryInspectCull ("Cull", Float) = 2
    }
    SubShader
    {
        Pass
        {
            Cull [_ThryInspectCull]
            ZWrite Off ZTest LEqual
            Offset -1, -1
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _ThryInspectTex;
            float4 _ThryInspectST, _ThryInspectPan;
            float _ThryInspectUV, _ThryInspectNormal, _ThryInspectChannel;
            float4 _ThryInspectUVTiling, _ThryInspectUVOffset, _ThryInspectUVPan;
            float _ThryInspectUVAngle, _ThryInspectUVRotate, _ThryInspectShiftBackface, _ThryInspectTimeSource;
            uint _VRChatTimeNetworkMs;
            float4 InspectionTime()
            {
                return (_ThryInspectTimeSource == 1 && _VRChatTimeNetworkMs != 0)
                    ? ((_VRChatTimeNetworkMs << 6) >> 6) * float4(.00005, .001, .002, .003) : _Time;
            }
            struct Input
            {
                float4 vertex : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float2 uv3 : TEXCOORD3;
            };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Input v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = _ThryInspectUV < .5 ? v.uv0 : _ThryInspectUV < 1.5 ? v.uv1 : _ThryInspectUV < 2.5 ? v.uv2 : v.uv3;
                return o;
            }
            float4 frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float4 time = InspectionTime();
                float2 uv = i.uv * _ThryInspectUVTiling.xy + _ThryInspectUVOffset.xy;
                float angle = radians(_ThryInspectUVAngle + _ThryInspectUVRotate * time.y);
                float sine, cosine; sincos(angle, sine, cosine);
                uv -= .5;
                uv = float2(uv.x * cosine - uv.y * sine, uv.x * sine + uv.y * cosine) + .5;
                uv += _ThryInspectUVPan.xy * time.y;
                if (_ThryInspectShiftBackface > .5 && !frontFace) uv.x += 1;
                uv = uv * _ThryInspectST.xy + _ThryInspectST.zw + _ThryInspectPan.xy * time.x;
                float4 sample = tex2D(_ThryInspectTex, uv);
                // Match the existing texture-card channel previews.
                if (_ThryInspectChannel > 3.5) return float4(sample.aaa, 1);
                if (_ThryInspectChannel > 2.5) return float4(0, 0, sample.b, 1);
                if (_ThryInspectChannel > 1.5) return float4(0, sample.g, 0, 1);
                if (_ThryInspectChannel > .5) return float4(sample.r, 0, 0, 1);
                return float4(_ThryInspectNormal > .5 ? UnpackNormal(sample) * .5 + .5 : sample.rgb, 1);
            }
            ENDCG
        }
    }
}
