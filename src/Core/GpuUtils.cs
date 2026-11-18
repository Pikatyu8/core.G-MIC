using UnityEngine;

namespace CoreGMIC.Core
{
    public static class GpuUtils
    {
        public static RenderTextureFormat ColorFormat =>
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

        public static RenderTextureFormat FloatFormat =>
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                ? RenderTextureFormat.ARGBFloat
                : ColorFormat;

        public static RenderTextureFormat LuminanceFormat =>
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat
                : FloatFormat;

        public static RenderTexture GetTempRt(
            int width,
            int height,
            RenderTextureFormat format,
            FilterMode filterMode = FilterMode.Bilinear,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear,
            bool randomWrite = false)
        {
            RenderTexture rt;
            if (randomWrite)
            {
                RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height, format, 0)
                {
                    sRGB = readWrite == RenderTextureReadWrite.sRGB ||
                           (readWrite == RenderTextureReadWrite.Default && QualitySettings.activeColorSpace == ColorSpace.Gamma),
                    enableRandomWrite = true
                };
                rt = RenderTexture.GetTemporary(desc);
            }
            else
            {
                rt = RenderTexture.GetTemporary(width, height, 0, format, readWrite);
            }

            rt.filterMode = filterMode;
            rt.wrapMode = TextureWrapMode.Clamp;
            return rt;
        }

        public static RenderTexture CreateRt(
            int width,
            int height,
            RenderTextureFormat format,
            FilterMode filterMode = FilterMode.Bilinear,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear,
            bool randomWrite = false)
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height, format, 0)
            {
                sRGB = readWrite == RenderTextureReadWrite.sRGB ||
                       (readWrite == RenderTextureReadWrite.Default && QualitySettings.activeColorSpace == ColorSpace.Gamma),
                enableRandomWrite = randomWrite
            };
            RenderTexture rt = new RenderTexture(desc)
            {
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        public static void ReleaseTemp(RenderTexture rt)
        {
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static void ReleaseRt(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
                rt = null;
            }
        }

        public static void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        public static void DestroyObject<T>(ref T obj) where T : Object
        {
            if (obj != null)
            {
                Object.Destroy(obj);
                obj = null;
            }
        }
    }
}