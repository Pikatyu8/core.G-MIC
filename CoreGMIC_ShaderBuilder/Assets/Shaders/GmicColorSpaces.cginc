#ifndef GMIC_COLOR_SPACES_CGINC
#define GMIC_COLOR_SPACES_CGINC

#include "GmicCommon.cginc"

float3 SrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max((c + 0.055) / 1.055, 0.0), 2.4);
    return float3(
        c.r <= 0.04045 ? lo.r : hi.r,
        c.g <= 0.04045 ? lo.g : hi.g,
        c.b <= 0.04045 ? lo.b : hi.b
    );
}

float3 LinearToSrgb(float3 c)
{
    c = max(c, 0.0);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(max(c, 1e-6), 1.0 / 2.4) - 0.055;
    return saturate(float3(
        c.r <= 0.0031308 ? lo.r : hi.r,
        c.g <= 0.0031308 ? lo.g : hi.g,
        c.b <= 0.0031308 ? lo.b : hi.b
    ));
}

float3 RgbToYCbCr(float3 c)
{
    float y = 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
    float cb = 0.5 + (-0.168736 * c.r - 0.331264 * c.g + 0.5 * c.b);
    float cr = 0.5 + ( 0.5 * c.r - 0.418688 * c.g - 0.081312 * c.b);
    return saturate(float3(y, cb, cr));
}

float3 YCbCrToRgb(float3 ycc)
{
    float y = ycc.x;
    float cb = ycc.y - 0.5;
    float cr = ycc.z - 0.5;
    float r = y + 1.402 * cr;
    float g = y - 0.344136 * cb - 0.714136 * cr;
    float b = y + 1.772 * cb;
    return saturate(float3(r, g, b));
}

float LabF(float t)
{
    return (t > 0.008856) ? pow(max(t, 1e-6), 1.0 / 3.0) : (7.787 * t + 16.0 / 116.0);
}

float LabInvF(float t)
{
    return (t > 0.20689655) ? (t * t * t) : ((t - 16.0 / 116.0) / 7.787);
}

float3 RgbToLab01(float3 srgb)
{
    float3 lin = SrgbToLinear(saturate(srgb));
    float X = (0.4124564 * lin.r + 0.3575761 * lin.g + 0.1804375 * lin.b) / 0.95047;
    float Y = (0.2126729 * lin.r + 0.7151522 * lin.g + 0.0721750 * lin.b) / 1.00000;
    float Z = (0.0193339 * lin.r + 0.1191920 * lin.g + 0.9503041 * lin.b) / 1.08883;

    float fx = LabF(X);
    float fy = LabF(Y);
    float fz = LabF(Z);

    float L = (116.0 * fy - 16.0) / 100.0;
    float a = (500.0 * (fx - fy) + 128.0) / 255.0;
    float b = (200.0 * (fy - fz) + 128.0) / 255.0;
    return saturate(float3(L, a, b));
}

float3 Lab01ToRgb(float3 lab01)
{
    float L = lab01.x * 100.0;
    float a = lab01.y * 255.0 - 128.0;
    float b = lab01.z * 255.0 - 128.0;

    float fy = (L + 16.0) / 116.0;
    float fx = a / 500.0 + fy;
    float fz = fy - b / 200.0;

    float X = 0.95047 * LabInvF(fx);
    float Y = 1.00000 * LabInvF(fy);
    float Z = 1.08883 * LabInvF(fz);

    float r =  3.2404542 * X - 1.5371385 * Y - 0.4985314 * Z;
    float g = -0.9692660 * X + 1.8760108 * Y + 0.0415560 * Z;
    float bc = 0.0556434 * X - 0.2040259 * Y + 1.0572252 * Z;
    return LinearToSrgb(float3(r, g, bc));
}

float3 RgbToLch01(float3 srgb)
{
    float3 lab01 = RgbToLab01(srgb);
    float a = lab01.y * 255.0 - 128.0;
    float b = lab01.z * 255.0 - 128.0;
    float C = saturate(sqrt(a * a + b * b) / 181.0);
    float H = (abs(a) < 1e-5 && abs(b) < 1e-5) ? 0.0 : frac(atan2(b, a) / 6.28318530718);
    return float3(lab01.x, C, H);
}

float3 Lch01ToRgb(float3 lch01)
{
    float C = lch01.y * 181.0;
    float angle = lch01.z * 6.28318530718;
    float sA, cA;
    sincos(angle, sA, cA);
    float a01 = saturate((C * cA + 128.0) / 255.0);
    float b01 = saturate((C * sA + 128.0) / 255.0);
    return Lab01ToRgb(float3(lch01.x, a01, b01));
}

