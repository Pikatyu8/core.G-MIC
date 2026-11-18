Shader "CoreGMIC/Quadtree"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    // (screenW, screenH, gridW, gridH) where gridW/gridH are padded multiples of 256.
    float4 _SourceSize;

    sampler2D _ColorL0;
    sampler2D _ColorL1;
    sampler2D _ColorL2;
    sampler2D _ColorL3;
    sampler2D _ColorL4;
    sampler2D _ColorL5;
    sampler2D _ColorL6;

    sampler2D _TensorL0;
    sampler2D _TensorL1;
    sampler2D _TensorL2;
    sampler2D _TensorL3;
    sampler2D _TensorL4;
    sampler2D _TensorL5;
    sampler2D _TensorL6;

    int _Mode;
    float _Precision;
    float _Homogeneity;
    float _Outline;
    float _Radius1;
    float _Radius2;
    float _Anisotropy;
    float _OnlyLeaves;

    float4 SampleColor(int level, float2 uv)
    {
        float4 tc = float4(uv, 0.0, 0.0);
        if (level == 0) return tex2Dlod(_ColorL0, tc);
        if (level == 1) return tex2Dlod(_ColorL1, tc);
        if (level == 2) return tex2Dlod(_ColorL2, tc);
        if (level == 3) return tex2Dlod(_ColorL3, tc);
        if (level == 4) return tex2Dlod(_ColorL4, tc);
        if (level == 5) return tex2Dlod(_ColorL5, tc);
        return tex2Dlod(_ColorL6, tc);
    }

    float4 SampleTensor(int level, float2 uv)
    {
        float4 tc = float4(uv, 0.0, 0.0);
        if (level == 0) return tex2Dlod(_TensorL0, tc);
        if (level == 1) return tex2Dlod(_TensorL1, tc);
        if (level == 2) return tex2Dlod(_TensorL2, tc);
        if (level == 3) return tex2Dlod(_TensorL3, tc);
        if (level == 4) return tex2Dlod(_TensorL4, tc);
        if (level == 5) return tex2Dlod(_TensorL5, tc);
        return tex2Dlod(_TensorL6, tc);
    }

    float LevelCellSize(int level)
    {
        return exp2((float)(level + 2)); // 4, 8, 16, 32, 64, 128, 256
    }

    float GetSplitThreshold()
    {
        float normPrec = saturate((_Precision - 2.0) / 4094.0);
        return pow(10.0, lerp(-0.5, -4.2, pow(normPrec, 0.55)));
    }

    float CellScore(float4 colorLuma, float4 tensorVar, float cellSize)
    {
        float meanL = colorLuma.a;
        float meanL2 = tensorVar.r;
        float variance = max(0.0, meanL2 - meanL * meanL);
        float area = cellSize * cellSize;
        return variance * pow(area / 1024.0, _Homogeneity);
    }

    bool IsReachableNode(float2 cellCenterPx, int targetLevel, float threshold)
    {
        for (int a = 6; a > targetLevel; a--)
        {
            float acs = LevelCellSize(a);
            float2 adims = _SourceSize.zw / acs;
            float2 aIdx = clamp(floor(cellCenterPx / acs), 0.0, adims - 1.0);
            float2 auv = (aIdx + 0.5) / adims;
            float4 acl = SampleColor(a, auv);
            float4 atv = SampleTensor(a, auv);
            if (CellScore(acl, atv, acs) <= threshold)
            {
                return false;
            }
        }
        return true;
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Base 4x4 block -> (avgR, avgG, avgB, avgNorm)
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragExtractColor

            float4 fragExtractColor(v2f i) : SV_Target
            {
                float2 baseOrigin = floor(i.uv * (_SourceSize.zw * 0.25)) * 4.0;
                float3 sumRgb = float3(0.0, 0.0, 0.0);
                float sumL = 0.0;

                [unroll]
                for (int y = 0; y < 4; y++)
                {
                    [unroll]
                    for (int x = 0; x < 4; x++)
                    {
                        float2 px = clamp(baseOrigin + float2(x + 0.5, y + 0.5), float2(0.5, 0.5), _SourceSize.xy - 0.5);
                        float2 suv = px / _SourceSize.xy;
                        float3 rgb = tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).rgb;
                        sumRgb += rgb;
                        sumL += length(rgb);
                    }
                }

                return float4(sumRgb / 16.0, sumL / 16.0);
            }
            ENDCG
        }

        // Pass 1: Base 4x4 block -> (avgNormSq, Jxx, Jxy, Jyy)
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragExtractTensor

            float4 fragExtractTensor(v2f i) : SV_Target
            {
                float2 baseOrigin = floor(i.uv * (_SourceSize.zw * 0.25)) * 4.0;
                float2 invSrc = 1.0 / _SourceSize.xy;

                float sumL2 = 0.0;
                [unroll]
                for (int y = 0; y < 4; y++)
                {
                    [unroll]
                    for (int x = 0; x < 4; x++)
                    {
                        float2 px = clamp(baseOrigin + float2(x + 0.5, y + 0.5), float2(0.5, 0.5), _SourceSize.xy - 0.5);
                        float3 rgb = tex2Dlod(_MainTex, float4(px * invSrc, 0.0, 0.0)).rgb;
                        sumL2 += dot(rgb, rgb);
                    }
                }

                // Sample RGB structure tensor across 2x2 sub-quadrants of the 4x4 cell.
                float jxx = 0.0;
                float jxy = 0.0;
                float jyy = 0.0;

                [unroll]
                for (int qy = 0; qy < 2; qy++)
                {
                    [unroll]
                    for (int qx = 0; qx < 2; qx++)
                    {
                        float2 c = clamp(baseOrigin + float2(qx * 2.0 + 1.0, qy * 2.0 + 1.0), float2(1.5, 1.5), _SourceSize.xy - 1.5);
                        float2 uv = c * invSrc;
                        float3 cxP = tex2Dlod(_MainTex, float4(uv + float2(invSrc.x, 0.0), 0.0, 0.0)).rgb;
                        float3 cxM = tex2Dlod(_MainTex, float4(uv - float2(invSrc.x, 0.0), 0.0, 0.0)).rgb;
                        float3 cyP = tex2Dlod(_MainTex, float4(uv + float2(0.0, invSrc.y), 0.0, 0.0)).rgb;
                        float3 cyM = tex2Dlod(_MainTex, float4(uv - float2(0.0, invSrc.y), 0.0, 0.0)).rgb;

                        float3 dx = 0.5 * (cxP - cxM);
                        float3 dy = 0.5 * (cyP - cyM);

                        jxx += dot(dx, dx);
                        jxy += dot(dx, dy);
                        jyy += dot(dy, dy);
                    }
                }

                return float4(sumL2 / 16.0, jxx * 0.25, jxy * 0.25, jyy * 0.25);
            }
            ENDCG
        }

        // Pass 2: Exact 2x2 downsample for pyramid construction
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragDownsample

            float4 fragDownsample(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * 0.5;
                float4 s0 = tex2Dlod(_MainTex, float4(i.uv + float2(-d.x, -d.y), 0.0, 0.0));
                float4 s1 = tex2Dlod(_MainTex, float4(i.uv + float2( d.x, -d.y), 0.0, 0.0));
                float4 s2 = tex2Dlod(_MainTex, float4(i.uv + float2(-d.x,  d.y), 0.0, 0.0));
                float4 s3 = tex2Dlod(_MainTex, float4(i.uv + float2( d.x,  d.y), 0.0, 0.0));
                return 0.25 * (s0 + s1 + s2 + s3);
            }
            ENDCG
        }

        // Pass 3: Quadtree Composite (Mode 0: Squares, Mode 1: Sierpinski)
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragCompositeSimple

            float4 fragCompositeSimple(v2f i) : SV_Target
            {
                float2 pixelPos = i.uv * _SourceSize.xy;
                float threshold = GetSplitThreshold();

                float4 leafColor = float4(0.0, 0.0, 0.0, 0.0);
                float4 leafTensor = float4(0.0, 0.0, 0.0, 0.0);
                float leafSize = 4.0;
                float2 leafOrigin = float2(0.0, 0.0);

                for (int k = 6; k >= 0; k--)
                {
                    float cs = LevelCellSize(k);
                    float2 dims = _SourceSize.zw / cs;
                    float2 cIdx = clamp(floor(pixelPos / cs), 0.0, dims - 1.0);
                    float2 uv = (cIdx + 0.5) / dims;
                    float4 cl = SampleColor(k, uv);
                    float4 tv = SampleTensor(k, uv);

                    if (k == 0 || CellScore(cl, tv, cs) <= threshold)
                    {
                        leafColor = cl;
                        leafTensor = tv;
                        leafSize = cs;
                        leafOrigin = cIdx * cs;
                        break;
                    }
                }

                // Mode 0: Squares
                if (_Mode == 0)
                {
                    if (_Outline > 0.0)
                    {
                        float2 localPx = pixelPos - leafOrigin;
                        float edgeDist = min(min(localPx.x, leafSize - localPx.x), min(localPx.y, leafSize - localPx.y));
                        if (edgeDist < _Outline)
                        {
                            return float4(0.0, 0.0, 0.0, 1.0);
                        }
                    }
                    return float4(leafColor.rgb, 1.0);
                }

                // Mode 1: Sierpinski Design
                float jxx = leafTensor.g;
                float jxy = leafTensor.b;
                float jyy = leafTensor.a;
                float angleRad = 0.5 * atan2(2.0 * jxy, jxx - jyy) + 1.57079632679;
                float angleDeg = angleRad * 57.2957795;
                if (angleDeg > 180.0) angleDeg -= 360.0;

                float2 localUV = (pixelPos - leafOrigin) / leafSize;
                float2 gmicUV = float2(localUV.x, 1.0 - localUV.y);

                bool primaryTri;
                if ((angleDeg >= 0.0 && angleDeg < 90.0) || angleDeg < -90.0)
                {
                    primaryTri = (gmicUV.x >= gmicUV.y);
                }
                else
                {
                    primaryTri = (gmicUV.x + gmicUV.y >= 1.0);
                }

                // G'MIC uses opacity 1.0 for primary triangle and 64/255 for secondary over black.
                float3 triColor = primaryTri ? leafColor.rgb : (leafColor.rgb * (64.0 / 255.0));
                return float4(triColor, 1.0);
            }
            ENDCG
        }

        // Pass 4: Quadtree Composite (Mode 2: Ellipse Painting)
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragCompositeEllipse

            float4 fragCompositeEllipse(v2f i) : SV_Target
            {
                float2 pixelPos = i.uv * _SourceSize.xy;
                float threshold = GetSplitThreshold();

                float4 leafColor = float4(0.0, 0.0, 0.0, 0.0);
                if (_Outline <= 0.0)
                {
                    for (int k = 6; k >= 0; k--)
                    {
                        float cs = LevelCellSize(k);
                        float2 dims = _SourceSize.zw / cs;
                        float2 cIdx = clamp(floor(pixelPos / cs), 0.0, dims - 1.0);
                        float2 uv = (cIdx + 0.5) / dims;
                        float4 cl = SampleColor(k, uv);
                        float4 tv = SampleTensor(k, uv);

                        if (k == 0 || CellScore(cl, tv, cs) <= threshold)
                        {
                            leafColor = cl;
                            break;
                        }
                    }
                }

                float3 accum = (_Outline > 0.0) ? float3(0.0, 0.0, 0.0) : leafColor.rgb;
                float maxRadiusParam = max(_Radius1, _Radius2);
                int reach = (maxRadiusParam > 4.0) ? 3 : ((maxRadiusParam > 2.0) ? 2 : 1);
                bool onlyLeaves = (_OnlyLeaves > 0.5);

                for (int level = 6; level >= 0; level--)
                {
                    float cs = LevelCellSize(level);
                    float2 dims = _SourceSize.zw / cs;
                    float2 baseIdx = floor(pixelPos / cs);
                    float halfW = 0.5 * cs;

                    for (int dy = -3; dy <= 3; dy++)
                    {
                        if (abs(dy) > reach) continue;
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            if (abs(dx) > reach) continue;

                            float2 nIdx = baseIdx + float2(dx, dy);
                            if (nIdx.x < 0.0 || nIdx.y < 0.0 || nIdx.x >= dims.x || nIdx.y >= dims.y)
                            {
                                continue;
                            }

                            float2 cellCenter = (nIdx + 0.5) * cs;
                            if (cellCenter.x >= _SourceSize.x + cs || cellCenter.y >= _SourceSize.y + cs)
                            {
                                continue;
                            }

                            float2 uv = (nIdx + 0.5) / dims;
                            float4 cl = SampleColor(level, uv);
                            float4 tv = SampleTensor(level, uv);

                            if (onlyLeaves && level > 0 && CellScore(cl, tv, cs) > threshold)
                            {
                                continue;
                            }

                            float jxx = tv.g;
                            float jxy = tv.b;
                            float jyy = tv.a;
                            float trace = jxx + jyy;
                            float diff = jxx - jyy;
                            float disc = sqrt(max(0.0, diff * diff + 4.0 * jxy * jxy));
                            float eigMax = 0.5 * (trace + disc) * 65025.0;
                            float eigMin = 0.5 * (trace - disc) * 65025.0;

                            float anisoRatio = (1.0 + eigMin) / (1.0 + eigMax);
                            float R = max(0.5, _Radius1 * halfW);
                            float r = max(0.5, _Radius2 * halfW * pow(max(1e-5, anisoRatio), _Anisotropy));

                            float angleRad = 0.5 * atan2(2.0 * jxy, diff) + 1.57079632679;
                            float sA, cA;
                            sincos(angleRad, sA, cA);

                            float2 d = pixelPos - cellCenter;
                            float u =  d.x * cA + d.y * sA;
                            float v = -d.x * sA + d.y * cA;

                            float normOuter = (u * u) / (R * R) + (v * v) / (r * r);
                            if (normOuter > 1.0)
                            {
                                continue;
                            }

                            if (!IsReachableNode(cellCenter, level, threshold))
                            {
                                continue;
                            }

                            if (_Outline > 0.0)
                            {
                                float Ri = max(0.0, R - _Outline);
                                float ri = max(0.0, r - _Outline);
                                bool insideInner = (Ri > 0.0 && ri > 0.0) && (((u * u) / (Ri * Ri) + (v * v) / (ri * ri)) <= 1.0);
                                accum = insideInner ? cl.rgb : float3(0.0, 0.0, 0.0);
                            }
                            else
                            {
                                accum = cl.rgb;
                            }
                        }
                    }
                }

                return float4(accum, 1.0);
            }
            ENDCG
        }
    }
}