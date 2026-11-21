Shader "CoreGMIC/PlayerSwirl"
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

            #define GMIC_USE_BOUNDARY 1
            #include "GmicCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float2 _CenterUv;
            float _Strength;
            float _Exponent;
            int _BoundaryMode;

            float4 frag(v2f i) : SV_Target
            {
                float aspect = max(1e-5, _MainTex_TexelSize.z * _MainTex_TexelSize.y);
                float2 deltaUv = i.uv - _CenterUv;
                float2 deltaAspect = float2(deltaUv.x * aspect, deltaUv.y);

                float dist = length(deltaAspect) * 2.0;
                float angle = (dist > 1e-6) ? (_Strength * pow(dist, _Exponent)) : 0.0;

                float sA, cA;
                sincos(angle, sA, cA);

                float2 rotAspect = float2(
                    deltaAspect.x * cA - deltaAspect.y * sA,
                    deltaAspect.x * sA + deltaAspect.y * cA);

                float2 sampleUv = _CenterUv + float2(rotAspect.x / aspect, rotAspect.y);
                return SampleWithBoundary(_MainTex, sampleUv, _BoundaryMode);
            }
            ENDCG
        }
    }
}