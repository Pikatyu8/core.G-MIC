using System;
using CoreGMIC.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreGMIC.Heavy
{
    public class CubismFilter : FilterBase
    {
        private const int MaxQuadsPerIteration = 30000;
        private const float MaxIterOverdrawFactor = 600f;
        private const float MaxFrameOverdrawFactor = 1800f;
        private const int HistorySlotsPerQuad = 64;

        private ComputeShader _stepCompute;
        private int _kernelInitHistory;
        private int _kernelStepQuads;

        private ComputeBuffer _dummyOffsetBuffer;
        private ComputeBuffer _offsetsBuffer;
        private ComputeBuffer _historyBuffer;

        private CommandBuffer _cmd;

        private int _iterations = 1;
        private float _density = 21f;
        private float _thickness = 12f;
        private float _angle = 90f;
        private float _opacity = 1f;
        private float _smoothness = 0f;

        private bool _animate = false;
        private int _driftRadius = 16;
        private int _historySteps = 63;
        private float _persistence = 1f;
        private float _stepRate = 60f;

        private int _allocatedTotalQuads = -1;
        private int _lastQuadsPerIter = -1;
        private float _lastHalfSize = -1f;
        private int _lastW = -1;
        private int _lastH = -1;
        private uint _stepIndex;
        private float _nextStepTime;

        public override string Id => "fx_cubism";
        public override string DisplayName => "Cubism";
        public override string Description => "Transforms the frame into a cubist composition of rotated textured squares.";
        public override FilterCategory Category => FilterCategory.Standard;

        public CubismFilter()
        {
            AddParameter(FilterParameter.Int(
                "iterations",
                "Iterations",
                "Number of cubism passes applied sequentially.",
                0, 10, 1,
                v => _iterations = v));

            AddParameter(FilterParameter.Float(
                "density",
                "Density",
                "Tile coverage density.",
                0f, 80f, 21f,
                v => _density = v));

            AddParameter(FilterParameter.Float(
                "thickness",
                "Thickness",
                "Relative tile size (% of frame).",
                0f, 50f, 12f,
                v => _thickness = v));

            AddParameter(FilterParameter.Float(
                "angle",
                "Angle",
                "Maximum random tile rotation in degrees.",
                0f, 360f, 90f,
                v => _angle = v));

            AddParameter(FilterParameter.Float(
                "opacity",
                "Opacity",
                "Blend opacity per iteration.",
                0.01f, 1f, 1f,
                v => _opacity = v));

            AddParameter(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Pre-blur sigma applied to tile texture samples.",
                0f, 5f, 0f,
                v => _smoothness = v));

            AddParameter(FilterParameter.Bool(
                "animate",
                "Animate Placement",
                "Shift each tile within a max pixel radius while avoiding its last M positions.",
                false,
                v =>
                {
                    _animate = v;
                    ResetAnimationState();
                }));

            AddParameter(FilterParameter.Int(
                "drift_radius",
                "Max Step (px)",
                "Maximum step distance in pixels per move.",
                1, 100, 16,
                v => _driftRadius = v,
                isVisible: () => _animate));

            AddParameter(FilterParameter.Int(
                "history_steps",
                "No-Repeat Steps",
                "Number of previous positions each tile must avoid.",
                0, 63, 63,
                v => _historySteps = v,
                isVisible: () => _animate));

            AddParameter(FilterParameter.Float(
                "persistence",
                "Direction Persistence",
                "Momentum along each tile's travel direction (0 = random, 1.0 = long streams).",
                0f, 1f, 1f,
                v => _persistence = v,
                isVisible: () => _animate));

            AddParameter(FilterParameter.Float(
                "step_rate",
                "Step Rate (Hz)",
                "Number of placement steps per second.",
                1f, 60f, 60f,
                v =>
                {
                    _stepRate = v;
                    _nextStepTime = Time.realtimeSinceStartup;
                },
                isVisible: () => _animate));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Cubism"))
            {
                return;
            }

            _cmd = new CommandBuffer
            {
                name = "CoreGMIC_Cubism"
            };

            _dummyOffsetBuffer = new ComputeBuffer(1, sizeof(float) * 4);
            _dummyOffsetBuffer.SetData(new[] { Vector4.zero });

            if (SystemInfo.supportsComputeShaders)
            {
                _stepCompute = ShaderManager.GetComputeShader("GmicCubismStep");
                if (_stepCompute != null)
                {
                    _kernelInitHistory = _stepCompute.FindKernel("CSInitHistory");
                    _kernelStepQuads = _stepCompute.FindKernel("CSStepQuads");
                }
            }
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            if (_iterations <= 0 || _density <= 0.0001f || _thickness <= 0.0001f || _opacity <= 0.0001f ||
                (_angle <= 0.0001f && _smoothness <= 0.0001f))
            {
                Graphics.Blit(source, destination);
                return;
            }

            int w = source.width;
            int h = source.height;

            float p = Mathf.Max(0.5f, _thickness * Mathf.Max(w, h) / 200f);
            long rawN = (long)Math.Round(1.5 * _density * w * h / (4.0 * p) / 100.0);
            double areaPerQuad = Math.Max(4.0, 4.0 * p * p);
            float maxIterOverdraw = Mathf.Min(MaxIterOverdrawFactor, MaxFrameOverdrawFactor / Mathf.Max(1, _iterations));
            long fillCapN = (long)((maxIterOverdraw * w * h) / areaPerQuad);
            int quadCount = (int)Math.Min(rawN, Math.Min(MaxQuadsPerIteration, Math.Max(1L, fillCapN)));

            if (quadCount <= 0)
            {
                Graphics.Blit(source, destination);
                return;
            }

            bool useOffsets = _animate && _stepCompute != null;
            if (useOffsets)
            {
                UpdatePlacementHistory(quadCount, _iterations, p, w, h);
            }

            RenderTextureFormat format = source.format;
            RenderTexture rtA = null;
            RenderTexture rtB = null;
            RenderTexture blurA = null;
            RenderTexture blurB = null;
            RenderTexture canvasRt = null;

            try
            {
                rtA = GpuUtils.GetTempRt(w, h, format, FilterMode.Bilinear, RenderTextureReadWrite.Default);
                if (_iterations > 1)
                {
                    rtB = GpuUtils.GetTempRt(w, h, format, FilterMode.Bilinear, RenderTextureReadWrite.Default);
                }

                canvasRt = GpuUtils.GetTempRt(w, h, format, FilterMode.Bilinear, RenderTextureReadWrite.Default);

                if (_smoothness > 0.001f)
                {
                    blurA = GpuUtils.GetTempRt(w, h, format, FilterMode.Bilinear, RenderTextureReadWrite.Default);
                    blurB = GpuUtils.GetTempRt(w, h, format, FilterMode.Bilinear, RenderTextureReadWrite.Default);
                }

                Matrix4x4 orthoProj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), false);
                RenderTexture curr = source;

                for (int iter = 0; iter < _iterations; iter++)
                {
                    RenderTexture sampleTex = curr;
                    if (_smoothness > 0.001f)
                    {
                        Material.SetFloat("_Smoothness", _smoothness);
                        Graphics.Blit(curr, blurA, Material, 0);
                        Graphics.Blit(blurA, blurB, Material, 1);
                        sampleTex = blurB;
                    }

                    Graphics.Blit(curr, canvasRt);

                    Material.SetTexture("_MainTex", sampleTex);
                    Material.SetVector("_CubismParams", new Vector4(p, _angle * Mathf.Deg2Rad, w, h));
                    Material.SetInt("_IterSeed", iter + 1);
                    Material.SetInt("_UseOffsets", useOffsets ? 1 : 0);
                    Material.SetInt("_OffsetBase", iter * quadCount);
                    Material.SetBuffer("_CurrentOffsets", useOffsets ? _offsetsBuffer : _dummyOffsetBuffer);

                    _cmd.Clear();
                    _cmd.SetRenderTarget(canvasRt);
                    _cmd.SetViewport(new Rect(0f, 0f, w, h));
                    _cmd.SetViewProjectionMatrices(Matrix4x4.identity, orthoProj);
                    _cmd.DrawProcedural(Matrix4x4.identity, Material, 2, MeshTopology.Triangles, 6, quadCount);
                    Graphics.ExecuteCommandBuffer(_cmd);

                    bool isLast = (iter == _iterations - 1);
                    RenderTexture iterOut = isLast
                        ? destination
                        : ((curr == rtA) ? rtB : rtA);

                    if (_opacity >= 0.999f)
                    {
                        Graphics.Blit(canvasRt, iterOut);
                    }
                    else
                    {
                        Material.SetTexture("_BaseTex", curr);
                        Material.SetFloat("_Opacity", _opacity);
                        Graphics.Blit(canvasRt, iterOut, Material, 3);
                    }

                    curr = iterOut;
                }
            }
            finally
            {
                GpuUtils.ReleaseTemp(rtA);
                GpuUtils.ReleaseTemp(rtB);
                GpuUtils.ReleaseTemp(blurA);
                GpuUtils.ReleaseTemp(blurB);
                GpuUtils.ReleaseTemp(canvasRt);
            }
        }

        public override void Dispose()
        {
            if (_cmd != null)
            {
                _cmd.Release();
                _cmd = null;
            }

            ReleaseComputeBuffers();
            GpuUtils.ReleaseBuffer(ref _dummyOffsetBuffer);
            _stepCompute = null;
            base.Dispose();
        }

        private void UpdatePlacementHistory(int quadsPerIter, int iterations, float halfSizePx, int width, int height)
        {
            int totalQuads = quadsPerIter * iterations;
            int groups = (totalQuads + 63) / 64;

            if (_offsetsBuffer == null || _historyBuffer == null ||
                _allocatedTotalQuads != totalQuads ||
                _lastQuadsPerIter != quadsPerIter ||
                !Mathf.Approximately(_lastHalfSize, halfSizePx) ||
                _lastW != width ||
                _lastH != height)
            {
                if (_offsetsBuffer == null || _allocatedTotalQuads != totalQuads)
                {
                    ReleaseComputeBuffers();
                    _offsetsBuffer = new ComputeBuffer(totalQuads, sizeof(float) * 4);
                    _historyBuffer = new ComputeBuffer(totalQuads * HistorySlotsPerQuad, sizeof(int) * 2);
                    _allocatedTotalQuads = totalQuads;
                }

                _lastQuadsPerIter = quadsPerIter;
                _lastHalfSize = halfSizePx;
                _lastW = width;
                _lastH = height;
                _stepIndex = 0u;
                _nextStepTime = Time.realtimeSinceStartup + (1f / Mathf.Max(0.1f, _stepRate));

                _stepCompute.SetInt("_TotalQuads", totalQuads);
                _stepCompute.SetInt("_QuadsPerIter", quadsPerIter);
                _stepCompute.SetFloat("_HalfSizePx", halfSizePx);
                _stepCompute.SetVector("_ScreenRes", new Vector4(width, height, 0f, 0f));
                _stepCompute.SetBuffer(_kernelInitHistory, "_CurrentOffsets", _offsetsBuffer);
                _stepCompute.SetBuffer(_kernelInitHistory, "_HistoryBuffer", _historyBuffer);
                _stepCompute.Dispatch(_kernelInitHistory, groups, 1, 1);
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextStepTime)
            {
                return;
            }

            float interval = 1f / Mathf.Max(0.1f, _stepRate);
            _nextStepTime = now + interval;
            _stepIndex++;

            _stepCompute.SetInt("_TotalQuads", totalQuads);
            _stepCompute.SetFloat("_HalfSizePx", halfSizePx);
            _stepCompute.SetVector("_ScreenRes", new Vector4(width, height, 0f, 0f));
            _stepCompute.SetInt("_DriftRadius", _driftRadius);
            _stepCompute.SetInt("_HistoryCount", _historySteps);
            _stepCompute.SetFloat("_Persistence", _persistence);
            _stepCompute.SetInt("_StepIndex", unchecked((int)_stepIndex));

            _stepCompute.SetBuffer(_kernelStepQuads, "_CurrentOffsets", _offsetsBuffer);
            _stepCompute.SetBuffer(_kernelStepQuads, "_HistoryBuffer", _historyBuffer);
            _stepCompute.Dispatch(_kernelStepQuads, groups, 1, 1);
        }

        private void ResetAnimationState()
        {
            _stepIndex = 0u;
            _lastQuadsPerIter = -1;
            _lastHalfSize = -1f;
            _nextStepTime = Time.realtimeSinceStartup;
        }

        private void ReleaseComputeBuffers()
        {
            GpuUtils.ReleaseBuffer(ref _offsetsBuffer);
            GpuUtils.ReleaseBuffer(ref _historyBuffer);
            _allocatedTotalQuads = -1;
        }
    }
}