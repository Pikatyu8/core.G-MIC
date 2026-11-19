using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class VectorPaintingFilter : FilterBase
    {
        private ComputeShader _compute;

        private int _kernelCCLInit;
        private int _kernelCCLMergeBorders;
        private int _kernelCCLFlattenAndAccum;

        private ComputeBuffer _parentBuffer;
        private ComputeBuffer _shapeSumBuffer;
        private RenderTexture _prevSourceRt;
        private bool _hasPrevSource;
        private int _allocW = -1;
        private int _allocH = -1;
        private RenderTextureFormat _allocSourceFormat;

        private float _details = 9.0f;
        private float _colorThreshold = 0.0f;
        private float _minAlpha = 0.15f;

        public override string Id => "fx_vector_painting";
        public override string DisplayName => "Vector Painting";
        public override string Description => "Simulates a vector art style with clean areas of color.";
        public override FilterCategory Category => FilterCategory.Experimental;
        public override bool IsAvailable =>
            base.IsAvailable && GpuBlur.IsAvailable && _compute != null && SystemInfo.supportsComputeShaders;

        public VectorPaintingFilter()
        {
            AddParameter(FilterParameter.Float(
                "details",
                "Details",
                "Controls vector region granularity (higher = finer shapes).",
                7.0f, 10.0f, 9.0f,
                v => _details = v,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "color_threshold",
                "Stability Threshold",
                "Coherent motion threshold separating real motion from frame noise.",
                0.0f, 0.2f, 0.0f,
                v => _colorThreshold = v,
                step: 0.002f));

            AddParameter(FilterParameter.Float(
                "min_alpha",
                "Trail Fade Speed",
                "Convergence speed in static areas (higher = faster trail fade, lower = smoother).",
                0.02f, 1.0f, 0.15f,
                v => _minAlpha = v,
                step: 0.01f));
        }

        public override void Initialize()
        {
            if (!TryLoadComputeShader("GmicVectorPainting", out _compute) ||
                !TryCreateMaterial("CoreGMIC/VectorPainting"))
            {
                _compute = null;
                return;
            }

            _kernelCCLInit = _compute.FindKernel("CSCCLInit");
            _kernelCCLMergeBorders = _compute.FindKernel("CSCCLMergeBorders");
            _kernelCCLFlattenAndAccum = _compute.FindKernel("CSCCLFlattenAndAccum");
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;

            RenderTextureFormat lumFormat = GpuUtils.LuminanceFormat;
            RenderTextureFormat labelFormat = GpuUtils.ColorFormat;
            RenderTextureFormat stableFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : source.format;

            EnsureBuffers(w, h, stableFormat);

            RenderTexture stableSource = null;
            RenderTexture lumFull = null;
            RenderTexture labelRt = null;
            RenderTexture labelCleanRt = null;

            try
            {
                // Soft adaptive IIR in ARGBHalf eliminates 8-bit quantization trails and grain flicker.
                RenderTexture activeSource = source;
                if (_colorThreshold > 0.0001f)
                {
                    if (_hasPrevSource)
                    {
                        stableSource = GpuUtils.GetTempRt(w, h, stableFormat, FilterMode.Point);

                        Material.SetTexture("_PrevSourceTex", _prevSourceRt);
                        Material.SetFloat("_ColorThreshold", _colorThreshold);
                        Material.SetFloat("_MinAlpha", _minAlpha);
                        Graphics.Blit(source, stableSource, Material, 4);
                        activeSource = stableSource;
                    }

                    Graphics.Blit(activeSource, _prevSourceRt);
                    _hasPrevSource = true;
                }
                else
                {
                    _hasPrevSource = false;
                }

                lumFull = GpuUtils.GetTempRt(w, h, lumFormat);
                Graphics.Blit(activeSource, lumFull, Material, 0);

                float sigmaFull = (10f - _details) * 0.01f * Mathf.Max(w, h);
                GpuBlur.BlurInPlace(lumFull, sigmaFull);

                // Classify gradient direction and smooth out 1px bridges/speckles with two 3x3 mode passes.
                labelRt = GpuUtils.GetTempRt(w, h, labelFormat, FilterMode.Point);
                labelCleanRt = GpuUtils.GetTempRt(w, h, labelFormat, FilterMode.Point);

                Graphics.Blit(lumFull, labelRt, Material, 1);
                Graphics.Blit(labelRt, labelCleanRt, Material, 3);
                Graphics.Blit(labelCleanRt, labelRt, Material, 3);

                int groupsX = (w + 15) / 16;
                int groupsY = (h + 15) / 16;

                _compute.SetInt("_Width", w);
                _compute.SetInt("_Height", h);

                _compute.SetTexture(_kernelCCLInit, "_LabelTex", labelRt);
                _compute.SetBuffer(_kernelCCLInit, "_ParentBuffer", _parentBuffer);
                _compute.SetBuffer(_kernelCCLInit, "_ShapeSumBuffer", _shapeSumBuffer);
                _compute.Dispatch(_kernelCCLInit, groupsX, groupsY, 1);

                _compute.SetTexture(_kernelCCLMergeBorders, "_LabelTex", labelRt);
                _compute.SetBuffer(_kernelCCLMergeBorders, "_ParentBuffer", _parentBuffer);
                _compute.Dispatch(_kernelCCLMergeBorders, groupsX, groupsY, 1);
                _compute.Dispatch(_kernelCCLMergeBorders, groupsX, groupsY, 1);

                _compute.SetTexture(_kernelCCLFlattenAndAccum, "_SourceTex", activeSource);
                _compute.SetBuffer(_kernelCCLFlattenAndAccum, "_ParentBuffer", _parentBuffer);
                _compute.SetBuffer(_kernelCCLFlattenAndAccum, "_ShapeSumBuffer", _shapeSumBuffer);
                _compute.Dispatch(_kernelCCLFlattenAndAccum, groupsX, groupsY, 1);

                Material.SetInt("_Width", w);
                Material.SetInt("_Height", h);
                Material.SetBuffer("_ParentBuffer", _parentBuffer);
                Material.SetBuffer("_ShapeSumBuffer", _shapeSumBuffer);
                Graphics.Blit(activeSource, destination, Material, 2);
            }
            finally
            {
                GpuUtils.ReleaseTemp(stableSource);
                GpuUtils.ReleaseTemp(lumFull);
                GpuUtils.ReleaseTemp(labelRt);
                GpuUtils.ReleaseTemp(labelCleanRt);
            }
        }

        public override void ResetParameters()
        {
            base.ResetParameters();
            _hasPrevSource = false;
        }

        public override void Dispose()
        {
            GpuUtils.ReleaseRt(ref _prevSourceRt);
            GpuUtils.ReleaseBuffer(ref _parentBuffer);
            GpuUtils.ReleaseBuffer(ref _shapeSumBuffer);
            _allocW = -1;
            _allocH = -1;
            _hasPrevSource = false;
            _compute = null;
            base.Dispose();
        }

        private void EnsureBuffers(int w, int h, RenderTextureFormat stableFormat)
        {
            if (_parentBuffer != null && _shapeSumBuffer != null && _prevSourceRt != null &&
                _allocW == w && _allocH == h && _allocSourceFormat == stableFormat)
            {
                return;
            }

            GpuUtils.ReleaseBuffer(ref _parentBuffer);
            GpuUtils.ReleaseBuffer(ref _shapeSumBuffer);
            GpuUtils.ReleaseRt(ref _prevSourceRt);

            int totalPixels = Mathf.Max(1, w * h);
            _parentBuffer = new ComputeBuffer(totalPixels, sizeof(uint));
            _shapeSumBuffer = new ComputeBuffer(totalPixels * 4, sizeof(uint));

            _prevSourceRt = GpuUtils.CreateRt(w, h, stableFormat, FilterMode.Point, RenderTextureReadWrite.Linear);

            _allocW = w;
            _allocH = h;
            _allocSourceFormat = stableFormat;
            _hasPrevSource = false;
        }
    }
}