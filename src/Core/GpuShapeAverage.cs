using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    public sealed class GpuShapeAverage : IDisposable
    {
        private ComputeShader _compute;
        private int _kernelInit;
        private int _kernelMerge;
        private int _kernelCompress;
        private int _kernelFlattenAndAccum;

        private ComputeBuffer _parentBuffer;
        private ComputeBuffer _shapeSumBuffer;
        private int _allocW = -1;
        private int _allocH = -1;

        public bool IsAvailable =>
            _compute != null && SystemInfo.supportsComputeShaders && GpuOpsResources.IsAvailable;

        private GpuShapeAverage(ComputeShader compute)
        {
            _compute = compute;
            _kernelInit = _compute.FindKernel("CSCCLInit");
            _kernelMerge = _compute.FindKernel("CSCCLMergeBorders");
            _kernelCompress = _compute.FindKernel("CSCCLCompress");
            _kernelFlattenAndAccum = _compute.FindKernel("CSCCLFlattenAndAccum");
        }

        public static bool TryCreate(out GpuShapeAverage shapeAverage)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                shapeAverage = null;
                return false;
            }

            ComputeShader cs = ShaderManager.GetComputeShader("GmicShapeAverage");
            if (cs == null)
            {
                shapeAverage = null;
                return false;
            }

            shapeAverage = new GpuShapeAverage(cs);
            return true;
        }

        public void Apply(RenderTexture labelTex, RenderTexture sourceTex, RenderTexture destination, int mergePasses = 2)
        {
            if (!IsAvailable || labelTex == null || sourceTex == null || destination == null)
            {
                return;
            }

            Material mat = GpuOpsResources.OpsMaterial;
            if (mat == null)
            {
                return;
            }

            int w = sourceTex.width;
            int h = sourceTex.height;
            EnsureBuffers(w, h);

            int groupsX = (w + 15) / 16;
            int groupsY = (h + 15) / 16;

            _compute.SetInt("_Width", w);
            _compute.SetInt("_Height", h);

            _compute.SetTexture(_kernelInit, "_LabelTex", labelTex);
            _compute.SetBuffer(_kernelInit, "_ParentBuffer", _parentBuffer);
            _compute.SetBuffer(_kernelInit, "_ShapeSumBuffer", _shapeSumBuffer);
            _compute.Dispatch(_kernelInit, groupsX, groupsY, 1);

            _compute.SetTexture(_kernelMerge, "_LabelTex", labelTex);
            _compute.SetBuffer(_kernelMerge, "_ParentBuffer", _parentBuffer);
            _compute.SetBuffer(_kernelCompress, "_ParentBuffer", _parentBuffer);
            int passes = Mathf.Max(1, mergePasses);
            for (int p = 0; p < passes; p++)
            {
                _compute.Dispatch(_kernelMerge, groupsX, groupsY, 1);
                _compute.Dispatch(_kernelCompress, groupsX, groupsY, 1);
            }

            _compute.SetTexture(_kernelFlattenAndAccum, "_SourceTex", sourceTex);
            _compute.SetBuffer(_kernelFlattenAndAccum, "_ParentBuffer", _parentBuffer);
            _compute.SetBuffer(_kernelFlattenAndAccum, "_ShapeSumBuffer", _shapeSumBuffer);
            _compute.Dispatch(_kernelFlattenAndAccum, groupsX, groupsY, 1);

            mat.SetInt("_Width", w);
            mat.SetInt("_Height", h);
            mat.SetBuffer("_ParentBuffer", _parentBuffer);
            mat.SetBuffer("_ShapeSumBuffer", _shapeSumBuffer);
            Graphics.Blit(sourceTex, destination, mat, 4);
        }

        public void Dispose()
        {
            GpuUtils.ReleaseBuffer(ref _parentBuffer);
            GpuUtils.ReleaseBuffer(ref _shapeSumBuffer);
            _allocW = -1;
            _allocH = -1;
            _compute = null;
        }

        private void EnsureBuffers(int w, int h)
        {
            if (_parentBuffer != null && _shapeSumBuffer != null && _allocW == w && _allocH == h)
            {
                return;
            }

            GpuUtils.ReleaseBuffer(ref _parentBuffer);
            GpuUtils.ReleaseBuffer(ref _shapeSumBuffer);

            int totalPixels = Mathf.Max(1, w * h);
            _parentBuffer = new ComputeBuffer(totalPixels, sizeof(uint));
            _shapeSumBuffer = new ComputeBuffer(totalPixels * 4, sizeof(uint));
            _allocW = w;
            _allocH = h;
        }
    }
}