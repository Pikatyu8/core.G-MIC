using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    public sealed class GpuQuadtree : IDisposable
    {
        private const int PyramidLevels = 7; // 4, 8, 16, 32, 64, 128, 256 px

        private static readonly string[] ColorUniforms =
        {
            "_ColorL0", "_ColorL1", "_ColorL2", "_ColorL3", "_ColorL4", "_ColorL5", "_ColorL6"
        };

        private static readonly string[] TensorUniforms =
        {
            "_TensorL0", "_TensorL1", "_TensorL2", "_TensorL3", "_TensorL4", "_TensorL5", "_TensorL6"
        };

        private Material _material;

        public bool IsAvailable => _material != null;

        private GpuQuadtree(Material material)
        {
            _material = material;
        }

        public static bool TryCreate(out GpuQuadtree quadtree)
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Quadtree");
            if (shader == null)
            {
                quadtree = null;
                return false;
            }

            quadtree = new GpuQuadtree(new Material(shader));
            return true;
        }

        public void Apply(
            RenderTexture source,
            RenderTexture destination,
            int mode,
            int precision,
            float homogeneity,
            int outline = 0,
            float radius1 = 3.0f,
            float radius2 = 1.5f,
            float anisotropy = 1.0f,
            bool onlyLeaves = true)
        {
            if (!IsAvailable || source == null || destination == null)
            {
                return;
            }

            int srcW = source.width;
            int srcH = source.height;
            int gridW = ((srcW + 255) / 256) * 256;
            int gridH = ((srcH + 255) / 256) * 256;

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat tensorFormat = GpuUtils.FloatFormat;

            RenderTexture[] colorLevels = new RenderTexture[PyramidLevels];
            RenderTexture[] tensorLevels = new RenderTexture[PyramidLevels];

            try
            {
                for (int k = 0; k < PyramidLevels; k++)
                {
                    int w = Mathf.Max(1, gridW >> (k + 2));
                    int h = Mathf.Max(1, gridH >> (k + 2));

                    colorLevels[k] = GpuUtils.GetTempRt(w, h, colorFormat, FilterMode.Point);
                    tensorLevels[k] = GpuUtils.GetTempRt(w, h, tensorFormat, FilterMode.Point);
                }

                _material.SetVector("_SourceSize", new Vector4(srcW, srcH, gridW, gridH));

                Graphics.Blit(source, colorLevels[0], _material, 0);
                Graphics.Blit(source, tensorLevels[0], _material, 1);

                for (int k = 1; k < PyramidLevels; k++)
                {
                    Graphics.Blit(colorLevels[k - 1], colorLevels[k], _material, 2);
                    Graphics.Blit(tensorLevels[k - 1], tensorLevels[k], _material, 2);
                }

                for (int k = 0; k < PyramidLevels; k++)
                {
                    _material.SetTexture(ColorUniforms[k], colorLevels[k]);
                    _material.SetTexture(TensorUniforms[k], tensorLevels[k]);
                }

                _material.SetInt("_Mode", mode);
                _material.SetFloat("_Precision", precision);
                _material.SetFloat("_Homogeneity", homogeneity);
                _material.SetFloat("_Outline", outline);
                _material.SetFloat("_Radius1", radius1);
                _material.SetFloat("_Radius2", radius2);
                _material.SetFloat("_Anisotropy", anisotropy);
                _material.SetFloat("_OnlyLeaves", onlyLeaves ? 1f : 0f);

                int compositePass = (mode == 2) ? 4 : 3;
                Graphics.Blit(source, destination, _material, compositePass);
            }
            finally
            {
                for (int k = 0; k < PyramidLevels; k++)
                {
                    GpuUtils.ReleaseTemp(colorLevels[k]);
                    GpuUtils.ReleaseTemp(tensorLevels[k]);
                }
            }
        }

        public void Dispose()
        {
            GpuUtils.DestroyObject(ref _material);
        }
    }
}
