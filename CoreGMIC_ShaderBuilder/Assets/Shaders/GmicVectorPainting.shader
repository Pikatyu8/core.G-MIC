Shader "CoreGMIC/VectorPainting"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _PrevSourceTex;

    float _ColorThreshold;
    float _MinAlpha;
    int _Width;
    int _Height;

    StructuredBuffer<uint> _ParentBuffer;
    StructuredBuffer<uint> _ShapeSumBuffer;
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: RGB -> Luminance
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragLuminance

            float4 fragLuminance(v2f i) : SV_Target
            {
                float3 rgb = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb;
                float lum = dot(rgb, float3(0.299, 0.587, 0.114));
                return float4(lum, lum, lum, 1.0);
            }
            ENDCG
        }

        // Pass 1: Max Gradient Direction Label (1..8)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragGradientDirLabel

            float4 fragGradientDirLabel(v2f i) : SV_Target
            {
                static const int2 kOffsets[8] =
                {
                    int2(-1, -1), int2(0, -1), int2(1, -1),
                    int2(-1,  0),              int2(1,  0),
                    int2(-1,  1), int2(0,  1), int2(1,  1)
                };

                float2 tx = _MainTex_TexelSize.xy;
                float center = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;

                float dmax = -1.0;
                int nmax = 1;

                [unroll]
                for (int n = 0; n < 8; n++)
                {
                    float2 suv = saturate(i.uv + float2((float)kOffsets[n].x, (float)kOffsets[n].y) * tx);
                    float nb = tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).r;
                    float diff = (nb - center) * 255.0;
                    float val = diff * diff;
                    if (val > dmax)
                    {
                        dmax = val;
                        nmax = n + 1;
                    }
                }

                if (dmax < 1e-6)
                {
                    nmax = 1;
                }

                float encoded = ((float)nmax + 0.5) / 16.0;
                return float4(encoded, encoded, encoded, encoded);
            }
            ENDCG
        }

        // Pass 4: Resolve Shape Average Color onto Source Alpha
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragResolveShapeAverage

            float4 fragResolveShapeAverage(v2f i) : SV_Target
            {
                uint2 px = min(
                    uint2((uint)max(0, _Width - 1), (uint)max(0, _Height - 1)),
                    (uint2)floor(saturate(i.uv) * float2((float)_Width, (float)_Height)));
                uint idx = px.y * (uint)_Width + px.x;

                uint root = _ParentBuffer[idx];
                uint baseOff = root * 4u;
                uint cnt = max(1u, _ShapeSumBuffer[baseOff + 3u]);

                float invDenom = 1.0 / ((float)cnt * 1024.0);
                float3 avgRgb = float3(
                    (float)_ShapeSumBuffer[baseOff + 0u] * invDenom,
                    (float)_ShapeSumBuffer[baseOff + 1u] * invDenom,
                    (float)_ShapeSumBuffer[baseOff + 2u] * invDenom);

                float alpha = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(saturate(avgRgb), alpha);
            }
            ENDCG
        }

        // Pass 5: 3x3 Mode Filter on Direction Labels (removes 1px speckles and bridges)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragMajorityLabel3x3

            float4 fragMajorityLabel3x3(v2f i) : SV_Target
            {
                int c1 = 0, c2 = 0, c3 = 0, c4 = 0, c5 = 0, c6 = 0, c7 = 0, c8 = 0;

                [unroll]
                for (int dy = -1; dy <= 1; dy++)
                {
                    [unroll]
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 suv = saturate(i.uv + float2((float)dx, (float)dy) * _MainTex_TexelSize.xy);
                        int lbl = clamp((int)floor(tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).a * 16.0), 1, 8);
                        int w = (dx == 0 && dy == 0) ? 2 : 1;
                        if (lbl == 1) c1 += w;
                        else if (lbl == 2) c2 += w;
                        else if (lbl == 3) c3 += w;
                        else if (lbl == 4) c4 += w;
                        else if (lbl == 5) c5 += w;
                        else if (lbl == 6) c6 += w;
                        else if (lbl == 7) c7 += w;
                        else c8 += w;
                    }
                }

                int bestLbl = 1;
                int bestCnt = c1;
                if (c2 > bestCnt) { bestCnt = c2; bestLbl = 2; }
                if (c3 > bestCnt) { bestCnt = c3; bestLbl = 3; }
                if (c4 > bestCnt) { bestCnt = c4; bestLbl = 4; }
                if (c5 > bestCnt) { bestCnt = c5; bestLbl = 5; }
                if (c6 > bestCnt) { bestCnt = c6; bestLbl = 6; }
                if (c7 > bestCnt) { bestCnt = c7; bestLbl = 7; }
                if (c8 > bestCnt) { bestCnt = c8; bestLbl = 8; }

                float enc = ((float)bestLbl + 0.5) / 16.0;
                return float4(enc, enc, enc, enc);
            }
            ENDCG
        }

        // Pass 6: Coherent 3x3 Temporal Adaptive IIR Filter on Source RGB
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragTemporalDeadzoneSource

            float4 fragTemporalDeadzoneSource(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float4 cur = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float3 prev = tex2Dlod(_PrevSourceTex, float4(i.uv, 0.0, 0.0)).rgb;

                // Signed 3x3 average (2px stride) cancels uncorrelated grain while preserving coherent motion.
                float3 avgDiff = float3(0.0, 0.0, 0.0);
                [unroll]
                for (int dy = -1; dy <= 1; dy++)
                {
                    [unroll]
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 suv = saturate(i.uv + float2((float)dx, (float)dy) * tx * 2.0);
                        avgDiff += tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).rgb
                                 - tex2Dlod(_PrevSourceTex, float4(suv, 0.0, 0.0)).rgb;
                    }
                }
                avgDiff /= 9.0;

                float3 a3 = abs(avgDiff);
                float coherent = max(a3.r, max(a3.g, a3.b));
                float thresh = max(1e-5, _ColorThreshold);
                float alpha = lerp(saturate(_MinAlpha), 1.0, smoothstep(thresh, thresh * 2.0, coherent));

                return float4(lerp(prev, cur.rgb, alpha), cur.a);
            }
            ENDCG
        }
    }
}