using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class FireEdgesFilter : FilterBase
    {
        private static readonly GpuStatsOptions HistOptions = new GpuStatsOptions
        {
            RgbMode = false,
            ClipDivisor = 0,
            ZeroBasedCdf = true,
            TemporalAlpha = 1f
        };

        private GpuStats _stats;

        private RenderTexture _fireRtA;
        private RenderTexture _fireRtB;
        private int _allocW = -1;
        private int _allocH = -1;
        private RenderTextureFormat _allocFormat;
        private bool _hasFireHistory;
        private int _stepCounter;
        private float _lastStepTime;
        private float _stepAccum;

        private float _edges = 0.7f;
        private float _attenuation = 0.25f;
        private float _smoothness = 0.5f;
        private float _threshold = 25f;
        private int _startingFrame = 20;
        private int _frameSkip = 0;
        private float _stepRate = 60f;

        public override string Id => "fx_fire_edges";
        public override string DisplayName => "Edges on Fire";
        public override string Description => "Creates an animated fire effect rising from image edges.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool SuppressFastNoise => true;
        public override bool IsAvailable =>
            base.IsAvailable &&
            GpuNorm.IsAvailable &&
            GpuGradientNorm.IsAvailable &&
            GpuNormalize.IsAvailable &&
            GpuBlur.IsAvailable &&
            _stats != null &&
            _stats.IsAvailable;

        public FireEdgesFilter()
        {
            AddParameter(FilterParameter.Float(
                "edges",
                "Edges",
                "Roundify gamma exponent shaping edge intensity.",
                0f, 3f, 0.7f,
                v => _edges = v,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "attenuation",
                "Attenuation",
                "Flame decay factor (lower = taller flames).",
                0f, 1f, 0.25f,
                v => _attenuation = v,
                step: 0.02f));

            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Gaussian blur radius applied to spark noise.",
                0f, 5f, 0.5f,
                v => _smoothness = v,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "threshold",
                "Threshold",
                "Spark threshold percentage (0 = histogram equalization).",
                0f, 100f, 25f,
                v => _threshold = v,
                step: 1f));

            AddParameter(FilterParameter.Int(
                "starting_frame",
                "Warmup Steps",
                "Initial simulation steps executed on startup or reset.",
                0, 199, 20,
                v => _startingFrame = v));

            AddParameter(FilterParameter.Int(
                "frame_skip",
                "Frame Skip",
                "Additional fire simulation steps run per update tick.",
                0, 20, 0,
                v => _frameSkip = v));

            AddParameter(FilterParameter.Float(
                "step_rate",
                "Step Rate (Hz)",
                "Simulation update rate in ticks per second.",
                1f, 120f, 60f,
                v => _stepRate = v,
                step: 1f));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/FireEdges"))
            {
                return;
            }

            GpuStats.TryCreate(out _stats);
            _hasFireHistory = false;
            _stepCounter = 0;
            _stepAccum = 0f;
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            RenderTextureFormat lumFmt = GpuUtils.LuminanceFormat;
            EnsureFireRts(w, h, lumFmt);

            float now = Time.realtimeSinceStartup;
            int simSteps = 0;
            if (!_hasFireHistory)
            {
                Graphics.Blit(Texture2D.blackTexture, _fireRtA);
                simSteps = Mathf.Max(1, _startingFrame + 1);
                _lastStepTime = now;
                _stepAccum = 0f;
                _hasFireHistory = true;
            }
            else
            {
                float dt = Mathf.Clamp(now - _lastStepTime, 0f, 0.25f);
                _lastStepTime = now;
                _stepAccum += dt * _stepRate;
                int ticks = Mathf.Min(8, Mathf.FloorToInt(_stepAccum));
                if (ticks > 0)
                {
                    _stepAccum -= ticks;
                    simSteps = ticks * (1 + _frameSkip);
                }
            }

            if (simSteps <= 0)
            {
                Material.SetTexture("_BaseTex", source);
                Graphics.Blit(_fireRtA, destination, Material, 6);
                return;
            }

            RenderTextureFormat floatFmt = GpuUtils.FloatFormat;
            RenderTexture normRt = null;
            RenderTexture gradRt = null;
            RenderTexture edgeMaskRt = null;
            RenderTexture sparksRt = null;
            RenderTexture procSparksRt = null;

            try
            {
                normRt = GpuUtils.GetTempRt(w, h, floatFmt);
                gradRt = GpuUtils.GetTempRt(w, h, floatFmt);
                edgeMaskRt = GpuUtils.GetTempRt(w, h, lumFmt, FilterMode.Point);
                sparksRt = GpuUtils.GetTempRt(w, h, lumFmt);
                procSparksRt = GpuUtils.GetTempRt(w, h, lumFmt, FilterMode.Point);

                // 1. norm -> gradient_norm -> n 0,1 -> roundify
                GpuNorm.ComputeNorm(source, normRt);
                GpuGradientNorm.ComputeGradientNorm(normRt, gradRt, scalarMode: true);
                GpuNormalize.Normalize(gradRt, normRt, rgbMode: false, stats: _stats);

                Material.SetFloat("_Edges", _edges);
                Graphics.Blit(normRt, edgeMaskRt, Material, 0);

                float attenCoeff = (1f - Mathf.Pow(Mathf.Clamp01(_attenuation), 4f)) * 0.25f;
                Material.SetFloat("_AttenCoeff", attenCoeff);
                Material.SetFloat("_ThresholdPct", _threshold);
                Material.SetTexture("_EdgeMaskTex", edgeMaskRt);

                // 2. Step fire simulation: noise * mask -> blur -> threshold/equalize -> blend -> correlate upward
                for (int s = 0; s < simSteps; s++)
                {
                    _stepCounter++;
                    Material.SetInt("_StepSeed", _stepCounter);
                    Graphics.Blit(edgeMaskRt, sparksRt, Material, 1);

                    if (_smoothness > 0.05f)
                    {
                        GpuBlur.BlurInPlace(sparksRt, _smoothness);
                    }

                    if (_threshold > 0.0001f)
                    {
                        _stats.ComputeMinMax(sparksRt, rgbMode: false);
                        Material.SetBuffer("_StatsBuf", _stats.StatsBuffer);
                        Graphics.Blit(sparksRt, procSparksRt, Material, 2);
                    }
                    else
                    {
                        _stats.ComputeHistogramCdf(sparksRt, HistOptions);
                        Material.SetTexture("_CdfTex", _stats.CdfTexture);
                        Graphics.Blit(sparksRt, procSparksRt, Material, 3);
                    }

                    Material.SetTexture("_SparksTex", procSparksRt);
                    Graphics.Blit(_fireRtA, _fireRtB, Material, 4);
                    Graphics.Blit(_fireRtB, _fireRtA, Material, 5);
                }

                // 3. Map scalar fire buffer through 5-stop fire LUT
                Material.SetTexture("_BaseTex", source);
                Graphics.Blit(_fireRtA, destination, Material, 6);
            }
            finally
            {
                GpuUtils.ReleaseTemp(normRt);
                GpuUtils.ReleaseTemp(gradRt);
                GpuUtils.ReleaseTemp(edgeMaskRt);
                GpuUtils.ReleaseTemp(sparksRt);
                GpuUtils.ReleaseTemp(procSparksRt);
            }
        }

        public override void ResetParameters()
        {
            base.ResetParameters();
            _stats?.ResetHistory();
            _hasFireHistory = false;
            _stepAccum = 0f;
        }

        public override void Dispose()
        {
            _stats?.Dispose();
            _stats = null;
            GpuUtils.ReleaseRt(ref _fireRtA);
            GpuUtils.ReleaseRt(ref _fireRtB);
            _allocW = -1;
            _allocH = -1;
            _hasFireHistory = false;
            base.Dispose();
        }

        private void EnsureFireRts(int w, int h, RenderTextureFormat format)
        {
            if (_fireRtA != null && _fireRtB != null && _allocW == w && _allocH == h && _allocFormat == format)
            {
                return;
            }

            GpuUtils.ReleaseRt(ref _fireRtA);
            GpuUtils.ReleaseRt(ref _fireRtB);

            _fireRtA = GpuUtils.CreateRt(w, h, format, FilterMode.Point, RenderTextureReadWrite.Linear);
            _fireRtB = GpuUtils.CreateRt(w, h, format, FilterMode.Point, RenderTextureReadWrite.Linear);

            _allocW = w;
            _allocH = h;
            _allocFormat = format;
            _hasFireHistory = false;
            _stepAccum = 0f;
        }
    }
}