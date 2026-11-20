using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class QuadtreeVariationsFilter : FilterBase
    {
        private GpuQuadtree _quadtree;

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
        public override bool IsAvailable => _quadtree != null && _quadtree.IsAvailable;

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
            GpuQuadtree.TryCreate(out _quadtree);
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            _quadtree.Apply(
                source,
                destination,
                _mode,
                _precision,
                _homogeneity,
                _outline,
                _radius1,
                _radius2,
                _anisotropy,
                _onlyLeaves);
        }

        public override void Dispose()
        {
            _quadtree?.Dispose();
            _quadtree = null;
            base.Dispose();
        }
    }
}