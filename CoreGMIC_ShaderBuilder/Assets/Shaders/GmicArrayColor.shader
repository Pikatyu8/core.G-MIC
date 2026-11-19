Shader "CoreGMIC/ArrayColor"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _XTiles ("X Tiles", Float) = 5.0
        _YTiles ("Y Tiles", Float) = 5.0
        _Opacity ("Opacity", Range(0.0, 1.0)) = 0.5
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

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);

                float2 tileCount = max(float2(1.0, 1.0), float2(_XTiles, _YTiles));
                uint2 tileIdx = (uint2)min(tileCount - 1.0, floor(saturate(i.uv) * tileCount));

                uint seed = HashU32(tileIdx.x * 73856093u ^ tileIdx.y * 19349663u ^ 0x9E3779B9u);
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