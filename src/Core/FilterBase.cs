using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    public abstract class FilterBase : IFilter
    {
        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();
        private Material _material;

        protected Material Material
        {
            get => _material;
            set => _material = value;
        }

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }
        public abstract FilterCategory Category { get; }

        public virtual bool IsAvailable => _material != null;
        public virtual bool SuppressFastNoise => false;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public abstract void Initialize();
        public abstract void Apply(RenderTexture source, RenderTexture destination);

        public virtual void ResetParameters()
        {
            foreach (FilterParameter param in _parameters)
            {
                param.Reset();
            }
        }

        public virtual void Dispose()
        {
            GpuUtils.DestroyObject(ref _material);
        }

        protected void AddParameter(FilterParameter parameter)
        {
            if (parameter != null)
            {
                _parameters.Add(parameter);
            }
        }

        protected bool TryCreateMaterial(string shaderName)
        {
            Shader shader = ShaderManager.GetShader(shaderName);
            if (shader == null)
            {
                return false;
            }

            _material = new Material(shader);
            return true;
        }

        protected bool TryLoadComputeShader(string computeShaderName, out ComputeShader compute)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                CoreGMICMod.Instance?.LogWarn($"Compute shaders are not supported; {GetType().Name} disabled.");
                compute = null;
                return false;
            }

            compute = ShaderManager.GetComputeShader(computeShaderName);
            return compute != null;
        }
    }
}