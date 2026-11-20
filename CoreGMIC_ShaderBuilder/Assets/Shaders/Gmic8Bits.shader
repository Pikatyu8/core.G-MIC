Shader "CoreGMIC/Oldschool8Bits"
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

    StructuredBuffer<uint> _StatsBuf;
    StructuredBuffer<uint> _ChannelStatsBuf;

    float _Dithering;
    float _Levels;

    float QuantizeChannel(float v, float minV, float maxV, float lv)
    {
        float range = maxV - minV;
        if (range <= 1e-6)
        {
            return v;
        }

        float t = saturate((v - minV) / range);
        float q = min(lv - 1.0, round(t * lv));
        return minV + (q / (lv - 1.0)) * range;
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: G'MIC Luminance
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragLuminance

            float4 fragLuminance(v2f i) : SV_Target
            {
                float lum = GmicLuminance(tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb);
                return float4(lum, lum, lum, 1.0);
            }
            ENDCG
        }

        // Pass 1: G'MIC Sharpen (inverse diffusion Laplacian) clamped to [minLum, maxLum]
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragSharpen

            float4 fragSharpen(v2f i) : SV_Target
            {
                float c = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                if (_Dithering <= 0.0001)
                {
                    return float4(c, c, c, 1.0);
                }

                float minLum = asfloat(_StatsBuf[0]);
                float maxLum = asfloat(_StatsBuf[1]);
                float vM = max(abs(minLum), abs(maxLum));
                if (vM <= 1e-8)
                {
                    return float4(c, c, c, 1.0);
                }

                float2 tx = _MainTex_TexelSize.xy;
                float l = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0)).r;
                float r = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0)).r;
                float d = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, tx.y)), 0.0, 0.0)).r;
                float u = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, tx.y)), 0.0, 0.0)).r;

                float lap = l + r + d + u - 4.0 * c;
                float val = clamp(c - (_Dithering / vM) * lap, minLum, maxLum);
                return float4(val, val, val, 1.0);
            }
            ENDCG
        }

        // Pass 2: Binary Otsu Mask (0 or 1)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragOtsuMask

            float4 fragOtsuMask(v2f i) : SV_Target
            {
                float v = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float thresh = asfloat(_StatsBuf[2]);
                float lbl = (v >= thresh) ? 1.0 : 0.0;
                return float4(lbl, lbl, lbl, 1.0);
            }
            ENDCG
        }

        // Pass 3: Per-Channel Uniform Quantize + Nearest Upscale + Original Alpha
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragQuantizeUpscale

            float4 fragQuantizeUpscale(v2f i) : SV_Target
            {
                float3 rgb = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb;

                float minR = asfloat(_ChannelStatsBuf[0]);
                float minG = asfloat(_ChannelStatsBuf[1]);
                float minB = asfloat(_ChannelStatsBuf[2]);
                float maxR = asfloat(_ChannelStatsBuf[3]);
                float maxG = asfloat(_ChannelStatsBuf[4]);
                float maxB = asfloat(_ChannelStatsBuf[5]);

                float lv = max(2.0, _Levels);
                float3 outRgb = float3(
                    QuantizeChannel(rgb.r, minR, maxR, lv),
                    QuantizeChannel(rgb.g, minG, maxG, lv),
                    QuantizeChannel(rgb.b, minB, maxB, lv));

                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(saturate(outRgb), alpha);
            }
            ENDCG
        }
    }
}