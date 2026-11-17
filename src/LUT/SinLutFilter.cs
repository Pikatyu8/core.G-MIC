using System.Collections.Generic;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.LUT
{
    public class SinLutFilter : IFilter
    {
        private const int LutSize = 64;

        private Material _material;
        private Texture2D _lutTexture;

        private int _mode = 0; // 0: |sin|, 1: |tan|, 2: |sec| - 1
        private float _sinOscillations = 5.0f;
        private float _tanOscillations = 2.0f;
        private float _secOscillations = 1.5f;
        private bool _forceWhite = false;
        private float _intensity = 1f;

        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();

        public string Id => "test_sin_lut";
        public string DisplayName => "Trigonometry LUT";
        public string Description => "Color LUT computing |sin|, |tan|, or |sec|-1 oscillations per channel.";
        public FilterCategory Category => FilterCategory.Lut;
        public bool IsAvailable => _material != null && _lutTexture != null;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public SinLutFilter()
        {
            _parameters.Add(FilterParameter.Choice(
                "trig_mode",
                "Function",
                "Trigonometric mapping function applied per RGB channel.",
                new[] { "|sin|", "|tan|", "|sec| - 1" },
                0,
                val =>
                {
                    _mode = val;
                    RebuildLut();
                }));

            _parameters.Add(FilterParameter.Float(
                "sin_osc",
                "Sine Osc (xPI)",
                "Half-wave oscillation count for |sin(c * N * PI)|.",
                0.5f, 30f, 5.0f,
                val =>
                {
                    _sinOscillations = val;
                    RebuildLut();
                },
                isVisible: () => _mode == 0,
                step: 0.1f));

            _parameters.Add(FilterParameter.Float(
                "tan_osc",
                "Tan Osc (xPI)",
                "Period frequency for clamped |tan(c * N * PI)|.",
                0.2f, 12f, 2.0f,
                val =>
                {
                    _tanOscillations = val;
                    RebuildLut();
                },
                isVisible: () => _mode == 1,
                step: 0.1f));

            _parameters.Add(FilterParameter.Float(
                "sec_osc",
                "Sec Osc (xPI)",
                "Period frequency for clamped |sec(c * N * PI)| - 1.",
                0.2f, 10f, 1.5f,
                val =>
                {
                    _secOscillations = val;
                    RebuildLut();
                },
                isVisible: () => _mode == 2,
                step: 0.1f));

            _parameters.Add(FilterParameter.Bool(
                "force_white",
                "Force White -> White",
                "Smoothly preserve pure white highlights instead of wrapping them to black.",
                false,
                val =>
                {
                    _forceWhite = val;
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
                },
                step: 0.02f));
        }

        public void Initialize()
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Lut");
            if (shader == null)
            {
                return;
            }

            _lutTexture = LutTextureGenerator.GenerateStrip(EvaluateCurrent, LutSize);

            _material = new Material(shader);
            _material.SetTexture("_LutTex", _lutTexture);
            _material.SetFloat("_LutSize", LutSize);
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

            LutTextureGenerator.FillStrip(_lutTexture, EvaluateCurrent, LutSize);
        }

        private Color EvaluateCurrent(float r, float g, float b)
        {
            return new Color(
                EvaluateChannel(r),
                EvaluateChannel(g),
                EvaluateChannel(b),
                1f);
        }

        private float EvaluateChannel(float c)
        {
            float val;
            switch (_mode)
            {
                case 1:
                {
                    float x = c * _tanOscillations * Mathf.PI;
                    float cos = Mathf.Abs(Mathf.Cos(x));
                    val = (cos < 1e-4f) ? 1f : Mathf.Clamp01(Mathf.Abs(Mathf.Sin(x)) / cos);
                    break;
                }
                case 2:
                {
                    float x = c * _secOscillations * Mathf.PI;
                    float cos = Mathf.Abs(Mathf.Cos(x));
                    val = (cos < 1e-4f) ? 1f : Mathf.Clamp01((1f / cos) - 1f);
                    break;
                }
                default:
                {
                    float x = c * _sinOscillations * Mathf.PI;
                    val = Mathf.Clamp01(Mathf.Abs(Mathf.Sin(x)));
                    break;
                }
            }

            if (_forceWhite && c > 0.85f)
            {
                float t = Mathf.InverseLerp(0.85f, 1.0f, c);
                float smooth = t * t * (3f - 2f * t);
                val = Mathf.Lerp(val, 1f, smooth);
            }

            return val;
        }
    }
}