Shader "CoreGMIC/ChalkItUp"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    sampler2D _AuxTex;
    sampler2D _AvgTex;
    sampler2D _BaseTex;

    float _ColorMul;

    int _MorphoSize;
    int _MorphoAction;
    float _MorphoIsDilate;
    float _InvertColors;
    float _SpreadX;
    float _SpreadY;

    #define GMIC_USE_HASH 1
    #include "GmicColorSpaces.cginc"
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: RGB -> YCbCr
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragRgbToYCbCr

            float4 fragRgbToYCbCr(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                return float4(RgbToYCbCr(saturate(c.rgb)), c.a);
            }
            ENDCG
        }

        // Pass 1: Local 13x1 Horizontal Min/Max -> RG
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragLocalMinMaxH

            float4 fragLocalMinMaxH(v2f i) : SV_Target
            {
                float tx = _MainTex_TexelSize.x;
                float minV = 1e6;
                float maxV = -1e6;

                [unroll]
                for (int k = -6; k <= 6; k++)
                {
                    float3 v = tex2Dlod(_MainTex, float4(saturate(i.uv + float2((float)k * tx, 0.0)), 0.0, 0.0)).rgb;
                    minV = min(minV, min(v.r, min(v.g, v.b)));
                    maxV = max(maxV, max(v.r, max(v.g, v.b)));
                }
                return float4(minV, maxV, 0.0, 1.0);
            }
            ENDCG
        }

        // Pass 2: Local 1x13 Vertical Min/Max -> RG
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragLocalMinMaxV

            float4 fragLocalMinMaxV(v2f i) : SV_Target
            {
                float ty = _MainTex_TexelSize.y;
                float minV = 1e6;
                float maxV = -1e6;

                [unroll]
                for (int k = -6; k <= 6; k++)
                {
                    float2 v = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, (float)k * ty)), 0.0, 0.0)).rg;
                    minV = min(minV, v.r);
                    maxV = max(maxV, v.g);
                }
                return float4(minV, maxV, 0.0, 1.0);
            }
            ENDCG
        }

        // Pass 3: Separable Gaussian Blur Sigma=5 Horizontal
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurSigma5H

            float4 fragBlurSigma5H(v2f i) : SV_Target
            {
                float stepU = _MainTex_TexelSize.x * 2.0;
                float4 accum = float4(0.0, 0.0, 0.0, 0.0);
                float sumW = 0.0;

                [unroll]
                for (int k = -6; k <= 6; k++)
                {
                    float w = exp(-0.08 * (float)(k * k));
                    accum += tex2Dlod(_MainTex, float4(saturate(i.uv + float2((float)k * stepU, 0.0)), 0.0, 0.0)) * w;
                    sumW += w;
                }
                return accum / sumW;
            }
            ENDCG
        }

        // Pass 4: Separable Gaussian Blur Sigma=5 Vertical
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurSigma5V

            float4 fragBlurSigma5V(v2f i) : SV_Target
            {
                float stepV = _MainTex_TexelSize.y * 2.0;
                float4 accum = float4(0.0, 0.0, 0.0, 0.0);
                float sumW = 0.0;

                [unroll]
                for (int k = -6; k <= 6; k++)
                {
                    float w = exp(-0.08 * (float)(k * k));
                    accum += tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, (float)k * stepV)), 0.0, 0.0)) * w;
                    sumW += w;
                }
                return accum / sumW;
            }
            ENDCG
        }

        // Pass 5: normalize_local combine (amp = 10)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragNormalizeLocalCombine

            float4 fragNormalizeLocalCombine(v2f i) : SV_Target
            {
                float4 ycc = tex2D(_MainTex, i.uv);
                float2 ed = tex2D(_AuxTex, i.uv).rg;
                float3 avg = tex2D(_AvgTex, i.uv).rgb;

                float eS = ed.r;
                float dS = ed.g;

                float range = (dS - eS) + (0.01 / 255.0);
                float3 z = (ycc.rgb - eS) / range;

                float3 minT = max(0.0, 11.0 * eS - 10.0 * avg);
                float3 maxT = min(1.0, 11.0 * dS - 10.0 * avg);
                float3 yccOut = saturate(minT + z * (maxT - minT));

                return float4(yccOut, ycc.a);
            }
            ENDCG
        }

        // Pass 6: YCbCr -> RGB
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragYCbCrToRgb

            float4 fragYCbCrToRgb(v2f i) : SV_Target
            {
                float4 ycc = tex2D(_MainTex, i.uv);
                return float4(YCbCrToRgb(ycc.rgb), ycc.a);
            }
            ENDCG
        }

        // Pass 11: RGB -> Lab01
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragRgbToLab01

            float4 fragRgbToLab01(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                return float4(RgbToLab01(saturate(c.rgb)), c.a);
            }
            ENDCG
        }

        // Pass 12: Scale a,b by _ColorMul and blur chroma horizontally
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragLabChromaH

            float4 fragLabChromaH(v2f i) : SV_Target
            {
                float4 center = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float stepPx = clamp(0.0015 * _MainTex_TexelSize.z, 0.5, 3.0);
                float stepU = _MainTex_TexelSize.x * stepPx;

                static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
                float2 abCenter = (center.gb - (128.0 / 255.0)) * _ColorMul;
                float2 accumAB = abCenter * weights[0];

                [unroll]
                for (int k = 1; k <= 4; k++)
                {
                    float off = stepU * (float)k;
                    float2 abP = (tex2Dlod(_MainTex, float4(saturate(i.uv + float2(off, 0.0)), 0.0, 0.0)).gb - (128.0 / 255.0)) * _ColorMul;
                    float2 abM = (tex2Dlod(_MainTex, float4(saturate(i.uv - float2(off, 0.0)), 0.0, 0.0)).gb - (128.0 / 255.0)) * _ColorMul;
                    accumAB += (abP + abM) * weights[k];
                }

                return float4(center.r, accumAB + (128.0 / 255.0), center.a);
            }
            ENDCG
        }

        // Pass 13: Blur Lab chroma vertically
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragLabChromaV

            float4 fragLabChromaV(v2f i) : SV_Target
            {
                float4 center = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float stepPx = clamp(0.0015 * _MainTex_TexelSize.z, 0.5, 3.0);
                float stepV = _MainTex_TexelSize.y * stepPx;

                static const float weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
                float2 accumAB = center.gb * weights[0];

                [unroll]
                for (int k = 1; k <= 4; k++)
                {
                    float off = stepV * (float)k;
                    float2 abP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, off)), 0.0, 0.0)).gb;
                    float2 abM = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, off)), 0.0, 0.0)).gb;
                    accumAB += (abP + abM) * weights[k];
                }

                return float4(center.r, accumAB, center.a);
            }
            ENDCG
        }

        // Pass 14: Lab01 -> RGB
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragLab01ToRgb

            float4 fragLab01ToRgb(v2f i) : SV_Target
            {
                float4 lab01 = tex2D(_MainTex, i.uv);
                return float4(Lab01ToRgb(float3(saturate(lab01.r), lab01.g, lab01.b)), lab01.a);
            }
            ENDCG
        }

        // Pass 15: Morpho Square Horizontal (width _MorphoSize)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMorphoSquareH

            float4 fragMorphoSquareH(v2f i) : SV_Target
            {
                int dx1 = _MorphoSize / 2;
                int dx2 = _MorphoSize - dx1 - 1;
                float tx = _MainTex_TexelSize.x;

                float4 acc = (_MorphoIsDilate > 0.5) ? float4(-1e6, -1e6, -1e6, -1e6) : float4(1e6, 1e6, 1e6, 1e6);
                for (int k = -dx1; k <= dx2; k++)
                {
                    float4 s = tex2Dlod(_MainTex, float4(saturate(i.uv + float2((float)k * tx, 0.0)), 0.0, 0.0));
                    acc = (_MorphoIsDilate > 0.5) ? max(acc, s) : min(acc, s);
                }
                return acc;
            }
            ENDCG
        }

        // Pass 16: Morpho Square Vertical (height _MorphoSize)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMorphoSquareV

            float4 fragMorphoSquareV(v2f i) : SV_Target
            {
                int dy1 = _MorphoSize / 2;
                int dy2 = _MorphoSize - dy1 - 1;
                float ty = _MainTex_TexelSize.y;

                float4 acc = (_MorphoIsDilate > 0.5) ? float4(-1e6, -1e6, -1e6, -1e6) : float4(1e6, 1e6, 1e6, 1e6);
                for (int k = -dy1; k <= dy2; k++)
                {
                    float4 s = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, (float)k * ty)), 0.0, 0.0));
                    acc = (_MorphoIsDilate > 0.5) ? max(acc, s) : min(acc, s);
                }
                return acc;
            }
            ENDCG
        }

        // Pass 17: Morpho 3x3 Diamond (cross) step for octagonal morphology
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMorphoDiamond

            float4 fragMorphoDiamond(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float4 l = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0));
                float4 r = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0));
                float4 d = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, tx.y)), 0.0, 0.0));
                float4 u = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, tx.y)), 0.0, 0.0));

                if (_MorphoIsDilate > 0.5)
                {
                    return max(c, max(max(l, r), max(d, u)));
                }
                return min(c, min(min(l, r), min(d, u)));
            }
            ENDCG
        }

        // Pass 18: Morpho Combine Action (0..7) + Optional Invert
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMorphoCombine

            float4 fragMorphoCombine(v2f i) : SV_Target
            {
                float4 proc = tex2D(_MainTex, i.uv);
                float4 orig = tex2D(_BaseTex, i.uv);

                float4 res;
                if (_MorphoAction == 4 || _MorphoAction == 6)
                {
                    res = saturate(orig - proc);
                }
                else if (_MorphoAction == 5 || _MorphoAction == 7)
                {
                    res = saturate(proc - orig);
                }
                else
                {
                    res = saturate(proc);
                }

                if (_InvertColors > 0.5)
                {
                    res = 1.0 - res;
                }

                return res;
            }
            ENDCG
        }

        // Pass 19: Deterministic Pixel Spread
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragSpread

            float4 fragSpread(v2f i) : SV_Target
            {
                uint2 p = (uint2)max(float2(0.0, 0.0), floor(i.uv * _MainTex_TexelSize.zw));
                uint h1 = HashU32(p.x * 1664525u ^ p.y * 1013904223u ^ 0x9E3779B9u);
                uint h2 = HashU32(h1 ^ 0x85EBCA6Bu);

                float rx = ((float)(h1 & 0xFFFFu) / 65535.0) * 2.0 - 1.0;
                float ry = ((float)(h2 & 0xFFFFu) / 65535.0) * 2.0 - 1.0;

                float2 offsetUv = float2(rx * _SpreadX, ry * _SpreadY) * _MainTex_TexelSize.xy;
                return tex2Dlod(_MainTex, float4(saturate(i.uv + offsetUv), 0.0, 0.0));
            }
            ENDCG
        }
    }
}