float3 RgbToHsv(float3 c)
{
    float cmax = max(c.r, max(c.g, c.b));
    float cmin = min(c.r, min(c.g, c.b));
    float d = cmax - cmin;
    float h = 0.0;
    if (d >= 1e-5)
    {
        if (cmax == c.r) h = ((c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0)) / 6.0;
        else if (cmax == c.g) h = ((c.b - c.r) / d + 2.0) / 6.0;
        else h = ((c.r - c.g) / d + 4.0) / 6.0;
    }
    float s = (cmax > 1e-5) ? (d / cmax) : 0.0;
    return float3(frac(h), saturate(s), saturate(cmax));
}

float3 HsvToRgb(float3 hsv)
{
    float h = frac(hsv.x) * 6.0;
    float s = saturate(hsv.y);
    float v = saturate(hsv.z);
    float f = frac(h);
    float p = v * (1.0 - s);
    float q = v * (1.0 - s * f);
    float t = v * (1.0 - s * (1.0 - f));
    if (h < 1.0) return float3(v, t, p);
    if (h < 2.0) return float3(q, v, p);
    if (h < 3.0) return float3(p, v, t);
    if (h < 4.0) return float3(p, q, v);
    if (h < 5.0) return float3(t, p, v);
    return float3(v, p, q);
}

float3 RgbToHsi(float3 c)
{
    float sum = c.r + c.g + c.b;
    float inten = sum / 3.0;
    float cmin = min(c.r, min(c.g, c.b));
    float s = (sum > 1e-5) ? (1.0 - 3.0 * cmin / sum) : 0.0;
    float num = 0.5 * ((c.r - c.g) + (c.r - c.b));
    float den = sqrt(max(1e-10, (c.r - c.g) * (c.r - c.g) + (c.r - c.b) * (c.g - c.b)));
    float theta = acos(clamp(num / den, -1.0, 1.0)) / 6.28318530718;
    float h = (c.b <= c.g) ? theta : (1.0 - theta);
    return float3(frac(h), saturate(s), saturate(inten));
}

float3 HsiToRgb(float3 hsi)
{
    float h = frac(hsi.x) * 6.28318530718;
    float s = saturate(hsi.y);
    float inten = saturate(hsi.z);
    float pi23 = 2.09439510239;
    float pi43 = 4.18879020479;
    float r, g, b;
    if (h < pi23)
    {
        b = inten * (1.0 - s);
        r = inten * (1.0 + (s * cos(h)) / max(1e-4, cos(1.0471975512 - h)));
        g = 3.0 * inten - (r + b);
    }
    else if (h < pi43)
    {
        float hp = h - pi23;
        r = inten * (1.0 - s);
        g = inten * (1.0 + (s * cos(hp)) / max(1e-4, cos(1.0471975512 - hp)));
        b = 3.0 * inten - (r + g);
    }
    else
    {
        float hp = h - pi43;
        g = inten * (1.0 - s);
        b = inten * (1.0 + (s * cos(hp)) / max(1e-4, cos(1.0471975512 - hp)));
        r = 3.0 * inten - (g + b);
    }
    return saturate(float3(r, g, b));
}

float4 RgbToCmyk(float3 c)
{
    float k = 1.0 - max(c.r, max(c.g, c.b));
    float denom = 1.0 - k;
    if (denom < 1e-5) return float4(0.0, 0.0, 0.0, 1.0);
    return saturate(float4((denom - c.r) / denom, (denom - c.g) / denom, (denom - c.b) / denom, k));
}

float3 CmykToRgb(float4 cmyk)
{
    float ik = 1.0 - saturate(cmyk.w);
    return saturate((1.0 - saturate(cmyk.xyz)) * ik);
}

float3 RgbToYiq01(float3 c)
{
    float y = dot(c, float3(0.299, 0.587, 0.114));
    float i = dot(c, float3(0.595716, -0.274453, -0.321263));
    float q = dot(c, float3(0.211456, -0.522591, 0.311135));
    return saturate(float3(y, 0.5 + i / 1.191432, 0.5 + q / 1.045182));
}

float3 Yiq01ToRgb(float3 yiq01)
{
    float y = saturate(yiq01.x);
    float i = (yiq01.y - 0.5) * 1.191432;
    float q = (yiq01.z - 0.5) * 1.045182;
    float r = y + 0.9562957 * i + 0.6210244 * q;
    float g = y - 0.2721221 * i - 0.6473806 * q;
    float b = y - 1.1069890 * i + 1.7046150 * q;
    return saturate(float3(r, g, b));
}

float4 EncodeChannelSpace(float4 rgba, int mode)
{
    rgba = saturate(rgba);
    if (mode <= 6)  return rgba;
    if (mode <= 10) return float4(SrgbToLinear(rgba.rgb), rgba.a);
    if (mode <= 14) return float4(RgbToYCbCr(rgba.rgb), rgba.a);
    if (mode == 15) return float4(RgbToYCbCr(rgba.grb), rgba.a);
    if (mode <= 19) return float4(RgbToLab01(rgba.rgb), rgba.a);
    if (mode <= 22) return float4(RgbToLch01(rgba.rgb), rgba.a);
    if (mode <= 25) return float4(RgbToHsv(rgba.rgb), rgba.a);
    if (mode == 26) return float4(RgbToHsi(rgba.rgb), rgba.a);
    if (mode == 27) return float4(RgbToHsl(rgba.rgb), rgba.a);
    if (mode <= 31) return RgbToCmyk(rgba.rgb);
    return float4(RgbToYiq01(rgba.rgb), rgba.a);
}

