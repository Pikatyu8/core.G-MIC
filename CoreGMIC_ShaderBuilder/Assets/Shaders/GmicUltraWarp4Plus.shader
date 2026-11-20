Shader "CoreGMIC/UltraWarp4Plus"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #define GMIC_USE_HASH 1
    #include "GmicColorSpaces.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _PlasmaTex;

    StructuredBuffer<uint> _StatsBuf;

    int _NoiseType;
    float _NoiseAmount;
    uint _NoiseSeed;

    float2 _WarpScale;
    float2 _WarpOffset;
    float _Correlated;
    int _BoundaryMode;

    float _Gamma;
    float _HueShiftDeg;
    float _UseStats;

    float3 _HsvMul;
    float3 _HsvAdd;
    float _PlasmaAlpha;

    float4 SampleWithBoundary(float2 uv, int boundaryMode)
    {
        float4 result = float4(0.0, 0.0, 0.0, 0.0);
        if (boundaryMode == 0)
        {
            if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0)
                result = tex2Dlod(_MainTex, float4(uv, 0.0, 0.0));
        }
        else if (boundaryMode == 1)
        {
            result = tex2Dlod(_MainTex, float4(saturate(uv), 0.0, 0.0));
        }
        else if (boundaryMode == 2)
        {
            result = tex2Dlod(_MainTex, float4(frac(uv), 0.0, 0.0));
        }
        else
        {
            float2 m = 1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0);
            result = tex2Dlod(_MainTex, float4(m, 0.0, 0.0));
        }
        return result;
    }

    float ApplyNoiseScalar(float val01, float amount, int noiseType, uint seed)
    {
        if (amount <= 1e-5)
            return val01;

        uint h1 = HashU32(seed);
        uint h2 = HashU32(h1 ^ 0x85EBCA6Bu);
        float u1 = max(1e-6, (float)(h1 & 0xFFFFu) / 65535.0);
        float u2 = (float)(h2 & 0xFFFFu) / 65535.0;

        if (noiseType == 0)
        {
            // Gaussian noise (Box-Muller)
            float z = sqrt(-2.0 * log(u1)) * cos(6.28318530718 * u2);
            return val01 + z * (amount / 255.0);
        }
        if (noiseType == 1)
        {
            // Uniform noise
            return val01 + (u1 * 2.0 - 1.0) * (amount / 255.0);
        }
        if (noiseType == 2)
        {
            // Salt and Pepper
            float prob = saturate(amount * 0.01);
            if (u1 < prob)
                return (u2 < 0.5) ? 0.0 : 1.0;
            return val01;
        }

        // Poisson approximation (noise amount scales signal level)
        float scale = max(1e-4, amount / 255.0);
        float lambda = max(0.0, val01 * 255.0 * scale);
        float zP = sqrt(-2.0 * log(u1)) * cos(6.28318530718 * u2);
        float sampleP = max(0.0, lambda + sqrt(lambda) * zP);
        return sampleP / (255.0 * scale);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: G'MIC Noise (0=Gaussian, 1=Uniform, 2=Salt&Pepper, 3=Poisson)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragNoise

            float4 fragNoise(v2f i) : SV_Target
            {
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                if (_NoiseAmount <= 1e-5)
                    return c;

                uint2 px = (uint2)max(float2(0.0, 0.0), floor(i.uv * _MainTex_TexelSize.zw));
                uint baseSeed = px.x * 73856093u ^ px.y * 19349663u ^ _NoiseSeed;

                float r = ApplyNoiseScalar(c.r, _NoiseAmount, _NoiseType, baseSeed ^ 0x1111u);
                float g = ApplyNoiseScalar(c.g, _NoiseAmount, _NoiseType, baseSeed ^ 0x2222u);
                float b = ApplyNoiseScalar(c.b, _NoiseAmount, _NoiseType, baseSeed ^ 0x3333u);
                float a = ApplyNoiseScalar(c.a, _NoiseAmount, _NoiseType, baseSeed ^ 0x4444u);
                return float4(r, g, b, a);
            }
            ENDCG
        }

        // Pass 1: _fx_warp_by_intensity
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragWarpByIntensity

            float4 fragWarpByIntensity(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));

                if (_Correlated > 0.5)
                {
                    float ni = length(c.rgb) * 255.0;
                    float2 dispPx = float2((ni - _WarpOffset.x) * _WarpScale.x, (ni - _WarpOffset.y) * _WarpScale.y);
                    return SampleWithBoundary(i.uv + dispPx * tx, _BoundaryMode);
                }

                float4 c255 = c * 255.0;
                float r = SampleWithBoundary(i.uv + float2((c255.r - _WarpOffset.x) * _WarpScale.x, (c255.r - _WarpOffset.y) * _WarpScale.y) * tx, _BoundaryMode).r;
                float g = SampleWithBoundary(i.uv + float2((c255.g - _WarpOffset.x) * _WarpScale.x, (c255.g - _WarpOffset.y) * _WarpScale.y) * tx, _BoundaryMode).g;
                float b = SampleWithBoundary(i.uv + float2((c255.b - _WarpOffset.x) * _WarpScale.x, (c255.b - _WarpOffset.y) * _WarpScale.y) * tx, _BoundaryMode).b;
                float a = SampleWithBoundary(i.uv + float2((c255.a - _WarpOffset.x) * _WarpScale.x, (c255.a - _WarpOffset.y) * _WarpScale.y) * tx, _BoundaryMode).a;
                return float4(r, g, b, a);
            }
            ENDCG
        }

        // Pass 2: G'MIC negate
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
