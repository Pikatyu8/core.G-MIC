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
    sampler2D _TensorTex;
    sampler2D _GradTex;
    sampler2D _JfaTex;
    sampler2D _BaseTex;

    float _Sigma;
    float _Sharpness;
    float _Anisotropy;
    float _Dl;
    float _ColorMul;
    float _JfaStep;

    int _ChannelMode;
    int _MorphoSize;
    int _MorphoAction;
    float _MorphoIsDilate;
    float _InvertColors;
    float _SpreadX;
    float _SpreadY;

    #define GMIC_USE_HASH 1
    #define GMIC_USE_SOBEL 1
    #define GMIC_USE_TENSOR 1
    #include "GmicCommon.cginc"

    // --- Color Space Helpers ---

    float3 SrgbToLinear(float3 c)
    {
        float3 lo = c / 12.92;
        float3 hi = pow(max((c + 0.055) / 1.055, 0.0), 2.4);
        return float3(
            c.r <= 0.04045 ? lo.r : hi.r,
            c.g <= 0.04045 ? lo.g : hi.g,
            c.b <= 0.04045 ? lo.b : hi.b
        );
    }

    float3 LinearToSrgb(float3 c)
    {
        c = max(c, 0.0);
        float3 lo = c * 12.92;
        float3 hi = 1.055 * pow(max(c, 1e-6), 1.0 / 2.4) - 0.055;
        return saturate(float3(
            c.r <= 0.0031308 ? lo.r : hi.r,
            c.g <= 0.0031308 ? lo.g : hi.g,
            c.b <= 0.0031308 ? lo.b : hi.b
        ));
    }

    float3 RgbToYCbCr(float3 c)
    {
        float y = 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
        float cb = 0.5 + (-0.168736 * c.r - 0.331264 * c.g + 0.5 * c.b);
        float cr = 0.5 + ( 0.5 * c.r - 0.418688 * c.g - 0.081312 * c.b);
        return saturate(float3(y, cb, cr));
    }

    float3 YCbCrToRgb(float3 ycc)
    {
        float y = ycc.x;
        float cb = ycc.y - 0.5;
        float cr = ycc.z - 0.5;
        float r = y + 1.402 * cr;
        float g = y - 0.344136 * cb - 0.714136 * cr;
        float b = y + 1.772 * cb;
        return saturate(float3(r, g, b));
    }

    float LabF(float t)
    {
        return (t > 0.008856) ? pow(max(t, 1e-6), 1.0 / 3.0) : (7.787 * t + 16.0 / 116.0);
    }

    float LabInvF(float t)
    {
        return (t > 0.20689655) ? (t * t * t) : ((t - 16.0 / 116.0) / 7.787);
    }

    float3 RgbToLab01(float3 srgb)
    {
        float3 lin = SrgbToLinear(saturate(srgb));
        float X = (0.4124564 * lin.r + 0.3575761 * lin.g + 0.1804375 * lin.b) / 0.95047;
        float Y = (0.2126729 * lin.r + 0.7151522 * lin.g + 0.0721750 * lin.b) / 1.00000;
        float Z = (0.0193339 * lin.r + 0.1191920 * lin.g + 0.9503041 * lin.b) / 1.08883;

        float fx = LabF(X);
        float fy = LabF(Y);
        float fz = LabF(Z);

        float L = (116.0 * fy - 16.0) / 100.0;
        float a = (500.0 * (fx - fy) + 128.0) / 255.0;
        float b = (200.0 * (fy - fz) + 128.0) / 255.0;
        return saturate(float3(L, a, b));
    }

    float3 Lab01ToRgb(float3 lab01)
    {
        float L = lab01.x * 100.0;
        float a = lab01.y * 255.0 - 128.0;
        float b = lab01.z * 255.0 - 128.0;

        float fy = (L + 16.0) / 116.0;
        float fx = a / 500.0 + fy;
        float fz = fy - b / 200.0;

        float X = 0.95047 * LabInvF(fx);
        float Y = 1.00000 * LabInvF(fy);
        float Z = 1.08883 * LabInvF(fz);

        float r =  3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z;
        float g = -0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z;
        float bc = 0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z;
        return LinearToSrgb(float3(r, g, bc));
    }

    float3 RgbToLch01(float3 srgb)
    {
        float3 lab01 = RgbToLab01(srgb);
        float a = lab01.y * 255.0 - 128.0;
        float b = lab01.z * 255.0 - 128.0;
        float C = saturate(sqrt(a * a + b * b) / 181.0);
        float H = (abs(a) < 1e-5 && abs(b) < 1e-5) ? 0.0 : frac(atan2(b, a) / 6.28318530718);
        return float3(lab01.x, C, H);
    }

    float3 Lch01ToRgb(float3 lch01)
    {
        float C = lch01.y * 181.0;
        float angle = lch01.z * 6.28318530718;
        float sA, cA;
        sincos(angle, sA, cA);
        float a01 = saturate((C * cA + 128.0) / 255.0);
        float b01 = saturate((C * sA + 128.0) / 255.0);
        return Lab01ToRgb(float3(lch01.x, a01, b01));
    }

    float3 RgbToHsv(float3 c)
    {
        float cmax = max(c.r, max(c.g, c.b));
        float cmin = min(c.r, min(c.g, c.b));
        float d = cmax - cmin;
        float h = 0.0;
        if (d >= 1e-5)
        {
            if (cmax == c.r) h = ((c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0)) / 6.0;
            else if (cmax == c.g) h = ((c.b - c.r) / d + 2.0) / 6.0;
            else h = ((c.r - c.g) / d + 4.0) / 6.0;
        }
        float s = (cmax > 1e-5) ? (d / cmax) : 0.0;
        return float3(frac(h), saturate(s), saturate(cmax));
    }

    float3 HsvToRgb(float3 hsv)
    {
        float h = frac(hsv.x) * 6.0;
        float s = saturate(hsv.y);
        float v = saturate(hsv.z);
        float f = frac(h);
        float p = v * (1.0 - s);
        float q = v * (1.0 - s * f);
        float t = v * (1.0 - s * (1.0 - f));
        if (h < 1.0) return float3(v, t, p);
        if (h < 2.0) return float3(q, v, p);
        if (h < 3.0) return float3(p, v, t);
        if (h < 4.0) return float3(p, q, v);
        if (h < 5.0) return float3(t, p, v);
        return float3(v, p, q);
    }


    float3 RgbToHsi(float3 c)
    {
        float sum = c.r + c.g + c.b;
        float inten = sum / 3.0;
        float cmin = min(c.r, min(c.g, c.b));
        float s = (sum > 1e-5) ? (1.0 - 3.0 * cmin / sum) : 0.0;
        float num = 0.5 * ((c.r - c.g) + (c.r - c.b));
        float den = sqrt(max(1e-10, (c.r - c.g) * (c.r - c.g) + (c.r - c.b) * (c.g - c.b)));
        float theta = acos(clamp(num / den, -1.0, 1.0)) / 6.28318530718;
        float h = (c.b <= c.g) ? theta : (1.0 - theta);
        return float3(frac(h), saturate(s), saturate(inten));
    }

    float3 HsiToRgb(float3 hsi)
    {
        float h = frac(hsi.x) * 6.28318530718;
        float s = saturate(hsi.y);
        float inten = saturate(hsi.z);
        float pi23 = 2.09439510239;
        float pi43 = 4.18879020479;
        float r, g, b;
        if (h < pi23)
        {
            b = inten * (1.0 - s);
            r = inten * (1.0 + (s * cos(h)) / max(1e-4, cos(1.0471975512 - h)));
            g = 3.0 * inten - (r + b);
        }
        else if (h < pi43)
        {
            float hp = h - pi23;
            r = inten * (1.0 - s);
            g = inten * (1.0 + (s * cos(hp)) / max(1e-4, cos(1.0471975512 - hp)));
            b = 3.0 * inten - (r + g);
        }
        else
        {
            float hp = h - pi43;
            g = inten * (1.0 - s);
            b = inten * (1.0 + (s * cos(hp)) / max(1e-4, cos(1.0471975512 - hp)));
            r = 3.0 * inten - (g + b);
        }
        return saturate(float3(r, g, b));
    }

    float4 RgbToCmyk(float3 c)
    {
        float k = 1.0 - max(c.r, max(c.g, c.b));
        float denom = 1.0 - k;
        if (denom < 1e-5) return float4(0.0, 0.0, 0.0, 1.0);
        return saturate(float4((denom - c.r) / denom, (denom - c.g) / denom, (denom - c.b) / denom, k));
    }

    float3 CmykToRgb(float4 cmyk)
    {
        float ik = 1.0 - saturate(cmyk.w);
        return saturate((1.0 - saturate(cmyk.xyz)) * ik);
    }

    float4 EncodeChannelSpace(float3 srgb, int mode)
    {
        srgb = saturate(srgb);
        if (mode <= 6)  return float4(srgb, 1.0);
        if (mode <= 10) return float4(SrgbToLinear(srgb), 1.0);
        if (mode <= 14) return float4(RgbToYCbCr(srgb), 1.0);
        if (mode == 15) return float4(RgbToYCbCr(srgb.grb), 1.0);
        if (mode <= 19) return float4(RgbToLab01(srgb), 1.0);
        if (mode <= 22) return float4(RgbToLch01(srgb), 1.0);
        if (mode <= 25) return float4(RgbToHsv(srgb), 1.0);
        if (mode == 26) return float4(RgbToHsi(srgb), 1.0);
        if (mode == 27) return float4(RgbToHsl(srgb), 1.0);
        return RgbToCmyk(srgb);
    }

    float3 DecodeAndMergeChannel(float3 origRgb, float4 procEnc, int mode)
    {
        if (mode == 6) return saturate(origRgb);

        float4 m = EncodeChannelSpace(origRgb, mode);

        if (mode == 0 || mode == 1 || mode == 2 || mode == 7)
            m.rgb = procEnc.rgb;
        else if (mode == 3 || mode == 8 || mode == 11 || mode == 16 || mode == 23 || mode == 28)
            m.r = procEnc.r;
        else if (mode == 4 || mode == 9 || mode == 13 || mode == 18 || mode == 21 || mode == 24 || mode == 29)
            m.g = procEnc.g;
        else if (mode == 5 || mode == 10 || mode == 14 || mode == 15 || mode == 19 || mode == 22 || mode == 25 || mode == 26 || mode == 27 || mode == 30)
            m.b = procEnc.b;
        else if (mode == 12 || mode == 17 || mode == 20)
            m.gb = procEnc.gb;
        else if (mode == 31)
            m.a = procEnc.a;

        if (mode <= 6)  return saturate(m.rgb);
        if (mode <= 10) return LinearToSrgb(m.rgb);
        if (mode <= 14) return YCbCrToRgb(m.rgb);
        if (mode == 15) return YCbCrToRgb(m.rgb).grb;
        if (mode <= 19) return Lab01ToRgb(m.rgb);
        if (mode <= 22) return Lch01ToRgb(m.rgb);
        if (mode <= 25) return HsvToRgb(m.rgb);
        if (mode == 26) return HsiToRgb(m.rgb);
        if (mode == 27) return HslToRgb(m.rgb);
        return CmykToRgb(m);
    }

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

        // Pass 1: Local 13x1 Horizontal Min/Max on .r -> RG
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

        // Pass 2: Local 1x13 Vertical Min/Max on .rg -> RG
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

        // Pass 7: Structure Tensor (Jxx, Jxy, Jyy)
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

        // Pass 8: Separable Tensor Gaussian Blur Horizontal
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurTensorH

            float4 fragBlurTensorH(v2f i) : SV_Target
            {
                float stepPx = max(0.5, _Sigma * 1.2);
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

        // Pass 9: Separable Tensor Gaussian Blur Vertical
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlurTensorV

            float4 fragBlurTensorV(v2f i) : SV_Target
            {
                float stepPx = max(0.5, _Sigma * 1.2);
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

        // Pass 10: Curved Streamline LIC Anisotropic Smooth
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

                float anisoNorm = saturate(_Anisotropy * 2.0);
                float baseStepPx = max(0.25, _Dl * 0.35);
                float sharpColor = _Sharpness * 0.00045;
                float sharpEdge = _Sharpness * 0.0015;

                float3 sumRgb = center.rgb;
                float sumW = 1.0;

                // Forward streamline (10 curved steps)
                float2 posF = i.uv;
                float2 curTF = t0;
                [unroll]
                for (int kf = 1; kf <= 10; kf++)
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
                    float edgeGuard = exp(-edgeJump * 0.04 * (_Sharpness * 0.01));
                    float w = exp(-0.02 * (float)(kf * kf)) * exp(-dot(dc, dc) * sharpColor) * edgeGuard;

                    sumRgb += sRgb * w;
                    sumW += w;
                }

                // Backward streamline (10 curved steps)
                float2 posB = i.uv;
                float2 curTB = -t0;
                [unroll]
                for (int kb = 1; kb <= 10; kb++)
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
                    float edgeGuard = exp(-edgeJump * 0.04 * (_Sharpness * 0.01));
                    float w = exp(-0.02 * (float)(kb * kb)) * exp(-dot(dc, dc) * sharpColor) * edgeGuard;

                    sumRgb += sRgb * w;
                    sumW += w;
                }

                // Lateral diffusion controlled by (1 - Anisotropy) and edge stopping
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

        // Pass 12: Scale a,b by _ColorMul and blur chroma horizontally (0.5%)
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

        // Pass 15: Gaussian Blur Sigma=1.0 Horizontal (for watershed)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlur1H

            float4 fragBlur1H(v2f i) : SV_Target
            {
                float tx = _MainTex_TexelSize.x * 1.4;
                float4 acc = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)) * 0.38774;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv + float2( tx, 0.0)), 0.0, 0.0)) * 0.24477;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv - float2( tx, 0.0)), 0.0, 0.0)) * 0.24477;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv + float2(2.0 * tx, 0.0)), 0.0, 0.0)) * 0.06136;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv - float2(2.0 * tx, 0.0)), 0.0, 0.0)) * 0.06136;
                return acc;
            }
            ENDCG
        }

        // Pass 16: Gaussian Blur Sigma=1.0 Vertical (for watershed)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlur1V

            float4 fragBlur1V(v2f i) : SV_Target
            {
                float ty = _MainTex_TexelSize.y;
                float4 acc = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)) * 0.38774;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0,  ty)), 0.0, 0.0)) * 0.24477;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0,  ty)), 0.0, 0.0)) * 0.24477;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, 2.0 * ty)), 0.0, 0.0)) * 0.06136;
                acc += tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, 2.0 * ty)), 0.0, 0.0)) * 0.06136;
                return acc;
            }
            ENDCG
        }

        // Pass 17: Sobel Gradient Norm
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragGradientNorm

            float4 fragGradientNorm(v2f i) : SV_Target
            {
                float3 dx, dy;
                ComputeSobelGradients(i.uv, _MainTex_TexelSize.xy, dx, dy);
                float gn = sqrt(dot(dx, dx) + dot(dy, dy));
                return float4(gn, gn, gn, 1.0);
            }
            ENDCG
        }

        // Pass 18: Watershed Seed Init (local minima below threshold + sparse flat grid seeds)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragWatershedSeedInit

            float4 fragWatershedSeedInit(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float gn = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float gL = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0)).r;
                float gR = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0)).r;
                float gD = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, tx.y)), 0.0, 0.0)).r;
                float gU = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, tx.y)), 0.0, 0.0)).r;

                float threshold = 10.0 / 255.0;
                bool strictMin = (gn < threshold) && (gn < gL) && (gn < gR) && (gn < gD) && (gn < gU);

                int2 px = (int2)floor(i.uv * _MainTex_TexelSize.zw);
                bool flatGrid = (gn < 0.002) && ((px.x % 24) == 0) && ((px.y % 24) == 0);

                if (strictMin || flatGrid)
                {
                    return float4(i.uv.x, i.uv.y, gn, 1.0);
                }
                return float4(-1.0, -1.0, 1e6, 0.0);
            }
            ENDCG
        }

        // Pass 19: Watershed Jump Flooding Step (edge-barrier geodesic cost)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragWatershedJfa

            float4 fragWatershedJfa(v2f i) : SV_Target
            {
                float2 stepUv = _MainTex_TexelSize.xy * _JfaStep;
                float2 sizePx = _MainTex_TexelSize.zw;
                float gCur = tex2Dlod(_GradTex, float4(i.uv, 0.0, 0.0)).r;

                float4 best = float4(-1.0, -1.0, 1e6, 0.0);
                float bestCost = 1e20;

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

                        float dist = length((i.uv - cand.xy) * sizePx);
                        float gMid = tex2Dlod(_GradTex, float4(0.5 * (i.uv + cand.xy), 0.0, 0.0)).r;
                        float barrier = max(gMid, 0.5 * (gCur + cand.z));
                        float cost = dist + barrier * 320.0 + cand.z * 25.0;

                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = cand;
                        }
                    }
                }

                return best;
            }
            ENDCG
        }

        // Pass 20: Watershed Resolve (sample blurred image at winning seed UV)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragWatershedResolve

            float4 fragWatershedResolve(v2f i) : SV_Target
            {
                float4 seed = tex2Dlod(_JfaTex, float4(i.uv, 0.0, 0.0));
                if (seed.a > 0.5)
                {
                    return tex2Dlod(_MainTex, float4(saturate(seed.xy), 0.0, 0.0));
                }
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }

        // Pass 21: Encode sRGB -> Target Channel Space
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragEncodeChannelSpace

            float4 fragEncodeChannelSpace(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                return EncodeChannelSpace(src.rgb, _ChannelMode);
            }
            ENDCG
        }

        // Pass 22: Morpho Square Horizontal (width _MorphoSize)
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

        // Pass 23: Morpho Square Vertical (height _MorphoSize)
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

        // Pass 24: Morpho 3x3 Diamond (cross) step for octagonal morphology
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

        // Pass 25: Morpho Combine Action (0..7) + Optional Invert
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
                    // Original - Erosion / Original - Opening
                    res = saturate(orig - proc);
                }
                else if (_MorphoAction == 5 || _MorphoAction == 7)
                {
                    // Dilation - Original / Closing - Original
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

        // Pass 26: Decode & Merge Active Channel Back to sRGB
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragDecodeAndMerge

            float4 fragDecodeAndMerge(v2f i) : SV_Target
            {
                float4 proc = tex2D(_MainTex, i.uv);
                float4 orig = tex2D(_BaseTex, i.uv);
                return float4(DecodeAndMergeChannel(orig.rgb, proc, _ChannelMode), orig.a);
            }
            ENDCG
        }

        // Pass 27: Deterministic Pixel Spread
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