float4 DecodeEncodedToRgba(float4 enc, float origAlpha, int mode)
{
    if (mode == 0 || mode == 1 || mode == 6) return saturate(enc);
    if (mode <= 5)  return float4(saturate(enc.rgb), origAlpha);
    if (mode <= 10) return float4(LinearToSrgb(enc.rgb), origAlpha);
    if (mode <= 14) return float4(YCbCrToRgb(enc.rgb), origAlpha);
    if (mode == 15) return float4(YCbCrToRgb(enc.rgb).grb, origAlpha);
    if (mode <= 19) return float4(Lab01ToRgb(enc.rgb), origAlpha);
    if (mode <= 22) return float4(Lch01ToRgb(enc.rgb), origAlpha);
    if (mode <= 25) return float4(HsvToRgb(enc.rgb), origAlpha);
    if (mode == 26) return float4(HsiToRgb(enc.rgb), origAlpha);
    if (mode == 27) return float4(HslToRgb(enc.rgb), origAlpha);
    if (mode <= 31) return float4(CmykToRgb(enc), origAlpha);
    return float4(Yiq01ToRgb(enc.rgb), origAlpha);
}

float4 DecodeAndMergeChannel(float4 origRgba, float4 procEnc, int mode)
{
    float4 m = EncodeChannelSpace(origRgba, mode);

    if (mode == 0 || mode == 1)
        m = procEnc;
    else if (mode == 2 || mode == 7)
        m.rgb = procEnc.rgb;
    else if (mode == 3 || mode == 8 || mode == 11 || mode == 16 || mode == 23 || mode == 28 || mode == 32)
        m.r = procEnc.r;
    else if (mode == 4 || mode == 9 || mode == 13 || mode == 18 || mode == 21 || mode == 24 || mode == 29)
        m.g = procEnc.g;
    else if (mode == 5 || mode == 10 || mode == 14 || mode == 15 || mode == 19 || mode == 22 || mode == 25 || mode == 26 || mode == 27 || mode == 30)
        m.b = procEnc.b;
    else if (mode == 12 || mode == 17 || mode == 20 || mode == 33)
        m.gb = procEnc.gb;
    else if (mode == 6 || mode == 31)
        m.a = procEnc.a;

    return DecodeEncodedToRgba(m, origRgba.a, mode);
}

float4 ExtractChannelSubset(float4 enc, int mode)
{
    if (mode == 0 || mode == 1 || mode == 2 || mode == 7)
        return enc;
    if (mode == 3 || mode == 8 || mode == 11 || mode == 16 || mode == 23 || mode == 28 || mode == 32)
        return float4(enc.r, enc.r, enc.r, 1.0);
    if (mode == 4 || mode == 9 || mode == 13 || mode == 18 || mode == 21 || mode == 24 || mode == 29)
        return float4(enc.g, enc.g, enc.g, 1.0);
    if (mode == 5 || mode == 10 || mode == 14 || mode == 15 || mode == 19 || mode == 22 || mode == 25 || mode == 26 || mode == 27 || mode == 30)
        return float4(enc.b, enc.b, enc.b, 1.0);
    if (mode == 6 || mode == 31)
        return float4(enc.a, enc.a, enc.a, 1.0);
    return float4(enc.g, enc.b, enc.g, 1.0);
}

float4 RepackSubsetAndDecode(float4 origRgba, float4 procSubset, int mode)
{
    float4 m = EncodeChannelSpace(origRgba, mode);

    if (mode == 0 || mode == 1)
        m = procSubset;
    else if (mode == 2 || mode == 7)
        m.rgb = procSubset.rgb;
    else if (mode == 3 || mode == 8 || mode == 11 || mode == 16 || mode == 23 || mode == 28 || mode == 32)
        m.r = procSubset.r;
    else if (mode == 4 || mode == 9 || mode == 13 || mode == 18 || mode == 21 || mode == 24 || mode == 29)
        m.g = procSubset.r;
    else if (mode == 5 || mode == 10 || mode == 14 || mode == 15 || mode == 19 || mode == 22 || mode == 25 || mode == 26 || mode == 27 || mode == 30)
        m.b = procSubset.r;
    else if (mode == 6 || mode == 31)
        m.a = procSubset.r;
    else if (mode == 12 || mode == 17 || mode == 20 || mode == 33)
        m.gb = procSubset.rg;

    return DecodeEncodedToRgba(m, origRgba.a, mode);
}

#endif
