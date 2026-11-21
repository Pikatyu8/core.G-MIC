#ifndef GMIC_COMMON_CGINC
#define GMIC_COMMON_CGINC

#ifndef GMIC_COMPUTE
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

v2f vert(appdata v)
{
    v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    return o;
}
#endif

#ifdef GMIC_USE_HASH
uint HashU32(uint x)
{
    x ^= x >> 16;
    x *= 0x7FEB352Du;
    x ^= x >> 15;
    x *= 0x846CA68Bu;
    x ^= x >> 16;
    return x;
}
#endif

float3 RgbToHsl(float3 c)
{
    float cmax = max(c.r, max(c.g, c.b));
    float cmin = min(c.r, min(c.g, c.b));
    float d = cmax - cmin;
    float l = 0.5 * (cmax + cmin);
    float s = (d < 1e-5) ? 0.0 : d / max(1e-5, 1.0 - abs(2.0 * l - 1.0));

    float h = 0.0;
    if (d >= 1e-5)
    {
        if (cmax == c.r)
        {
            h = ((c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0)) / 6.0;
        }
        else if (cmax == c.g)
        {
            h = ((c.b - c.r) / d + 2.0) / 6.0;
        }
        else
        {
            h = ((c.r - c.g) / d + 4.0) / 6.0;
        }
    }

    return float3(frac(h), saturate(s), saturate(l));
}

float3 HslToRgb(float3 hsl)
{
    float h = frac(hsl.x) * 6.0;
    float s = saturate(hsl.y);
    float l = saturate(hsl.z);

    float c = (1.0 - abs(2.0 * l - 1.0)) * s;
    float x = c * (1.0 - abs(frac(h * 0.5) * 2.0 - 1.0));
    float m = l - 0.5 * c;

    float3 rgb;
    if (h < 1.0)      rgb = float3(c, x, 0.0);
    else if (h < 2.0) rgb = float3(x, c, 0.0);
    else if (h < 3.0) rgb = float3(0.0, c, x);
    else if (h < 4.0) rgb = float3(0.0, x, c);
    else if (h < 5.0) rgb = float3(x, 0.0, c);
    else              rgb = float3(c, 0.0, x);

    return saturate(rgb + m);
}

#ifdef GMIC_USE_SOBEL
void ComputeSobelGradients(float2 uv, float2 tx, out float3 dx, out float3 dy)
{
    float3 tl = tex2Dlod(_MainTex, float4(saturate(uv + float2(-tx.x,  tx.y)), 0.0, 0.0)).rgb;
    float3 tm = tex2Dlod(_MainTex, float4(saturate(uv + float2(  0.0,  tx.y)), 0.0, 0.0)).rgb;
    float3 tr = tex2Dlod(_MainTex, float4(saturate(uv + float2( tx.x,  tx.y)), 0.0, 0.0)).rgb;
    float3 ml = tex2Dlod(_MainTex, float4(saturate(uv + float2(-tx.x,   0.0)), 0.0, 0.0)).rgb;
    float3 mr = tex2Dlod(_MainTex, float4(saturate(uv + float2( tx.x,   0.0)), 0.0, 0.0)).rgb;
    float3 bl = tex2Dlod(_MainTex, float4(saturate(uv + float2(-tx.x, -tx.y)), 0.0, 0.0)).rgb;
    float3 bm = tex2Dlod(_MainTex, float4(saturate(uv + float2(  0.0, -tx.y)), 0.0, 0.0)).rgb;
    float3 br = tex2Dlod(_MainTex, float4(saturate(uv + float2( tx.x, -tx.y)), 0.0, 0.0)).rgb;

    dx = ((tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl)) * 0.125;
    dy = ((tl + 2.0 * tm + tr) - (bl + 2.0 * bm + br)) * 0.125;
}
#endif

#ifdef GMIC_USE_LUMINANCE
float GmicLuminance(float3 srgb)
{
    srgb = saturate(srgb);
    float3 lo = srgb / 12.92;
    float3 hi = pow(max((srgb + 0.055) / 1.055, 0.0), 2.4);
    float3 lin = float3(
        srgb.r <= 0.04045 ? lo.r : hi.r,
        srgb.g <= 0.04045 ? lo.g : hi.g,
        srgb.b <= 0.04045 ? lo.b : hi.b
    );

    float l = saturate(dot(lin, float3(0.22248840, 0.71690369, 0.06060791)));
    return (l <= 0.0031308) ? (l * 12.92) : saturate(1.055 * pow(max(l, 1e-6), 0.416667) - 0.055);
}
#endif

#ifdef GMIC_USE_TENSOR
float2 GetTangentAndEdge(float2 uv, out float edgeEnergy)
{
    float4 t = tex2Dlod(_TensorTex, float4(uv, 0.0, 0.0));
    float jxx = t.r;
    float jxy = t.g;
    float jyy = t.b;
    edgeEnergy = (jxx + jyy) * 255.0;

    if (edgeEnergy < 1e-4)
    {
        return float2(1.0, 0.0);
    }

    float angle = 0.5 * atan2(2.0 * jxy, jxx - jyy) + 1.57079632679;
    float sA, cA;
    sincos(angle, sA, cA);
    return float2(cA, sA);
}
#endif

#ifdef GMIC_USE_BOUNDARY
float4 SampleWithBoundary(sampler2D tex, float2 uv, int boundaryMode)
{
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    if (boundaryMode == 0)
    {
        if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0)
            result = tex2Dlod(tex, float4(uv, 0.0, 0.0));
    }
    else if (boundaryMode == 1)
    {
        result = tex2Dlod(tex, float4(saturate(uv), 0.0, 0.0));
    }
    else if (boundaryMode == 2)
    {
        result = tex2Dlod(tex, float4(frac(uv), 0.0, 0.0));
    }
    else
    {
        float2 m = 1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0);
        result = tex2Dlod(tex, float4(m, 0.0, 0.0));
    }
    return result;
}
#endif

#endif