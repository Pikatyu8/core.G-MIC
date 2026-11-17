Shader "CoreGMIC/Cubism"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert(appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        return o;
    }

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;

    StructuredBuffer<float4> _CurrentOffsets;

    float4 _CubismParams; // x = P (half-size in px), y = maxAngleRad, z = width, w = height
    int _IterSeed;
    int _UseOffsets;
    int _OffsetBase;
    float _Smoothness;
    float _Opacity;

    uint HashU32(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Separable Smoothness Gaussian Blur Horizontal
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurH

            float4 fragBlurH(v2f i) : SV_Target
            {
                float stepPx = max(0.25, _Smoothness * 0.65);
                float2 stepUv = float2(_MainTex_TexelSize.x * stepPx, 0.0);

                static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
                float4 accum = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)) * weights[0];

                [unroll]
                for (int k = 1; k <= 4; k++)
                {
                    float2 off = stepUv * (float)k;
                    accum += tex2Dlod(_MainTex, float4(saturate(i.uv + off), 0.0, 0.0)) * weights[k];
                    accum += tex2Dlod(_MainTex, float4(saturate(i.uv - off), 0.0, 0.0)) * weights[k];
                }

                return accum;
            }
            ENDCG
        }

        // Pass 1: Separable Smoothness Gaussian Blur Vertical
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurV

            float4 fragBlurV(v2f i) : SV_Target
            {
                float stepPx = max(0.25, _Smoothness * 0.65);
                float2 stepUv = float2(0.0, _MainTex_TexelSize.y * stepPx);

                static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
                float4 accum = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)) * weights[0];

                [unroll]
                for (int k = 1; k <= 4; k++)
                {
                    float2 off = stepUv * (float)k;
                    accum += tex2Dlod(_MainTex, float4(saturate(i.uv + off), 0.0, 0.0)) * weights[k];
                    accum += tex2Dlod(_MainTex, float4(saturate(i.uv - off), 0.0, 0.0)) * weights[k];
                }

                return accum;
            }
            ENDCG
        }

        // Pass 2: Procedural Rotated Textured Cubism Quads
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vertQuad
            #pragma fragment fragQuad

            v2f vertQuad(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                static const float2 kCorners[6] =
                {
                    float2(-1.0, -1.0),
                    float2( 1.0, -1.0),
                    float2( 1.0,  1.0),
                    float2(-1.0, -1.0),
                    float2( 1.0,  1.0),
                    float2(-1.0,  1.0)
                };

                float halfSizePx = _CubismParams.x;
                float maxAngleRad = _CubismParams.y;
                float2 res = _CubismParams.zw;
                float2 invRes = 1.0 / res;

                uint seed = HashU32(iid ^ ((uint)_IterSeed * 2654435761u));
                float u1 = (float)(HashU32(seed + 1u) & 0xFFFFu) / 65535.0;
                float u2 = (float)(HashU32(seed + 2u) & 0xFFFFu) / 65535.0;
                float u3 = (float)(HashU32(seed + 3u) & 0xFFFFu) / 65535.0;

                float2 pad = min(float2(halfSizePx, halfSizePx), res * 0.45);
                float2 baseCenterPx = lerp(pad, res - pad, float2(u1, u2));
                float2 centerPx = (_UseOffsets > 0)
                    ? clamp(_CurrentOffsets[_OffsetBase + (int)iid].xy, pad, res - pad)
                    : baseCenterPx;
                float2 centerUv = centerPx * invRes;

                float angle = (u3 * 2.0 - 1.0) * maxAngleRad;
                float sA, cA;
                sincos(angle, sA, cA);

                float2 corner = kCorners[vid % 6u];
                float2 localPx = corner * halfSizePx;
                float2 rotPx = float2(
                    localPx.x * cA - localPx.y * sA,
                    localPx.x * sA + localPx.y * cA);

                float2 screenUv = centerUv + rotPx * invRes;
                float2 sampleUv = saturate(centerUv + localPx * invRes);

                v2f o;
                o.pos = UnityObjectToClipPos(float4(screenUv.x, screenUv.y, 0.0, 1.0));
                o.uv = sampleUv;
                return o;
            }

            float4 fragQuad(v2f i) : SV_Target
            {
                return float4(tex2D(_MainTex, i.uv).rgb, 1.0);
            }
            ENDCG
        }

        // Pass 3: Alpha Blend Cubism Canvas onto Iteration Base
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlend

            float4 fragBlend(v2f i) : SV_Target
            {
                float4 baseCol = tex2D(_BaseTex, i.uv);
                float4 cubismCol = tex2D(_MainTex, i.uv);
                return float4(lerp(baseCol.rgb, cubismCol.rgb, _Opacity), 1.0);
            }
            ENDCG
        }
    }
}