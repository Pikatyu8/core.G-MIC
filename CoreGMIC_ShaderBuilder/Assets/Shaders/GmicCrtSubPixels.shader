Shader "CoreGMIC/CrtSubPixels"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "GmicCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BaseTex;
    sampler2D _IeeTex;
    sampler2D _BorderTex;
    sampler2D _CdfTex;

    StructuredBuffer<uint> _StatsBuf;

    float4 _SrcSize; // xy = src (w, h), zw = expanded (w+24, h+24)
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Expand xy,12 + 24x14 RGB sub-pixel lattice + shapeaverage0 per phosphor cell
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragSubPixelMaskAndAvg

            float4 fragSubPixelMaskAndAvg(v2f i) : SV_Target
            {
                int srcW = max(1, (int)round(_SrcSize.x));
                int srcH = max(1, (int)round(_SrcSize.y));
                int expW = max(1, (int)round(_SrcSize.z));
                int expH = max(1, (int)round(_SrcSize.w));

                int2 px = clamp((int2)floor(i.uv * _SrcSize.zw), int2(0, 0), int2(expW - 1, expH - 1));
                int x = px.x;
                int y = px.y;

                if ((x % 4) == 0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                bool leftHalf = (x % 24) < 12;
                if (leftHalf && (y % 14) == 0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }
                if (!leftHalf && ((y + 7) % 14) == 0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                int mod12 = x % 12;
                int ch = (mod12 < 4) ? 0 : ((mod12 < 8) ? 1 : 2);

                int cellX0 = (x / 4) * 4 + 1;
                int cellY0 = leftHalf
                    ? ((y / 14) * 14 + 1)
                    : ((y < 7) ? -6 : (((y - 7) / 14) * 14 + 8));

                float2 invSrc = 1.0 / float2((float)srcW, (float)srcH);
                float sum = 0.0;
                float cnt = 0.0;

                [unroll]
                for (int dy = 0; dy < 13; dy++)
                {
                    int sy = cellY0 + dy;
                    if (sy >= 0 && sy < expH)
                    {
                        int iy = sy - 12;
                        [unroll]
                        for (int dx = 0; dx < 3; dx++)
                        {
                            int sx = cellX0 + dx;
                            if (sx >= 0 && sx < expW)
                            {
                                int ix = sx - 12;
                                if (ix >= 0 && ix < srcW && iy >= 0 && iy < srcH)
                                {
                                    float2 suv = (float2((float)ix, (float)iy) + 0.5) * invSrc;
                                    float3 rgb = tex2Dlod(_MainTex, float4(suv, 0.0, 0.0)).rgb;
                                    sum += (ch == 0) ? rgb.r : ((ch == 1) ? rgb.g : rgb.b);
                                }
                                cnt += 1.0;
                            }
                        }
                    }
                }

                float avg = (cnt > 0.0) ? (sum / cnt) : 0.0;
                float3 outRgb = (ch == 0)
                    ? float3(avg, 0.0, 0.0)
                    : ((ch == 1) ? float3(0.0, avg, 0.0) : float3(0.0, 0.0, avg));
                return float4(outRgb, 1.0);
            }
            ENDCG
        }

        // Pass 1: Gradient-orthogonal 2nd derivative (G'MIC iee)
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragIee

            float4 fragIee(v2f i) : SV_Target
            {
                float2 tx = _MainTex_TexelSize.xy;
                float3 c  = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0)).rgb * 255.0;
                float3 xP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2( tx.x,   0.0)), 0.0, 0.0)).rgb * 255.0;
                float3 xM = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(-tx.x,   0.0)), 0.0, 0.0)).rgb * 255.0;
                float3 yP = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(  0.0,  tx.y)), 0.0, 0.0)).rgb * 255.0;
                float3 yM = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(  0.0, -tx.y)), 0.0, 0.0)).rgb * 255.0;

                float3 pp = tex2Dlod(_MainTex, float4(saturate(i.uv + float2( tx.x,  tx.y)), 0.0, 0.0)).rgb * 255.0;
                float3 mp = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(-tx.x,  tx.y)), 0.0, 0.0)).rgb * 255.0;
                float3 pm = tex2Dlod(_MainTex, float4(saturate(i.uv + float2( tx.x, -tx.y)), 0.0, 0.0)).rgb * 255.0;
                float3 mm = tex2Dlod(_MainTex, float4(saturate(i.uv + float2(-tx.x, -tx.y)), 0.0, 0.0)).rgb * 255.0;

                float3 ix = 0.5 * (xP - xM);
                float3 iy = 0.5 * (yP - yM);
                float3 ixx = xP - 2.0 * c + xM;
                float3 iyy = yP - 2.0 * c + yM;
                float3 ixy = 0.25 * (pp - mp - pm + mm);

                float3 denom = ix * ix + iy * iy + 1e-8;
                float3 iee = (iy * iy * ixx - 2.0 * ix * iy * ixy + ix * ix * iyy) / denom;
                return float4(iee / 255.0, 1.0);
            }
            ENDCG
        }

        // Pass 2: Max absolute component of iee for pde_flow normalization
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragAbsMaxIee

            float4 fragAbsMaxIee(v2f i) : SV_Target
            {
                float3 iee = abs(tex2D(_MainTex, i.uv).rgb);
                float m = max(iee.r, max(iee.g, iee.b));
                return float4(m, m, m, 1.0);
            }
            ENDCG
        }

        // Pass 3: Apply one meancurvature_flow step (dt = 30)
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragApplyPdeFlow

            float4 fragApplyPdeFlow(v2f i) : SV_Target
            {
                float4 cur = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float3 iee = tex2Dlod(_IeeTex, float4(i.uv, 0.0, 0.0)).rgb;
                float maxAbs01 = asfloat(_StatsBuf[1]);
                float stepScale = 30.0 / (0.01 + maxAbs01 * 255.0);
                return float4(cur.rgb + iee * stepScale, cur.a);
            }
            ENDCG
        }

        // Pass 4: 5%-95% screen border rectangle mask
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragBorderRect

            float4 fragBorderRect(v2f i) : SV_Target
            {
                float inside = (i.uv.x >= 0.05 && i.uv.x <= 0.95 && i.uv.y >= 0.05 && i.uv.y <= 0.95) ? 1.0 : 0.0;
                return float4(inside, inside, inside, 1.0);
            }
            ENDCG
        }

        // Pass 5: Multiply image by blurred border mask
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragMultiplyBorder

            float4 fragMultiplyBorder(v2f i) : SV_Target
            {
                float4 cur = tex2D(_MainTex, i.uv);
                float mask = saturate(tex2D(_BorderTex, i.uv).r);
                return float4(cur.rgb * mask, cur.a);
            }
            ENDCG
        }

        // Pass 6: Shrink xy,12 back to source resolution + preserve original alpha
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragShrinkAndResolve

            float4 fragShrinkAndResolve(v2f i) : SV_Target
            {
                float2 srcPx = i.uv * _SrcSize.xy;
                float2 expUv = (12.0 + srcPx) / _SrcSize.zw;
                float3 rgb = tex2Dlod(_MainTex, float4(expUv, 0.0, 0.0)).rgb;
                float alpha = tex2Dlod(_BaseTex, float4(i.uv, 0.0, 0.0)).a;
                return float4(rgb, alpha);
            }
            ENDCG
        }

        // Pass 7: 256-bin CDF histogram equalization
        Pass
        {
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment fragEqualize

            float4 fragEqualize(v2f i) : SV_Target
            {
                float4 src = tex2Dlod(_MainTex, float4(i.uv, 0.0, 0.0));
                float3 rgb = saturate(src.rgb);
                float r = tex2Dlod(_CdfTex, float4((0.5 + rgb.r * 255.0) / 256.0, 0.5, 0.0, 0.0)).r;
                float g = tex2Dlod(_CdfTex, float4((0.5 + rgb.g * 255.0) / 256.0, 0.5, 0.0, 0.0)).r;
                float b = tex2Dlod(_CdfTex, float4((0.5 + rgb.b * 255.0) / 256.0, 0.5, 0.0, 0.0)).r;
                return float4(saturate(float3(r, g, b)), src.a);
            }
            ENDCG
        }
    }
}