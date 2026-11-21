Shader "CoreGMIC/Stencil"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    sampler2D _BlurTex;
    sampler2D _BaseTex;

    float _Amount;
    float _Transparency;
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Unsharp mask + clamp [0, 255]
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragUnsharpClamp255

            float4 fragUnsharpClamp255(v2f i) : SV_Target
            {
                float4 b1 = tex2D(_MainTex, i.uv);
                float4 b2 = tex2D(_BlurTex, i.uv);
                float3 res = clamp(b1.rgb + _Amount * (b1.rgb - b2.rgb), 0.0, 255.0);
                return float4(res, b1.a);
            }
            ENDCG
        }

        // Pass 1: Normalize [0, 255] -> [0, 1] + Optional Black Transparency
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragFinalizeBW

            float4 fragFinalizeBW(v2f i) : SV_Target
            {
                float3 rgb = saturate(tex2D(_MainTex, i.uv).rgb * (1.0 / 255.0));
                float alpha = tex2D(_BaseTex, i.uv).a;
                if (_Transparency > 0.5 && dot(rgb, rgb) < 1e-4)
                {
                    alpha = 0.0;
                }
                return float4(rgb, alpha);
            }
            ENDCG
        }

        // Pass 2: Binary Mask (>= 50% of 255) for Color ShapeAverage
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBinaryMask

            float4 fragBinaryMask(v2f i) : SV_Target
            {
                float v = tex2D(_MainTex, i.uv).r;
                float mask = (v >= 127.5) ? 1.0 : 0.0;
                return float4(mask, mask, mask, 1.0);
            }
            ENDCG
        }

        // Pass 3: Optional Black Transparency
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragApplyTransparency

            float4 fragApplyTransparency(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float alpha = tex2D(_BaseTex, i.uv).a;
                if (_Transparency > 0.5 && dot(src.rgb, src.rgb) < 1e-4)
                {
                    alpha = 0.0;
                }
                return float4(src.rgb, alpha);
            }
            ENDCG
        }
    }
}