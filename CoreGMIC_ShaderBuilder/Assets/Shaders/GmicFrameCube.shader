Shader "CoreGMIC/FrameCube"
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

            #include "GmicCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float _Depth;
            float2 _Center;
            int _OrientLeft;
            int _OrientRight;
            int _OrientUp;
            int _OrientDown;

            float2 ApplyOrientation(float2 uv, int mode)
            {
                if (mode == 1) return float2(1.0 - uv.x, uv.y);
                if (mode == 2) return float2(uv.x, 1.0 - uv.y);
                if (mode == 3) return float2(1.0 - uv.x, 1.0 - uv.y);
                return uv;
            }

            float4 SampleCubeAtUv(float2 uv)
            {
                float2 p = uv - 0.5;
                float2 cShift = _Center - 0.5;

                if (_Depth <= 1e-5)
                {
                    float2 flatUv = (p - cShift) + 0.5;
                    if (flatUv.x >= 0.0 && flatUv.x <= 1.0 && flatUv.y >= 0.0 && flatUv.y <= 1.0)
                    {
                        return tex2Dlod(_MainTex, float4(flatUv, 0.0, 0.0));
                    }
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float zDepth = _Depth * 1.92;
                float tBack = 1.0 + zDepth;
                float cx = cShift.x * tBack;
                float cy = cShift.y * tBack;

                float bestT = 1e20;
                float2 bestUv = uv;
                bool hit = false;

                // Back face (t = tBack)
                float2 uvBack = float2((p.x * tBack - cx) + 0.5, (p.y * tBack - cy) + 0.5);
                if (uvBack.x >= -1e-4 && uvBack.x <= 1.0001 && uvBack.y >= -1e-4 && uvBack.y <= 1.0001)
                {
                    bestT = tBack;
                    bestUv = saturate(uvBack);
                    hit = true;
                }

                // Left wall (X = -0.5 + s * cx)
                float denomX = p.x * zDepth - cx;
                if (abs(denomX) > 1e-6)
                {
                    float sL = (-0.5 - p.x) / denomX;
                    if (sL >= -1e-4 && sL <= 1.0001)
                    {
                        float s = saturate(sL);
                        float t = 1.0 + s * zDepth;
                        float v = (p.y * t - s * cy) + 0.5;
                        if (v >= -1e-4 && v <= 1.0001 && t < bestT)
                        {
                            bestT = t;
                            bestUv = ApplyOrientation(float2(s, saturate(v)), _OrientLeft);
                            hit = true;
                        }
                    }

                    // Right wall (X = 0.5 + s * cx)
                    float sR = (0.5 - p.x) / denomX;
                    if (sR >= -1e-4 && sR <= 1.0001)
                    {
                        float s = saturate(sR);
                        float t = 1.0 + s * zDepth;
                        float v = (p.y * t - s * cy) + 0.5;
                        if (v >= -1e-4 && v <= 1.0001 && t < bestT)
                        {
                            bestT = t;
                            bestUv = ApplyOrientation(float2(1.0 - s, saturate(v)), _OrientRight);
                            hit = true;
                        }
                    }
                }

                // Upper and lower walls
                float denomY = p.y * zDepth - cy;
                if (abs(denomY) > 1e-6)
                {
                    // Upper wall (Y = 0.5 + s * cy)
                    float sU = (0.5 - p.y) / denomY;
                    if (sU >= -1e-4 && sU <= 1.0001)
                    {
                        float s = saturate(sU);
                        float t = 1.0 + s * zDepth;
                        float u = (p.x * t - s * cx) + 0.5;
                        if (u >= -1e-4 && u <= 1.0001 && t < bestT)
                        {
                            bestT = t;
                            bestUv = ApplyOrientation(float2(saturate(u), 1.0 - s), _OrientUp);
                            hit = true;
                        }
                    }

                    // Lower wall (Y = -0.5 + s * cy)
                    float sD = (-0.5 - p.y) / denomY;
                    if (sD >= -1e-4 && sD <= 1.0001)
                    {
                        float s = saturate(sD);
                        float t = 1.0 + s * zDepth;
                        float u = (p.x * t - s * cx) + 0.5;
                        if (u >= -1e-4 && u <= 1.0001 && t < bestT)
                        {
                            bestT = t;
                            bestUv = ApplyOrientation(float2(saturate(u), s), _OrientDown);
                            hit = true;
                        }
                    }
                }

                if (!hit)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                return tex2Dlod(_MainTex, float4(bestUv, 0.0, 0.0));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 q = _MainTex_TexelSize.xy * 0.25;
                float4 c0 = SampleCubeAtUv(i.uv + float2(-q.x, -q.y));
                float4 c1 = SampleCubeAtUv(i.uv + float2( q.x, -q.y));
                float4 c2 = SampleCubeAtUv(i.uv + float2(-q.x,  q.y));
                float4 c3 = SampleCubeAtUv(i.uv + float2( q.x,  q.y));
                return 0.25 * (c0 + c1 + c2 + c3);
            }
            ENDCG
        }
    }
}