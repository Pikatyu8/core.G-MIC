Shader "CoreGMIC/Pixelate"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlockSize ("Block Size (px)", Float) = 3.0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlockSize;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_BlockSize <= 1.0)
                {
                    return tex2D(_MainTex, i.uv);
                }

                float2 pixelCoord = i.uv * _MainTex_TexelSize.zw;
                float2 blockOrigin = floor(pixelCoord / _BlockSize) * _BlockSize;

                // Average a 3x3 sub-grid inside each block (exact 3x3 pixel merge when _BlockSize == 3).
                float4 accum = float4(0.0, 0.0, 0.0, 0.0);
                [unroll]
                for (int y = 0; y < 3; y++)
                {
                    [unroll]
                    for (int x = 0; x < 3; x++)
                    {
                        float2 samplePixel = blockOrigin + ((float2(x, y) + 0.5) / 3.0) * _BlockSize;
                        float2 sampleUv = saturate(samplePixel * _MainTex_TexelSize.xy);
                        accum += tex2D(_MainTex, sampleUv);
                    }
                }

                return accum / 9.0;
            }
            ENDCG
        }
    }
}