using System;
using System.Collections.Generic;

namespace CoreGMIC.Core
{
    [Serializable]
    public class ModSettings
    {
        public bool Enabled { get; set; } = true;
        public bool DisablePlayer { get; set; } = false;
        public bool DisableTiktik { get; set; } = false;
        public Dictionary<string, bool> ExclusionRules { get; set; } = new Dictionary<string, bool>();
        public bool EnableExperimentalShaders { get; set; } = false;
        public bool NoFastNoise { get; set; } = false;
        public string SelectedFilterId { get; set; } = "none";
        public Dictionary<string, Dictionary<string, float>> FilterParameters { get; set; } = new Dictionary<string, Dictionary<string, float>>();
        public Dictionary<string, FilterAnimationSettings> FilterAnimations { get; set; } = new Dictionary<string, FilterAnimationSettings>();
    }

    [Serializable]
    public class ParameterAnimationSettings
    {
        public bool Enabled { get; set; } = false;
        public float Frequency { get; set; } = 0.5f;
        public float MinValue { get; set; } = 0f;
        public float MaxValue { get; set; } = 1f;
    }

    [Serializable]
    public class FilterAnimationSettings
    {
        public bool Enabled { get; set; } = false;
        public bool SynchronizeFreq { get; set; } = false;
        public Dictionary<string, ParameterAnimationSettings> Parameters { get; set; } = new Dictionary<string, ParameterAnimationSettings>();
    }
}