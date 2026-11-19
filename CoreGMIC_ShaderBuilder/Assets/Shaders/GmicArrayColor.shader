Shader "CoreGMIC/ArrayColor"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _XTiles ("X Tiles", Float) = 5.0
        _YTiles ("Y Tiles", Float) = 5.0
        _Opacity ("Opacity", Range(0.0, 1.0)) = 0.5
        _Seed ("Seed", Int) = 0
        _RandomSizes ("Random Sizes", Float) = 0.0
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

            #define GMIC_USE_HASH 1
            #include "GmicCommon.cginc"

            sampler2D _MainTex;
            float _XTiles;
            float _YTiles;
            float _Opacity;
            int _Seed;
            float _RandomSizes;

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float2 uv = saturate(i.uv);
                uint frameSeed = (uint)_Seed * 2654435761u;

                uint2 tileIdx;
                if (_RandomSizes < 0.5)
                {
                    float2 tileCount = max(float2(1.0, 1.0), float2(_XTiles, _YTiles));
                    tileIdx = (uint2)min(tileCount - 1.0, floor(uv * tileCount));
                }
                else
                {
                    int nx = clamp((int)round(_XTiles), 1, 20);
                    int ny = clamp((int)round(_YTiles), 1, 20);

                    float wY[20];
                    float totalH = 0.0;
                    [unroll]
                    for (int y0 = 0; y0 < 20; y0++)
                    {
                        if (y0 < ny)
                        {
                            uint hy = HashU32(frameSeed ^ ((uint)y0 * 19349663u) ^ 0xA511E9B3u);
                            float w = lerp(0.25, 1.75, (float)(hy & 0xFFFFu) / 65535.0);
                            wY[y0] = w;
                            totalH += w;
                        }
                        else
                        {
                            wY[y0] = 0.0;
                        }
                    }

                    float targetY = uv.y * totalH;
                    float accY = 0.0;
                    uint ry = (uint)(ny - 1);
                    int foundY = 0;
                    [unroll]
                    for (int y1 = 0; y1 < 20; y1++)
                    {
                        accY += wY[y1];
                        if (y1 < ny && foundY == 0 && targetY <= accY)
                        {
                            ry = (uint)y1;
                            foundY = 1;
                        }
                    }

                    uint rowSeed = frameSeed ^ (ry * 83492791u) ^ 0x632BE59Bu;
                    float wX[20];
                    float totalW = 0.0;
                    [unroll]
                    for (int x0 = 0; x0 < 20; x0++)
                    {
                        if (x0 < nx)
                        {
                            uint hx = HashU32(rowSeed ^ ((uint)x0 * 73856093u));
                            float w = lerp(0.25, 1.75, (float)(hx & 0xFFFFu) / 65535.0);
                            wX[x0] = w;
                            totalW += w;
                        }
                        else
                        {
                            wX[x0] = 0.0;
                        }
                    }

                    float targetX = uv.x * totalW;
                    float accX = 0.0;
                    uint cx = (uint)(nx - 1);
                    int foundX = 0;
                    [unroll]
                    for (int x1 = 0; x1 < 20; x1++)
                    {
                        accX += wX[x1];
                        if (x1 < nx && foundX == 0 && targetX <= accX)
                        {
                            cx = (uint)x1;
                            foundX = 1;
                        }
                    }

                    tileIdx = uint2(cx, ry);
                }

                uint seed = HashU32(tileIdx.x * 73856093u ^ tileIdx.y * 19349663u ^ frameSeed ^ 0x9E3779B9u);
                float3 tileColor = float3(
                    (float)(HashU32(seed + 1u) & 0xFFFFu) / 65535.0,
                    (float)(HashU32(seed + 2u) & 0xFFFFu) / 65535.0,
                    (float)(HashU32(seed + 3u) & 0xFFFFu) / 65535.0
                );

                return fixed4(lerp(src.rgb, tileColor, saturate(_Opacity)), src.a);
            }
            ENDCG
        }
    }
}