using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class ArrayColorFilter : FilterBase
    {
        private int _xTiles = 5;
        private int _yTiles = 5;
        private float _opacity = 0.5f;

        public override string Id => "fx_array_color";
        public override string DisplayName => "Array [Random Colors]";
        public override string Description => "Overlays a grid of randomized colored tiles.";
        public override FilterCategory Category => FilterCategory.Standard;

        public ArrayColorFilter()
        {
            AddParameter(FilterParameter.Int(
                "x_tiles",
                "X-Tiles",
                "Number of horizontal colored tiles.",
                1, 20, 5,
                val =>
                {
                    _xTiles = val;
                    if (Material != null)
                    {
                        Material.SetFloat("_XTiles", _xTiles);
                    }
                }));

            AddParameter(FilterParameter.Int(
                "y_tiles",
                "Y-Tiles",
                "Number of vertical colored tiles.",
                1, 20, 5,
                val =>
                {
                    _yTiles = val;
                    if (Material != null)
                    {
                        Material.SetFloat("_YTiles", _yTiles);
                    }
                }));

            AddParameter(FilterParameter.Float(
                "opacity",
                "Opacity",
                "Overlay opacity of the colored tile grid.",
                0f, 1f, 0.5f,
                val =>
                {
                    _opacity = val;
                    if (Material != null)
                    {
                        Material.SetFloat("_Opacity", _opacity);
                    }
                },
                step: 0.02f));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/ArrayColor"))
            {
                return;
            }

            Material.SetFloat("_XTiles", _xTiles);
            Material.SetFloat("_YTiles", _yTiles);
            Material.SetFloat("_Opacity", _opacity);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination, Material);
        }
    }
}