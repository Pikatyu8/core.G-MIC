Shader "CoreGMIC/Lava"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;
    sampler2D _PlasmaTex;
    sampler2D _CdfTex;
    sampler2D _PaletteTex;

    #include "GmicCommon.cginc"

    float _Sharpness;
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Multiply norm(source) * smoothPlasma -> gradient_norm
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMulGradNorm

            float SampleField(float2 uv)
            {
                float n = tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).r;
                float p = tex2Dlod(_PlasmaTex, float4(uv, 0.0, 0.0)).r;
                return n * p;
            }

            float4 fragMulGradNorm(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float xP = SampleField(saturate(i.uv + float2(tx.x, 0.0)));
                float xM = SampleField(saturate(i.uv - float2(tx.x, 0.0)));
                float yP = SampleField(saturate(i.uv + float2(0.0, tx.y)));
                float yM = SampleField(saturate(i.uv - float2(0.0, tx.y)));

                float dx = 0.5 * (xP - xM);
                float dy = 0.5 * (yP - yM);
                float gn = sqrt(dx * dx + dy * dy);
                return float4(gn, gn, gn, 1.0);
            }
            ENDCG
        }

        // Pass 1: Normalize [min, max] -> Equalize CDF -> Map Palette ("hot")
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragEqualizeAndMap

            float4 fragEqualizeAndMap(v2f i) : SV_Target
            {
                float gn = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).r;
                float4 meta = tex2Dlod(_CdfTex, float4(0.5 / 256.0, 0.5, 0.0, 0.0));
                float minV = meta.g;
                float maxV = meta.b;
                float range = maxV - minV;

                float norm = (range > 1e-5) ? saturate((gn - minV) / range) : 0.0;
                float cdfU = (0.5 + norm * 255.0) / 256.0;
                float eq = tex2Dlod(_CdfTex, float4(cdfU, 0.5, 0.0, 0.0)).r;

                float palU = (0.5 + saturate(eq) * 255.0) / 256.0;
                float3 col = tex2Dlod(_PaletteTex, float4(palU, 0.5, 0.0, 0.0)).rgb;
                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(col, alpha);
            }
            ENDCG
        }

        // Pass 2: Inverse-diffusion Laplacian sharpen
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragSharpen

            float4 fragSharpen(v2f i) : SV_Target
            {
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                if (_Sharpness <= 0.0001)
                {
                    return c;
                }

                float2 tx = _MainTex_TexelSize.xy;
                float3 l = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(tx.x, 0.0)), 0.0, 0.0)).rgb;
                float3 r = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(tx.x, 0.0)), 0.0, 0.0)).rgb;
                float3 d = tex2Dlod(_MainTex, float4(saturate(i.uv - float2(0.0, tx.y)), 0.0, 0.0)).rgb;
                float3 u = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(0.0, tx.y)), 0.0, 0.0)).rgb;

                float3 lap = l + r + d + u - 4.0 * c.rgb;
                float3 outRgb = saturate(c.rgb - (_Sharpness * (1.0 / 255.0)) * lap);
                return float4(outRgb, c.a);
            }
            ENDCG
        }
    }
}