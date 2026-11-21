using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class FrameCubeFilter : FilterBase
    {
        private static readonly string[] OrientationChoices =
        {
            "Normal",
            "Mirror-X",
            "Mirror-Y",
            "Mirror-XY"
        };

        private float _depth = 3f;
        private float _centerX = 50f;
        private float _centerY = 50f;
        private int _orientLeft = 0;
        private int _orientRight = 0;
        private int _orientUp = 0;
        private int _orientDown = 0;

        public override string Id => "fx_frame_cube";
        public override string DisplayName => "Frame [Cube]";
        public override string Description => "Maps the image onto the inside faces of a 3D cube.";
        public override FilterCategory Category => FilterCategory.Standard;

        public FrameCubeFilter()
        {
            AddParameter(FilterParameter.Float(
                "depth",
                "Depth",
                "Depth of the 3D cube corridor.",
                0f, 30f, 3f,
                v => _depth = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "center_x",
                "Center X (%)",
                "Horizontal vanishing point of the inner back face.",
                0f, 100f, 50f,
                v => _centerX = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "center_y",
                "Center Y (%)",
                "Vertical vanishing point of the inner back face.",
                0f, 100f, 50f,
                v => _centerY = v,
                step: 1f));

            AddParameter(FilterParameter.Choice(
                "orient_left",
                "Left Side Orientation",
                "Texture orientation on the left inner face.",
                OrientationChoices,
                0,
                v => _orientLeft = v));

            AddParameter(FilterParameter.Choice(
                "orient_right",
                "Right Side Orientation",
                "Texture orientation on the right inner face.",
                OrientationChoices,
                0,
                v => _orientRight = v));

            AddParameter(FilterParameter.Choice(
                "orient_up",
                "Upper Side Orientation",
                "Texture orientation on the upper inner face.",
                OrientationChoices,
                0,
                v => _orientUp = v));

            AddParameter(FilterParameter.Choice(
                "orient_down",
                "Lower Side Orientation",
                "Texture orientation on the lower inner face.",
                OrientationChoices,
                0,
                v => _orientDown = v));
        }

        public override void Initialize()
        {
            TryCreateMaterial("CoreGMIC/FrameCube");
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            Material.SetFloat("_Depth", _depth);
            Material.SetVector("_Center", new Vector4(_centerX * 0.01f, _centerY * 0.01f, 0f, 0f));
            Material.SetInt("_OrientLeft", _orientLeft);
            Material.SetInt("_OrientRight", _orientRight);
            Material.SetInt("_OrientUp", _orientUp);
            Material.SetInt("_OrientDown", _orientDown);

            Graphics.Blit(source, destination, Material);
        }
    }
}