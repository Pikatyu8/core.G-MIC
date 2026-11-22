using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    internal static class GpuOpsResources
    {
        private static Material _opsMaterial;
        private static GpuStats _sharedStats;

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

        public static GpuStats SharedStats
        {
            get
            {
                if (_sharedStats == null)
                {
                    GpuStats.TryCreate(out _sharedStats);
                }

                return _sharedStats;
            }
        }

        public static bool IsAvailable => OpsMaterial != null;

        public static void Reset()
        {
            GpuUtils.DestroyObject(ref _opsMaterial);
            _sharedStats?.Dispose();
            _sharedStats = null;
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

        public static void BlurSeparable(RenderTexture source, RenderTexture destination, float sigmaX, float sigmaY)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            bool doX = sigmaX > 0.05f;
            bool doY = sigmaY > 0.05f;
            if (mat == null || (!doX && !doY))
            {
                if (source != destination)
                {
                    Graphics.Blit(source, destination);
                }
                return;
            }

            int passesX = doX ? Mathf.Max(1, Mathf.CeilToInt((sigmaX * sigmaX) / (MaxSigmaPerPass * MaxSigmaPerPass))) : 0;
            int passesY = doY ? Mathf.Max(1, Mathf.CeilToInt((sigmaY * sigmaY) / (MaxSigmaPerPass * MaxSigmaPerPass))) : 0;
            float perPassX = doX ? (sigmaX / Mathf.Sqrt(passesX)) : 0f;
            float perPassY = doY ? (sigmaY / Mathf.Sqrt(passesY)) : 0f;
            int totalPasses = passesX + passesY;

            RenderTextureReadWrite rw = destination.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            RenderTexture scratchA = null;
            RenderTexture scratchB = null;

            try
            {
                if (totalPasses > 1 || source == destination)
                {
                    scratchA = GpuUtils.GetTempRt(destination.width, destination.height, destination.format, FilterMode.Bilinear, rw);
                }
                if (totalPasses > 2 || (totalPasses > 1 && source == destination))
                {
                    scratchB = GpuUtils.GetTempRt(destination.width, destination.height, destination.format, FilterMode.Bilinear, rw);
                }

                RenderTexture curr = source;
                for (int p = 0; p < totalPasses; p++)
                {
                    bool isX = p < passesX;
                    int axisPass = isX ? 0 : 1;
                    float sigma = isX ? perPassX : perPassY;
                    bool isLast = (p == totalPasses - 1);
                    RenderTexture next = isLast ? destination : ((curr == scratchA) ? scratchB : scratchA);

                    mat.SetFloat("_SigmaPx", sigma);
                    Graphics.Blit(curr, next, mat, axisPass);
                    curr = next;
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(scratchA);
                GpuUtils.ReleaseTemp(scratchB);
            }
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

    public static class GpuNormalize
    {
        public static bool IsAvailable =>
            GpuOpsResources.IsAvailable && GpuOpsResources.SharedStats != null && GpuOpsResources.SharedStats.IsAvailable;

        public static void Normalize(
            RenderTexture source,
            RenderTexture destination,
            RenderTexture preserveAlphaFrom = null,
            bool rgbMode = true,
            GpuStats stats = null,
            float temporalAlpha = 1f,
            bool resetHistory = false)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            GpuStats activeStats = stats ?? GpuOpsResources.SharedStats;
            if (mat == null || activeStats == null || !activeStats.IsAvailable)
            {
                if (source != destination)
                {
                    Graphics.Blit(source, destination);
                }
                return;
            }

            RenderTexture scratch = null;
            RenderTexture inputRt = source;
            if (source == destination)
            {
                RenderTextureReadWrite rw = source.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                scratch = GpuUtils.GetTempRt(source.width, source.height, source.format, source.filterMode, rw);
                Graphics.Blit(source, scratch);
                inputRt = scratch;
            }

            try
            {
                activeStats.ComputeMinMax(inputRt, rgbMode, temporalAlpha, resetHistory);
                mat.SetBuffer("_StatsBuf", activeStats.StatsBuffer);
                mat.SetFloat("_UseBaseAlpha", preserveAlphaFrom != null ? 1f : 0f);
                mat.SetTexture("_BaseTex", preserveAlphaFrom != null ? preserveAlphaFrom : Texture2D.whiteTexture);
                Graphics.Blit(inputRt, destination, mat, 5);
            }
            finally
            {
                GpuUtils.ReleaseTemp(scratch);
            }
        }

        public static void Cut01(RenderTexture source, RenderTexture destination, RenderTexture preserveAlphaFrom = null)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null)
            {
                if (source != destination)
                {
                    Graphics.Blit(source, destination);
                }
                return;
            }

            RenderTexture scratch = null;
            RenderTexture inputRt = source;
            if (source == destination)
            {
                RenderTextureReadWrite rw = source.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                scratch = GpuUtils.GetTempRt(source.width, source.height, source.format, source.filterMode, rw);
                Graphics.Blit(source, scratch);
                inputRt = scratch;
            }

            try
            {
                mat.SetFloat("_UseBaseAlpha", preserveAlphaFrom != null ? 1f : 0f);
                mat.SetTexture("_BaseTex", preserveAlphaFrom != null ? preserveAlphaFrom : Texture2D.whiteTexture);
                Graphics.Blit(inputRt, destination, mat, 10);
            }
            finally
            {
                GpuUtils.ReleaseTemp(scratch);
            }
        }
    }

    public static class GpuChannelOps
    {
        public static readonly string[] ChannelChoices =
        {
            "All",
            "RGBA [all]",
            "RGB [all]",
            "RGB [red]",
            "RGB [green]",
            "RGB [blue]",
            "RGBA [alpha]",
            "Linear RGB [all]",
            "Linear RGB [red]",
            "Linear RGB [green]",
            "Linear RGB [blue]",
            "YCbCr [Y]",
            "YCbCr [CbCr]",
            "YCbCr [Cb]",
            "YCbCr [Cr]",
            "YCbCr [Cg]",
            "Lab [L]",
            "Lab [ab]",
            "Lab [a]",
            "Lab [b]",
            "Lch [ch]",
            "Lch [c]",
            "Lch [h]",
            "HSV [H]",
            "HSV [S]",
            "HSV [V]",
            "HSI [I]",
            "HSL [L]",
            "CMYK [C]",
            "CMYK [M]",
            "CMYK [Y]",
            "CMYK [K]",
            "YIQ [Y]",
            "YIQ [IQ]"
        };

        public static bool IsAvailable => GpuOpsResources.IsAvailable;

        public static bool IsSingleChannel(int channelMode)
        {
            return channelMode != 0 &&
                   channelMode != 1 &&
                   channelMode != 2 &&
                   channelMode != 7 &&
                   channelMode != 12 &&
                   channelMode != 17 &&
                   channelMode != 20 &&
                   channelMode != 33;
        }

        public static void Encode(RenderTexture source, RenderTexture destination, int channelMode)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || source == null || destination == null)
            {
                return;
            }

            mat.SetInt("_ChannelMode", channelMode);
            Graphics.Blit(source, destination, mat, 6);
        }

        public static void DecodeAndMerge(
            RenderTexture originalRgba,
            RenderTexture processedEncoded,
            RenderTexture destination,
            int channelMode)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || originalRgba == null || processedEncoded == null || destination == null)
            {
                return;
            }

            mat.SetTexture("_BaseTex", originalRgba);
            mat.SetInt("_ChannelMode", channelMode);
            Graphics.Blit(processedEncoded, destination, mat, 7);
        }

        public static void ExtractSubset(RenderTexture encoded, RenderTexture destination, int channelMode)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || encoded == null || destination == null)
            {
                return;
            }

            mat.SetInt("_ChannelMode", channelMode);
            Graphics.Blit(encoded, destination, mat, 8);
        }

        public static void RepackSubsetAndDecode(
            RenderTexture originalRgba,
            RenderTexture processedSubset,
            RenderTexture destination,
            int channelMode)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || originalRgba == null || processedSubset == null || destination == null)
            {
                return;
            }

            mat.SetTexture("_BaseTex", originalRgba);
            mat.SetInt("_ChannelMode", channelMode);
            Graphics.Blit(processedSubset, destination, mat, 9);
        }

        /// <summary>
        /// Mirrors G'MIC's _apply_channels ("ac") with optional valueAction (0=None, 1=Cut, 2=Normalize).
        /// </summary>
        public static void ApplyInChannelSpace(
            RenderTexture source,
            RenderTexture destination,
            int channelMode,
            Action<RenderTexture, RenderTexture> applyOp,
            int valueAction = 0,
            GpuStats stats = null)
        {
            if (source == null || destination == null || applyOp == null)
            {
                return;
            }

            int w = source.width;
            int h = source.height;
            RenderTextureFormat floatFmt = GpuUtils.FloatFormat;

            if (channelMode <= 2)
            {
                bool preserveOrigAlpha = (channelMode == 2);
                RenderTexture workRt = null;
                RenderTexture safeSrc = source;
                RenderTexture tempCopy = null;

                if (source == destination)
                {
                    tempCopy = GpuUtils.GetTempRt(w, h, floatFmt);
                    Graphics.Blit(source, tempCopy);
                    safeSrc = tempCopy;
                }

                try
                {
                    if (valueAction == 0 && !preserveOrigAlpha)
                    {
                        applyOp(safeSrc, destination);
                        return;
                    }

                    workRt = GpuUtils.GetTempRt(w, h, floatFmt);
                    applyOp(safeSrc, workRt);

                    if (valueAction == 1)
                    {
                        GpuNormalize.Cut01(workRt, destination, preserveOrigAlpha ? safeSrc : null);
                    }
                    else if (valueAction == 2)
                    {
                        GpuNormalize.Normalize(workRt, destination, preserveOrigAlpha ? safeSrc : null, rgbMode: true, stats: stats);
                    }
                    else
                    {
                        DecodeAndMerge(safeSrc, workRt, destination, channelMode);
                    }
                }
                finally
                {
                    GpuUtils.ReleaseTemp(workRt);
                    GpuUtils.ReleaseTemp(tempCopy);
                }
                return;
            }

            RenderTexture safeOrig = source;
            RenderTexture origCopy = null;
            RenderTexture encRt = null;
            RenderTexture subRt = null;
            RenderTexture procSubRt = null;
            RenderTexture actionRt = null;

            try
            {
                if (source == destination)
                {
                    origCopy = GpuUtils.GetTempRt(w, h, floatFmt);
                    Graphics.Blit(source, origCopy);
                    safeOrig = origCopy;
                }

                encRt = GpuUtils.GetTempRt(w, h, floatFmt);
                subRt = GpuUtils.GetTempRt(w, h, floatFmt);
                procSubRt = GpuUtils.GetTempRt(w, h, floatFmt);

                Encode(safeOrig, encRt, channelMode);
                ExtractSubset(encRt, subRt, channelMode);
                applyOp(subRt, procSubRt);

                RenderTexture finalSub = procSubRt;
                if (valueAction == 1)
                {
                    actionRt = GpuUtils.GetTempRt(w, h, floatFmt);
                    GpuNormalize.Cut01(procSubRt, actionRt);
                    finalSub = actionRt;
                }
                else if (valueAction == 2)
                {
                    actionRt = GpuUtils.GetTempRt(w, h, floatFmt);
                    GpuNormalize.Normalize(procSubRt, actionRt, rgbMode: !IsSingleChannel(channelMode), stats: stats);
                    finalSub = actionRt;
                }

                RepackSubsetAndDecode(safeOrig, finalSub, destination, channelMode);
            }
            finally
            {
                GpuUtils.ReleaseTemp(origCopy);
                GpuUtils.ReleaseTemp(encRt);
                GpuUtils.ReleaseTemp(subRt);
                GpuUtils.ReleaseTemp(procSubRt);
                GpuUtils.ReleaseTemp(actionRt);
            }
        }
    }

    public static class GpuAnisoSmooth
    {
        public static bool IsAvailable => GpuOpsResources.IsAvailable && GpuBlur.IsAvailable;

        public static void ComputeStructureTensor(RenderTexture source, RenderTexture destination, float sigmaPx = 0f)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || source == null || destination == null)
            {
                return;
            }

            if (sigmaPx <= 0.05f)
            {
                Graphics.Blit(source, destination, mat, 11);
                return;
            }

            RenderTexture tensorB = GpuUtils.GetTempRt(source.width, source.height, GpuUtils.FloatFormat);
            try
            {
                Graphics.Blit(source, destination, mat, 11);
                mat.SetFloat("_SigmaPx", sigmaPx);
                Graphics.Blit(destination, tensorB, mat, 12);
                Graphics.Blit(tensorB, destination, mat, 13);
            }
            finally
            {
                GpuUtils.ReleaseTemp(tensorB);
            }
        }

        public static void Smooth(
            RenderTexture source,
            RenderTexture destination,
            float amplitude,
            float sharpness = 0f,
            float anisotropy = 1f,
            float alpha = 1f,
            float sigma = 1.1f,
            float dl = 0.8f)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || amplitude <= 0.01f)
            {
                if (source != destination)
                {
                    Graphics.Blit(source, destination);
                }
                return;
            }

            int w = source.width;
            int h = source.height;
            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat tensorFormat = GpuUtils.FloatFormat;

            int passes = Mathf.Clamp(Mathf.CeilToInt(amplitude / 30f), 1, 6);
            float stepDl = Mathf.Max(0.25f, dl * Mathf.Sqrt(amplitude / passes));

            RenderTexture pingA = null;
            RenderTexture pingB = null;
            RenderTexture preTensorRt = null;
            RenderTexture tensorA = null;
            RenderTexture tensorB = null;

            try
            {
                pingA = GpuUtils.GetTempRt(w, h, colorFormat);
                if (passes > 1 || source == destination)
                {
                    pingB = GpuUtils.GetTempRt(w, h, colorFormat);
                }

                if (alpha > 0.05f)
                {
                    preTensorRt = GpuUtils.GetTempRt(w, h, colorFormat);
                }

                tensorA = GpuUtils.GetTempRt(w, h, tensorFormat);
                if (sigma > 0.05f)
                {
                    tensorB = GpuUtils.GetTempRt(w, h, tensorFormat);
                }

                mat.SetFloat("_AnisoSharpness", sharpness);
                mat.SetFloat("_AnisoAnisotropy", anisotropy);
                mat.SetFloat("_AnisoDl", stepDl);

                RenderTexture curr = source;
                for (int p = 0; p < passes; p++)
                {
                    RenderTexture tensorSrc = curr;
                    if (preTensorRt != null)
                    {
                        GpuBlur.Blur(curr, preTensorRt, alpha);
                        tensorSrc = preTensorRt;
                    }

                    Graphics.Blit(tensorSrc, tensorA, mat, 11);
                    if (sigma > 0.05f && tensorB != null)
                    {
                        mat.SetFloat("_SigmaPx", sigma);
                        Graphics.Blit(tensorA, tensorB, mat, 12);
                        Graphics.Blit(tensorB, tensorA, mat, 13);
                    }

                    bool isLast = (p == passes - 1);
                    RenderTexture next = isLast
                        ? destination
                        : ((curr == pingA) ? pingB : pingA);

                    mat.SetTexture("_TensorTex", tensorA);
                    Graphics.Blit(curr, next, mat, 14);
                    curr = next;
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(pingA);
                GpuUtils.ReleaseTemp(pingB);
                GpuUtils.ReleaseTemp(preTensorRt);
                GpuUtils.ReleaseTemp(tensorA);
                GpuUtils.ReleaseTemp(tensorB);
            }
        }
    }

    public static class GpuNorm
    {
        public static bool IsAvailable => GpuOpsResources.IsAvailable;

        public static void ComputeNorm(RenderTexture source, RenderTexture destination)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || source == null || destination == null)
            {
                return;
            }

            Graphics.Blit(source, destination, mat, 15);
        }
    }

    public static class GpuGradientNorm
    {
        public static bool IsAvailable => GpuOpsResources.IsAvailable;

        public static void ComputeGradientNorm(RenderTexture source, RenderTexture destination, bool scalarMode = false)
        {
            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || source == null || destination == null)
            {
                return;
            }

            mat.SetInt("_ScalarMode", scalarMode ? 1 : 0);
            Graphics.Blit(source, destination, mat, 16);
        }
    }

    public static class GpuNoise
    {
        public static readonly string[] NoiseTypeChoices =
        {
            "Gaussian",
            "Uniform",
            "Salt and Pepper",
            "Poisson"
        };

        public static bool IsAvailable => GpuOpsResources.IsAvailable;

        public static void ApplyNoise(
            RenderTexture source,
            RenderTexture destination,
            float amount,
            int noiseType,
            uint seed)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null || amount <= 1e-5f)
            {
                if (source != destination)
                {
                    Graphics.Blit(source, destination);
                }
                return;
            }

            RenderTexture scratch = null;
            RenderTexture inputRt = source;
            if (source == destination)
            {
                RenderTextureReadWrite rw = source.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                scratch = GpuUtils.GetTempRt(source.width, source.height, source.format, source.filterMode, rw);
                Graphics.Blit(source, scratch);
                inputRt = scratch;
            }

            try
            {
                mat.SetInt("_NoiseType", noiseType);
                mat.SetFloat("_NoiseAmount", amount);
                mat.SetInt("_NoiseSeed", unchecked((int)seed));
                Graphics.Blit(inputRt, destination, mat, 17);
            }
            finally
            {
                GpuUtils.ReleaseTemp(scratch);
            }
        }
    }
}
