using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class Pixelate3x3Filter : IFilter
    {
        private Material _material;

        public string Id => "test_pixelate_3x3";
        public string DisplayName => "Test: Pixelate 3x3";
        public string Description => "Test spatial filter merging 3x3 pixel blocks into one color.";
        public FilterCategory Category => FilterCategory.Heavy;
        public bool IsAvailable => _material != null;

        public void Initialize()
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/Pixelate");
            if (shader == null)
            {
                return;
            }

            _material = new Material(shader);
            _material.SetFloat("_BlockSize", 3f);
        }

        public void Apply(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination, _material);
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