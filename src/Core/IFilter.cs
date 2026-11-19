using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    public enum FilterCategory
    {
        None = 0,
        Lut = 1,
        Standard = 2,
        Heavy = 3,
        Experimental = 4
    }

    public interface IFilter
    {
        string Id { get; }
        string DisplayName { get; }
        string Description { get; }
        FilterCategory Category { get; }
        bool IsAvailable { get; }
        bool SuppressFastNoise { get; }
        IReadOnlyList<FilterParameter> Parameters { get; }

        void Initialize();
        void Apply(RenderTexture source, RenderTexture destination);
        void ResetParameters();
        void Dispose();
    }
}