using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class QuadtreeVariationsFilter : FilterBase
    {
        private const int PyramidLevels = 7; // 4, 8, 16, 32, 64, 128, 256 px

        private static readonly string[] ColorUniforms =
        {
            "_ColorL0", "_ColorL1", "_ColorL2", "_ColorL3", "_ColorL4", "_ColorL5", "_ColorL6"
        };

        private static readonly string[] TensorUniforms =
        {
            "_TensorL0", "_TensorL1", "_TensorL2", "_TensorL3", "_TensorL4", "_TensorL5", "_TensorL6"
        };

        private int _mode = 0;
        private int _precision = 1024;
        private float _homogeneity = 0.5f;
        private int _outline = 0;
        private float _radius1 = 3.0f;
        private float _radius2 = 1.5f;
        private float _anisotropy = 1.0f;
        private bool _onlyLeaves = true;

        public override string Id => "fx_quadtree";
        public override string DisplayName => "Quadtree Variations";
        public override string Description => "Decomposes the frame into adaptive quadtree squares, triangles, or ellipses.";
        public override FilterCategory Category => FilterCategory.Experimental;

        public QuadtreeVariationsFilter()
        {
            AddParameter(FilterParameter.Choice(
                "mode",
                "Mode",
                "Quadtree rendering mode.",
                new[] { "Squares", "Sierpinski Design", "Ellipse Painting" },
                0,
                v => _mode = v));

            AddParameter(FilterParameter.Int(
                "precision",
                "Precision",
                "Subdivision detail level (higher = finer cells).",
                2, 4096, 1024,
                v => _precision = v));

            AddParameter(FilterParameter.Float(
                "homogeneity",
                "Homogeneity",
                "Bias toward splitting larger regions over local contrast.",
                0f, 2f, 0.5f,
                v => _homogeneity = v));

            AddParameter(FilterParameter.Int(
                "outline",
                "Outline",
                "Border thickness in pixels (Squares and Ellipse Painting).",
                0, 4, 0,
                v => _outline = v));

            AddParameter(FilterParameter.Float(
                "radius1",
                "Primary Radius",
                "Primary ellipse radius multiplier.",
                0f, 5f, 3.0f,
                v => _radius1 = v,
                isVisible: () => _mode == 2));

            AddParameter(FilterParameter.Float(
                "radius2",
                "Secondary Radius",
                "Secondary ellipse radius multiplier.",
                0f, 5f, 1.5f,
                v => _radius2 = v,
                isVisible: () => _mode == 2));

            AddParameter(FilterParameter.Float(
                "anisotropy",
                "Anisotropy",
                "Ellipse elongation strength along local edges.",
                0f, 4f, 1.0f,
                v => _anisotropy = v,
                isVisible: () => _mode == 2));

            AddParameter(FilterParameter.Bool(
                "only_leaves",
                "Only Leaves",
                "Render only leaf ellipses instead of layering parent levels.",
                true,
                v => _onlyLeaves = v,
                isVisible: () => _mode == 2));
        }

        public override void Initialize()
        {
            TryCreateMaterial("CoreGMIC/Quadtree");
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int srcW = source.width;
            int srcH = source.height;
            int gridW = ((srcW + 255) / 256) * 256;
            int gridH = ((srcH + 255) / 256) * 256;

            RenderTextureFormat colorFormat = GpuUtils.ColorFormat;
            RenderTextureFormat tensorFormat = GpuUtils.FloatFormat;

            RenderTexture[] colorLevels = new RenderTexture[PyramidLevels];
            RenderTexture[] tensorLevels = new RenderTexture[PyramidLevels];

            try
            {
                for (int k = 0; k < PyramidLevels; k++)
                {
                    int w = Mathf.Max(1, gridW >> (k + 2));
                    int h = Mathf.Max(1, gridH >> (k + 2));

                    colorLevels[k] = GpuUtils.GetTempRt(w, h, colorFormat, FilterMode.Point);
                    tensorLevels[k] = GpuUtils.GetTempRt(w, h, tensorFormat, FilterMode.Point);
                }

                Material.SetVector("_SourceSize", new Vector4(srcW, srcH, gridW, gridH));

                // Pass 0 & Pass 1: Extract base 4x4 cell statistics and structure tensor.
                Graphics.Blit(source, colorLevels[0], Material, 0);
                Graphics.Blit(source, tensorLevels[0], Material, 1);

                // Pass 2: Build exact 2x2 downsample pyramid.
                for (int k = 1; k < PyramidLevels; k++)
                {
                    Graphics.Blit(colorLevels[k - 1], colorLevels[k], Material, 2);
                    Graphics.Blit(tensorLevels[k - 1], tensorLevels[k], Material, 2);
                }

                for (int k = 0; k < PyramidLevels; k++)
                {
                    Material.SetTexture(ColorUniforms[k], colorLevels[k]);
                    Material.SetTexture(TensorUniforms[k], tensorLevels[k]);
                }

                Material.SetInt("_Mode", _mode);
                Material.SetFloat("_Precision", _precision);
                Material.SetFloat("_Homogeneity", _homogeneity);
                Material.SetFloat("_Outline", _outline);
                Material.SetFloat("_Radius1", _radius1);
                Material.SetFloat("_Radius2", _radius2);
                Material.SetFloat("_Anisotropy", _anisotropy);
                Material.SetFloat("_OnlyLeaves", _onlyLeaves ? 1f : 0f);

                // Pass 3: Squares & Sierpinski; Pass 4: Ellipse Painting.
                int compositePass = (_mode == 2) ? 4 : 3;
                Graphics.Blit(source, destination, Material, compositePass);
            }
            finally
            {
                for (int k = 0; k < PyramidLevels; k++)
                {
                    GpuUtils.ReleaseTemp(colorLevels[k]);
                    GpuUtils.ReleaseTemp(tensorLevels[k]);
                }
            }
        }
    }
}