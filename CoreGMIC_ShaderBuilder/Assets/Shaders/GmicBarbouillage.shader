Shader "CoreGMIC/Barbouillage"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    sampler2D _PlasmaTex;
    sampler2D _CdfTex;
    sampler2D _BaseTex;

    int _PlasmaMode;
    float _DoEqualize;
    float _FinalPass;

    #define GMIC_USE_SOBEL 1
    #include "GmicCommon.cginc"
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

    }
}