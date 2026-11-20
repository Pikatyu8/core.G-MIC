using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    public sealed class GpuStatsOptions
    {
        public bool RgbMode { get; set; } = false;
        public int ClipDivisor { get; set; } = 0;
        public bool ZeroBasedCdf { get; set; } = false;
        public float TemporalAlpha { get; set; } = 1f;
        public bool ResetHistory { get; set; } = false;
    }

    public sealed class GpuStats : IDisposable
    {
        private static readonly GpuStatsOptions DefaultOptions = new GpuStatsOptions();

        private ComputeShader _compute;
        private int _kernelClear;
        private int _kernelReduceMinMax;
        private int _kernelReduceChannelMinMax;
        private int _kernelSmoothMinMax;
        private int _kernelSmoothChannelMinMax;
        private int _kernelHistogram;
        private int _kernelBuildCdf;
        private int _kernelOtsuThreshold;

        private ComputeBuffer _statsBuf;
        private ComputeBuffer _channelStatsBuf;
        private ComputeBuffer _histBuf;
        private ComputeBuffer _temporalMinMaxBuf;
        private ComputeBuffer _temporalChannelMinMaxBuf;
        private ComputeBuffer _temporalOtsuBuf;
        private ComputeBuffer _cdfHistoryBuf;
        private ComputeBuffer _cdfBuffer;
        private RenderTexture _cdfTex;

        private bool _hasMinMaxHistory;
        private bool _hasChannelMinMaxHistory;
        private bool _hasOtsuHistory;
        private bool _hasCdfHistory;

        public bool IsAvailable => _compute != null && SystemInfo.supportsComputeShaders;
        public ComputeBuffer StatsBuffer => _statsBuf;
        public ComputeBuffer ChannelStatsBuffer => _channelStatsBuf;
        public ComputeBuffer CdfBuffer => _cdfBuffer;
        public RenderTexture CdfTexture => _cdfTex;

        private GpuStats(ComputeShader compute)
        {
            _compute = compute;
            _kernelClear = _compute.FindKernel("CSClear");
            _kernelReduceMinMax = _compute.FindKernel("CSReduceMinMax");
            _kernelReduceChannelMinMax = _compute.FindKernel("CSReduceChannelMinMax");
            _kernelSmoothMinMax = _compute.FindKernel("CSSmoothMinMax");
            _kernelSmoothChannelMinMax = _compute.FindKernel("CSSmoothChannelMinMax");
            _kernelHistogram = _compute.FindKernel("CSHistogram");
            _kernelBuildCdf = _compute.FindKernel("CSBuildCdf");
            _kernelOtsuThreshold = _compute.FindKernel("CSOtsuThreshold");

            _statsBuf = new ComputeBuffer(3, sizeof(uint));
            _channelStatsBuf = new ComputeBuffer(6, sizeof(uint));
            _histBuf = new ComputeBuffer(256, sizeof(uint));
            _temporalMinMaxBuf = new ComputeBuffer(1, sizeof(float) * 2);
            _temporalMinMaxBuf.SetData(new[] { Vector2.zero });
            _temporalChannelMinMaxBuf = new ComputeBuffer(6, sizeof(float));
            _temporalOtsuBuf = new ComputeBuffer(1, sizeof(float));
            _cdfHistoryBuf = new ComputeBuffer(256, sizeof(float) * 4);
            _cdfBuffer = new ComputeBuffer(256, sizeof(float));
            _cdfTex = GpuUtils.CreateRt(256, 1, RenderTextureFormat.ARGBFloat, FilterMode.Bilinear, RenderTextureReadWrite.Linear, randomWrite: true);
        }

        public static bool TryCreate(out GpuStats stats)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                stats = null;
                return false;
            }

            ComputeShader cs = ShaderManager.GetComputeShader("GmicStats");
            if (cs == null)
            {
                stats = null;
                return false;
            }

            stats = new GpuStats(cs);
            return true;
        }

        public void ComputeMinMax(RenderTexture input, bool rgbMode = false, float temporalAlpha = 1f, bool resetHistory = false)
        {
            if (!IsAvailable || input == null)
            {
                return;
            }

            int w = input.width;
            int h = input.height;
            int groupsX = (w + 15) / 16;
            int groupsY = (h + 15) / 16;

            _compute.SetInt("_Width", w);
            _compute.SetInt("_Height", h);
            _compute.SetInt("_RgbMode", rgbMode ? 1 : 0);

            DispatchClear();

            _compute.SetTexture(_kernelReduceMinMax, "_InputTex", input);
            _compute.SetBuffer(_kernelReduceMinMax, "_StatsBuf", _statsBuf);
            _compute.Dispatch(_kernelReduceMinMax, groupsX, groupsY, 1);

            if (temporalAlpha < 0.9999f)
            {
                bool doReset = resetHistory || !_hasMinMaxHistory;
                _compute.SetInt("_ResetHistory", doReset ? 1 : 0);
                _compute.SetFloat("_TemporalAlpha", Mathf.Clamp01(temporalAlpha));
                _compute.SetBuffer(_kernelSmoothMinMax, "_StatsBuf", _statsBuf);
                _compute.SetBuffer(_kernelSmoothMinMax, "_TemporalMinMax", _temporalMinMaxBuf);
                _compute.Dispatch(_kernelSmoothMinMax, 1, 1, 1);
                _hasMinMaxHistory = true;
            }
        }

        public void ComputeChannelMinMax(RenderTexture input, float temporalAlpha = 1f, bool resetHistory = false)
        {
            if (!IsAvailable || input == null)
            {
                return;
            }

            int w = input.width;
            int h = input.height;
            int groupsX = (w + 15) / 16;
            int groupsY = (h + 15) / 16;

            _compute.SetInt("_Width", w);
            _compute.SetInt("_Height", h);

            DispatchClear();

            _compute.SetTexture(_kernelReduceChannelMinMax, "_InputTex", input);
            _compute.SetBuffer(_kernelReduceChannelMinMax, "_ChannelStatsBuf", _channelStatsBuf);
            _compute.Dispatch(_kernelReduceChannelMinMax, groupsX, groupsY, 1);

            if (temporalAlpha < 0.9999f)
            {
                bool doReset = resetHistory || !_hasChannelMinMaxHistory;
                _compute.SetInt("_ResetHistory", doReset ? 1 : 0);
                _compute.SetFloat("_TemporalAlpha", Mathf.Clamp01(temporalAlpha));
                _compute.SetBuffer(_kernelSmoothChannelMinMax, "_ChannelStatsBuf", _channelStatsBuf);
                _compute.SetBuffer(_kernelSmoothChannelMinMax, "_TemporalChannelMinMax", _temporalChannelMinMaxBuf);
                _compute.Dispatch(_kernelSmoothChannelMinMax, 1, 1, 1);
                _hasChannelMinMaxHistory = true;
            }
        }

        public void ComputeHistogramCdf(RenderTexture input, GpuStatsOptions options = null)
        {
            if (!IsAvailable || input == null)
            {
                return;
            }

            options ??= DefaultOptions;
            ComputeMinMax(input, options.RgbMode);

            int groupsX = (input.width + 15) / 16;
            int groupsY = (input.height + 15) / 16;

            _compute.SetTexture(_kernelHistogram, "_InputTex", input);
            _compute.SetBuffer(_kernelHistogram, "_StatsBuf", _statsBuf);
            _compute.SetBuffer(_kernelHistogram, "_HistBuf", _histBuf);
            _compute.Dispatch(_kernelHistogram, groupsX, groupsY, 1);

            bool useTemporal = options.TemporalAlpha < 0.9999f;
            bool doReset = options.ResetHistory || !_hasCdfHistory;

            _compute.SetInt("_ClipDivisor", Mathf.Max(0, options.ClipDivisor));
            _compute.SetInt("_ZeroBasedCdf", options.ZeroBasedCdf ? 1 : 0);
            _compute.SetInt("_UseTemporal", useTemporal ? 1 : 0);
            _compute.SetInt("_ResetHistory", doReset ? 1 : 0);
            _compute.SetFloat("_TemporalAlpha", Mathf.Clamp01(options.TemporalAlpha));

            _compute.SetBuffer(_kernelBuildCdf, "_StatsBuf", _statsBuf);
            _compute.SetBuffer(_kernelBuildCdf, "_HistBuf", _histBuf);
            _compute.SetBuffer(_kernelBuildCdf, "_CdfHistory", _cdfHistoryBuf);
            _compute.SetBuffer(_kernelBuildCdf, "_CdfBuffer", _cdfBuffer);
            _compute.SetTexture(_kernelBuildCdf, "_CdfTex", _cdfTex);
            _compute.Dispatch(_kernelBuildCdf, 1, 1, 1);

            if (useTemporal)
            {
                _hasCdfHistory = true;
            }
        }

        public void ComputeOtsuThreshold(RenderTexture input, bool rgbMode = false, float temporalAlpha = 1f, bool resetHistory = false)
        {
            if (!IsAvailable || input == null)
            {
                return;
            }

            ComputeMinMax(input, rgbMode);

            int groupsX = (input.width + 15) / 16;
            int groupsY = (input.height + 15) / 16;

            _compute.SetTexture(_kernelHistogram, "_InputTex", input);
            _compute.SetBuffer(_kernelHistogram, "_StatsBuf", _statsBuf);
            _compute.SetBuffer(_kernelHistogram, "_HistBuf", _histBuf);
            _compute.Dispatch(_kernelHistogram, groupsX, groupsY, 1);

            bool useTemporal = temporalAlpha < 0.9999f;
            bool doReset = resetHistory || !_hasOtsuHistory;

            _compute.SetInt("_UseTemporal", useTemporal ? 1 : 0);
            _compute.SetInt("_ResetHistory", doReset ? 1 : 0);
            _compute.SetFloat("_TemporalAlpha", Mathf.Clamp01(temporalAlpha));

            _compute.SetBuffer(_kernelOtsuThreshold, "_HistBuf", _histBuf);
            _compute.SetBuffer(_kernelOtsuThreshold, "_StatsBuf", _statsBuf);
            _compute.SetBuffer(_kernelOtsuThreshold, "_TemporalOtsu", _temporalOtsuBuf);
            _compute.Dispatch(_kernelOtsuThreshold, 1, 1, 1);

            if (useTemporal)
            {
                _hasOtsuHistory = true;
            }
        }

        public void ResetHistory()
        {
            _hasMinMaxHistory = false;
            _hasChannelMinMaxHistory = false;
            _hasOtsuHistory = false;
            _hasCdfHistory = false;
        }

        public void Dispose()
        {
            GpuUtils.ReleaseBuffer(ref _statsBuf);
            GpuUtils.ReleaseBuffer(ref _channelStatsBuf);
            GpuUtils.ReleaseBuffer(ref _histBuf);
            GpuUtils.ReleaseBuffer(ref _temporalMinMaxBuf);
            GpuUtils.ReleaseBuffer(ref _temporalChannelMinMaxBuf);
            GpuUtils.ReleaseBuffer(ref _temporalOtsuBuf);
            GpuUtils.ReleaseBuffer(ref _cdfHistoryBuf);
            GpuUtils.ReleaseBuffer(ref _cdfBuffer);
            GpuUtils.ReleaseRt(ref _cdfTex);
            _hasMinMaxHistory = false;
            _hasChannelMinMaxHistory = false;
            _hasOtsuHistory = false;
            _hasCdfHistory = false;
            _compute = null;
        }

        private void DispatchClear()
        {
            _compute.SetBuffer(_kernelClear, "_StatsBuf", _statsBuf);
            _compute.SetBuffer(_kernelClear, "_ChannelStatsBuf", _channelStatsBuf);
            _compute.SetBuffer(_kernelClear, "_HistBuf", _histBuf);
            _compute.Dispatch(_kernelClear, 1, 1, 1);
        }
    }
}