using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class CutoutFilter : FilterBase
    {
        private const int MaxLevels = 32;
        private const int KMeansIters = 6;
        private const float TemporalAlpha = 0.15f;
        private static readonly int[] JfaSteps = { 64, 32, 16, 8, 4, 2, 1 };

        private ComputeShader _compute;

        private int _kernelInitCentroids;
        private int _kernelClearKMeans;
        private int _kernelKMeansStep;
        private int _kernelKMeansUpdate;
        private int _kernelSmoothCentroids;
        private int _kernelCCLInit;
        private int _kernelCCLMergeBorders;
        private int _kernelCCLFlattenAndCount;
        private int _kernelClearAreaHist;
        private int _kernelAreaHist;
        private int _kernelAreaFindMedian;
        private int _kernelBuildHoleSeeds;
        private int _kernelReduceMinMax;
        private int _kernelSmoothMinMax;

        private ComputeBuffer _targetCentroidsBuffer;
        private ComputeBuffer _centroidsBuffer;
        private ComputeBuffer _kMeansAccumBuffer;
        private ComputeBuffer _maxErrorBuffer;
        private ComputeBuffer _parentBuffer;
        private ComputeBuffer _areaBuffer;
        private ComputeBuffer _areaHistBuffer;
        private ComputeBuffer _statsBuffer;
        private ComputeBuffer _temporalStateBuffer;

        private int _allocW = -1;
        private int _allocH = -1;
        private bool _centroidsInitialized;
        private bool _hasAreaHistory;
        private bool _hasMinMaxHistory;

        private int _numLevels = 4;
        private float _edgeSimplicity = 0.5f;
        private int _edgeFidelity = 4;
        private bool _normalize = true;

        public override string Id => "fx_cutout";
        public override string DisplayName => "Cutout";
        public override string Description => "Reduces colors into clean paper-cutout shapes with small-island removal.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable => base.IsAvailable && _compute != null && SystemInfo.supportsComputeShaders;

        public CutoutFilter()
        {
            AddParameter(FilterParameter.Int(
                "num_levels",
                "Number of Levels",
                "Number of quantized cutout color layers.",
                2, 32, 4,
                v =>
                {
                    if (_numLevels != v)
                    {
                        _numLevels = v;
                        _centroidsInitialized = false;
                    }
                }));

            AddParameter(FilterParameter.Float(
                "edge_simplicity",
                "Edge Simplicity",
                "Area threshold (% of median region area) for removing small islands.",
                0f, 3f, 0.5f,
                v => _edgeSimplicity = v));

            AddParameter(FilterParameter.Int(
                "edge_fidelity",
                "Edge Fidelity",
                "Preserves finer silhouette detail (lower = stronger median smoothing).",
                0, 10, 4,
                v => _edgeFidelity = v));

            AddParameter(FilterParameter.Bool(
                "normalize",
                "Normalize",
                "Stretch final color range to [0, 255].",
                true,
                v => _normalize = v));
        }

        public override void Initialize()
        {
            if (!TryLoadComputeShader("GmicCutout", out _compute) ||
                !TryCreateMaterial("CoreGMIC/Cutout"))
            {
                _compute = null;
                return;
            }

            _kernelInitCentroids = _compute.FindKernel("CSInitCentroids");
            _kernelClearKMeans = _compute.FindKernel("CSClearKMeans");
            _kernelKMeansStep = _compute.FindKernel("CSKMeansStep");
            _kernelKMeansUpdate = _compute.FindKernel("CSKMeansUpdate");
            _kernelSmoothCentroids = _compute.FindKernel("CSSmoothCentroids");
            _kernelCCLInit = _compute.FindKernel("CSCCLInit");
            _kernelCCLMergeBorders = _compute.FindKernel("CSCCLMergeBorders");
            _kernelCCLFlattenAndCount = _compute.FindKernel("CSCCLFlattenAndCount");
            _kernelClearAreaHist = _compute.FindKernel("CSClearAreaHist");
            _kernelAreaHist = _compute.FindKernel("CSAreaHist");
            _kernelAreaFindMedian = _compute.FindKernel("CSAreaFindMedian");
            _kernelBuildHoleSeeds = _compute.FindKernel("CSBuildHoleSeeds");
            _kernelReduceMinMax = _compute.FindKernel("CSReduceMinMax");
            _kernelSmoothMinMax = _compute.FindKernel("CSSmoothMinMax");

            _targetCentroidsBuffer = new ComputeBuffer(MaxLevels, sizeof(float) * 4);
            _centroidsBuffer = new ComputeBuffer(MaxLevels, sizeof(float) * 4);
            _kMeansAccumBuffer = new ComputeBuffer(MaxLevels * 4, sizeof(uint));
            _maxErrorBuffer = new ComputeBuffer(1, sizeof(uint));
            _areaHistBuffer = new ComputeBuffer(64, sizeof(uint));
            _statsBuffer = new ComputeBuffer(3, sizeof(uint));
            _temporalStateBuffer = new ComputeBuffer(1, sizeof(float) * 4);
            _temporalStateBuffer.SetData(new[] { Vector4.zero });

            _centroidsInitialized = false;
            _hasAreaHistory = false;
            _hasMinMaxHistory = false;
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            EnsurePixelBuffers(w, h);

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat jfaFormat = GpuUtils.FloatFormat;

            RenderTexture medA = null;
            RenderTexture medB = null;
            RenderTexture dsA = null;
            RenderTexture dsB = null;
            RenderTexture quantRt = null;
            RenderTexture jfaA = null;
            RenderTexture jfaB = null;
            RenderTexture resolvedRt = null;

            try
            {
                // 1. Separable Median {10 - EdgeFidelity}
                RenderTexture preQuant = source;
                int medianSize = Mathf.Clamp(10 - _edgeFidelity, 0, 10);
                if (medianSize > 1)
                {
                    medA = GpuUtils.GetTempRt(w, h, colorFormat);
                    medB = GpuUtils.GetTempRt(w, h, colorFormat);

                    Material.SetInt("_MedianSize", medianSize);
                    Graphics.Blit(source, medA, Material, 0);
                    Graphics.Blit(medA, medB, Material, 1);
                    preQuant = medB;
                }

                // 2. Downsample to 64x64 via bilinear pyramid for stable, flicker-free K-Means.
                dsA = GpuUtils.GetTempRt(256, 144, colorFormat);
                dsB = GpuUtils.GetTempRt(64, 64, colorFormat);
                Graphics.Blit(preQuant, dsA);
                Graphics.Blit(dsA, dsB);
                RunKMeans(dsB);

                quantRt = GpuUtils.GetTempRt(w, h, colorFormat, FilterMode.Point);
                Material.SetInt("_NumLevels", _numLevels);
                Material.SetBuffer("_Centroids", _centroidsBuffer);
                Graphics.Blit(preQuant, quantRt, Material, 2);

                RenderTexture cutoutOut = quantRt;

                // 3. GPU Connected-Component Area + Median + JFA Inpaint Holes
                if (_edgeSimplicity > 0.0001f)
                {
                    int groupsX = (w + 15) / 16;
                    int groupsY = (h + 15) / 16;

                    _compute.SetInt("_Width", w);
                    _compute.SetInt("_Height", h);
                    _compute.SetFloat("_EdgeSimplicity", _edgeSimplicity);
                    _compute.SetFloat("_TemporalAlpha", TemporalAlpha);
                    _compute.SetInt("_ResetAreaHistory", _hasAreaHistory ? 0 : 1);

                    _compute.SetTexture(_kernelCCLInit, "_QuantTex", quantRt);
                    _compute.SetBuffer(_kernelCCLInit, "_ParentBuffer", _parentBuffer);
                    _compute.SetBuffer(_kernelCCLInit, "_AreaBuffer", _areaBuffer);
                    _compute.Dispatch(_kernelCCLInit, groupsX, groupsY, 1);

                    _compute.SetTexture(_kernelCCLMergeBorders, "_QuantTex", quantRt);
                    _compute.SetBuffer(_kernelCCLMergeBorders, "_ParentBuffer", _parentBuffer);
                    _compute.Dispatch(_kernelCCLMergeBorders, groupsX, groupsY, 1);

                    _compute.SetBuffer(_kernelCCLFlattenAndCount, "_ParentBuffer", _parentBuffer);
                    _compute.SetBuffer(_kernelCCLFlattenAndCount, "_AreaBuffer", _areaBuffer);
                    _compute.Dispatch(_kernelCCLFlattenAndCount, groupsX, groupsY, 1);

                    _compute.SetBuffer(_kernelClearAreaHist, "_AreaHist", _areaHistBuffer);
                    _compute.SetBuffer(_kernelClearAreaHist, "_StatsBuf", _statsBuffer);
                    _compute.Dispatch(_kernelClearAreaHist, 1, 1, 1);

                    _compute.SetBuffer(_kernelAreaHist, "_ParentBuffer", _parentBuffer);
                    _compute.SetBuffer(_kernelAreaHist, "_AreaBuffer", _areaBuffer);
                    _compute.SetBuffer(_kernelAreaHist, "_AreaHist", _areaHistBuffer);
                    _compute.Dispatch(_kernelAreaHist, groupsX, groupsY, 1);

                    _compute.SetBuffer(_kernelAreaFindMedian, "_AreaHist", _areaHistBuffer);
                    _compute.SetBuffer(_kernelAreaFindMedian, "_StatsBuf", _statsBuffer);
                    _compute.SetBuffer(_kernelAreaFindMedian, "_TemporalState", _temporalStateBuffer);
                    _compute.Dispatch(_kernelAreaFindMedian, 1, 1, 1);
                    _hasAreaHistory = true;

                    jfaA = GpuUtils.GetTempRt(w, h, jfaFormat, FilterMode.Point, RenderTextureReadWrite.Linear, randomWrite: true);
                    jfaB = GpuUtils.GetTempRt(w, h, jfaFormat, FilterMode.Point);

                    _compute.SetBuffer(_kernelBuildHoleSeeds, "_ParentBuffer", _parentBuffer);
                    _compute.SetBuffer(_kernelBuildHoleSeeds, "_AreaBuffer", _areaBuffer);
                    _compute.SetBuffer(_kernelBuildHoleSeeds, "_StatsBuf", _statsBuffer);
                    _compute.SetTexture(_kernelBuildHoleSeeds, "_JfaOutTex", jfaA);
                    _compute.Dispatch(_kernelBuildHoleSeeds, groupsX, groupsY, 1);

                    RenderTexture jfaSrc = jfaA;
                    RenderTexture jfaDst = jfaB;
                    for (int s = 0; s < JfaSteps.Length; s++)
                    {
                        Material.SetFloat("_JfaStep", JfaSteps[s]);
                        Graphics.Blit(jfaSrc, jfaDst, Material, 3);
                        jfaSrc = jfaDst;
                        jfaDst = (jfaSrc == jfaA) ? jfaB : jfaA;
                    }

                    Material.SetTexture("_JfaTex", jfaSrc);
                    Material.SetTexture("_BaseTex", source);

                    if (_normalize)
                    {
                        resolvedRt = GpuUtils.GetTempRt(w, h, colorFormat, FilterMode.Point);
                        Graphics.Blit(quantRt, resolvedRt, Material, 4);
                        cutoutOut = resolvedRt;
                    }
                    else
                    {
                        Graphics.Blit(quantRt, destination, Material, 4);
                        return;
                    }
                }
                else if (!_normalize)
                {
                    Material.SetTexture("_JfaTex", Texture2D.blackTexture);
                    Material.SetTexture("_BaseTex", source);
                    Graphics.Blit(quantRt, destination, Material, 4);
                    return;
                }

                // 4. Optional Normalize [0, 255] with temporal EMA smoothing
                if (_normalize)
                {
                    int groupsX = (w + 15) / 16;
                    int groupsY = (h + 15) / 16;

                    _compute.SetInt("_Width", w);
                    _compute.SetInt("_Height", h);
                    _compute.SetFloat("_TemporalAlpha", TemporalAlpha);
                    _compute.SetInt("_ResetMinMaxHistory", _hasMinMaxHistory ? 0 : 1);

                    _compute.SetBuffer(_kernelClearAreaHist, "_AreaHist", _areaHistBuffer);
                    _compute.SetBuffer(_kernelClearAreaHist, "_StatsBuf", _statsBuffer);
                    _compute.Dispatch(_kernelClearAreaHist, 1, 1, 1);

                    _compute.SetTexture(_kernelReduceMinMax, "_InputTex", cutoutOut);
                    _compute.SetBuffer(_kernelReduceMinMax, "_StatsBuf", _statsBuffer);
                    _compute.Dispatch(_kernelReduceMinMax, groupsX, groupsY, 1);

                    _compute.SetBuffer(_kernelSmoothMinMax, "_StatsBuf", _statsBuffer);
                    _compute.SetBuffer(_kernelSmoothMinMax, "_TemporalState", _temporalStateBuffer);
                    _compute.Dispatch(_kernelSmoothMinMax, 1, 1, 1);
                    _hasMinMaxHistory = true;

                    Material.SetTexture("_BaseTex", source);
                    Material.SetBuffer("_StatsBuf", _statsBuffer);
                    Graphics.Blit(cutoutOut, destination, Material, 5);
                }
                else
                {
                    Graphics.Blit(cutoutOut, destination);
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(medA);
                GpuUtils.ReleaseTemp(medB);
                GpuUtils.ReleaseTemp(dsA);
                GpuUtils.ReleaseTemp(dsB);
                GpuUtils.ReleaseTemp(quantRt);
                GpuUtils.ReleaseTemp(jfaA);
                GpuUtils.ReleaseTemp(jfaB);
                GpuUtils.ReleaseTemp(resolvedRt);
            }
        }

        public override void ResetParameters()
        {
            base.ResetParameters();
            _centroidsInitialized = false;
            _hasAreaHistory = false;
            _hasMinMaxHistory = false;
        }

        public override void Dispose()
        {
            GpuUtils.ReleaseBuffer(ref _targetCentroidsBuffer);
            GpuUtils.ReleaseBuffer(ref _centroidsBuffer);
            GpuUtils.ReleaseBuffer(ref _kMeansAccumBuffer);
            GpuUtils.ReleaseBuffer(ref _maxErrorBuffer);
            GpuUtils.ReleaseBuffer(ref _parentBuffer);
            GpuUtils.ReleaseBuffer(ref _areaBuffer);
            GpuUtils.ReleaseBuffer(ref _areaHistBuffer);
            GpuUtils.ReleaseBuffer(ref _statsBuffer);
            GpuUtils.ReleaseBuffer(ref _temporalStateBuffer);

            _allocW = -1;
            _allocH = -1;
            _centroidsInitialized = false;
            _hasAreaHistory = false;
            _hasMinMaxHistory = false;
            _compute = null;
            base.Dispose();
        }

        private void EnsurePixelBuffers(int w, int h)
        {
            if (_parentBuffer != null && _areaBuffer != null && _allocW == w && _allocH == h)
            {
                return;
            }

            GpuUtils.ReleaseBuffer(ref _parentBuffer);
            GpuUtils.ReleaseBuffer(ref _areaBuffer);

            int totalPixels = Mathf.Max(1, w * h);
            _parentBuffer = new ComputeBuffer(totalPixels, sizeof(uint));
            _areaBuffer = new ComputeBuffer(totalPixels, sizeof(uint));
            _allocW = w;
            _allocH = h;
            _centroidsInitialized = false;
            _hasAreaHistory = false;
            _hasMinMaxHistory = false;
        }

        private void RunKMeans(RenderTexture inputTex)
        {
            _compute.SetInt("_NumLevels", _numLevels);
            _compute.SetFloat("_TemporalAlpha", TemporalAlpha);

            bool resetCentroids = !_centroidsInitialized;
            if (resetCentroids)
            {
                _compute.SetTexture(_kernelInitCentroids, "_InputTex", inputTex);
                _compute.SetBuffer(_kernelInitCentroids, "_TargetCentroids", _targetCentroidsBuffer);
                _compute.SetBuffer(_kernelInitCentroids, "_Centroids", _centroidsBuffer);
                _compute.Dispatch(_kernelInitCentroids, 1, 1, 1);
                _centroidsInitialized = true;
            }

            for (int iter = 0; iter < KMeansIters; iter++)
            {
                _compute.SetBuffer(_kernelClearKMeans, "_KMeansAccum", _kMeansAccumBuffer);
                _compute.SetBuffer(_kernelClearKMeans, "_MaxErrorSample", _maxErrorBuffer);
                _compute.Dispatch(_kernelClearKMeans, 1, 1, 1);

                _compute.SetTexture(_kernelKMeansStep, "_InputTex", inputTex);
                _compute.SetBuffer(_kernelKMeansStep, "_TargetCentroids", _targetCentroidsBuffer);
                _compute.SetBuffer(_kernelKMeansStep, "_KMeansAccum", _kMeansAccumBuffer);
                _compute.SetBuffer(_kernelKMeansStep, "_MaxErrorSample", _maxErrorBuffer);
                _compute.Dispatch(_kernelKMeansStep, 4, 4, 1);

                _compute.SetTexture(_kernelKMeansUpdate, "_InputTex", inputTex);
                _compute.SetBuffer(_kernelKMeansUpdate, "_TargetCentroids", _targetCentroidsBuffer);
                _compute.SetBuffer(_kernelKMeansUpdate, "_KMeansAccum", _kMeansAccumBuffer);
                _compute.SetBuffer(_kernelKMeansUpdate, "_MaxErrorSample", _maxErrorBuffer);
                _compute.Dispatch(_kernelKMeansUpdate, 1, 1, 1);
            }

            _compute.SetInt("_ResetHistory", resetCentroids ? 1 : 0);
            _compute.SetBuffer(_kernelSmoothCentroids, "_TargetCentroids", _targetCentroidsBuffer);
            _compute.SetBuffer(_kernelSmoothCentroids, "_Centroids", _centroidsBuffer);
            _compute.Dispatch(_kernelSmoothCentroids, 1, 1, 1);
        }
    }
}