using UnityEngine;

namespace CoreGMIC.Core
{
    internal static class GpuOpsResources
    {
        private static Material _opsMaterial;

        public static Material OpsMaterial
        {
            get
            {
                if (_opsMaterial == null)
                {
                    Shader shader = ShaderManager.GetShader("CoreGMIC/Ops");
                    if (shader != null)
                    {
                        _opsMaterial = new Material(shader);
                    }
                }

                return _opsMaterial;
            }
        }

        public static bool IsAvailable => OpsMaterial != null;

        public static void Reset()
        {
            GpuUtils.DestroyObject(ref _opsMaterial);
        }
    }

    public static class GpuBlur
    {
        private const float MaxSigmaPerPass = 3.3f;
        private const float ReconstructSigma = 2.2f;
        private const int MaxPyramidDepth = 4; // up to 1/16 resolution

        public static bool IsAvailable => GpuOpsResources.IsAvailable;

        public static void Blur(RenderTexture source, RenderTexture destination, float sigmaPx)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            if (sigmaPx <= 0.05f || mat == null)
            {
                if (source != destination)
                {
                    Graphics.Blit(source, destination);
                }
                return;
            }

            if (source == destination)
            {
                BlurInPlace(destination, sigmaPx);
                return;
            }

            if (sigmaPx <= MaxSigmaPerPass)
            {
                RenderTextureReadWrite rw = destination.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                RenderTexture scratch = GpuUtils.GetTempRt(destination.width, destination.height, destination.format, FilterMode.Bilinear, rw);
                try
                {
                    mat.SetFloat("_SigmaPx", sigmaPx);
                    Graphics.Blit(source, scratch, mat, 0);
                    Graphics.Blit(scratch, destination, mat, 1);
                }
                finally
                {
                    GpuUtils.ReleaseTemp(scratch);
                }
                return;
            }

            Graphics.Blit(source, destination);
            BlurInPlace(destination, sigmaPx);
        }

        public static void BlurInPlace(RenderTexture target, float sigmaPx)
        {
            if (target == null || sigmaPx <= 0.05f)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null)
            {
                return;
            }

            int w = target.width;
            int h = target.height;
            RenderTextureFormat format = target.format;
            RenderTextureReadWrite rw = target.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;

            int depth = ChoosePyramidDepth(sigmaPx, w, h);
            RenderTexture[] levels = new RenderTexture[depth + 1];
            RenderTexture[] scratches = new RenderTexture[depth + 1];

            try
            {
                levels[0] = target;
                scratches[0] = GpuUtils.GetTempRt(w, h, format, FilterMode.Bilinear, rw);

                for (int k = 1; k <= depth; k++)
                {
                    int lw = Mathf.Max(1, w >> k);
                    int lh = Mathf.Max(1, h >> k);
                    levels[k] = GpuUtils.GetTempRt(lw, lh, format, FilterMode.Bilinear, rw);
                    scratches[k] = GpuUtils.GetTempRt(lw, lh, format, FilterMode.Bilinear, rw);
                    Graphics.Blit(levels[k - 1], levels[k]);
                }

                float reconVar = (depth == 0)
                    ? 0f
                    : ReconstructSigma * ReconstructSigma * ((Mathf.Pow(4f, depth) - 1f) / 3f);
                float remVar = Mathf.Max(0.25f, sigmaPx * sigmaPx - reconVar);
                float sigmaAtDepth = (depth == 0) ? sigmaPx : (Mathf.Sqrt(remVar) / (1 << depth));

                RunMultiPassGaussian(mat, levels[depth], scratches[depth], sigmaAtDepth);

                for (int k = depth; k >= 1; k--)
                {
                    Graphics.Blit(levels[k], levels[k - 1]);
                    RunSingleGaussian(mat, levels[k - 1], scratches[k - 1], ReconstructSigma);
                }
            }
            finally
            {
                for (int k = 0; k <= depth; k++)
                {
                    if (k > 0)
                    {
                        GpuUtils.ReleaseTemp(levels[k]);
                    }
                    GpuUtils.ReleaseTemp(scratches[k]);
                }
            }
        }

        private static int ChoosePyramidDepth(float sigmaPx, int w, int h)
        {
            int depth;
            if (sigmaPx <= 6.0f) depth = 0;
            else if (sigmaPx <= 14.0f) depth = 1;
            else if (sigmaPx <= 28.0f) depth = 2;
            else if (sigmaPx <= 60.0f) depth = 3;
            else depth = MaxPyramidDepth;

            int minDim = Mathf.Min(w, h);
            while (depth > 0 && (minDim >> depth) < 2)
            {
                depth--;
            }

            return depth;
        }

        private static void RunMultiPassGaussian(Material mat, RenderTexture target, RenderTexture scratch, float totalSigma)
        {
            int passes = Mathf.Max(1, Mathf.CeilToInt((totalSigma * totalSigma) / (MaxSigmaPerPass * MaxSigmaPerPass)));
            float sigmaPerPass = totalSigma / Mathf.Sqrt(passes);
            for (int p = 0; p < passes; p++)
            {
                RunSingleGaussian(mat, target, scratch, sigmaPerPass);
            }
        }

        private static void RunSingleGaussian(Material mat, RenderTexture target, RenderTexture scratch, float sigma)
        {
            mat.SetFloat("_SigmaPx", sigma);
            Graphics.Blit(target, scratch, mat, 0);
            Graphics.Blit(scratch, target, mat, 1);
        }
    }

    public static class GpuJfa
    {
        public static bool IsAvailable => GpuOpsResources.IsAvailable;

        public static RenderTexture Run(RenderTexture seedBuffer, RenderTexture pingBuffer, int maxStep = 64)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || seedBuffer == null || pingBuffer == null)
            {
                return seedBuffer;
            }

            RenderTexture src = seedBuffer;
            RenderTexture dst = pingBuffer;

            for (int step = Mathf.Max(1, maxStep); step >= 1; step >>= 1)
            {
                mat.SetFloat("_JfaStep", step);
                Graphics.Blit(src, dst, mat, 2);

                RenderTexture tmp = src;
                src = dst;
                dst = tmp;
            }

            return src;
        }

        public static void ResolveDistance(RenderTexture jfaResult, RenderTexture destination, float fallbackDistance = 0f)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || jfaResult == null || destination == null)
            {
                return;
            }

            mat.SetFloat("_FallbackDistance", fallbackDistance);
            Graphics.Blit(jfaResult, destination, mat, 3);
        }
    }
}