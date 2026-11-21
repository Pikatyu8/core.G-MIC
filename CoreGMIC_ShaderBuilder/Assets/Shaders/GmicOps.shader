Shader "CoreGMIC/Ops"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;
    sampler2D _TensorTex;

    #define GMIC_USE_SOBEL 1
    #define GMIC_USE_TENSOR 1
    #include "GmicColorSpaces.cginc"

    StructuredBuffer<uint> _ParentBuffer;
    StructuredBuffer<uint> _ShapeSumBuffer;
    StructuredBuffer<uint> _StatsBuf;

    int _Width;
    int _Height;
    int _ChannelMode;
    float _SigmaPx;
    float _JfaStep;
    float _FallbackDistance;
    float _UseBaseAlpha;
    float _AnisoSharpness;
    float _AnisoAnisotropy;
    float _AnisoDl;

    float4 RunGaussian1DRgba(float2 uv, float2 axisTexel)
    {
        float sigma = max(0.25, _SigmaPx);
        float invTwoSigmaSq = 0.5 / (sigma * sigma);

        float4 accum = float4(0.0, 0.0, 0.0, 0.0);
        float sumW = 0.0;

        [unroll]
        for (int k = -10; k <= 10; k++)
        {
            float offsetPx = (float)k;
            float w = exp(-(offsetPx * offsetPx) * invTwoSigmaSq);
            float2 suv = saturate(uv + axisTexel * offsetPx);
            accum += tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)) * w;
            sumW += w;
        }

        return accum / max(1e-6, sumW);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Separable Gaussian Blur Horizontal (RGBA)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurH

            float4 fragBlurH(v2f i) : SV_Target
            {
                return RunGaussian1DRgba(i.uv, float2(_MainTex_TexelSize.x, 0.0));
            }
            ENDCG
        }

        // Pass 1: Separable Gaussian Blur Vertical (RGBA)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurV

            float4 fragBlurV(v2f i) : SV_Target
            {
                return RunGaussian1DRgba(i.uv, float2(0.0, _MainTex_TexelSize.y));
            }
            ENDCG
        }

        // Pass 2: Euclidean Jump Flooding Step
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragJfaStep

            float4 fragJfaStep(v2f i) : SV_Target
            {
                float2 stepUv = _MainTex_TexelSize.xy * _JfaStep;
                float2 sizePx = _MainTex_TexelSize.zw;

                float4 best = float4(0.0, 0.0, 0.0, 0.0);
                float bestDistSq = 1e20;

                [unroll]
                for (int dy = -1; dy <= 1; dy++)
                {
                    [unroll]
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 suv = i.uv + float2((float)dx, (float)dy) * stepUv;
                        if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0)
                            continue;

                        float4 cand = tex2Dlod(_MainTex, float4(suv, 0.0, 0.0));
                        if (cand.a < 0.5)
                            continue;

                        float2 diffPx = (i.uv - cand.xy) * sizePx;
                        float d2 = dot(diffPx, diffPx);
                        if (d2 < bestDistSq)
                        {
                            bestDistSq = d2;
                            best = cand;
                        }
                    }
                }

                return best;
            }
            ENDCG
        }

        // Pass 3: Resolve Euclidean Distance from JFA Seeds (in pixels)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragJfaResolveDistance

            float4 fragJfaResolveDistance(v2f i) : SV_Target
            {
                float4 seed = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float dist = (seed.a > 0.5)
                    ? length((i.uv - seed.xy) * _MainTex_TexelSize.zw)
                    : _FallbackDistance;
                return float4(dist, dist, dist, 1.0);
            }
            ENDCG
        }

        // Pass 4: Resolve 4-Connected Shape Average Color onto Source Alpha
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

                float invDenom = 1.0 / ((float)cnt * 256.0);
                float3 avgRgb = float3(
                    (float)_ShapeSumBuffer[baseOff + 0u] * invDenom,
                    (float)_ShapeSumBuffer[baseOff + 1u] * invDenom,
                    (float)_ShapeSumBuffer[baseOff + 2u] * invDenom);

                float alpha = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(saturate(avgRgb), alpha);
            }
            ENDCG
        }

        // Pass 5: Normalize [0, 1] using Min/Max from _StatsBuf
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragNormalize

            float4 fragNormalize(v2f i) : SV_Target
            {
                float4 src = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float minV = asfloat(_StatsBuf[0]);
                float maxV = asfloat(_StatsBuf[1]);
                float range = maxV - minV;

                float3 outRgb = (range > 1e-5) ? saturate((src.rgb - minV) / range) : saturate(src.rgb);
                float alpha = (_UseBaseAlpha > 0.5)
                    ? tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a
                    : src.a;
                return float4(outRgb, alpha);
            }
            ENDCG
        }

        // Pass 6: Encode sRGB/RGBA -> Target Channel Space
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragEncodeChannelSpace

            float4 fragEncodeChannelSpace(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                return EncodeChannelSpace(src, _ChannelMode);
            }
            ENDCG
        }

        // Pass 7: Decode & Merge Active Channel Back to RGBA
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragDecodeAndMergeChannel

            float4 fragDecodeAndMergeChannel(v2f i) : SV_Target
            {
                float4 proc = tex2D(_MainTex, i.uv);
                float4 orig = tex2D(_BaseTex, i.uv);
                return DecodeAndMergeChannel(orig, proc, _ChannelMode);
            }
            ENDCG
        }

        // Pass 8: Extract Channel Subset into RGB for Generic Single/Multi-Channel Ops
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragExtractChannelSubset

            float4 fragExtractChannelSubset(v2f i) : SV_Target
            {
                float4 enc = tex2D(_MainTex, i.uv);
                return ExtractChannelSubset(enc, _ChannelMode);
            }
            ENDCG
        }

        // Pass 9: Repack Subset & Decode Back to RGBA
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragRepackSubsetAndDecode

            float4 fragRepackSubsetAndDecode(v2f i) : SV_Target
            {
                float4 proc = tex2D(_MainTex, i.uv);
                float4 orig = tex2D(_BaseTex, i.uv);
                return RepackSubsetAndDecode(orig, proc, _ChannelMode);
            }
            ENDCG
        }

        // Pass 10: Clamp/Cut [0, 1] with Optional Original Alpha
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragCut01

            float4 fragCut01(v2f i) : SV_Target
            {
                float4 src = saturate(tex2D(_MainTex, i.uv));
                float alpha = (_UseBaseAlpha > 0.5)
                    ? tex2D(_BaseTex, i.uv).a
                    : src.a;
                return float4(src.rgb, alpha);
            }
            ENDCG
        }

        // Pass 11: Structure Tensor (Jxx, Jxy, Jyy)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragStructureTensor

            float4 fragStructureTensor(v2f i) : SV_Target
            {
                float3 dx, dy;
                ComputeSobelGradients(i.uv, _MainTex_TexelSize.xy, dx, dy);
                return float4(dot(dx, dx), dot(dx, dy), dot(dy, dy), 1.0);
            }
            ENDCG
        }

        // Pass 12: Separable Tensor Gaussian Blur Horizontal
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurTensorH

            float4 fragBlurTensorH(v2f i) : SV_Target
            {
                float stepPx = max(0.5, _SigmaPx * 1.2);
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

        // Pass 13: Separable Tensor Gaussian Blur Vertical
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurTensorV

            float4 fragBlurTensorV(v2f i) : SV_Target
            {
                float stepPx = max(0.5, _SigmaPx * 1.2);
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

        // Pass 14: Curved Streamline LIC Anisotropic Smooth
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragAnisotropicSmooth

            float4 fragAnisotropicSmooth(v2f i) : SV_Target
            {
                float4 center = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float centerEdge;
                float2 t0 = GetTangentAndEdge(i.uv, centerEdge);
                float2 n0 = float2(-t0.y, t0.x);

                float anisoNorm = saturate(_AnisoAnisotropy * 2.0);
                float baseStepPx = max(0.25, _AnisoDl * 0.35);
                float sharpColor = _AnisoSharpness * 0.00045;
                float sharpEdge = _AnisoSharpness * 0.0015;

                float3 sumRgb = center.rgb;
                float sumW = 1.0;

                float2 posF = i.uv;
                float2 curTF = t0;
                [unroll]
                for (int kf = 1; kf <= 12; kf++)
                {
                    float stepEdge;
                    float2 nextT = GetTangentAndEdge(posF, stepEdge);
                    if (dot(nextT, curTF) < 0.0) nextT = -nextT;
                    curTF = nextT;

                    float brake = pow(1.0 + stepEdge, -sharpEdge);
                    posF = saturate(posF + curTF * (_MainTex_TexelSize.xy * baseStepPx * lerp(1.0, brake, 0.35)));

                    float3 sRgb = tex2Dlod(_MainTex, float4(posF, 0.0, 0.0)).rgb;
                    float3 dc = sRgb - center.rgb;
                    float edgeJump = abs(stepEdge - centerEdge);
                    float edgeGuard = exp(-edgeJump * 0.04 * (_AnisoSharpness * 0.01));
                    float w = exp(-0.016 * (float)(kf * kf)) * exp(-dot(dc, dc) * sharpColor) * edgeGuard;

                    sumRgb += sRgb * w;
                    sumW += w;
                }

                float2 posB = i.uv;
                float2 curTB = -t0;
                [unroll]
                for (int kb = 1; kb <= 12; kb++)
                {
                    float stepEdge;
                    float2 nextT = GetTangentAndEdge(posB, stepEdge);
                    if (dot(nextT, curTB) < 0.0) nextT = -nextT;
                    curTB = nextT;

                    float brake = pow(1.0 + stepEdge, -sharpEdge);
                    posB = saturate(posB + curTB * (_MainTex_TexelSize.xy * baseStepPx * lerp(1.0, brake, 0.35)));

                    float3 sRgb = tex2Dlod(_MainTex, float4(posB, 0.0, 0.0)).rgb;
                    float3 dc = sRgb - center.rgb;
                    float edgeJump = abs(stepEdge - centerEdge);
                    float edgeGuard = exp(-edgeJump * 0.04 * (_AnisoSharpness * 0.01));
                    float w = exp(-0.016 * (float)(kb * kb)) * exp(-dot(dc, dc) * sharpColor) * edgeGuard;

                    sumRgb += sRgb * w;
                    sumW += w;
                }

                float lateralScale = (1.0 - anisoNorm) * pow(1.0 + centerEdge, -sharpEdge * 1.5);
                if (lateralScale > 0.02)
                {
                    float2 latOff = n0 * (_MainTex_TexelSize.xy * baseStepPx * 1.4 * lateralScale);
                    float3 sP = tex2Dlod(_MainTex, float4(saturate(i.uv + latOff), 0.0, 0.0)).rgb;
                    float3 sM = tex2Dlod(_MainTex, float4(saturate(i.uv - latOff), 0.0, 0.0)).rgb;

                    float3 dcP = sP - center.rgb;
                    float3 dcM = sM - center.rgb;
                    float wP = 0.65 * lateralScale * exp(-dot(dcP, dcP) * sharpColor);
                    float wM = 0.65 * lateralScale * exp(-dot(dcM, dcM) * sharpColor);

                    sumRgb += sP * wP + sM * wM;
                    sumW += wP + wM;
                }

                return float4(sumRgb / max(1e-5, sumW), center.a);
            }
            ENDCG
        }

        // Pass 15: Euclidean RGB Norm -> R,G,B,A
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragRgbNorm

            float4 fragRgbNorm(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float n = length(src.rgb);
                return float4(n, n, n, src.a);
            }
            ENDCG
        }
    }
}