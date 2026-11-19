Shader "CoreGMIC/GradientNorm"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;

    StructuredBuffer<uint> _StatsBuf;

    float _Linearity;
    float _MinThreshold;
    float _MaxThreshold;
    float _NegativeColors;
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: G'MIC gradient_norm (scheme=0 centered, boundary=1 Neumann) ^ Linearity
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragGradientNormPow

            float4 fragGradientNormPow(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float3 xP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0)).rgb;
                float3 xM = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0)).rgb;
                float3 yP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, tx.y)), 0.0, 0.0)).rgb;
                float3 yM = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, tx.y)), 0.0, 0.0)).rgb;

                float3 dx = 0.5 * (xP - xM);
                float3 dy = 0.5 * (yP - yM);
                float gn = sqrt(dot(dx, dx) + dot(dy, dy));

                float val = (_Linearity <= 0.0001)
                    ? 1.0
                    : ((gn > 0.0) ? pow(gn, _Linearity) : 0.0);

                return float4(val, val, val, 1.0);
            }
            ENDCG
        }

        // Pass 1: Cut [MinThreshold%, MaxThreshold%] -> Normalize [0, 1] -> Optional Negative
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragCutAndNormalize

            float4 fragCutAndNormalize(v2f i) : SV_Target
            {
                float val = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float minG = asfloat(_StatsBuf[0]);
                float maxG = asfloat(_StatsBuf[1]);
                float range = maxG - minG;

                float lowPct = saturate(min(_MinThreshold, _MaxThreshold) * 0.01);
                float highPct = saturate(max(_MinThreshold, _MaxThreshold) * 0.01);

                float lowCut = minG + lowPct * range;
                float highCut = minG + highPct * range;
                float cutRange = highCut - lowCut;

                float outVal = 0.0;
                if (cutRange > 1e-6)
                {
                    outVal = saturate((clamp(val, lowCut, highCut) - lowCut) / cutRange);
                    if (_NegativeColors > 0.5)
                    {
                        outVal = 1.0 - outVal;
                    }
                }

                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(outVal, outVal, outVal, alpha);
            }
            ENDCG
        }
    }
}