Shader "CoreGMIC/ImageObject3D"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    StructuredBuffer<float4> _DotPosBuffer;
    StructuredBuffer<float4> _DotUvBuffer;

    float4x4 _RotMat;
    float _ObjectSize;
    float _FocalLen;
    float3 _LightRaw;
    float _SpecLightness;
    float _SpecShininess;
    float _PointSizePx;
    float2 _ScreenSize;

    struct appdata3d
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv0 : TEXCOORD0;
        float3 bary : TEXCOORD1;
        float3 edgeMask : TEXCOORD2;
    };

    struct v2f3d
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 viewPos : TEXCOORD1;
        float3 viewNormal : TEXCOORD2;
        float3 bary : TEXCOORD3;
        float3 edgeMask : TEXCOORD4;
        float2 gouraudLit : TEXCOORD5; // x = diffuse, y = specular
    };

    float3 ToViewPos(float3 localPos)
    {
        float3 rotated = mul((float3x3)_RotMat, localPos);
        return rotated * _ObjectSize + float3(0.0, 0.0, -_FocalLen);
    }

    float3 ToViewNormal(float3 localNormal)
    {
        return normalize(mul((float3x3)_RotMat, localNormal));
    }

    void ComputeBlinnPhong(float3 viewPos, float3 N, out float diff, out float spec)
    {
        float3 lightPos = float3(_LightRaw.x * _FocalLen, _LightRaw.y * _FocalLen, -_LightRaw.z * _FocalLen - _FocalLen);
        float3 L = normalize(lightPos - viewPos);
        float3 V = normalize(-viewPos);

        if (dot(N, V) < 0.0)
        {
            N = -N;
        }

        diff = max(0.05, saturate(dot(N, L)));

        float3 H = normalize(L + V);
        float expPow = exp2(2.0 + _SpecShininess * 4.0);
        spec = _SpecLightness * pow(saturate(dot(N, H)), expPow);
    }

    v2f3d vertMesh(appdata3d v)
    {
        v2f3d o;
        float3 vp = ToViewPos(v.vertex.xyz);
        float3 vn = ToViewNormal(v.normal);

        o.pos = UnityObjectToClipPos(float4(vp, 1.0));
        o.uv = v.uv0;
        o.viewPos = vp;
        o.viewNormal = vn;
        o.bary = v.bary;
        o.edgeMask = v.edgeMask;

        float diff, spec;
        ComputeBlinnPhong(vp, vn, diff, spec);
        o.gouraudLit = float2(diff, spec);
        return o;
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite On
        ZTest LEqual

        // Pass 0: Dots (Procedural quads per vertex)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vertDot
            #pragma fragment fragDot

            v2f vertDot(uint vid : SV_VertexID, uint iid : SV_InstanceID)
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

                float3 vp = ToViewPos(_DotPosBuffer[iid].xyz);
                float4 clipPos = UnityObjectToClipPos(float4(vp, 1.0));

                float2 corner = kCorners[vid % 6u];
                float2 offsetNdc = (corner * _PointSizePx / max(float2(1.0, 1.0), _ScreenSize)) * 2.0;
                clipPos.xy += offsetNdc * clipPos.w;

                v2f o;
                o.pos = clipPos;
                o.uv = _DotUvBuffer[iid].xy;
                return o;
            }

            float4 fragDot(v2f i) : SV_Target
            {
                float3 col = tex2D(_MainTex, saturate(i.uv)).rgb;
                return float4(col, 1.0);
            }
            ENDCG
        }

        // Pass 1: Wireframe (screen-space edge distance ignoring quad diagonals)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vertMesh
            #pragma fragment fragWireframe

            float4 fragWireframe(v2f3d i) : SV_Target
            {
                float3 fw = max(fwidth(i.bary), 1e-5);
                float3 d = (i.bary + (1.0 - i.edgeMask)) / fw;
                float minDist = min(d.x, min(d.y, d.z));
                float lineAlpha = 1.0 - smoothstep(0.6, 1.6, minDist);
                if (lineAlpha <= 0.05)
                {
                    discard;
                }

                float3 col = tex2D(_MainTex, saturate(i.uv)).rgb;
                return float4(col, 1.0);
            }
            ENDCG
        }

        // Pass 2: Flat (unlit texture)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vertMesh
            #pragma fragment fragFlat

            float4 fragFlat(v2f3d i) : SV_Target
            {
                float3 col = tex2D(_MainTex, saturate(i.uv)).rgb;
                return float4(col, 1.0);
            }
            ENDCG
        }

        // Pass 3: Flat-Shaded (per-triangle ddx/ddy flat normal)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vertMesh
            #pragma fragment fragFlatShaded

            float4 fragFlatShaded(v2f3d i) : SV_Target
            {
                float3 texCol = tex2D(_MainTex, saturate(i.uv)).rgb;
                float3 flatN = normalize(cross(ddx(i.viewPos), ddy(i.viewPos)));
                if (dot(flatN, flatN) < 1e-5)
                {
                    flatN = normalize(i.viewNormal);
                }

                float diff, spec;
                ComputeBlinnPhong(i.viewPos, flatN, diff, spec);
                return float4(saturate(texCol * diff + spec), 1.0);
            }
            ENDCG
        }

        // Pass 4: Gouraud (vertex-interpolated lighting)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vertMesh
            #pragma fragment fragGouraud

            float4 fragGouraud(v2f3d i) : SV_Target
            {
                float3 texCol = tex2D(_MainTex, saturate(i.uv)).rgb;
                return float4(saturate(texCol * i.gouraudLit.x + i.gouraudLit.y), 1.0);
            }
            ENDCG
        }

        // Pass 5: Phong (per-pixel normal lighting)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vertMesh
            #pragma fragment fragPhong

            float4 fragPhong(v2f3d i) : SV_Target
            {
                float3 texCol = tex2D(_MainTex, saturate(i.uv)).rgb;
                float3 N = normalize(i.viewNormal);

                float diff, spec;
                ComputeBlinnPhong(i.viewPos, N, diff, spec);
                return float4(saturate(texCol * diff + spec), 1.0);
            }
            ENDCG
        }

        // Pass 6: 2x2 Premultiplied-Alpha Edge Resolve
        Pass
        {
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragResolveAA

            float4 fragResolveAA(v2f i) : SV_Target
            {
                float2 q = _MainTex_TexelSize.xy * 0.5;
                float4 c0 = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(-q.x, -q.y)), 0.0, 0.0));
                float4 c1 = tex2Dlod(_MainTex, float4(saturate(i.uv + float2( q.x, -q.y)), 0.0, 0.0));
                float4 c2 = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(-q.x,  q.y)), 0.0, 0.0));
                float4 c3 = tex2Dlod(_MainTex, float4(saturate(i.uv + float2( q.x,  q.y)), 0.0, 0.0));

                float4 avg = 0.25 * (c0 + c1 + c2 + c3);
                if (avg.a <= 1e-5)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                // Match G'MIC fx_render3d edge un-premultiply while keeping smooth antialiased edge coverage
                float3 col = saturate(avg.rgb / max(1e-5, avg.a)) * avg.a;
                return float4(col, avg.a);
            }
            ENDCG
        }
    }
}