using System;
using System.Collections.Generic;

namespace CoreGMIC.Core
{
    [Serializable]
    public class ModSettings
    {
        public bool Enabled { get; set; } = true;
        public string SelectedFilterId { get; set; } = "none";
        public Dictionary<string, Dictionary<string, float>> FilterParameters { get; set; } = new Dictionary<string, Dictionary<string, float>>();
    }
}