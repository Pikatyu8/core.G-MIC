using System.Collections.Generic;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class Pixelate3x3Filter : IFilter
    {
        private Material _material;
        private int _blockSize = 3;
        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();

        public string Id => "test_pixelate_3x3";
        public string DisplayName => "Test: Pixelate";
        public string Description => "Spatial filter merging NxN pixel blocks into one color.";
        public FilterCategory Category => FilterCategory.Standard;
        public bool IsAvailable => _material != null;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public Pixelate3x3Filter()
        {
            _parameters.Add(FilterParameter.Int(
                "block_size",
                "Block Size (px)",
                "Size of each pixel block in pixels.",
                1, 32, 3,
                val =>
                {
                    _blockSize = val;
                    if (_material != null)
                    {
                        _material.SetFloat("_BlockSize", _blockSize);
                    }
                }));
        }

        public void Initialize()
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Pixelate");
            if (shader == null)
            {
                return;
            }

            _material = new Material(shader);
            _material.SetFloat("_BlockSize", _blockSize);
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
        }
    }
}