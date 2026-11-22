using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class NoiseFilter : FilterBase
    {
        private static readonly string[] ValueActionChoices =
        {
            "None",
            "Cut",
            "Normalize"
        };

        private float _amplitude = 10f;
        private int _noiseType = 0;
        private int _channelMode = 7;
        private int _valueAction = 1;
        private bool _animate = true;

        private uint _seedCounter = 1u;

        public override string Id => "fx_noise";
        public override string DisplayName => "Noise [Additive]";
        public override string Description => "Adds standard random noise (Gaussian, Uniform, Salt and Pepper, or Poisson).";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable =>
            GpuNoise.IsAvailable && GpuChannelOps.IsAvailable && GpuNormalize.IsAvailable;

        public NoiseFilter()
        {
            AddParameter(FilterParameter.Float(
                "amplitude",
                "Amplitude",
                "Noise strength or percentage probability (for Salt and Pepper).",
                0f, 200f, 10f,
                v => _amplitude = v,
                step: 1f));

            AddParameter(FilterParameter.Choice(
                "noise_type",
                "Noise Type",
                "Random noise distribution.",
                GpuNoise.NoiseTypeChoices,
                0,
                v => _noiseType = v));

            AddParameter(FilterParameter.Choice(
                "channels",
                "Channel(s)",
                "Target color space/channels.",
                GpuChannelOps.ChannelChoices,
                7,
                v => _channelMode = v));

            AddParameter(FilterParameter.Choice(
                "value_action",
                "Value Action",
                "Post-action applied after adding noise.",
                ValueActionChoices,
                1,
                v => _valueAction = v));

            AddParameter(FilterParameter.Bool(
                "animate",
                "Animate",
                "Randomize the noise seed every frame.",
                true,
                v => _animate = v));
        }

        public override void Initialize()
        {
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            uint seed = _animate ? unchecked(_seedCounter++ * 2654435761u) : 0x13579BDFu;

            GpuChannelOps.ApplyInChannelSpace(
                source,
                destination,
                _channelMode,
                (src, dst) => GpuNoise.ApplyNoise(src, dst, _amplitude, _noiseType, seed),
                _valueAction);
        }
    }
}