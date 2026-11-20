using System;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class UltraWarp4PlusFilter : FilterBase
    {
        private const int PlasmaTextureSize = 513;

        private static readonly string[] NoiseTypeChoices =
        {
            "Gaussian",
            "Uniform",
            "Salt and Pepper",
            "Poisson"
        };

        private static readonly string[] CorrelatedChoices =
        {
            "Random",
            "Off",
            "On"
        };

        private static readonly string[] BoundaryChoices =
        {
            "Random",
            "Random [non-Transparent]",
            "Transparent",
            "Nearest",
            "Periodic",
            "Mirror"
        };

        private static readonly string[] WarpDirectionChoices =
        {
            "Random (G'MIC)",
            "Manual",
            "Both Directions"
        };

        private static readonly string[] IterColorChoices =
        {
            "Random (G'MIC)",
            "Off",
            "Manual"
        };

        private static readonly string[] WarpChannelChoices;
        private static readonly string[] NegationChannelChoices;
        private static readonly string[] ValueActionChoices =
        {
            "None",
            "Cut",
            "Normalize"
        };

        static UltraWarp4PlusFilter()
        {
            WarpChannelChoices = (string[])GpuChannelOps.ChannelChoices.Clone();
            WarpChannelChoices[0] = "Random";

            NegationChannelChoices = (string[])GpuChannelOps.ChannelChoices.Clone();
            NegationChannelChoices[0] = "Random";
        }

        private GpuStats _stats;
        private GpuWatershed _watershed;
        private GpuQuadtree _quadtree;
        private Texture2D _plasmaTex;
        private bool _plasmaDirty = true;
        private int _seed = 0x13579BDF;

        // Plasma
        private bool _plasmaTexture = false;
        private float _plasmaScale = 3.3f;
        private bool _plasmaAlpha = false;
        private bool _randomPlasmaTint = true;
        private float _plasmaR = 0.5f;
        private float _plasmaG = 0.5f;
        private float _plasmaB = 0.5f;

        // Segmentation
        private bool _segmentation = false;
        private float _edgeThreshold = 5f;
        private float _segSmoothness = 0f;

        // Blur
        private float _blur = 0f;

        // Quadtree
        private bool _quadtreeEnabled = false;
        private int _qtMinPrec = 4;
        private int _qtMaxPrec = 256;
        private float _qtMinHomog = 4.8f;
        private float _qtMaxHomog = 5f;

        // Noise
        private int _noiseType = 2;
        private float _minNoise = 0f;
        private float _maxNoise = 0f;
        private int _noiseChannels = 2;

        // Warping
        private int _warpIterations = 3;
        private float _warpIntensity = 3f;
        private float _warpOffset = 20f;
        private bool _scaleWidth = true;
        private bool _scaleHeight = true;
        private int _correlatedChannels = 0;
        private int _boundary = 5;
        private int _warpChannels = 2;
        private int _warpDirectionMode = 0;
        private float _warpDirX = 0.5f;
        private float _warpDirY = 0.5f;
        private float _warpOffsetBias = 0f;
        private bool _randomNegation = true;
        private int _negationChannels = 2;
        private int _iterColorMode = 0;
        private float _iterGamma = 0f;
        private float _iterHue = 0f;
        private bool _warpNormalise = true;

        // Final HSV Scaling
        private float _minHue = 0f;
        private float _maxHue = 5f;
        private float _minSat = 0f;
        private float _maxSat = 3f;
        private float _minVal = 0.5f;
        private float _maxVal = 2f;
        private float _hsvHueOffset = -180f;
        private float _hsvSatOffset = 0f;
        private float _hsvValOffset = 0f;

        // Normalise & Overall
        private bool _finalNormalise = true;
        private int _normalisationChannels = 11;
        private int _overallChannels = 0;
        private int _valueAction = 0;
        private bool _randomGlobalHue = true;
        private float _globalHueShift = 0f;

        public override string Id => "fx_ultrawarp4plus";
        public override string DisplayName => "UltraWarp++++";
        public override string Description => "Multi-iteration randomized plasma, segmentation, quadtree, noise, intensity-warp, and HSV scaling.";
        public override FilterCategory Category => FilterCategory.Heavy;
        public override bool IsAvailable =>
            base.IsAvailable &&
            GpuBlur.IsAvailable &&
            GpuNormalize.IsAvailable &&
            GpuChannelOps.IsAvailable &&
            _stats != null && _stats.IsAvailable &&
            _watershed != null && _watershed.IsAvailable &&
            _quadtree != null && _quadtree.IsAvailable;

        public UltraWarp4PlusFilter()
        {
            AddParameter(FilterParameter.Button(
                "recompute",
                "Recompute",
                "Roll a new random seed for all stochastic stages.",
                Recompute));

            AddParameter(FilterParameter.Bool(
                "plasma_texture",
                "Plasma Texture",
                "Replace input image with a procedural plasma texture (discards input).",
                false,
                v =>
                {
                    _plasmaTexture = v;
                    if (v) _plasmaDirty = true;
                }));

            AddParameter(FilterParameter.Float(
                "plasma_scale",
                "Plasma Scale",
                "Fractal scale of generated plasma texture.",
                0f, 20f, 3.3f,
                v =>
                {
                    _plasmaScale = v;
                    _plasmaDirty = true;
                },
                isVisible: () => _plasmaTexture,
                step: 0.1f));

            AddParameter(FilterParameter.Bool(
                "plasma_alpha",
                "Plasma Alpha",
                "Also generate alpha channel noise in plasma texture.",
                false,
                v =>
                {
                    _plasmaAlpha = v;
                    _plasmaDirty = true;
                },
                isVisible: () => _plasmaTexture));

            AddParameter(FilterParameter.Bool(
                "random_plasma_tint",
                "Random Plasma Tint",
                "Randomize plasma balance_gamma RGB tint on Recompute.",
                true,
                v =>
                {
                    _randomPlasmaTint = v;
                    _plasmaDirty = true;
                },
                isVisible: () => _plasmaTexture));

            AddParameter(FilterParameter.Float(
                "plasma_r",
                "Plasma Red",
                "Manual red balance target for plasma texture.",
                0f, 1f, 0.5f,
                v =>
                {
                    _plasmaR = v;
                    _plasmaDirty = true;
                },
                isVisible: () => _plasmaTexture && !_randomPlasmaTint,
                step: 0.02f));

            AddParameter(FilterParameter.Float(
                "plasma_g",
                "Plasma Green",
                "Manual green balance target for plasma texture.",
                0f, 1f, 0.5f,
                v =>
                {
                    _plasmaG = v;
                    _plasmaDirty = true;
                },
                isVisible: () => _plasmaTexture && !_randomPlasmaTint,
                step: 0.02f));

            AddParameter(FilterParameter.Float(
                "plasma_b",
                "Plasma Blue",
                "Manual blue balance target for plasma texture.",
                0f, 1f, 0.5f,
                v =>
                {
                    _plasmaB = v;
                    _plasmaDirty = true;
                },
                isVisible: () => _plasmaTexture && !_randomPlasmaTint,
                step: 0.02f));

            AddParameter(FilterParameter.Bool(
                "segmentation",
                "Segmentation",
                "Apply watershed segmentation before warping (no alpha channel).",
                false,
                v => _segmentation = v));

            AddParameter(FilterParameter.Float(
                "edge_threshold",
                "Edge Threshold",
                "Watershed gradient threshold.",
                0.01f, 60f, 5f,
                v => _edgeThreshold = v,
                isVisible: () => _segmentation,
                step: 0.25f));

            AddParameter(FilterParameter.Float(
                "seg_smoothness",
                "Seg Smoothness",
                "Pre-blur radius before watershed segmentation.",
                0f, 60f, 0f,
                v => _segSmoothness = v,
                isVisible: () => _segmentation,
                step: 0.5f));

            AddParameter(FilterParameter.Float(
                "blur",
                "Blur",
                "Gaussian blur radius applied before quadtree/warping.",
                0f, 30f, 0f,
                v => _blur = v,
                step: 0.25f));

            AddParameter(FilterParameter.Bool(
                "quadtree",
                "Quadtree",
                "Apply randomized square quadtree pixelisation (no alpha channel).",
                false,
                v => _quadtreeEnabled = v));

            AddParameter(FilterParameter.Int(
                "qt_min_prec",
                "QT Min Precision",
                "Minimum randomized quadtree precision.",
                2, 8192, 4,
                v => _qtMinPrec = v,
                isVisible: () => _quadtreeEnabled));

            AddParameter(FilterParameter.Int(
                "qt_max_prec",
                "QT Max Precision",
                "Maximum randomized quadtree precision.",
                0, 8192, 256,
                v => _qtMaxPrec = v,
                isVisible: () => _quadtreeEnabled));

            AddParameter(FilterParameter.Float(
                "qt_min_homog",
                "QT Min Homogeneity",
                "Minimum randomized quadtree homogeneity.",
                0f, 5f, 4.8f,
                v => _qtMinHomog = v,
                isVisible: () => _quadtreeEnabled,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "qt_max_homog",
                "QT Max Homogeneity",
                "Maximum randomized quadtree homogeneity.",
                0f, 5f, 5f,
                v => _qtMaxHomog = v,
                isVisible: () => _quadtreeEnabled,
                step: 0.1f));

            AddParameter(FilterParameter.Choice(
                "noise_type",
                "Noise Type",
                "Noise distribution model.",
                NoiseTypeChoices,
                2,
                v => _noiseType = v));

            AddParameter(FilterParameter.Float(
                "min_noise",
                "Minimum Noise",
                "Minimum exponential noise intensity.",
                0f, 8f, 0f,
                v => _minNoise = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "max_noise",
                "Maximum Noise",
                "Maximum exponential noise intensity.",
                0f, 8f, 0f,
                v => _maxNoise = v,
                step: 0.1f));

            AddParameter(FilterParameter.Choice(
                "noise_channels",
                "Noise Channel(s)",
                "Target color space/channels for noise.",
                GpuChannelOps.ChannelChoices,
                2,
                v => _noiseChannels = v,
                isVisible: () => _minNoise > 0.001f || _maxNoise > 0.001f));

            AddParameter(FilterParameter.Int(
                "warp_iterations",
                "Warp Iterations",
                "Number of sequential intensity-warp iterations.",
                0, 30, 3,
                v => _warpIterations = v));

            AddParameter(FilterParameter.Float(
                "warp_intensity",
                "Warp Intensity",
                "Displacement scaling factor per iteration.",
                0f, 10f, 3f,
                v => _warpIntensity = v,
                isVisible: () => _warpIterations > 0,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "warp_offset",
                "Warp Offset",
                "Intensity zero-point offset range.",
                0f, 2000f, 20f,
                v => _warpOffset = v,
                isVisible: () => _warpIterations > 0,
                step: 1f));

            AddParameter(FilterParameter.Bool(
                "scale_width",
                "Scale to Width",
                "Scale horizontal warp factor by frame width.",
                true,
                v => _scaleWidth = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Bool(
                "scale_height",
                "Scale to Height",
                "Retained for G'MIC parameter parity (original script checks Correlated Channels instead).",
                true,
                v => _scaleHeight = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Choice(
                "correlated_channels",
                "Correlated Channels",
                "Warp channels together using RGB norm or independently.",
                CorrelatedChoices,
                0,
                v => _correlatedChannels = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Choice(
                "boundary",
                "Boundary",
                "Sampling boundary behavior during warp.",
                BoundaryChoices,
                5,
                v => _boundary = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Choice(
                "warp_channels",
                "Warp Channel(s)",
                "Target color space/channels for warping.",
                WarpChannelChoices,
                2,
                v => _warpChannels = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Choice(
                "warp_direction_mode",
                "Warp Direction",
                "Randomize warp direction/offset per iteration or control manually.",
                WarpDirectionChoices,
                0,
                v => _warpDirectionMode = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Float(
                "warp_dir_x",
                "Warp Dir X",
                "Manual horizontal warp direction multiplier.",
                -1f, 1f, 0.5f,
                v => _warpDirX = v,
                isVisible: () => _warpIterations > 0 && _warpDirectionMode != 0,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "warp_dir_y",
                "Warp Dir Y",
                "Manual vertical warp direction multiplier.",
                -1f, 1f, 0.5f,
                v => _warpDirY = v,
                isVisible: () => _warpIterations > 0 && _warpDirectionMode != 0,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "warp_offset_bias",
                "Warp Offset Bias",
                "Manual intensity zero-point bias.",
                -1f, 1f, 0f,
                v => _warpOffsetBias = v,
                isVisible: () => _warpIterations > 0 && _warpDirectionMode != 0,
                step: 0.05f));

            AddParameter(FilterParameter.Bool(
                "random_negation",
                "Random Negation",
                "Randomly invert selected channels on each warp iteration.",
                true,
                v => _randomNegation = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Choice(
                "negation_channels",
                "Negation Channel(s)",
                "Target color space/channels for random negation.",
                NegationChannelChoices,
                2,
                v => _negationChannels = v,
                isVisible: () => _warpIterations > 0 && _randomNegation));

            AddParameter(FilterParameter.Choice(
                "iter_color_mode",
                "Iter Color Shift",
                "Per-iteration gamma/hue adjustment mode (Random matches G'MIC).",
                IterColorChoices,
                0,
                v => _iterColorMode = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Float(
                "iter_gamma",
                "Iter Gamma",
                "Manual per-iteration gamma adjustment.",
                -100f, 100f, 0f,
                v => _iterGamma = v,
                isVisible: () => _warpIterations > 0 && _iterColorMode == 2,
                step: 2f));

            AddParameter(FilterParameter.Float(
                "iter_hue",
                "Iter Hue",
                "Manual per-iteration hue adjustment.",
                -100f, 100f, 0f,
                v => _iterHue = v,
                isVisible: () => _warpIterations > 0 && _iterColorMode == 2,
                step: 2f));

            AddParameter(FilterParameter.Bool(
                "warp_normalise",
                "Warp Normalise",
                "Normalize red channel at the end of each warp iteration.",
                true,
                v => _warpNormalise = v,
                isVisible: () => _warpIterations > 0));

            AddParameter(FilterParameter.Float(
                "min_hue",
                "Minimum Hue",
                "Minimum HSV hue multiplier.",
                0f, 20f, 0f,
                v => _minHue = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "max_hue",
                "Maximum Hue",
                "Maximum HSV hue multiplier.",
                0f, 20f, 5f,
                v => _maxHue = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "min_sat",
                "Minimum Saturation",
                "Minimum HSV saturation multiplier.",
                0f, 20f, 0f,
                v => _minSat = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "max_sat",
                "Maximum Saturation",
                "Maximum HSV saturation multiplier.",
                0f, 20f, 3f,
                v => _maxSat = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "min_val",
                "Minimum Value",
                "Minimum HSV value multiplier.",
                0f, 20f, 0.5f,
                v => _minVal = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "max_val",
                "Maximum Value",
                "Maximum HSV value multiplier.",
                0f, 20f, 2f,
                v => _maxVal = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "hsv_hue_offset",
                "HSV Hue Offset",
                "HSV hue offset in degrees (-180 randomizes).",
                -180f, 180f, -180f,
                v => _hsvHueOffset = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "hsv_sat_offset",
                "HSV Sat Offset",
                "HSV saturation offset.",
                -1.01f, 1f, 0f,
                v => _hsvSatOffset = v,
                step: 0.02f));

            AddParameter(FilterParameter.Float(
                "hsv_val_offset",
                "HSV Val Offset",
                "HSV value offset (-20.01 randomizes).",
                -20.01f, 20f, 0f,
                v => _hsvValOffset = v,
                step: 0.05f));

            AddParameter(FilterParameter.Bool(
                "final_normalise",
                "Final Normalise",
                "Normalize selected channel after HSV scaling.",
                true,
                v => _finalNormalise = v));

            AddParameter(FilterParameter.Choice(
                "normalisation_channels",
                "Norm Channel(s)",
                "Target color space/channels for final normalization.",
                GpuChannelOps.ChannelChoices,
                11,
                v => _normalisationChannels = v,
                isVisible: () => _finalNormalise));

            AddParameter(FilterParameter.Choice(
                "overall_channels",
                "Overall Channel(s)",
                "Outer channel space wrapper applied to the entire pipeline.",
                GpuChannelOps.ChannelChoices,
                0,
                v => _overallChannels = v));

            AddParameter(FilterParameter.Choice(
                "value_action",
                "Value Action",
                "Post-action applied by outer channel wrapper.",
                ValueActionChoices,
                0,
                v => _valueAction = v));

            AddParameter(FilterParameter.Bool(
                "random_global_hue",
                "Random Global Hue",
                "Randomize final global hue rotation (G'MIC default).",
                true,
                v => _randomGlobalHue = v));

            AddParameter(FilterParameter.Float(
                "global_hue_shift",
                "Global Hue Shift",
                "Manual final global hue rotation in degrees.",
                -180f, 180f, 0f,
                v => _globalHueShift = v,
                isVisible: () => !_randomGlobalHue,
                step: 1f));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/UltraWarp4Plus"))
            {
                return;
            }

            GpuStats.TryCreate(out _stats);
            GpuWatershed.TryCreate(out _watershed);
            GpuQuadtree.TryCreate(out _quadtree);
            _plasmaDirty = true;
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            RenderTextureFormat fmt = GpuUtils.FloatFormat;

            RenderTexture coreOut = null;
            try
            {
                coreOut = GpuUtils.GetTempRt(w, h, fmt);

                // G'MIC fx_ultrawarp4plus: ac "ultrawarp4plus ${2--3}",$-2,$-1
                GpuChannelOps.ApplyInChannelSpace(
                    source,
                    coreOut,
                    _overallChannels,
                    RunCorePipeline,
                    _valueAction,
                    _stats);

                // G'MIC fx_ultrawarp4plus: fx_adjust_colors 0,0,0,{(u-0.5)*200},0,0
                float globalHueDeg = _randomGlobalHue
                    ? (Rand01(0x9001u) - 0.5f) * 360f
                    : _globalHueShift;
                Material.SetFloat("_UseStats", 0f);
                Material.SetFloat("_Gamma", 1f);
                Material.SetFloat("_HueShiftDeg", globalHueDeg);
                Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                Graphics.Blit(coreOut, destination, Material, 3);
            }
            finally
            {
                GpuUtils.ReleaseTemp(coreOut);
            }
        }

        public override void ResetParameters()
        {
            base.ResetParameters();
            _stats?.ResetHistory();
            Recompute();
        }

        public override void Dispose()
        {
            _stats?.Dispose();
            _stats = null;
            _watershed?.Dispose();
            _watershed = null;
            _quadtree?.Dispose();
            _quadtree = null;
            GpuUtils.DestroyObject(ref _plasmaTex);
            base.Dispose();
        }

        private void Recompute()
        {
            _seed = unchecked(_seed * 1664525 + 1013904223);
            if (_seed == 0)
            {
                _seed = 0x13579BDF;
            }
            _plasmaDirty = true;
        }

        private void RunCorePipeline(RenderTexture input, RenderTexture output)
        {
            int w = input.width;
            int h = input.height;
            RenderTextureFormat fmt = GpuUtils.FloatFormat;

            RenderTexture rtA = null;
            RenderTexture rtB = null;

            try
            {
                rtA = GpuUtils.GetTempRt(w, h, fmt);
                rtB = GpuUtils.GetTempRt(w, h, fmt);

                RenderTexture curr = input;
                RenderTexture NextRt(RenderTexture c) => (c == rtA) ? rtB : rtA;

                float hueMin = Mathf.Min(_minHue, _maxHue);
                float hueMax = Mathf.Max(_minHue, _maxHue);
                float satMin = Mathf.Min(_minSat, _maxSat);
                float satMax = Mathf.Max(_minSat, _maxSat);
                float valMin = Mathf.Min(_minVal, _maxVal);
                float valMax = Mathf.Max(_minVal, _maxVal);

                float j = hueMin + Rand01(0x1001u) * (hueMax - hueMin);
                float k = satMin + Rand01(0x1002u) * (satMax - satMin);
                float l = valMin + Rand01(0x1003u) * (valMax - valMin);

                // Plasma Texture
                if (_plasmaTexture)
                {
                    if (_plasmaDirty || _plasmaTex == null)
                    {
                        Color targetBalance = _randomPlasmaTint
                            ? new Color(
                                Mathf.Round(Rand01(0x1010u) * 255f) / 255f,
                                Mathf.Round(Rand01(0x1011u) * 255f) / 255f,
                                Mathf.Round(Rand01(0x1012u) * 255f) / 255f,
                                1f)
                            : new Color(_plasmaR, _plasmaG, _plasmaB, 1f);

                        PlasmaGenerator.CreateOrUpdateGmic(
                            ref _plasmaTex,
                            PlasmaTextureSize,
                            _plasmaScale,
                            unchecked(_seed ^ 0x4E67C6A7),
                            targetBalance,
                            _plasmaAlpha);
                        _plasmaDirty = false;
                    }

                    // Original G'MIC quirk: `if $2 ch=0 else ch=2 fi` checks Plasma Scale != 0 rather than Plasma Alpha.
                    int plasmaCh = (Mathf.Abs(_plasmaScale) > 1e-5f) ? 0 : 2;
                    RenderTexture next = NextRt(curr);
                    GpuChannelOps.ApplyInChannelSpace(
                        curr,
                        next,
                        plasmaCh,
                        (src, dst) =>
                        {
                            Material.SetTexture("_PlasmaTex", _plasmaTex);
                            Material.SetFloat("_PlasmaAlpha", _plasmaAlpha ? 1f : 0f);
                            Graphics.Blit(src, dst, Material, 5);
                        },
                        valueAction: 0,
                        stats: _stats);
                    curr = next;
                }

                // Gaussian Blur
                if (_blur > 0.05f)
                {
                    RenderTexture next = NextRt(curr);
                    GpuBlur.Blur(curr, next, _blur);
                    curr = next;
                }

                // Noise amount
                float nMin = Mathf.Min(_minNoise, _maxNoise);
                float nMax = Mathf.Max(_minNoise, _maxNoise);
                float noiseExp = Mathf.Max(0f, nMin + Rand01(0x1020u) * (nMax - nMin));
                float noiseAmt = Mathf.Pow(2f, noiseExp - 3f) * noiseExp;

                // Segmentation
                if (_segmentation)
                {
                    RenderTexture segRt = NextRt(curr);
                    _watershed.Apply(curr, segRt, _edgeThreshold, _segSmoothness);
                    RenderTexture normRt = NextRt(segRt);
                    GpuNormalize.Normalize(segRt, normRt, preserveAlphaFrom: curr, rgbMode: true, stats: _stats);
                    curr = normRt;
                }

                // Quadtree Pixelisation (+ initial Noise)
                if (_quadtreeEnabled)
                {
                    int pMin = Mathf.Min(_qtMinPrec, _qtMaxPrec);
                    int pMax = Mathf.Max(_qtMinPrec, _qtMaxPrec);
                    float hMin = Mathf.Min(_qtMinHomog, _qtMaxHomog);
                    float hMax = Mathf.Max(_qtMinHomog, _qtMaxHomog);

                    int qtPrec = Mathf.Max(2, Mathf.RoundToInt(pMin + Rand01(0x1030u) * (pMax - pMin)));
                    float qtHomog = Mathf.Max(0f, Mathf.Round(hMin + Rand01(0x1031u) * (hMax - hMin)));

                    if (noiseAmt > 0.0001f)
                    {
                        RenderTexture nRt = NextRt(curr);
                        ApplyNoiseInChannel(curr, nRt, noiseAmt, 0x1040u);
                        curr = nRt;
                    }

                    RenderTexture qtRt = NextRt(curr);
                    _quadtree.Apply(curr, qtRt, mode: 0, precision: qtPrec, homogeneity: qtHomog, outline: 0);
                    curr = qtRt;
                }

                // Main Noise pass
                if (noiseAmt > 0.0001f)
                {
                    RenderTexture nRt = NextRt(curr);
                    ApplyNoiseInChannel(curr, nRt, noiseAmt, 0x1050u);
                    curr = nRt;
                }

                // Warp Iterations
                int iters = Mathf.Max(0, _warpIterations);
                float warpSignM = Mathf.Abs(_warpOffset) > 1e-6f ? Mathf.Sign(_warpOffset) : 0f;
                float intensityFactor = Mathf.Pow(2f, _warpIntensity - 5f) * _warpIntensity;

                for (int it = 0; it < iters; it++)
                {
                    uint baseSalt = 0x2000u + (uint)it * 32u;

                    float dirX = (_warpDirectionMode != 0) ? (_warpDirX * 0.5f) : (Rand01(baseSalt + 0u) - 0.5f);
                    float dirY = (_warpDirectionMode != 0) ? (_warpDirY * 0.5f) : (Rand01(baseSalt + 1u) - 0.5f);
                    float biasX = (_warpDirectionMode != 0) ? (_warpOffsetBias * 0.5f) : (Rand01(baseSalt + 2u) - 0.5f);
                    float biasY = (_warpDirectionMode != 0) ? (_warpOffsetBias * 0.5f) : (Rand01(baseSalt + 3u) - 0.5f);

                    float scaleX = _scaleWidth ? (w / 10f) : 1f;
                    float a = intensityFactor * dirX * (_scaleWidth ? scaleX * 0.025f : 1f);

                    // Original G'MIC quirk: checks `$22` (Correlated Channels != 0) instead of `$21` (Scale to Height).
                    bool useScaleY = _correlatedChannels != 0;
                    float scaleY = useScaleY ? (h / 10f) : 1f;
                    float b = intensityFactor * dirY * (useScaleY ? scaleY * 0.025f : 1f);

                    float c = biasX * _warpOffset * warpSignM * scaleX;
                    float d = biasY * _warpOffset * warpSignM * scaleY;

                    int corr = (_correlatedChannels == 1) ? 0
                             : (_correlatedChannels == 2) ? 1
                             : Mathf.RoundToInt(Rand01(baseSalt + 4u));

                    int bound;
                    if (_boundary == 0)
                    {
                        bound = Mathf.Clamp(Mathf.RoundToInt(Rand01(baseSalt + 5u) * 2.98f - 0.49f), 0, 2);
                    }
                    else if (_boundary == 1)
                    {
                        bound = Mathf.Clamp(Mathf.RoundToInt(Rand01(baseSalt + 5u) * 2.98f + 0.51f), 1, 3);
                    }
                    else
                    {
                        bound = Mathf.Clamp(_boundary - 2, 0, 3);
                    }

                    // Original G'MIC passes `_warpChannels` directly to `ac` (so 0 acts as `all`).
                    RenderTexture warpOut = NextRt(curr);
                    GpuChannelOps.ApplyInChannelSpace(
                        curr,
                        warpOut,
                        _warpChannels,
                        (src, dst) =>
                        {
                            Material.SetVector("_WarpScale", new Vector4(a, b, 0f, 0f));
                            Material.SetVector("_WarpOffset", new Vector4(c, d, 0f, 0f));
                            Material.SetFloat("_Correlated", corr > 0 ? 1f : 0f);
                            Material.SetInt("_BoundaryMode", bound);
                            Material.SetFloat("_Bidirectional", _warpDirectionMode == 2 ? 1f : 0f);
                            Graphics.Blit(src, dst, Material, 1);
                        },
                        valueAction: 0,
                        stats: _stats);
                    curr = warpOut;

                    // Random Negation
                    if (_randomNegation && Mathf.RoundToInt(Rand01(baseSalt + 6u)) == 1)
                    {
                        int nch = (_negationChannels == 0)
                            ? Mathf.Clamp(Mathf.RoundToInt(Rand01(baseSalt + 7u) * 33.98f - 0.49f), 0, 33)
                            : _negationChannels;

                        RenderTexture negOut = NextRt(curr);
                        GpuChannelOps.ApplyInChannelSpace(
                            curr,
                            negOut,
                            nch,
                            (src, dst) => Graphics.Blit(src, dst, Material, 2),
                            valueAction: 0,
                            stats: _stats);
                        curr = negOut;
                    }

                    // Per-iteration fx_adjust_colors
                    if (_iterColorMode != 1)
                    {
                        float adjGammaParam = (_iterColorMode == 2)
                            ? _iterGamma
                            : (Rand01(baseSalt + 8u) * 2f - 1f) * 100f;
                        float adjHueParam = (_iterColorMode == 2)
                            ? _iterHue
                            : (Rand01(baseSalt + 9u) * 2f - 1f) * 100f;

                        float gammaExp = Mathf.Pow(10f, -adjGammaParam / 100f);
                        float hueDeg = adjHueParam * 1.8f;

                        RenderTexture adjOut = NextRt(curr);
                        _stats.ComputeMinMax(curr, rgbMode: true);
                        Material.SetFloat("_UseStats", 1f);
                        Material.SetFloat("_Gamma", gammaExp);
                        Material.SetFloat("_HueShiftDeg", hueDeg);
                        Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                        Graphics.Blit(curr, adjOut, Material, 3);
                        curr = adjOut;
                    }

                    // Warp Normalise (original G'MIC: `ac "n 0,255",3` i.e. RGB [red])
                    if (_warpNormalise)
                    {
                        RenderTexture normOut = NextRt(curr);
                        GpuChannelOps.ApplyInChannelSpace(
                            curr,
                            normOut,
                            3,
                            (src, dst) => GpuNormalize.Normalize(src, dst, rgbMode: false, stats: _stats),
                            valueAction: 0,
                            stats: _stats);
                        curr = normOut;
                    }
                }

                // Final HSV Scaling (fx_mix_hsv)
                float hh = Mathf.Approximately(_hsvHueOffset, -180f)
                    ? (Rand01(0x8001u) - 0.5f) * 360f
                    : _hsvHueOffset;

                // Original G'MIC checks `$37 == -8.01` even though slider min is `-1.01`.
                float ss = Mathf.Approximately(_hsvSatOffset, -8.01f)
                    ? (Rand01(0x8002u) - 0.5f) * 2f
                    : _hsvSatOffset;

                float vv = Mathf.Approximately(_hsvValOffset, -20.01f)
                    ? (Rand01(0x8003u) - 0.5f) * 2f
                    : _hsvValOffset;

                RenderTexture hsvOut = NextRt(curr);
                Material.SetVector("_HsvMul", new Vector4(j, k, l, 0f));
                Material.SetVector("_HsvAdd", new Vector4(hh, ss, vv, 0f));
                Graphics.Blit(curr, hsvOut, Material, 4);
                curr = hsvOut;

                // Final Normalise
                if (_finalNormalise)
                {
                    GpuChannelOps.ApplyInChannelSpace(
                        curr,
                        output,
                        _normalisationChannels,
                        (src, dst) => GpuNormalize.Normalize(
                            src,
                            dst,
                            rgbMode: !GpuChannelOps.IsSingleChannel(_normalisationChannels),
                            stats: _stats),
                        valueAction: 0,
                        stats: _stats);
                }
                else
                {
                    Graphics.Blit(curr, output);
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(rtA);
                GpuUtils.ReleaseTemp(rtB);
            }
        }

        private void ApplyNoiseInChannel(RenderTexture source, RenderTexture destination, float noiseAmt, uint salt)
        {
            uint noiseSeed = HashU32(unchecked((uint)_seed) ^ salt);
            GpuChannelOps.ApplyInChannelSpace(
                source,
                destination,
                _noiseChannels,
                (src, dst) =>
                {
                    Material.SetInt("_NoiseType", _noiseType);
                    Material.SetFloat("_NoiseAmount", noiseAmt);
                    Material.SetInt("_NoiseSeed", unchecked((int)noiseSeed));
                    Graphics.Blit(src, dst, Material, 0);
                },
                valueAction: 1,
                stats: _stats);
        }

        private float Rand01(uint salt)
        {
            uint h = HashU32(unchecked((uint)_seed) ^ salt);
            return (h & 0xFFFFFFu) / 16777215f;
        }

        private static uint HashU32(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}