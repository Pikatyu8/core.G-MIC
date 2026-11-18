using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.LUT
{
    public class SinLutFilter : FilterBase
    {
        private const int LutSize = 64;

        private Texture2D _lutTexture;

        private int _mode = 0; // 0: |sin|, 1: |tan|, 2: |sec| - 1
        private float _sinOscillations = 5.0f;
        private float _tanOscillations = 2.0f;
        private float _secOscillations = 1.5f;
        private bool _forceWhite = false;
        private float _intensity = 1f;

        public override string Id => "test_sin_lut";
        public override string DisplayName => "Trigonometry LUT";
        public override string Description => "Color LUT computing |sin|, |tan|, or |sec|-1 oscillations per channel.";
        public override FilterCategory Category => FilterCategory.Lut;
        public override bool IsAvailable => base.IsAvailable && _lutTexture != null;

        public SinLutFilter()
        {
            AddParameter(FilterParameter.Choice(
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

            AddParameter(FilterParameter.Float(
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

            AddParameter(FilterParameter.Float(
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

            AddParameter(FilterParameter.Float(
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

            AddParameter(FilterParameter.Bool(
                "force_white",
                "Force White -> White",
                "Smoothly preserve pure white highlights instead of wrapping them to black.",
                false,
                val =>
                {
                    _forceWhite = val;
                    RebuildLut();
                }));

            AddParameter(FilterParameter.Float(
                "intensity",
                "Intensity",
                "Blend intensity with original frame.",
                0f, 1f, 1f,
                val =>
                {
                    _intensity = val;
                    if (Material != null)
                    {
                        Material.SetFloat("_Intensity", _intensity);
                    }
                },
                step: 0.02f));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Lut"))
            {
                return;
            }

            _lutTexture = LutTextureGenerator.GenerateStrip(EvaluateCurrent, LutSize);

            Material.SetTexture("_LutTex", _lutTexture);
            Material.SetFloat("_LutSize", LutSize);
            Material.SetFloat("_Intensity", _intensity);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination, Material);
        }

        public override void Dispose()
        {
            GpuUtils.DestroyObject(ref _lutTexture);
            base.Dispose();
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