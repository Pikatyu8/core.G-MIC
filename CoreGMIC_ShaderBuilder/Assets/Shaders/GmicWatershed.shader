Shader "CoreGMIC/Watershed"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _GradTex;
    sampler2D _JfaTex;

    float _EdgeThreshold;
    float _JfaStep;

    #define GMIC_USE_SOBEL 1
    #include "GmicCommon.cginc"
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Sobel Gradient Norm
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

        // Pass 1: Watershed Seed Init (local minima below threshold + sparse flat grid seeds)
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

                float threshold = max(1e-5, _EdgeThreshold / 255.0);
                bool strictMin = (gn < threshold) && (gn < gL) && (gn < gR) && (gn < gD) && (gn < gU);

                uint2 px = (uint2)max(float2(0.0, 0.0), floor(i.uv * _MainTex_TexelSize.zw));
                bool flatGrid = (gn < 0.002) && ((px.x % 24u) == 0u) && ((px.y % 24u) == 0u);

                if (strictMin || flatGrid)
                {
                    return float4(i.uv.x, i.uv.y, gn, 1.0);
                }
                return float4(-1.0, -1.0, 1e6, 0.0);
            }
            ENDCG
        }

        // Pass 2: Watershed Jump Flooding Step (edge-barrier geodesic cost)
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

        // Pass 3: Watershed Resolve (sample blurred image at winning seed UV)
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
    }
}
