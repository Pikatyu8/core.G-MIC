Shader "CoreGMIC/Barbouillage"
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

    sampler2D _PlasmaTex;
    sampler2D _CdfTex;
    sampler2D _BaseTex;
    sampler2D _TensorTex;

    int _PlasmaMode;
    float _DoEqualize;
    float _FinalPass;
    float _Sigma;
    float _Sharpness;
    float _Anisotropy;
    float _Dl;

    float3 RgbToHsl(float3 c)
    {
        float cmax = max(c.r, max(c.g, c.b));
        float cmin = min(c.r, min(c.g, c.b));
        float d = cmax - cmin;
        float l = 0.5 * (cmax + cmin);
        float s = (d < 1e-5) ? 0.0 : d / max(1e-5, 1.0 - abs(2.0 * l - 1.0));

        float h = 0.0;
        if (d >= 1e-5)
        {
            if (cmax == c.r)
            {
                h = ((c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0)) / 6.0;
            }
            else if (cmax == c.g)
            {
                h = ((c.b - c.r) / d + 2.0) / 6.0;
            }
            else
            {
                h = ((c.r - c.g) / d + 4.0) / 6.0;
            }
        }

        return float3(frac(h), saturate(s), saturate(l));
    }

    float3 HslToRgb(float3 hsl)
    {
        float h = frac(hsl.x) * 6.0;
        float s = saturate(hsl.y);
        float l = saturate(hsl.z);

        float c = (1.0 - abs(2.0 * l - 1.0)) * s;
        float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
        float m = l - 0.5 * c;

        float3 rgb;
        if (h < 1.0)      rgb = float3(c, x, 0.0);
        else if (h < 2.0) rgb = float3(x, c, 0.0);
        else if (h < 3.0) rgb = float3(0.0, c, x);
        else if (h < 4.0) rgb = float3(0.0, x, c);
        else if (h < 5.0) rgb = float3(x, 0.0, c);
        else              rgb = float3(c, 0.0, x);

        return saturate(rgb + m);
    }

    void ComputeSobelGradients(float2 uv, float2 tx, out float3 dx, out float3 dy)
    {
        float3 tl = tex2Dlod(_MainTex, float4(saturate(uv + float2(-tx.x,  tx.y)), 0.0, 0.0)).rgb;
        float3 tm = tex2Dlod(_MainTex, float4(saturate(uv + float2(  0.0,  tx.y)), 0.0, 0.0)).rgb;
        float3 tr = tex2Dlod(_MainTex, float4(saturate(uv + float2( tx.x,  tx.y)), 0.0, 0.0)).rgb;
        float3 ml = tex2Dlod(_MainTex, float4(saturate(uv + float2(-tx.x,   0.0)), 0.0, 0.0)).rgb;
        float3 mr = tex2Dlod(_MainTex, float4(saturate(uv + float2( tx.x,   0.0)), 0.0, 0.0)).rgb;
        float3 bl = tex2Dlod(_MainTex, float4(saturate(uv + float2(-tx.x, -tx.y)), 0.0, 0.0)).rgb;
        float3 bm = tex2Dlod(_MainTex, float4(saturate(uv + float2(  0.0, -tx.y)), 0.0, 0.0)).rgb;
        float3 br = tex2Dlod(_MainTex, float4(saturate(uv + float2( tx.x, -tx.y)), 0.0, 0.0)).rgb;

        dx = ((tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl)) * 0.125;
        dy = ((tl + 2.0 * tm + tr) - (bl + 2.0 * bm + br)) * 0.125;
    }

    float2 GetTangentAndEdge(float2 uv, out float edgeEnergy)
    {
        float4 t = tex2Dlod(_TensorTex, float4(uv, 0.0, 0.0));
        float jxx = t.r;
        float jxy = t.g;
        float jyy = t.b;
        edgeEnergy = (jxx + jyy) * 255.0;

        if (edgeEnergy < 1e-4)
        {
            return float2(1.0, 0.0);
        }

        float angle = 0.5 * atan2(2.0 * jxy, jxx - jyy) + 1.57079632679;
        float sA, cA;
        sincos(angle, sA, cA);
        return float2(cA, sA);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Plasma HSL Channel Remix
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragPlasmaBlend

            float4 fragPlasmaBlend(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 plasma = tex2D(_PlasmaTex, i.uv).rgb;

                float3 srcHsl = RgbToHsl(saturate(src.rgb));
                float3 plsHsl = RgbToHsl(saturate(plasma));

                float3 outHsl;
                if (_PlasmaMode == 1)
                {
                    outHsl = float3(srcHsl.x, srcHsl.y, plsHsl.z);
                }
                else
                {
                    outHsl = float3(srcHsl.z, plsHsl.x, plsHsl.y);
                }

                return float4(HslToRgb(outHsl), src.a);
            }
            ENDCG
        }

        // Pass 1: Isotropic 3x3 Sobel gradient_norm
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
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

        // Pass 2: Normalize [0, 1] + Luminance CDF Equalize
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragNormalizeEqualize

            float SampleCdf(float v)
            {
                float u = (0.5 + saturate(v) * 255.0) / 256.0;
                return tex2Dlod(_CdfTex, float4(u, 0.5, 0.0, 0.0)).r;
            }

            float4 fragNormalizeEqualize(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float4 meta = tex2Dlod(_CdfTex, float4(0.5 / 256.0, 0.5, 0.0, 0.0));

                float minV = meta.g;
                float maxV = meta.b;
                float range = maxV - minV;

                if (range < 1e-5)
                {
                    return float4(0.0, 0.0, 0.0, src.a);
                }

                float3 normRgb = saturate((src.rgb - minV) / range);
                if (_DoEqualize < 0.5)
                {
                    return float4(normRgb, src.a);
                }

                float lum = dot(normRgb, float3(0.299, 0.587, 0.114));
                float eqLum = SampleCdf(lum);

                float3 eqRgb;
                if (lum > 1e-4)
                {
                    float scale = min(eqLum / lum, 2.5);
                    eqRgb = saturate(normRgb * scale);
                }
                else
                {
                    eqRgb = float3(eqLum, eqLum, eqLum);
                }

                if (_FinalPass > 0.5)
                {
                    float voidKeep = smoothstep(0.03, 0.16, lum);
                    eqRgb = lerp(normRgb, eqRgb, 0.42 * voidKeep);
                    eqRgb = pow(max(eqRgb, 1e-5), 1.18);
                }

                return float4(eqRgb, src.a);
            }
            ENDCG
        }

        // Pass 3: Strong Isotropic 3x3 Gaussian Engrave Sharpen + Clamp
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragEngraveSharpen

            float4 fragEngraveSharpen(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float center = tex2D(_MainTex, i.uv).r;

                float blur = center * 0.25;
                blur += tex2D(_MainTex, saturate(i.uv + float2(-tx.x,  0.0))).r * 0.125;
                blur += tex2D(_MainTex, saturate(i.uv + float2( tx.x,  0.0))).r * 0.125;
                blur += tex2D(_MainTex, saturate(i.uv + float2( 0.0, -tx.y))).r * 0.125;
                blur += tex2D(_MainTex, saturate(i.uv + float2( 0.0,  tx.y))).r * 0.125;
                blur += tex2D(_MainTex, saturate(i.uv + float2(-tx.x, -tx.y))).r * 0.0625;
                blur += tex2D(_MainTex, saturate(i.uv + float2( tx.x, -tx.y))).r * 0.0625;
                blur += tex2D(_MainTex, saturate(i.uv + float2(-tx.x,  tx.y))).r * 0.0625;
                blur += tex2D(_MainTex, saturate(i.uv + float2( tx.x,  tx.y))).r * 0.0625;

                float sharp = saturate(center + 3.2 * (center - blur));
                return float4(sharp, sharp, sharp, 1.0);
            }
            ENDCG
        }

        // Pass 4: Highlight-Aware Engrave Threshold + Add onto Base
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragEngraveThresholdAndAdd

            float4 fragEngraveThresholdAndAdd(v2f i) : SV_Target
            {
                float val = tex2D(_MainTex, i.uv).r;
                float engraveMask = smoothstep(0.55, 0.72, val);
                float4 baseCol = tex2D(_BaseTex, i.uv);
                float baseLum = dot(baseCol.rgb, float3(0.299, 0.587, 0.114));
                float addStrength = 0.52 * (1.0 - smoothstep(0.45, 0.95, baseLum) * 0.75);

                return float4(saturate(baseCol.rgb + engraveMask * addStrength), baseCol.a);
            }
            ENDCG
        }

        // Pass 5: Isotropic 3x3 Sobel Structure Tensor (Jxx, Jxy, Jyy)
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
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

        // Pass 6: Separable Tensor Gaussian Blur Horizontal
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragBlurTensorH

            float4 fragBlurTensorH(v2f i) : SV_Target
            {
                float stepPx = max(0.85, _Sigma * 1.8);
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

        // Pass 7: Separable Tensor Gaussian Blur Vertical
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragBlurTensorV

            float4 fragBlurTensorV(v2f i) : SV_Target
            {
                float stepPx = max(0.85, _Sigma * 1.8);
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

        // Pass 8: Bold 16-Step Curved Streamline LIC Anisotropic Smooth
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragAnisotropicSmooth

            float4 fragAnisotropicSmooth(v2f i) : SV_Target
            {
                float4 center = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float centerEdge;
                float2 t0 = GetTangentAndEdge(i.uv, centerEdge);
                float2 n0 = float2(-t0.y, t0.x);

                float anisoNorm = saturate(_Anisotropy * 2.0);
                float baseStepPx = max(0.25, _Dl * 0.34);
                float sharpColor = _Sharpness * 0.0010;
                float sharpEdge = _Sharpness * 0.004;

                float3 sumRgb = center.rgb;
                float sumW = 1.0;

                // Forward streamline (16 curved steps)
                float2 posF = i.uv;
                float2 curTF = t0;
                [unroll]
                for (int kf = 1; kf <= 16; kf++)
                {
                    float stepEdge;
                    float2 nextT = GetTangentAndEdge(posF, stepEdge);
                    if (dot(nextT, curTF) < 0.0) nextT = -nextT;
                    curTF = nextT;

                    float brake = pow(1.0 + stepEdge, -sharpEdge);
                    posF = saturate(posF + curTF * (_MainTex_TexelSize.xy * baseStepPx * lerp(1.0, brake, 0.25)));

                    float3 sRgb = tex2Dlod(_MainTex, float4(posF, 0.0, 0.0)).rgb;
                    float3 dc = sRgb - center.rgb;
                    float edgeJump = abs(stepEdge - centerEdge);
                    float edgeGuard = exp(-edgeJump * 0.04 * (_Sharpness * 0.01));
                    float w = exp(-0.009 * (float)(kf * kf)) * exp(-dot(dc, dc) * sharpColor) * edgeGuard;

                    sumRgb += sRgb * w;
                    sumW += w;
                }

                // Backward streamline (16 curved steps)
                float2 posB = i.uv;
                float2 curTB = -t0;
                [unroll]
                for (int kb = 1; kb <= 16; kb++)
                {
                    float stepEdge;
                    float2 nextT = GetTangentAndEdge(posB, stepEdge);
                    if (dot(nextT, curTB) < 0.0) nextT = -nextT;
                    curTB = nextT;

                    float brake = pow(1.0 + stepEdge, -sharpEdge);
                    posB = saturate(posB + curTB * (_MainTex_TexelSize.xy * baseStepPx * lerp(1.0, brake, 0.25)));

                    float3 sRgb = tex2Dlod(_MainTex, float4(posB, 0.0, 0.0)).rgb;
                    float3 dc = sRgb - center.rgb;
                    float edgeJump = abs(stepEdge - centerEdge);
                    float edgeGuard = exp(-edgeJump * 0.04 * (_Sharpness * 0.01));
                    float w = exp(-0.009 * (float)(kb * kb)) * exp(-dot(dc, dc) * sharpColor) * edgeGuard;

                    sumRgb += sRgb * w;
                    sumW += w;
                }

                // Lateral diffusion controlled by (1 - Anisotropy) and edge stopping
                float lateralScale = (1.0 - anisoNorm) * pow(1.0 + centerEdge, -sharpEdge * 2.0);
                if (lateralScale > 0.02)
                {
                    float2 latOff = n0 * (_MainTex_TexelSize.xy * baseStepPx * 1.5 * lateralScale);
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
    }
}