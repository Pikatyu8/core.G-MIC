using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class WarpByIntensityFilter : FilterBase
    {
        private static readonly string[] InterpChoices =
        {
            "Nearest Neighbor",
            "Linear"
        };

        private static readonly string[] BoundaryChoices =
        {
            "Transparent",
            "Nearest",
            "Periodic",
            "Mirror"
        };

        private float _xFactor = 0.04f;
        private float _yFactor = 0.04f;
        private float _xOffset = 128f;
        private float _yOffset = 128f;
        private bool _correlated = false;
        private int _interpolation = 1;
        private int _boundary = 3;
        private int _channelMode = 0;

        public override string Id => "fx_warp_by_intensity";
        public override string DisplayName => "Warp by Intensity";
        public override string Description => "Warps the image based on its own pixel intensity.";
        public override FilterCategory Category => FilterCategory.Standard;
        public override bool IsAvailable => GpuWarp.IsAvailable && GpuChannelOps.IsAvailable;

        public WarpByIntensityFilter()
        {
            AddParameter(FilterParameter.Float(
                "x_factor",
                "X-Factor",
                "Horizontal displacement scale per intensity unit.",
                -6f, 6f, 0.04f,
                v => _xFactor = v,
                step: 0.02f));

            AddParameter(FilterParameter.Float(
                "y_factor",
                "Y-Factor",
                "Vertical displacement scale per intensity unit.",
                -6f, 6f, 0.04f,
                v => _yFactor = v,
                step: 0.02f));

            AddParameter(FilterParameter.Float(
                "x_offset",
                "X-Offset",
                "Horizontal intensity zero-point offset.",
                0f, 255f, 128f,
                v => _xOffset = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "y_offset",
                "Y-Offset",
                "Vertical intensity zero-point offset.",
                0f, 255f, 128f,
                v => _yOffset = v,
                step: 1f));

            AddParameter(FilterParameter.Bool(
                "correlated",
                "Correlated Channels",
                "Warp RGB channels together using Euclidean norm instead of independently.",
                false,
                v => _correlated = v));

            AddParameter(FilterParameter.Choice(
                "interpolation",
                "Interpolation",
                "Pixel sampling interpolation mode.",
                InterpChoices,
                1,
                v => _interpolation = v));

            AddParameter(FilterParameter.Choice(
                "boundary",
                "Boundary",
                "Sampling behavior when displacement reaches outside the frame.",
                BoundaryChoices,
                3,
                v => _boundary = v));

            AddParameter(FilterParameter.Choice(
                "channels",
                "Channel(s)",
                "Target color space/channels.",
                GpuChannelOps.ChannelChoices,
                0,
                v => _channelMode = v));
        }

        public override void Initialize()
        {
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            GpuChannelOps.ApplyInChannelSpace(
                source,
                destination,
                _channelMode,
                (src, dst) => GpuWarp.WarpByIntensity(
                    src,
                    dst,
                    new Vector2(_xFactor, _yFactor),
                    new Vector2(_xOffset, _yOffset),
                    _correlated,
                    _interpolation,
                    _boundary,
                    bidirectional: false));
        }
    }
}