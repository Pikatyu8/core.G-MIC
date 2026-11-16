using UnityEngine;

namespace CoreGMIC.Core
{
    public enum FilterCategory
    {
        None,
        Lut,
        Heavy
    }

    public interface IFilter
    {
        string Id { get; }
        string DisplayName { get; }
        string Description { get; }
        FilterCategory Category { get; }
        bool IsAvailable { get; }

        void Initialize();
        void Apply(RenderTexture source, RenderTexture destination);
        void Dispose();
    }
}