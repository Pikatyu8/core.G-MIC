using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class Pixelate3x3Filter : FilterBase
    {
        private int _blockSize = 3;

        public override string Id => "fx_pixelate";
        public override string DisplayName => "Pixelate";
        public override string Description => "Spatial filter merging NxN pixel blocks into one color.";
        public override FilterCategory Category => FilterCategory.Standard;

        public Pixelate3x3Filter()
        {
            AddParameter(FilterParameter.Int(
                "block_size",
                "Block Size (px)",
                "Size of each pixel block in pixels.",
                1, 32, 3,
                val =>
                {
                    _blockSize = val;
                    if (Material != null)
                    {
                        Material.SetFloat("_BlockSize", _blockSize);
                    }
                }));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/Pixelate"))
            {
                return;
            }

            Material.SetFloat("_BlockSize", _blockSize);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination, Material);
        }
    }
}