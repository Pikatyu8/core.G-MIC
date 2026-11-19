using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class ArrayColorFilter : FilterBase
    {
        private int _xTiles = 5;
        private int _yTiles = 5;
        private float _opacity = 0.5f;
        private bool _animate = false;
        private float _updateRate = 4f;
        private bool _randomSizes = false;

        private int _seedCounter;
        private float _nextUpdateTime;

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

            AddParameter(FilterParameter.Bool(
                "animate",
                "Animate",
                "Continuously randomize tile colors (and optionally sizes).",
                false,
                val =>
                {
                    _animate = val;
                    _seedCounter = _animate ? 1 : 0;
                    _nextUpdateTime = Time.realtimeSinceStartup + (1f / Mathf.Max(0.1f, _updateRate));
                    SyncAnimationUniforms();
                }));

            AddParameter(FilterParameter.Float(
                "update_rate",
                "Update Hz",
                "Tile randomization frequency in updates per second.",
                0.5f, 60f, 4f,
                val =>
                {
                    _updateRate = val;
                    _nextUpdateTime = Time.realtimeSinceStartup;
                },
                isVisible: () => _animate,
                step: 0.5f));

            AddParameter(FilterParameter.Bool(
                "random_sizes",
                "Random Sizes",
                "Randomize tile widths and heights on each update.",
                false,
                val =>
                {
                    _randomSizes = val;
                    SyncAnimationUniforms();
                },
                isVisible: () => _animate));
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
            SyncAnimationUniforms();
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            if (_animate)
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextUpdateTime)
                {
                    _seedCounter++;
                    _nextUpdateTime = now + (1f / Mathf.Max(0.1f, _updateRate));
                }
            }

            SyncAnimationUniforms();
            Graphics.Blit(source, destination, Material);
        }

        public override void ResetParameters()
        {
            base.ResetParameters();
            _seedCounter = 0;
            _nextUpdateTime = Time.realtimeSinceStartup;
            SyncAnimationUniforms();
        }

        private void SyncAnimationUniforms()
        {
            if (Material == null)
            {
                return;
            }

            Material.SetInt("_Seed", _animate ? _seedCounter : 0);
            Material.SetFloat("_RandomSizes", (_animate && _randomSizes) ? 1f : 0f);
        }
    }
}