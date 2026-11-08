Shader "Hidden/Thry/StencilDebugOverlay"
{
    Properties
    {
        _NumberAtlas ("Number Atlas", 2D) = "white" {}
        _TileCount ("Tiling Amount", Float) = 32
        _BgOpacity ("Background Opacity", Range(0,1)) = 0.2
        _NumberRotation ("Number Rotation (Degrees)", Range(-180,180)) = 0
        _ShowNumbers ("Show Numbers", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1000" }
        LOD 100

        CGINCLUDE
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"

        sampler2D _NumberAtlas;
        float4 _NumberAtlas_TexelSize;
        float _TileCount;
        float _BgOpacity;
        float _NumberRotation;
        float _ShowNumbers;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float2 uv : TEXCOORD0;
            float4 vertex : SV_POSITION;
        };

        v2f vert (appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        fixed3 HSVtoRGB(float h, float s, float v)
        {
            float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
            float3 p = abs(frac(float3(h, h, h) + K.xyz) * 6.0 - K.www);
            return v * lerp(K.xxx, saturate(p - K.xxx), s);
        }

        // Premultiplied colour plus coverage, for Blend One OneMinusSrcAlpha, compositing straight
        // against the framebuffer - no copy needed.
        //   want  lerp(lerp(dst, S, b), N, a)   S = cell colour, N = glyph, b = _BgOpacity, a = coverage
        //   =     (1-a)(1-b)*dst + [(1-a)*b*S + a*N]   so alpha = 1-(1-a)(1-b)
        fixed4 SampleNumber(float2 meshUV, int number)
        {
            const float sat = 0.8;
            const float val = 1.0;

            // 24 hues, odd values flipped half a turn so neighbours contrast.
            float baseHue = (number - 1) / 24.0;
            float hue = (number % 2 == 0) ? baseHue : fmod(baseHue + 0.5, 1.0);
            fixed3 cellColor = HSVtoRGB(hue, sat, val);

            float coverage = 0.0;
            fixed3 glyphColor = 0.0;

            if (_ShowNumbers >= 0.5)
            {
                const int atlasSize = 16; // 16x16 grid covers 0-255

                float angle = radians(_NumberRotation);
                float2 center = float2(0.5, 0.5);
                float2 uvRel = meshUV - center;
                float cosA = cos(angle);
                float sinA = sin(angle);
                float2 rotUV = float2(uvRel.x * cosA - uvRel.y * sinA,
                                      uvRel.x * sinA + uvRel.y * cosA) + center;

                float2 tileUV = frac(rotUV * _TileCount);
                int numX = number % atlasSize;
                int numY = atlasSize - 1 - (number / atlasSize);
                // Half a texel inside the cell, or bilinear drags the neighbouring digit into every
                // tile edge. cellUV is in cell units, where one texel spans atlasSize of them.
                float2 inset = atlasSize * _NumberAtlas_TexelSize.xy;
                float2 cellUV = tileUV * (1.0 - inset) + 0.5 * inset;
                float2 atlasUV = (float2(numX, numY) + cellUV) / atlasSize;

                float atlasAlpha = tex2D(_NumberAtlas, atlasUV).a;
                if (atlasAlpha >= 0.1)
                {
                    coverage = atlasAlpha;

                    // Glyph darkens and saturates as the cell tint takes over.
                    float transition = saturate((_BgOpacity - 0.6) / 0.4);
                    glyphColor = HSVtoRGB(hue, lerp(sat, 1.0, transition), lerp(0.8, 0.5, transition));
                }
            }

            float outAlpha = 1.0 - (1.0 - coverage) * (1.0 - _BgOpacity);
            fixed3 outColor = (1.0 - coverage) * _BgOpacity * cellColor + coverage * glyphColor;
            return fixed4(outColor, outAlpha);
        }
        ENDCG

        // One pass per stencil value. 0 is absent on purpose: an unwritten buffer draws nothing.
        Pass { Stencil { Ref 1 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 1); } ENDCG }
        Pass { Stencil { Ref 2 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 2); } ENDCG }
        Pass { Stencil { Ref 3 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 3); } ENDCG }
        Pass { Stencil { Ref 4 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 4); } ENDCG }
        Pass { Stencil { Ref 5 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 5); } ENDCG }
        Pass { Stencil { Ref 6 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 6); } ENDCG }
        Pass { Stencil { Ref 7 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 7); } ENDCG }
        Pass { Stencil { Ref 8 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 8); } ENDCG }
        Pass { Stencil { Ref 9 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 9); } ENDCG }
        Pass { Stencil { Ref 10 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 10); } ENDCG }
        Pass { Stencil { Ref 11 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 11); } ENDCG }
        Pass { Stencil { Ref 12 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 12); } ENDCG }
        Pass { Stencil { Ref 13 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 13); } ENDCG }
        Pass { Stencil { Ref 14 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 14); } ENDCG }
        Pass { Stencil { Ref 15 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 15); } ENDCG }
        Pass { Stencil { Ref 16 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 16); } ENDCG }
        Pass { Stencil { Ref 17 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 17); } ENDCG }
        Pass { Stencil { Ref 18 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 18); } ENDCG }
        Pass { Stencil { Ref 19 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 19); } ENDCG }
        Pass { Stencil { Ref 20 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 20); } ENDCG }
        Pass { Stencil { Ref 21 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 21); } ENDCG }
        Pass { Stencil { Ref 22 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 22); } ENDCG }
        Pass { Stencil { Ref 23 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 23); } ENDCG }
        Pass { Stencil { Ref 24 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 24); } ENDCG }
        Pass { Stencil { Ref 25 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 25); } ENDCG }
        Pass { Stencil { Ref 26 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 26); } ENDCG }
        Pass { Stencil { Ref 27 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 27); } ENDCG }
        Pass { Stencil { Ref 28 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 28); } ENDCG }
        Pass { Stencil { Ref 29 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 29); } ENDCG }
        Pass { Stencil { Ref 30 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 30); } ENDCG }
        Pass { Stencil { Ref 31 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 31); } ENDCG }
        Pass { Stencil { Ref 32 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 32); } ENDCG }
        Pass { Stencil { Ref 33 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 33); } ENDCG }
        Pass { Stencil { Ref 34 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 34); } ENDCG }
        Pass { Stencil { Ref 35 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 35); } ENDCG }
        Pass { Stencil { Ref 36 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 36); } ENDCG }
        Pass { Stencil { Ref 37 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 37); } ENDCG }
        Pass { Stencil { Ref 38 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 38); } ENDCG }
        Pass { Stencil { Ref 39 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 39); } ENDCG }
        Pass { Stencil { Ref 40 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 40); } ENDCG }
        Pass { Stencil { Ref 41 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 41); } ENDCG }
        Pass { Stencil { Ref 42 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 42); } ENDCG }
        Pass { Stencil { Ref 43 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 43); } ENDCG }
        Pass { Stencil { Ref 44 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 44); } ENDCG }
        Pass { Stencil { Ref 45 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 45); } ENDCG }
        Pass { Stencil { Ref 46 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 46); } ENDCG }
        Pass { Stencil { Ref 47 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 47); } ENDCG }
        Pass { Stencil { Ref 48 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 48); } ENDCG }
        Pass { Stencil { Ref 49 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 49); } ENDCG }
        Pass { Stencil { Ref 50 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 50); } ENDCG }
        Pass { Stencil { Ref 51 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 51); } ENDCG }
        Pass { Stencil { Ref 52 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 52); } ENDCG }
        Pass { Stencil { Ref 53 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 53); } ENDCG }
        Pass { Stencil { Ref 54 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 54); } ENDCG }
        Pass { Stencil { Ref 55 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 55); } ENDCG }
        Pass { Stencil { Ref 56 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 56); } ENDCG }
        Pass { Stencil { Ref 57 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 57); } ENDCG }
        Pass { Stencil { Ref 58 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 58); } ENDCG }
        Pass { Stencil { Ref 59 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 59); } ENDCG }
        Pass { Stencil { Ref 60 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 60); } ENDCG }
        Pass { Stencil { Ref 61 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 61); } ENDCG }
        Pass { Stencil { Ref 62 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 62); } ENDCG }
        Pass { Stencil { Ref 63 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 63); } ENDCG }
        Pass { Stencil { Ref 64 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 64); } ENDCG }
        Pass { Stencil { Ref 65 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 65); } ENDCG }
        Pass { Stencil { Ref 66 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 66); } ENDCG }
        Pass { Stencil { Ref 67 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 67); } ENDCG }
        Pass { Stencil { Ref 68 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 68); } ENDCG }
        Pass { Stencil { Ref 69 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 69); } ENDCG }
        Pass { Stencil { Ref 70 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 70); } ENDCG }
        Pass { Stencil { Ref 71 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 71); } ENDCG }
        Pass { Stencil { Ref 72 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 72); } ENDCG }
        Pass { Stencil { Ref 73 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 73); } ENDCG }
        Pass { Stencil { Ref 74 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 74); } ENDCG }
        Pass { Stencil { Ref 75 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 75); } ENDCG }
        Pass { Stencil { Ref 76 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 76); } ENDCG }
        Pass { Stencil { Ref 77 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 77); } ENDCG }
        Pass { Stencil { Ref 78 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 78); } ENDCG }
        Pass { Stencil { Ref 79 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 79); } ENDCG }
        Pass { Stencil { Ref 80 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 80); } ENDCG }
        Pass { Stencil { Ref 81 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 81); } ENDCG }
        Pass { Stencil { Ref 82 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 82); } ENDCG }
        Pass { Stencil { Ref 83 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 83); } ENDCG }
        Pass { Stencil { Ref 84 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 84); } ENDCG }
        Pass { Stencil { Ref 85 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 85); } ENDCG }
        Pass { Stencil { Ref 86 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 86); } ENDCG }
        Pass { Stencil { Ref 87 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 87); } ENDCG }
        Pass { Stencil { Ref 88 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 88); } ENDCG }
        Pass { Stencil { Ref 89 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 89); } ENDCG }
        Pass { Stencil { Ref 90 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 90); } ENDCG }
        Pass { Stencil { Ref 91 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 91); } ENDCG }
        Pass { Stencil { Ref 92 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 92); } ENDCG }
        Pass { Stencil { Ref 93 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 93); } ENDCG }
        Pass { Stencil { Ref 94 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 94); } ENDCG }
        Pass { Stencil { Ref 95 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 95); } ENDCG }
        Pass { Stencil { Ref 96 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 96); } ENDCG }
        Pass { Stencil { Ref 97 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 97); } ENDCG }
        Pass { Stencil { Ref 98 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 98); } ENDCG }
        Pass { Stencil { Ref 99 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 99); } ENDCG }
        Pass { Stencil { Ref 100 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 100); } ENDCG }
        Pass { Stencil { Ref 101 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 101); } ENDCG }
        Pass { Stencil { Ref 102 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 102); } ENDCG }
        Pass { Stencil { Ref 103 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 103); } ENDCG }
        Pass { Stencil { Ref 104 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 104); } ENDCG }
        Pass { Stencil { Ref 105 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 105); } ENDCG }
        Pass { Stencil { Ref 106 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 106); } ENDCG }
        Pass { Stencil { Ref 107 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 107); } ENDCG }
        Pass { Stencil { Ref 108 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 108); } ENDCG }
        Pass { Stencil { Ref 109 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 109); } ENDCG }
        Pass { Stencil { Ref 110 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 110); } ENDCG }
        Pass { Stencil { Ref 111 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 111); } ENDCG }
        Pass { Stencil { Ref 112 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 112); } ENDCG }
        Pass { Stencil { Ref 113 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 113); } ENDCG }
        Pass { Stencil { Ref 114 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 114); } ENDCG }
        Pass { Stencil { Ref 115 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 115); } ENDCG }
        Pass { Stencil { Ref 116 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 116); } ENDCG }
        Pass { Stencil { Ref 117 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 117); } ENDCG }
        Pass { Stencil { Ref 118 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 118); } ENDCG }
        Pass { Stencil { Ref 119 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 119); } ENDCG }
        Pass { Stencil { Ref 120 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 120); } ENDCG }
        Pass { Stencil { Ref 121 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 121); } ENDCG }
        Pass { Stencil { Ref 122 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 122); } ENDCG }
        Pass { Stencil { Ref 123 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 123); } ENDCG }
        Pass { Stencil { Ref 124 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 124); } ENDCG }
        Pass { Stencil { Ref 125 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 125); } ENDCG }
        Pass { Stencil { Ref 126 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 126); } ENDCG }
        Pass { Stencil { Ref 127 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 127); } ENDCG }
        Pass { Stencil { Ref 128 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 128); } ENDCG }
        Pass { Stencil { Ref 129 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 129); } ENDCG }
        Pass { Stencil { Ref 130 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 130); } ENDCG }
        Pass { Stencil { Ref 131 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 131); } ENDCG }
        Pass { Stencil { Ref 132 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 132); } ENDCG }
        Pass { Stencil { Ref 133 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 133); } ENDCG }
        Pass { Stencil { Ref 134 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 134); } ENDCG }
        Pass { Stencil { Ref 135 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 135); } ENDCG }
        Pass { Stencil { Ref 136 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 136); } ENDCG }
        Pass { Stencil { Ref 137 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 137); } ENDCG }
        Pass { Stencil { Ref 138 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 138); } ENDCG }
        Pass { Stencil { Ref 139 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 139); } ENDCG }
        Pass { Stencil { Ref 140 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 140); } ENDCG }
        Pass { Stencil { Ref 141 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 141); } ENDCG }
        Pass { Stencil { Ref 142 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 142); } ENDCG }
        Pass { Stencil { Ref 143 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 143); } ENDCG }
        Pass { Stencil { Ref 144 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 144); } ENDCG }
        Pass { Stencil { Ref 145 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 145); } ENDCG }
        Pass { Stencil { Ref 146 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 146); } ENDCG }
        Pass { Stencil { Ref 147 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 147); } ENDCG }
        Pass { Stencil { Ref 148 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 148); } ENDCG }
        Pass { Stencil { Ref 149 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 149); } ENDCG }
        Pass { Stencil { Ref 150 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 150); } ENDCG }
        Pass { Stencil { Ref 151 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 151); } ENDCG }
        Pass { Stencil { Ref 152 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 152); } ENDCG }
        Pass { Stencil { Ref 153 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 153); } ENDCG }
        Pass { Stencil { Ref 154 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 154); } ENDCG }
        Pass { Stencil { Ref 155 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 155); } ENDCG }
        Pass { Stencil { Ref 156 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 156); } ENDCG }
        Pass { Stencil { Ref 157 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 157); } ENDCG }
        Pass { Stencil { Ref 158 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 158); } ENDCG }
        Pass { Stencil { Ref 159 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 159); } ENDCG }
        Pass { Stencil { Ref 160 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 160); } ENDCG }
        Pass { Stencil { Ref 161 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 161); } ENDCG }
        Pass { Stencil { Ref 162 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 162); } ENDCG }
        Pass { Stencil { Ref 163 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 163); } ENDCG }
        Pass { Stencil { Ref 164 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 164); } ENDCG }
        Pass { Stencil { Ref 165 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 165); } ENDCG }
        Pass { Stencil { Ref 166 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 166); } ENDCG }
        Pass { Stencil { Ref 167 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 167); } ENDCG }
        Pass { Stencil { Ref 168 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 168); } ENDCG }
        Pass { Stencil { Ref 169 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 169); } ENDCG }
        Pass { Stencil { Ref 170 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 170); } ENDCG }
        Pass { Stencil { Ref 171 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 171); } ENDCG }
        Pass { Stencil { Ref 172 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 172); } ENDCG }
        Pass { Stencil { Ref 173 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 173); } ENDCG }
        Pass { Stencil { Ref 174 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 174); } ENDCG }
        Pass { Stencil { Ref 175 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 175); } ENDCG }
        Pass { Stencil { Ref 176 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 176); } ENDCG }
        Pass { Stencil { Ref 177 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 177); } ENDCG }
        Pass { Stencil { Ref 178 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 178); } ENDCG }
        Pass { Stencil { Ref 179 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 179); } ENDCG }
        Pass { Stencil { Ref 180 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 180); } ENDCG }
        Pass { Stencil { Ref 181 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 181); } ENDCG }
        Pass { Stencil { Ref 182 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 182); } ENDCG }
        Pass { Stencil { Ref 183 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 183); } ENDCG }
        Pass { Stencil { Ref 184 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 184); } ENDCG }
        Pass { Stencil { Ref 185 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 185); } ENDCG }
        Pass { Stencil { Ref 186 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 186); } ENDCG }
        Pass { Stencil { Ref 187 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 187); } ENDCG }
        Pass { Stencil { Ref 188 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 188); } ENDCG }
        Pass { Stencil { Ref 189 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 189); } ENDCG }
        Pass { Stencil { Ref 190 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 190); } ENDCG }
        Pass { Stencil { Ref 191 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 191); } ENDCG }
        Pass { Stencil { Ref 192 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 192); } ENDCG }
        Pass { Stencil { Ref 193 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 193); } ENDCG }
        Pass { Stencil { Ref 194 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 194); } ENDCG }
        Pass { Stencil { Ref 195 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 195); } ENDCG }
        Pass { Stencil { Ref 196 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 196); } ENDCG }
        Pass { Stencil { Ref 197 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 197); } ENDCG }
        Pass { Stencil { Ref 198 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 198); } ENDCG }
        Pass { Stencil { Ref 199 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 199); } ENDCG }
        Pass { Stencil { Ref 200 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 200); } ENDCG }
        Pass { Stencil { Ref 201 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 201); } ENDCG }
        Pass { Stencil { Ref 202 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 202); } ENDCG }
        Pass { Stencil { Ref 203 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 203); } ENDCG }
        Pass { Stencil { Ref 204 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 204); } ENDCG }
        Pass { Stencil { Ref 205 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 205); } ENDCG }
        Pass { Stencil { Ref 206 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 206); } ENDCG }
        Pass { Stencil { Ref 207 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 207); } ENDCG }
        Pass { Stencil { Ref 208 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 208); } ENDCG }
        Pass { Stencil { Ref 209 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 209); } ENDCG }
        Pass { Stencil { Ref 210 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 210); } ENDCG }
        Pass { Stencil { Ref 211 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 211); } ENDCG }
        Pass { Stencil { Ref 212 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 212); } ENDCG }
        Pass { Stencil { Ref 213 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 213); } ENDCG }
        Pass { Stencil { Ref 214 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 214); } ENDCG }
        Pass { Stencil { Ref 215 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 215); } ENDCG }
        Pass { Stencil { Ref 216 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 216); } ENDCG }
        Pass { Stencil { Ref 217 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 217); } ENDCG }
        Pass { Stencil { Ref 218 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 218); } ENDCG }
        Pass { Stencil { Ref 219 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 219); } ENDCG }
        Pass { Stencil { Ref 220 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 220); } ENDCG }
        Pass { Stencil { Ref 221 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 221); } ENDCG }
        Pass { Stencil { Ref 222 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 222); } ENDCG }
        Pass { Stencil { Ref 223 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 223); } ENDCG }
        Pass { Stencil { Ref 224 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 224); } ENDCG }
        Pass { Stencil { Ref 225 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 225); } ENDCG }
        Pass { Stencil { Ref 226 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 226); } ENDCG }
        Pass { Stencil { Ref 227 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 227); } ENDCG }
        Pass { Stencil { Ref 228 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 228); } ENDCG }
        Pass { Stencil { Ref 229 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 229); } ENDCG }
        Pass { Stencil { Ref 230 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 230); } ENDCG }
        Pass { Stencil { Ref 231 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 231); } ENDCG }
        Pass { Stencil { Ref 232 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 232); } ENDCG }
        Pass { Stencil { Ref 233 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 233); } ENDCG }
        Pass { Stencil { Ref 234 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 234); } ENDCG }
        Pass { Stencil { Ref 235 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 235); } ENDCG }
        Pass { Stencil { Ref 236 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 236); } ENDCG }
        Pass { Stencil { Ref 237 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 237); } ENDCG }
        Pass { Stencil { Ref 238 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 238); } ENDCG }
        Pass { Stencil { Ref 239 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 239); } ENDCG }
        Pass { Stencil { Ref 240 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 240); } ENDCG }
        Pass { Stencil { Ref 241 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 241); } ENDCG }
        Pass { Stencil { Ref 242 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 242); } ENDCG }
        Pass { Stencil { Ref 243 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 243); } ENDCG }
        Pass { Stencil { Ref 244 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 244); } ENDCG }
        Pass { Stencil { Ref 245 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 245); } ENDCG }
        Pass { Stencil { Ref 246 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 246); } ENDCG }
        Pass { Stencil { Ref 247 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 247); } ENDCG }
        Pass { Stencil { Ref 248 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 248); } ENDCG }
        Pass { Stencil { Ref 249 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 249); } ENDCG }
        Pass { Stencil { Ref 250 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 250); } ENDCG }
        Pass { Stencil { Ref 251 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 251); } ENDCG }
        Pass { Stencil { Ref 252 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 252); } ENDCG }
        Pass { Stencil { Ref 253 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 253); } ENDCG }
        Pass { Stencil { Ref 254 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 254); } ENDCG }
        Pass { Stencil { Ref 255 Comp Equal } Blend One OneMinusSrcAlpha ZTest Always ZWrite Off ColorMask RGB CGPROGRAM fixed4 frag (v2f i) : SV_Target { return SampleNumber(i.uv, 255); } ENDCG }
    }
}
