Shader "CoreGMIC/Lut"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LutTex ("LUT Texture", 2D) = "white" {}
        _LutSize ("LUT Size", Float) = 32.0
        _Intensity ("Intensity", Range(0.0, 1.0)) = 1.0
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
            sampler2D _LutTex;
            float _LutSize;
            float _Intensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                float3 rgb = saturate(col.rgb);

                float maxIndex = _LutSize - 1.0;
                float blue = rgb.b * maxIndex;
                float slice0 = floor(blue);
                float slice1 = min(slice0 + 1.0, maxIndex);
                float blueLerp = blue - slice0;

                float totalWidth = _LutSize * _LutSize;
                float redTexel = 0.5 + rgb.r * maxIndex;
                float vCoord = (0.5 + rgb.g * maxIndex) / _LutSize;

                float u0 = (slice0 * _LutSize + redTexel) / totalWidth;
                float u1 = (slice1 * _LutSize + redTexel) / totalWidth;

                float3 lut0 = tex2D(_LutTex, float2(u0, vCoord)).rgb;
                float3 lut1 = tex2D(_LutTex, float2(u1, vCoord)).rgb;
                float3 lutColor = lerp(lut0, lut1, blueLerp);

                return fixed4(lerp(col.rgb, lutColor, _Intensity), col.a);
            }
            ENDCG
        }
    }
}