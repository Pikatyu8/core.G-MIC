Shader "CoreGMIC/Edges"
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
    StructuredBuffer<float> _CdfBuffer;

    float _ThresholdPct;
    float _NegativeColors;
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: G'MIC gradient_norm (scheme=0 centered, boundary=1 Neumann)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragGradientNorm

            float4 fragGradientNorm(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float3 xP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0)).rgb;
                float3 xM = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0)).rgb;
                float3 yP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, tx.y)), 0.0, 0.0)).rgb;
                float3 yM = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, tx.y)), 0.0, 0.0)).rgb;

                float3 dx = 0.5 * (xP - xM);
                float3 dy = 0.5 * (yP - yM);
                float gn = sqrt(dot(dx, dx) + dot(dy, dy));

                return float4(gn, gn, gn, 1.0);
            }
            ENDCG
        }

        // Pass 1: Threshold (>= Threshold%) and Init JFA Seeds for distance 0
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragThresholdAndSeed

            float4 fragThresholdAndSeed(v2f i) : SV_Target
            {
                float minG = asfloat(_StatsBuf[0]);
                float maxG = asfloat(_StatsBuf[1]);
                float thresh = minG + saturate(_ThresholdPct * 0.01) * (maxG - minG);

                float val = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                if (val < thresh)
                {
                    return float4(i.uv.x, i.uv.y, 0.0, 1.0);
                }

                return float4(0.0, 0.0, 0.0, 0.0);
            }
            ENDCG
        }

        // Pass 2: Equalize -> Negate -> Cut [30%, 70%] -> Normalize [0, 1] -> Optional Negative
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragEdgesFinal

            float4 fragEdgesFinal(v2f i) : SV_Target
            {
                float dist = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float minD = asfloat(_StatsBuf[0]);
                float maxD = asfloat(_StatsBuf[1]);
                float range = maxD - minD;

                float val = 0.0;
                if (range > 1e-5)
                {
                    float norm = saturate((dist - minD) / range);
                    uint bin = min(255u, (uint)floor(norm * 255.0));

                    float eqVal = _CdfBuffer[bin];
                    float minEq = _CdfBuffer[0];
                    float eqRange = 1.0 - minEq;

                    if (eqRange > 1e-6)
                    {
                        float tNeg = 1.0 - saturate((eqVal - minEq) / eqRange);
                        val = saturate((tNeg - 0.30) / 0.40);
                    }
                }

                if (_NegativeColors > 0.5)
                {
                    val = 1.0 - val;
                }

                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(val, val, val, alpha);
            }
            ENDCG
        }
    }
}