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

        // Pass 1: gradient_norm
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragGradientNorm

            float4 fragGradientNorm(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float3 cxP = tex2D(_MainTex, saturate(i.uv + float2(tx.x, 0.0))).rgb;
                float3 cxM = tex2D(_MainTex, saturate(i.uv - float2(tx.x, 0.0))).rgb;
                float3 cyP = tex2D(_MainTex, saturate(i.uv + float2(0.0, tx.y))).rgb;
                float3 cyM = tex2D(_MainTex, saturate(i.uv - float2(0.0, tx.y))).rgb;

                float3 dx = 0.5 * (cxP - cxM);
                float3 dy = 0.5 * (cyP - cyM);
                float gn = sqrt(dot(dx, dx) + dot(dy, dy));

                return float4(gn, gn, gn, 1.0);
            }
            ENDCG
        }

        // Pass 2: Normalize [0, 1] + Exact Compute Histogram CDF Equalize (Variant B)
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

                float3 eqRgb = float3(
                    SampleCdf(normRgb.r),
                    SampleCdf(normRgb.g),
                    SampleCdf(normRgb.b)
                );

                return float4(eqRgb, src.a);
            }
            ENDCG
        }

        // Pass 3: Engrave Crisp Ridge Sharpen + Clamp
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragEngraveSharpen

            float4 fragEngraveSharpen(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy * 0.5;
                float center = tex2D(_MainTex, i.uv).r;

                float blur = center * 0.4;
                blur += tex2D(_MainTex, saturate(i.uv + float2(-tx.x, 0.0))).r * 0.15;
                blur += tex2D(_MainTex, saturate(i.uv + float2( tx.x, 0.0))).r * 0.15;
                blur += tex2D(_MainTex, saturate(i.uv + float2(0.0, -tx.y))).r * 0.15;
                blur += tex2D(_MainTex, saturate(i.uv + float2(0.0,  tx.y))).r * 0.15;

                float sharp = saturate(center + 3.0 * (center - blur));
                return float4(sharp, sharp, sharp, 1.0);
            }
            ENDCG
        }

        // Pass 4: Engrave Threshold (>= 60%) + Add onto Base
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragEngraveThresholdAndAdd

            float4 fragEngraveThresholdAndAdd(v2f i) : SV_Target
            {
                float val = tex2D(_MainTex, i.uv).r;
                float engraveMask = smoothstep(0.56, 0.64, val) * 0.45;
                float4 baseCol = tex2D(_BaseTex, i.uv);

                return float4(saturate(baseCol.rgb + engraveMask), baseCol.a);
            }
            ENDCG
        }

        // Pass 5: RGB Structure Tensor (Jxx, Jxy, Jyy)
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragStructureTensor

            float4 fragStructureTensor(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float3 cxP = tex2D(_MainTex, saturate(i.uv + float2(tx.x, 0.0))).rgb;
                float3 cxM = tex2D(_MainTex, saturate(i.uv - float2(tx.x, 0.0))).rgb;
                float3 cyP = tex2D(_MainTex, saturate(i.uv + float2(0.0, tx.y))).rgb;
                float3 cyM = tex2D(_MainTex, saturate(i.uv - float2(0.0, tx.y))).rgb;

                float3 dx = 0.5 * (cxP - cxM);
                float3 dy = 0.5 * (cyP - cyM);

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
                float stepPx = max(0.25, _Sigma * 0.5);
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
                float stepPx = max(0.25, _Sigma * 0.5);
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

        // Pass 8: Anisotropic Paint-Daub LIC Smooth
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragAnisotropicSmooth

            float4 fragAnisotropicSmooth(v2f i) : SV_Target
            {
                float4 center = tex2D(_MainTex, i.uv);
                float4 tensor = tex2D(_TensorTex, i.uv);

                float jxx = tensor.r;
                float jxy = tensor.g;
                float jyy = tensor.b;

                float trace = jxx + jyy;
                float diff = jxx - jyy;
                float angleRad = 0.5 * atan2(2.0 * jxy, diff) + 1.57079632679;

                float sA, cA;
                sincos(angleRad, sA, cA);
                float2 tVec = float2(cA, sA) * _MainTex_TexelSize.xy;
                float2 nVec = float2(-sA, cA) * _MainTex_TexelSize.xy;

                float edgeStrength = trace * 255.0;
                float sharpExp = _Sharpness * 0.012;
                float edgeStop = pow(1.0 + edgeStrength, -sharpExp);

                float anisoNorm = saturate(_Anisotropy * 2.0);
                float lenT = _Dl * 0.5 * (0.6 + 0.8 * anisoNorm);
                float lenN = _Dl * 0.5 * lerp(1.0, 0.08, anisoNorm) * edgeStop;

                float bilateralSharp = _Sharpness * 0.08;

                float3 sumRgb = float3(0.0, 0.0, 0.0);
                float sumW = 0.0;

                [unroll]
                for (int ny = -1; ny <= 1; ny++)
                {
                    [unroll]
                    for (int tx = -2; tx <= 2; tx++)
                    {
                        float2 offset = tVec * ((float)tx * lenT) + nVec * ((float)ny * lenN);
                        float2 suv = saturate(i.uv + offset);
                        float3 sRgb = tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).rgb;

                        float spatialW = exp(-0.35 * (float)(tx * tx) - 0.8 * (float)(ny * ny));
                        float3 dc = sRgb - center.rgb;
                        float rangeW = exp(-dot(dc, dc) * bilateralSharp);

                        float w = spatialW * rangeW;
                        sumRgb += sRgb * w;
                        sumW += w;
                    }
                }

                float3 result = (sumW > 1e-5) ? (sumRgb / sumW) : center.rgb;
                return float4(result, center.a);
            }
            ENDCG
        }
    }
}