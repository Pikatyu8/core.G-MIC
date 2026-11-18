Shader "CoreGMIC/Cutout"
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
    sampler2D _JfaTex;

    StructuredBuffer<float4> _Centroids;
    StructuredBuffer<uint> _StatsBuf;

    int _MedianSize;
    int _NumLevels;
    float _JfaStep;

    float SortAndPickMedian(inout float arr[10], int count)
    {
        for (int a = 1; a < 10; a++)
        {
            if (a >= count)
                break;

            float key = arr[a];
            int b = a - 1;
            for (int step = 0; step < 9; step++)
            {
                if (b < 0 || arr[b] <= key)
                    break;
                arr[b + 1] = arr[b];
                b--;
            }
            arr[b + 1] = key;
        }

        int mid = count / 2;
        if ((count & 1) == 0 && mid > 0)
        {
            return 0.5 * (arr[mid - 1] + arr[mid]);
        }
        return arr[mid];
    }

    float4 RunMedian1D(float2 uv, float2 axisStep)
    {
        int n = clamp(_MedianSize, 1, 10);
        if (n <= 1)
        {
            return tex2Dlod(_MainTex, float4(uv, 0.0, 0.0));
        }

        float arrR[10];
        float arrG[10];
        float arrB[10];

        int rLo = -(n / 2);
        for (int k = 0; k < 10; k++)
        {
            if (k < n)
            {
                float2 suv = saturate(uv + axisStep * (float)(rLo + k));
                float3 rgb = tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).rgb;
                arrR[k] = rgb.r;
                arrG[k] = rgb.g;
                arrB[k] = rgb.b;
            }
            else
            {
                arrR[k] = 0.0;
                arrG[k] = 0.0;
                arrB[k] = 0.0;
            }
        }

        float outR = SortAndPickMedian(arrR, n);
        float outG = SortAndPickMedian(arrG, n);
        float outB = SortAndPickMedian(arrB, n);
        float alpha = tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).a;

        return float4(outR, outG, outB, alpha);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Separable 1D Median Horizontal
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragMedianH

            float4 fragMedianH(v2f i) : SV_Target
            {
                return RunMedian1D(i.uv, float2(_MainTex_TexelSize.x, 0.0));
            }
            ENDCG
        }

        // Pass 1: Separable 1D Median Vertical
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragMedianV

            float4 fragMedianV(v2f i) : SV_Target
            {
                return RunMedian1D(i.uv, float2(0.0, _MainTex_TexelSize.y));
            }
            ENDCG
        }

        // Pass 2: Palette Quantization (outputs quantized RGB + encoded cluster index in alpha)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragQuantize

            float4 fragQuantize(v2f i) : SV_Target
            {
                float3 rgb = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb;
                int count = clamp(_NumLevels, 2, 32);

                int bestIdx = 0;
                float bestDist = 1e20;
                float3 bestColor = _Centroids[0].rgb;

                for (int k = 0; k < 32; k++)
                {
                    if (k >= count)
                        break;

                    float3 c = _Centroids[k].rgb;
                    float3 diff = rgb - c;
                    float d2 = dot(diff, diff);
                    if (d2 < bestDist)
                    {
                        bestDist = d2;
                        bestIdx = k;
                        bestColor = c;
                    }
                }

                return float4(bestColor, ((float)bestIdx + 0.5) / 32.0);
            }
            ENDCG
        }

        // Pass 3: Jump Flooding Step for Hole Inpainting
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragJfaStep

            float4 fragJfaStep(v2f i) : SV_Target
            {
                float2 stepUv = _MainTex_TexelSize.xy * _JfaStep;
                float2 sizePx = _MainTex_TexelSize.zw;

                float4 best = float4(0.0, 0.0, 0.0, 0.0);
                float bestDistSq = 1e20;

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

                        float2 diffPx = (i.uv - cand.xy) * sizePx;
                        float d2 = dot(diffPx, diffPx);
                        if (d2 < bestDistSq)
                        {
                            bestDistSq = d2;
                            best = cand;
                        }
                    }
                }

                return best;
            }
            ENDCG
        }

        // Pass 4: Resolve Inpainted Cutout Image
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragResolve

            float4 fragResolve(v2f i) : SV_Target
            {
                float4 seed = tex2Dlod(_JfaTex, float4(i.uv, 0.0, 0.0));
                float3 col = (seed.a > 0.5)
                    ? tex2Dlod(_MainTex, float4(saturate(seed.xy), 0.0, 0.0)).rgb
                    : tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb;

                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(col, alpha);
            }
            ENDCG
        }

        // Pass 5: Normalize [0, 1] using Min/Max from _StatsBuf
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragNormalize

            float4 fragNormalize(v2f i) : SV_Target
            {
                float3 rgb = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb;
                float minV = asfloat(_StatsBuf[0]);
                float maxV = asfloat(_StatsBuf[1]);
                float range = maxV - minV;

                float3 outRgb = (range > 1e-5) ? saturate((rgb - minV) / range) : rgb;
                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(outRgb, alpha);
            }
            ENDCG
        }
    }
}