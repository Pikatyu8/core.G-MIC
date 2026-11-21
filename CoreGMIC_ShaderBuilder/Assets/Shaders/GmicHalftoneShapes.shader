Shader "CoreGMIC/HalftoneShapes"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            #define GMIC_USE_LUMINANCE 1
            #include "GmicCommon.cginc"

            sampler2D _TileTex;
            float2 _TileSizePx;
            float _Negative;
            float _Oversample;
            float _WarpIt;
            float _RotateRad;
            float _Twirl;

            float SampleHalftoneBinary(float2 uv)
            {
                float lum = GmicLuminance(tex2Dlod(_MainTex, float4(saturate(uv), 0.0, 0.0)).rgb);

                float2 center = 0.5 * _MainTex_TexelSize.zw;
                float2 d = uv * _MainTex_TexelSize.zw - center;

                if (_WarpIt > 0.5)
                {
                    if (abs(_Twirl) > 1e-5 && dot(d, d) > 1e-6)
                    {
                        float R = max(1.0, length(center));
                        float r = length(d);
                        float a = atan2(d.y, d.x);
                        float aspect = _MainTex_TexelSize.z * _MainTex_TexelSize.y;
                        a += _Twirl * (r / R) * aspect * 6.28318530718;
                        float sT, cT;
                        sincos(a, sT, cT);
                        d = r * float2(cT, sT);
                    }

                    if (abs(_RotateRad) > 1e-5)
                    {
                        float sR, cR;
                        sincos(-_RotateRad, sR, cR);
                        d = float2(d.x * cR - d.y * sR, d.x * sR + d.y * cR);
                    }
                }

                float2 pPattern = center + d;
                float2 tileUv = frac(pPattern / max(float2(1.0, 1.0), _TileSizePx));
                float tileVal = tex2Dlod(_TileTex, float4(tileUv, 0.0, 0.0)).r;

                if (_Negative > 0.5)
                {
                    tileVal = 1.0 - tileVal;
                }

                return (0.5 * lum + 0.5 * tileVal > 0.5) ? 1.0 : 0.0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float alpha = tex2D(_MainTex, i.uv).a;

                if (_Oversample < 0.5)
                {
                    float v = SampleHalftoneBinary(i.uv);
                    return float4(v, v, v, alpha);
                }

                float2 tx = _MainTex_TexelSize.xy;
                float accum = 0.0;

                [unroll]
                for (int sy = 0; sy < 4; sy++)
                {
                    [unroll]
                    for (int sx = 0; sx < 4; sx++)
                    {
                        float2 subOff = float2(((float)sx + 0.5) * 0.25 - 0.5, ((float)sy + 0.5) * 0.25 - 0.5) * tx;
                        accum += SampleHalftoneBinary(i.uv + subOff);
                    }
                }

                float val = accum * (1.0 / 16.0);
                return float4(val, val, val, alpha);
            }
            ENDCG
        }
    }
}