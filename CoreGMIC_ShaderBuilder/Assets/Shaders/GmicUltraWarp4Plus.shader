Shader "CoreGMIC/UltraWarp4Plus"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicColorSpaces.cginc"

    sampler2D _MainTex;
    sampler2D _PlasmaTex;

    StructuredBuffer<uint> _StatsBuf;

    float _Gamma;
    float _HueShiftDeg;
    float _UseStats;

    float3 _HsvMul;
    float3 _HsvAdd;
    float _PlasmaAlpha;
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: G'MIC negate
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragNegate

            float4 fragNegate(v2f i) : SV_Target
            {
                return 1.0 - saturate(tex2D(_MainTex, i.uv));
            }
            ENDCG
        }

        // Pass 3: G'MIC fx_adjust_colors (Hue shift + Gamma within [minV, maxV])
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragAdjustColors

            float4 fragAdjustColors(v2f i) : SV_Target
            {
                float4 src = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float minV = 0.0;
                float maxV = 1.0;

                if (_UseStats > 0.5)
                {
                    minV = asfloat(_StatsBuf[0]);
                    maxV = asfloat(_StatsBuf[1]);
                }

                float range = maxV - minV;
                if (range <= 1e-5)
                {
                    return src;
                }

                float3 rgb = saturate((src.rgb - minV) / range);

                if (abs(_HueShiftDeg) > 1e-4)
                {
                    float3 hsv = RgbToHsv(rgb);
                    hsv.x = frac(hsv.x + _HueShiftDeg / 360.0);
                    rgb = HsvToRgb(hsv);
                }

                if (abs(_Gamma - 1.0) > 1e-4)
                {
                    rgb = pow(max(rgb, 0.0), _Gamma);
                }

                float3 outRgb = clamp(rgb * range + minV, minV, maxV);
                return float4(outRgb, src.a);
            }
            ENDCG
        }

        // Pass 4: G'MIC fx_mix_hsv
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMixHsv

            float4 fragMixHsv(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 hsv = RgbToHsv(saturate(src.rgb));

                hsv.x = frac(hsv.x * _HsvMul.x + _HsvAdd.x / 360.0);
                hsv.y = saturate((hsv.y - 0.5) * _HsvMul.y + _HsvAdd.y + 0.5);
                hsv.z = saturate((hsv.z - 0.5) * _HsvMul.z + _HsvAdd.z + 0.5);

                return float4(HsvToRgb(hsv), src.a);
            }
            ENDCG
        }

        // Pass 5: Replace image with Plasma Texture (optionally keeping input alpha)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragPlasmaReplace

            float4 fragPlasmaReplace(v2f i) : SV_Target
            {
                float4 pls = tex2D(_PlasmaTex, i.uv);
                float alpha = (_PlasmaAlpha > 0.5) ? pls.a : tex2D(_MainTex, i.uv).a;
                return float4(pls.rgb, alpha);
            }
            ENDCG
        }
    }
}
