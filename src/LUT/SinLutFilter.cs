using System.Collections.Generic;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.LUT
{
    public class SinLutFilter : IFilter
    {
        private Material _material;
        private Texture2D _lutTexture;
        private float _frequency = 255f;
        private float _intensity = 1f;

        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();

        public string Id => "test_sin_lut";
        public string DisplayName => "|sin(RGB)| LUT";
        public string Description => "Test LUT filter computing (|sin(R*f)|, |sin(G*f)|, |sin(B*f)|).";
        public FilterCategory Category => FilterCategory.Lut;
        public bool IsAvailable => _material != null && _lutTexture != null;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public SinLutFilter()
        {
            _parameters.Add(FilterParameter.Float(
                "frequency",
                "Sin Frequency",
                "Multiplier inside |sin(channel * frequency)|.",
                1f, 255f, 255f,
                val =>
                {
                    _frequency = val;
                    RebuildLut();
                }));

            _parameters.Add(FilterParameter.Float(
                "intensity",
                "Intensity",
                "Blend intensity with original frame.",
                0f, 1f, 1f,
                val =>
                {
                    _intensity = val;
                    if (_material != null)
                    {
                        _material.SetFloat("_Intensity", _intensity);
                    }
                }));
        }

        public void Initialize()
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Lut");
            if (shader == null)
            {
                return;
            }

            _lutTexture = LutTextureGenerator.GenerateStrip((r, g, b) => EvaluateColor(r, g, b, _frequency));

            _material = new Material(shader);
            _material.SetTexture("_LutTex", _lutTexture);
            _material.SetFloat("_LutSize", LutTextureGenerator.DefaultSize);
            _material.SetFloat("_Intensity", _intensity);
        }

        public void Apply(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination, _material);
        }

        public void ResetParameters()
        {
            foreach (FilterParameter param in _parameters)
            {
                param.Reset();
            }
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

        private void RebuildLut()
        {
            if (_lutTexture == null)
            {
                return;
            }

            float freq = _frequency;
            LutTextureGenerator.FillStrip(_lutTexture, (r, g, b) => EvaluateColor(r, g, b, freq));
        }

        private static Color EvaluateColor(float r, float g, float b, float freq)
        {
            float nr = Mathf.Abs(Mathf.Sin(r * freq));
            float ng = Mathf.Abs(Mathf.Sin(g * freq));
            float nb = Mathf.Abs(Mathf.Sin(b * freq));
            return new Color(nr, ng, nb, 1f);
        }
    }
}