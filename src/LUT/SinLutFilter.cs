using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.LUT
{
    public class SinLutFilter : IFilter
    {
        private Material _material;
        private Texture2D _lutTexture;

        public string Id => "test_sin_lut";
        public string DisplayName => "|sin(RGB)| LUT";
        public string Description => "Test LUT filter computing (|sin(R)|, |sin(G)|, |sin(B)|) * 255.";
        public FilterCategory Category => FilterCategory.Lut;
        public bool IsAvailable => _material != null && _lutTexture != null;

        public void Initialize()
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Lut");
            if (shader == null)
            {
                return;
            }

            _lutTexture = LutTextureGenerator.GenerateStrip((r, g, b) =>
            {
                float nr = Mathf.Abs(Mathf.Sin(r * 255f));
                float ng = Mathf.Abs(Mathf.Sin(g * 255f));
                float nb = Mathf.Abs(Mathf.Sin(b * 255f));
                return new Color(nr, ng, nb, 1f);
            });

            _material = new Material(shader);
            _material.SetTexture("_LutTex", _lutTexture);
            _material.SetFloat("_LutSize", LutTextureGenerator.DefaultSize);
            _material.SetFloat("_Intensity", 1f);
        }

        public void Apply(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination, _material);
        }

        public void Dispose()
        {
            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }

            if (_lutTexture != null)
            {
                Object.Destroy(_lutTexture);
                _lutTexture = null;
            }
        }
    }
}