using System;

namespace CoreGMIC.Core
{
    [Serializable]
    public class ModSettings
    {
        public bool Enabled { get; set; } = true;
        public string SelectedFilterId { get; set; } = "none";
    }
}