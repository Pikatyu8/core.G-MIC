using System;
using CoreGMIC.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreGMIC.Heavy
{
    public class PackEllipsesFilter : FilterBase
    {
        private const int MaxEllipses = 32768;

        private static readonly string[] OrientationChoices =
        {
            "Isotropic (Circles Only)",
            "Anisotropic (Along Contours)",
            "Anisotropic (Orthogonal to Contours)"
        };

        private static readonly string[] ResolutionChoices =
        {
            "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X8"
        };

        private ComputeShader _compute;
        private int _kernelClearSlicAccum;
        private int _kernelInitSlic;
        private int _kernelAssignSlic;
        private int _kernelUpdateSlic;
        private int _kernelInitOccupancy;
        private int _kernelPackRound;
        private int _kernelBuildArgs;

        private CommandBuffer _cmd;

        private ComputeBuffer _ellipseGeomBuffer;
        private ComputeBuffer _ellipseDataBuffer;
        private ComputeBuffer _countBuffer;
        private ComputeBuffer _argsBuffer;

        private ComputeBuffer _slicSeedsA;
        private ComputeBuffer _slicSeedsB;
        private ComputeBuffer _slicAccum;
        private int _allocSlicSeeds = -1;

        private RenderTexture _occRt;
        private RenderTexture _slicLabelRt;
        private int _allocW = -1;
        private int _allocH = -1;

        private float _minRadius = 3f;
        private float _maxRadius = 20f;
        private float _radiusDilation = 0f;
        private float _minIsotropy = 30f;
        private float _maxIsotropy = 100f;
        private int _isotropyLevels = 6;
        private int _orientation = 1;
        private int _regionAnalysis = 3;
        private float _bgR = 0f;
        private float _bgG = 0f;
        private float _bgB = 0f;
        private float _bgA = 255f;
        private int _renderFactor = 0;

        public override string Id => "fx_pack_ellipses";
        public override string DisplayName => "Pack Ellipses";
        public override string Description => "Fills the image with non-overlapping, contour-aligned packed ellipses.";
        public override FilterCategory Category => FilterCategory.Heavy;
        public override bool IsAvailable =>
            base.IsAvailable &&
            _compute != null &&
            SystemInfo.supportsComputeShaders &&
            GpuBlur.IsAvailable &&
            GpuNorm.IsAvailable &&
            GpuAnisoSmooth.IsAvailable;

        public PackEllipsesFilter()
        {
            AddParameter(FilterParameter.Float(
                "min_radius",
                "Min Radius (px)",
                "Minimum radius of packed ellipses in pixels.",
                1f, 256f, 3f,
                v => _minRadius = v,
                step: 0.5f));

            AddParameter(FilterParameter.Float(
                "max_radius",
                "Max Radius (px)",
                "Maximum radius of packed ellipses in pixels.",
                1f, 256f, 20f,
                v => _maxRadius = v,
                step: 0.5f));

            AddParameter(FilterParameter.Float(
                "radius_dilation",
                "Radius Dilation (px)",
                "Shrink or grow drawn ellipse radii after packing.",
                -10f, 10f, 0f,
                v => _radiusDilation = v,
                step: 0.25f));

            AddParameter(FilterParameter.Float(
                "min_isotropy",
                "Min Isotropy (%)",
                "Minimum ellipse minor/major axis ratio.",
                0f, 100f, 30f,
                v => _minIsotropy = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "max_isotropy",
                "Max Isotropy (%)",
                "Maximum ellipse minor/major axis ratio.",
                0f, 100f, 100f,
                v => _maxIsotropy = v,
                step: 1f));

            AddParameter(FilterParameter.Int(
                "isotropy_levels",
                "Isotropy Quantization",
                "Number of discrete isotropy levels (0 = continuous).",
                0, 16, 6,
                v => _isotropyLevels = v));

            AddParameter(FilterParameter.Choice(
                "orientation",
                "Orientation",
                "Ellipse alignment relative to local image contours.",
                OrientationChoices,
                1,
                v => _orientation = v));

            AddParameter(FilterParameter.Int(
                "region_analysis",
                "Region Analysis Size",
                "Superpixel contour barrier scale (0 = disabled).",
                0, 10, 3,
                v => _regionAnalysis = v));

            AddParameter(FilterParameter.Float(
                "bg_r",
                "Background Red",
                "Background red channel (0..255).",
                0f, 255f, 0f,
                v => _bgR = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "bg_g",
                "Background Green",
                "Background green channel (0..255).",
                0f, 255f, 0f,
                v => _bgG = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "bg_b",
                "Background Blue",
                "Background blue channel (0..255).",
                0f, 255f, 0f,
                v => _bgB = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "bg_a",
                "Background Alpha",
                "Background alpha channel (0..255).",
                0f, 255f, 255f,
                v => _bgA = v,
                step: 1f));

            AddParameter(FilterParameter.Choice(
                "render_factor",
                "Render Quality",
                "Edge anti-aliasing sharpness factor.",
                ResolutionChoices,
                0,
                v => _renderFactor = v));
        }

        public override void Initialize()
        {
            if (!TryLoadComputeShader("GmicPackEllipses", out _compute) ||
                !TryCreateMaterial("CoreGMIC/PackEllipses"))
            {
                _compute = null;
                return;
            }

            _kernelClearSlicAccum = _compute.FindKernel("CSClearSlicAccum");
            _kernelInitSlic = _compute.FindKernel("CSInitSlic");
            _kernelAssignSlic = _compute.FindKernel("CSAssignSlic");
            _kernelUpdateSlic = _compute.FindKernel("CSUpdateSlic");
            _kernelInitOccupancy = _compute.FindKernel("CSInitOccupancy");
            _kernelPackRound = _compute.FindKernel("CSPackRound");
            _kernelBuildArgs = _compute.FindKernel("CSBuildIndirectArgs");

            _cmd = new CommandBuffer
            {
                name = "CoreGMIC_PackEllipses"
            };

            _ellipseGeomBuffer = new ComputeBuffer(MaxEllipses, sizeof(float) * 4);
            _ellipseDataBuffer = new ComputeBuffer(MaxEllipses, sizeof(float) * 4);
            _countBuffer = new ComputeBuffer(1, sizeof(uint));
            _argsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            EnsureRenderTextures(w, h);

            float rMin = Mathf.Min(_minRadius, _maxRadius);
            float rMax = Mathf.Max(_minRadius, _maxRadius);
            float isoMin = Mathf.Min(_minIsotropy, _maxIsotropy) * 0.01f;
            float isoMax = Mathf.Max(_minIsotropy, _maxIsotropy) * 0.01f;

            RenderTextureFormat floatFmt = GpuUtils.FloatFormat;
            RenderTexture tensorRt = null;
            RenderTexture fieldRt = null;
            RenderTexture normRt = null;
            RenderTexture blurNormRt = null;

            try
            {
                // 1. Structure tensor -> smoothed 1% -> (isotropy, angle)
                tensorRt = GpuUtils.GetTempRt(w, h, floatFmt);
                float tensorSigma = Mathf.Max(1f, 0.01f * Mathf.Max(w, h));
                GpuAnisoSmooth.ComputeStructureTensor(source, tensorRt, tensorSigma);

                fieldRt = GpuUtils.GetTempRt(w, h, floatFmt, FilterMode.Point);
                Material.SetFloat("_IsotropyMin", isoMin);
                Material.SetFloat("_IsotropyMax", isoMax);
                Material.SetInt("_IsotropyLevels", _isotropyLevels);
                Material.SetInt("_OrientationMode", _orientation);
                Graphics.Blit(tensorRt, fieldRt, Material, 0);

                // 2. Optional Region Analysis (GPU SLIC superpixel contours)
                _compute.SetInt("_Width", w);
                _compute.SetInt("_Height", h);
                _compute.SetInt("_UseRegionAnalysis", _regionAnalysis > 0 ? 1 : 0);

                if (_regionAnalysis > 0)
                {
                    normRt = GpuUtils.GetTempRt(w, h, floatFmt);
                    blurNormRt = GpuUtils.GetTempRt(w, h, floatFmt, FilterMode.Point);
                    GpuNorm.ComputeNorm(source, normRt);
                    GpuBlur.Blur(normRt, blurNormRt, Mathf.Max(1f, 0.005f * Mathf.Max(w, h)));

                    int slicSize = Mathf.Max(12, 30 * _regionAnalysis);
                    int slicW = Mathf.Max(1, (w + slicSize - 1) / slicSize);
                    int slicH = Mathf.Max(1, (h + slicSize - 1) / slicSize);
                    int totalSeeds = slicW * slicH;
                    EnsureSlicBuffers(totalSeeds);

                    _compute.SetInt("_SlicSize", slicSize);
                    _compute.SetInt("_SlicW", slicW);
                    _compute.SetInt("_SlicH", slicH);

                    _compute.SetTexture(_kernelInitSlic, "_BlurNormTex", blurNormRt);
                    _compute.SetBuffer(_kernelInitSlic, "_SlicSeeds", _slicSeedsA);
                    _compute.SetBuffer(_kernelInitSlic, "_SlicSeedsB", _slicSeedsB);
                    _compute.Dispatch(_kernelInitSlic, (slicW + 7) / 8, (slicH + 7) / 8, 1);

                    int seedGroups = (totalSeeds + 63) / 64;
                    int pixGroupsX = (w + 15) / 16;
                    int pixGroupsY = (h + 15) / 16;

                    for (int iter = 0; iter < 2; iter++)
                    {
                        _compute.SetBuffer(_kernelClearSlicAccum, "_SlicAccum", _slicAccum);
                        _compute.Dispatch(_kernelClearSlicAccum, seedGroups, 1, 1);

                        _compute.SetTexture(_kernelAssignSlic, "_BlurNormTex", blurNormRt);
                        _compute.SetTexture(_kernelAssignSlic, "_SlicLabelTex", _slicLabelRt);
                        _compute.SetBuffer(_kernelAssignSlic, "_SlicSeeds", _slicSeedsA);
                        _compute.SetBuffer(_kernelAssignSlic, "_SlicSeedsB", _slicSeedsB);
                        _compute.SetBuffer(_kernelAssignSlic, "_SlicAccum", _slicAccum);
                        _compute.Dispatch(_kernelAssignSlic, pixGroupsX, pixGroupsY, 1);

                        _compute.SetBuffer(_kernelUpdateSlic, "_SlicSeeds", _slicSeedsA);
                        _compute.SetBuffer(_kernelUpdateSlic, "_SlicSeedsB", _slicSeedsB);
                        _compute.SetBuffer(_kernelUpdateSlic, "_SlicAccum", _slicAccum);
                        _compute.Dispatch(_kernelUpdateSlic, seedGroups, 1, 1);
                    }

                    _compute.SetTexture(_kernelInitOccupancy, "_BlurNormTex", blurNormRt);
                    _compute.SetTexture(_kernelInitOccupancy, "_SlicLabelTex", _slicLabelRt);
                }
                else
                {
                    _compute.SetTexture(_kernelInitOccupancy, "_BlurNormTex", fieldRt);
                    _compute.SetTexture(_kernelInitOccupancy, "_SlicLabelTex", _slicLabelRt);
                }

                _compute.SetTexture(_kernelInitOccupancy, "_OccTex", _occRt);
                _compute.SetBuffer(_kernelInitOccupancy, "_CountBuf", _countBuffer);
                _compute.Dispatch(_kernelInitOccupancy, (w + 15) / 16, (h + 15) / 16, 1);

                // 3. Descending multi-round 3x3-phase GPU ellipse packing
                _compute.SetInt("_MaxEllipses", MaxEllipses);
                _compute.SetFloat("_MinRadius", rMin);
                _compute.SetFloat("_RadiusDilation", _radiusDilation);
                _compute.SetTexture(_kernelPackRound, "_SourceTex", source);
                _compute.SetTexture(_kernelPackRound, "_FieldTex", fieldRt);
                _compute.SetTexture(_kernelPackRound, "_OccTex", _occRt);
                _compute.SetBuffer(_kernelPackRound, "_EllipseGeom", _ellipseGeomBuffer);
                _compute.SetBuffer(_kernelPackRound, "_EllipseData", _ellipseDataBuffer);
                _compute.SetBuffer(_kernelPackRound, "_CountBuf", _countBuffer);

                const int rounds = 8;
                for (int rIdx = 0; rIdx < rounds; rIdx++)
                {
                    float t = (float)rIdx / Mathf.Max(1, rounds - 1);
                    float targetR = (rMax > rMin + 0.25f)
                        ? (rMax * Mathf.Pow(rMin / Mathf.Max(0.5f, rMax), t))
                        : rMax;
                    targetR = Mathf.Max(targetR, rMin + 0.1f);

                    int cellSize = Mathf.Max(3, Mathf.CeilToInt(2f * targetR));
                    int totalCellsX = (w + cellSize - 1) / cellSize;
                    int totalCellsY = (h + cellSize - 1) / cellSize;

                    _compute.SetFloat("_RoundRadius", targetR);
                    _compute.SetInt("_CellSize", cellSize);

                    for (int py = 0; py < 3; py++)
                    {
                        int phaseCellsY = (totalCellsY - py + 2) / 3;
                        if (phaseCellsY <= 0) continue;

                        for (int px = 0; px < 3; px++)
                        {
                            int phaseCellsX = (totalCellsX - px + 2) / 3;
                            if (phaseCellsX <= 0) continue;

                            _compute.SetInt("_PhaseX", px);
                            _compute.SetInt("_PhaseY", py);
                            _compute.SetInt("_RoundSeed", rIdx * 97 + py * 13 + px * 7 + 1);
                            _compute.Dispatch(_kernelPackRound, (phaseCellsX + 7) / 8, (phaseCellsY + 7) / 8, 1);
                        }
                    }
                }

                // 4. Build IndirectArgs & Render all ellipses cleanly
                _compute.SetBuffer(_kernelBuildArgs, "_CountBuf", _countBuffer);
                _compute.SetBuffer(_kernelBuildArgs, "_ArgsBuf", _argsBuffer);
                _compute.Dispatch(_kernelBuildArgs, 1, 1, 1);

                Color bgCol = new Color(_bgR / 255f, _bgG / 255f, _bgB / 255f, _bgA / 255f);
                Material.SetColor("_BgColor", bgCol);
                Graphics.Blit(source, destination, Material, 2);

                Material.SetVector("_ScreenSize", new Vector4(w, h, 0f, 0f));
                Material.SetFloat("_FeatherPx", 1f / (_renderFactor + 1));
                Material.SetBuffer("_EllipseGeom", _ellipseGeomBuffer);
                Material.SetBuffer("_EllipseData", _ellipseDataBuffer);

                Matrix4x4 orthoProj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), false);
                _cmd.Clear();
                _cmd.SetRenderTarget(destination);
                _cmd.SetViewport(new Rect(0f, 0f, w, h));
                _cmd.SetViewProjectionMatrices(Matrix4x4.identity, orthoProj);
                _cmd.DrawProceduralIndirect(Matrix4x4.identity, Material, 1, MeshTopology.Triangles, _argsBuffer, 0);
                Graphics.ExecuteCommandBuffer(_cmd);
            }
            finally
            {
                GpuUtils.ReleaseTemp(tensorRt);
                GpuUtils.ReleaseTemp(fieldRt);
                GpuUtils.ReleaseTemp(normRt);
                GpuUtils.ReleaseTemp(blurNormRt);
            }
        }

        public override void Dispose()
        {
            if (_cmd != null)
            {
                _cmd.Release();
                _cmd = null;
            }

            GpuUtils.ReleaseBuffer(ref _ellipseGeomBuffer);
            GpuUtils.ReleaseBuffer(ref _ellipseDataBuffer);
            GpuUtils.ReleaseBuffer(ref _countBuffer);
            GpuUtils.ReleaseBuffer(ref _argsBuffer);
            GpuUtils.ReleaseBuffer(ref _slicSeedsA);
            GpuUtils.ReleaseBuffer(ref _slicSeedsB);
            GpuUtils.ReleaseBuffer(ref _slicAccum);
            GpuUtils.ReleaseRt(ref _occRt);
            GpuUtils.ReleaseRt(ref _slicLabelRt);

            _allocSlicSeeds = -1;
            _allocW = -1;
            _allocH = -1;
            _compute = null;
            base.Dispose();
        }

        private void EnsureRenderTextures(int w, int h)
        {
            if (_occRt != null && _slicLabelRt != null && _allocW == w && _allocH == h)
            {
                return;
            }

            GpuUtils.ReleaseRt(ref _occRt);
            GpuUtils.ReleaseRt(ref _slicLabelRt);

            _occRt = GpuUtils.CreateRt(w, h, RenderTextureFormat.RFloat, FilterMode.Point, RenderTextureReadWrite.Linear, randomWrite: true);
            _slicLabelRt = GpuUtils.CreateRt(w, h, RenderTextureFormat.RInt, FilterMode.Point, RenderTextureReadWrite.Linear, randomWrite: true);
            _allocW = w;
            _allocH = h;
        }

        private void EnsureSlicBuffers(int totalSeeds)
        {
            if (_slicSeedsA != null && _slicSeedsB != null && _slicAccum != null && _allocSlicSeeds >= totalSeeds)
            {
                return;
            }

            GpuUtils.ReleaseBuffer(ref _slicSeedsA);
            GpuUtils.ReleaseBuffer(ref _slicSeedsB);
            GpuUtils.ReleaseBuffer(ref _slicAccum);

            int cap = Mathf.Max(64, totalSeeds);
            _slicSeedsA = new ComputeBuffer(cap, sizeof(float) * 4);
            _slicSeedsB = new ComputeBuffer(cap, sizeof(float) * 4);
            _slicAccum = new ComputeBuffer(cap * 6, sizeof(uint));
            _allocSlicSeeds = cap;
        }
    }
}