using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    public sealed class GpuWatershed : IDisposable
    {
        private static readonly int[] JfaSteps = { 64, 32, 16, 8, 4, 2, 1 };

        private Material _material;

        public bool IsAvailable => _material != null && GpuBlur.IsAvailable;

        private GpuWatershed(Material material)
        {
            _material = material;
        }

        public static bool TryCreate(out GpuWatershed watershed)
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Watershed");
            if (shader == null)
            {
                watershed = null;
                return false;
            }

            watershed = new GpuWatershed(new Material(shader));
            return true;
        }

        public void Apply(RenderTexture source, RenderTexture destination, float edgeThreshold = 10f, float smoothness = 1.0f)
        {
            if (!IsAvailable || source == null || destination == null)
            {
                return;
            }

            int w = source.width;
            int h = source.height;
            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat floatFormat = GpuUtils.FloatFormat;

            RenderTexture blurRt = null;
            RenderTexture gradRt = null;
            RenderTexture jfaA = null;
            RenderTexture jfaB = null;

            try
            {
                RenderTexture blurSrc = source;
                if (smoothness > 0.05f)
                {
                    blurRt = GpuUtils.GetTempRt(w, h, colorFormat);
                    GpuBlur.Blur(source, blurRt, smoothness);
                    blurSrc = blurRt;
                }

                gradRt = GpuUtils.GetTempRt(w, h, floatFormat);
                jfaA = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);
                jfaB = GpuUtils.GetTempRt(w, h, floatFormat, FilterMode.Point);

                Graphics.Blit(blurSrc, gradRt, _material, 0);

                _material.SetFloat("_EdgeThreshold", Mathf.Max(0.01f, edgeThreshold));
                Graphics.Blit(gradRt, jfaA, _material, 1);

                RenderTexture jfaSrc = jfaA;
                RenderTexture jfaDst = jfaB;
                _material.SetTexture("_GradTex", gradRt);

                for (int s = 0; s < JfaSteps.Length; s++)
                {
                    _material.SetFloat("_JfaStep", JfaSteps[s]);
                    Graphics.Blit(jfaSrc, jfaDst, _material, 2);

                    RenderTexture tmp = jfaSrc;
                    jfaSrc = jfaDst;
                    jfaDst = tmp;
                }

                _material.SetTexture("_JfaTex", jfaSrc);
                Graphics.Blit(blurSrc, destination, _material, 3);
            }
            finally
            {
                GpuUtils.ReleaseTemp(blurRt);
                GpuUtils.ReleaseTemp(gradRt);
                GpuUtils.ReleaseTemp(jfaA);
                GpuUtils.ReleaseTemp(jfaB);
            }
        }

        public void Dispose()
        {
            GpuUtils.DestroyObject(ref _material);
        }
    }
}
