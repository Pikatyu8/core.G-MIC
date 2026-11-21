Shader "CoreGMIC/PackEllipses"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;

    StructuredBuffer<float4> _EllipseGeom;
    StructuredBuffer<float4> _EllipseData;

    float _IsotropyMin;
    float _IsotropyMax;
    int _IsotropyLevels;
    int _OrientationMode;
    float2 _ScreenSize;
    float _FeatherPx;
    float4 _BgColor;

    struct v2fEllipse
    {
        float4 pos : SV_POSITION;
        float2 localPx : TEXCOORD0;
        float2 radii : TEXCOORD1;
        float4 color : TEXCOORD2;
    };
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Structure Tensor (Jxx, Jxy, Jyy) -> (isotropy, angleRad, 0, 1)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragTensorToField

            float4 fragTensorToField(v2f i) : SV_Target
            {
                float3 t = tex2D(_MainTex, i.uv).rgb;
                float jxx = t.r;
                float jxy = t.g;
                float jyy = t.b;

                float trace = jxx + jyy;
                float diff = jxx - jyy;
                float disc = sqrt(max(0.0, diff * diff + 4.0 * jxy * jxy));
                float eigMax = max(1e-7, 0.5 * (trace + disc));
                float eigMin = max(0.0, 0.5 * (trace - disc));

                float l0 = sqrt(eigMin);
                float l1 = sqrt(eigMax);
                float iso = saturate(1.0 - (l1 - l0) / max(1e-5, l1));

                if (_IsotropyLevels > 0)
                {
                    float lv = (float)_IsotropyLevels;
                    iso = floor(saturate(iso) * lv) / max(1.0, lv - 1.0);
                }

                iso = (_OrientationMode == 0) ? 1.0 : lerp(_IsotropyMin, _IsotropyMax, saturate(iso));

                // 1 = along contours, 2 = orthogonal to contours
                float angle = 0.5 * atan2(2.0 * jxy, diff);
                if (_OrientationMode == 1)
                {
                    angle += 1.57079632679;
                }

                return float4(iso, angle, 0.0, 1.0);
            }
            ENDCG
        }

        // Pass 1: Procedural Instanced Anti-Aliased Ellipses
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vertEllipse
            #pragma fragment fragEllipse

            v2fEllipse vertEllipse(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                static const float2 kCorners[6] =
                {
                    float2(-1.0, -1.0),
                    float2( 1.0, -1.0),
                    float2( 1.0,  1.0),
                    float2(-1.0, -1.0),
                    float2( 1.0,  1.0),
                    float2(-1.0,  1.0)
                };

                float4 geom = _EllipseGeom[iid];
                float4 data = _EllipseData[iid];

                float2 centerPx = geom.xy;
                float r1 = max(0.25, geom.z);
                float r2 = max(0.25, geom.w);
                float angle = data.x;

                float sA, cA;
                sincos(angle, sA, cA);

                float2 corner = kCorners[vid % 6u];
                float2 localPx = corner * float2(r1 + 1.5, r2 + 1.5);
                float2 rotPx = float2(
                    localPx.x * cA - localPx.y * sA,
                    localPx.x * sA + localPx.y * cA);

                float2 screenUv = (centerPx + rotPx) / max(float2(1.0, 1.0), _ScreenSize);

                v2fEllipse o;
                o.pos = UnityObjectToClipPos(float4(screenUv.x, screenUv.y, 0.0, 1.0));
                o.localPx = localPx;
                o.radii = float2(r1, r2);
                o.color = float4(data.yzw, 1.0);
                return o;
            }

            float4 fragEllipse(v2fEllipse i) : SV_Target
            {
                float2 invR2 = 1.0 / (i.radii * i.radii);
                float2 pNorm = i.localPx / i.radii;
                float d = length(pNorm);
                float grad = length(i.localPx * invR2) / max(1e-5, d);
                float signedDistPx = (d - 1.0) / max(1e-4, grad);

                float feather = max(0.125, _FeatherPx);
                float alpha = saturate(0.5 - signedDistPx / feather);
                if (alpha <= 0.001)
                {
                    discard;
                }

                return float4(i.color.rgb, alpha);
            }
            ENDCG
        }

        // Pass 2: Background Fill
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBg

            float4 fragBg(v2f i) : SV_Target
            {
                return _BgColor;
            }
            ENDCG
        }
    }
}