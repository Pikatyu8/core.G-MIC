Shader "CoreGMIC/Isophotes"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #define GMIC_USE_LUMINANCE 1
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;

    float _Levels;

    int GetBand(float lum, int levels)
    {
        if (levels <= 1)
        {
            return (lum >= 0.5) ? 1 : 0;
        }
        return clamp((int)floor(saturate(lum) * (float)levels), 0, levels - 1);
    }

    float3 SampleClampedRgb(int2 px, int2 maxPx, float2 tx)
    {
        int2 cpx = clamp(px, int2(0, 0), maxPx);
        float2 uv = (float2(cpx) + 0.5) * tx;
        return tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).rgb;
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Elevation Band Labels (for Colors / topographic_map mode)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBandLabels

            float4 fragBandLabels(v2f i) : SV_Target
            {
                int levels = max(1, (int)round(_Levels));
                if (levels == 1)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float lum = GmicLuminance(tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb);
                float band = (float)clamp((int)floor(saturate(lum) * (float)levels), 0, levels - 1);
                return float4(band, band, band, 1.0);
            }
            ENDCG
        }

        // Pass 1: Border-Free Colored Isolines (for Transparent mode)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragIsolines

            float4 fragIsolines(v2f i) : SV_Target
            {
                int levels = max(1, (int)round(_Levels));
                float2 tx = _MainTex_TexelSize.xy;
                int2 sizePx = max(int2(1, 1), (int2)round(_MainTex_TexelSize.zw));
                int2 maxPx = sizePx - int2(1, 1);
                int2 px = clamp((int2)floor(saturate(i.uv) * _MainTex_TexelSize.zw), int2(0, 0), maxPx);

                float3 rgbC = SampleClampedRgb(px, maxPx, tx);
                int bC = GetBand(GmicLuminance(rgbC), levels);
                int bL = GetBand(GmicLuminance(SampleClampedRgb(px + int2(-1,  0), maxPx, tx)), levels);
                int bR = GetBand(GmicLuminance(SampleClampedRgb(px + int2( 1,  0), maxPx, tx)), levels);
                int bD = GetBand(GmicLuminance(SampleClampedRgb(px + int2( 0, -1), maxPx, tx)), levels);
                int bU = GetBand(GmicLuminance(SampleClampedRgb(px + int2( 0,  1), maxPx, tx)), levels);

                bool onIso = (bC > bL) || (bC > bR) || (bC > bD) || (bC > bU);
                if (!onIso)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(rgbC, alpha);
            }
            ENDCG
        }
    }
}