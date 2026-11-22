Shader "CoreGMIC/FireEdges"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #define GMIC_USE_HASH 1
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;
    sampler2D _SparksTex;
    sampler2D _EdgeMaskTex;
    sampler2D _CdfTex;

    StructuredBuffer<uint> _StatsBuf;

    float _Edges;
    float _AttenCoeff;
    float _ThresholdPct;
    int _StepSeed;

    float Roundify01(float x, float gamma)
    {
        x = saturate(x);
        if (abs(gamma - 1.0) < 1e-5)
        {
            return x;
        }

        float r = round(x);
        float d = x - r;
        float s = (d > 0.0) ? 1.0 : ((d < 0.0) ? -1.0 : 0.0);
        float ad = abs(2.0 * d);
        float p = (ad > 1e-6) ? pow(ad, gamma) : ((gamma <= 0.0) ? 1.0 : 0.0);
        return saturate(r + 0.5 * s * p);
    }

    float3 FirePalette(float v)
    {
        float t = saturate(v) * 4.0;
        if (t < 1.0) return lerp(float3(0.0, 0.0, 0.0), float3(1.0, 0.0, 0.0), t);
        if (t < 2.0) return lerp(float3(1.0, 0.0, 0.0), float3(1.0, 1.0, 0.0), t - 1.0);
        if (t < 3.0) return lerp(float3(1.0, 1.0, 0.0), float3(1.0, 1.0, 128.0 / 255.0), t - 2.0);
        return lerp(float3(1.0, 1.0, 128.0 / 255.0), float3(1.0, 1.0, 1.0), t - 3.0);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: G'MIC roundify on normalized gradient norm [0, 1]
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragRoundify

            float4 fragRoundify(v2f i) : SV_Target
            {
                float val = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float r = Roundify01(val, _Edges);
                return float4(r, r, r, 1.0);
            }
            ENDCG
        }

        // Pass 1: Uniform random noise [0, 1] weighted by EdgeMask
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragSparksNoise

            float4 fragSparksNoise(v2f i) : SV_Target
            {
                float edgeMask = saturate(tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r);
                uint2 px = (uint2)max(float2(0.0, 0.0), floor(i.uv * _MainTex_TexelSize.zw));
                uint h = HashU32(px.x * 73856093u ^ px.y * 19349663u ^ ((uint)_StepSeed * 2654435761u));
                float rnd = (float)(h & 0xFFFFu) / 65535.0;
                float val = rnd * edgeMask;
                return float4(val, val, val, 1.0);
            }
            ENDCG
        }

        // Pass 2: Binary threshold >= Threshold% (constant buffer -> 0)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragThresholdMask

            float4 fragThresholdMask(v2f i) : SV_Target
            {
                float minV = asfloat(_StatsBuf[0]);
                float maxV = asfloat(_StatsBuf[1]);
                float range = maxV - minV;

                float val = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float thresh = minV + saturate(_ThresholdPct * 0.01) * range;
                float outVal = (range > 1e-6 && val >= thresh) ? 1.0 : 0.0;
                return float4(outVal, outVal, outVal, 1.0);
            }
            ENDCG
        }

        // Pass 3: Zero-based histogram equalization (Threshold == 0)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragEqualizeNormalize

            float4 fragEqualizeNormalize(v2f i) : SV_Target
            {
                float val = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float4 meta = tex2Dlod(_CdfTex, float4(0.5 / 256.0, 0.5, 0.0, 0.0));
                float minV = meta.g;
                float maxV = meta.b;
                float range = maxV - minV;

                float outVal = 0.0;
                if (range > 1e-6)
                {
                    float norm = saturate((val - minV) / range);
                    float cdfU = (0.5 + norm * 255.0) / 256.0;
                    outVal = saturate(tex2Dlod(_CdfTex, float4(cdfU, 0.5, 0.0, 0.0)).r);
                }

                return float4(outVal, outVal, outVal, 1.0);
            }
            ENDCG
        }

        // Pass 4: Blend sparks onto persistent fire buffer using EdgeMask
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBlendSparks

            float4 fragBlendSparks(v2f i) : SV_Target
            {
                float fire = tex2D(_MainTex, i.uv).r;
                float sparks = tex2D(_SparksTex, i.uv).r;
                float mask = saturate(tex2D(_EdgeMaskTex, i.uv).r);
                float blended = lerp(fire, sparks, mask);
                return float4(blended, blended, blended, 1.0);
            }
            ENDCG
        }

        // Pass 5: Upward fire advection/diffusion correlate (0,0,0; 0,0,0; 1,1,1; 0,1,0) * (1 - atten^4)/4
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragCorrelateFire

            float4 fragCorrelateFire(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float left = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0)).r;
                float center = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float right = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0)).r;

                float downUvY = i.uv.y - tx.y;
                float below = (downUvY >= 0.0)
                    ? tex2Dlod(_MainTex, float4(i.uv.x, downUvY, 0.0, 0.0)).r
                    : 0.0;

                float nextFire = saturate((left + center + right + below) * _AttenCoeff);
                return float4(nextFire, nextFire, nextFire, 1.0);
            }
            ENDCG
        }

        // Pass 6: Map fire intensity through 5-stop fire LUT + preserve original alpha
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragFirePaletteResolve

            float4 fragFirePaletteResolve(v2f i) : SV_Target
            {
                float val = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float3 rgb = FirePalette(val);
                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}