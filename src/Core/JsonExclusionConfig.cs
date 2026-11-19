using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace CoreGMIC.Core
{
    [Serializable]
    public sealed class JsonExclusionRuleEntry
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public List<string> ExactNames { get; set; } = new List<string>();
        public List<string> NameContains { get; set; } = new List<string>();
        public List<string> SceneContains { get; set; } = new List<string>();
        public bool IncludeChildren { get; set; } = true;
        public bool Enabled { get; set; } = false;
        public float PollIntervalSeconds { get; set; } = 0f;
    }

    [Serializable]
    public sealed class JsonExclusionConfigFile
    {
        public string _HowToUse { get; set; } =
            "Copy an entry from '_Examples' into the 'Rules' array below, adjust the fields, save the file, and press 'Reload Exclusions' in the mod menu.";

        public Dictionary<string, string> _FieldGuide { get; set; } = new Dictionary<string, string>
        {
            ["Id"] = "Unique rule ID used for saving toggle state (e.g. 'grubs').",
            ["DisplayName"] = "Button label shown in the mod's right-side panel.",
            ["ExactNames"] = "Exact GameObject names to match (case-insensitive), e.g. [\"Knight\", \"Hornet Boss 1\"].",
            ["NameContains"] = "Substrings to match in GameObject names (case-insensitive), e.g. [\"Grub\", \"Shade\"].",
            ["SceneContains"] = "Optional scene name substrings (empty = all scenes), e.g. [\"Crossroads\", \"GG_\"].",
            ["IncludeChildren"] = "true = also exclude all child renderers under a matched parent object.",
            ["Enabled"] = "Initial on/off state when loaded or reloaded via 'Reload Exclusions'.",
            ["PollIntervalSeconds"] = "0 = scan on scene load only. > 0 (e.g. 0.5) = also rescan periodically for spawned objects."
        };

        public List<JsonExclusionRuleEntry> _Examples { get; set; } = new List<JsonExclusionRuleEntry>
        {
            new JsonExclusionRuleEntry
            {
                Id = "grubs",
                DisplayName = "Disable Grubs",
                ExactNames = new List<string>(),
                NameContains = new List<string> { "Grub" },
                SceneContains = new List<string>(),
                IncludeChildren = true,
                Enabled = false,
                PollIntervalSeconds = 0f
            },
            new JsonExclusionRuleEntry
            {
                Id = "hornet_greenpath",
                DisplayName = "Disable Hornet",
                ExactNames = new List<string> { "Hornet Boss 1" },
                NameContains = new List<string> { "Hornet" },
                SceneContains = new List<string> { "Fungus1_04" },
                IncludeChildren = true,
                Enabled = false,
                PollIntervalSeconds = 0.5f
            }
        };

        public List<JsonExclusionRuleEntry> Rules { get; set; } = new List<JsonExclusionRuleEntry>();
    }

    public static class JsonExclusionConfig
    {
        private const string FileName = "exclusions.json";

        public static string ConfigPath
        {
            get
            {
                string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return string.IsNullOrEmpty(modDir) ? FileName : Path.Combine(modDir, FileName);
            }
        }

        public static void OpenInDefaultEditor()
        {
            string path = ConfigPath;
            try
            {
                if (!File.Exists(path))
                {
                    LoadOrCreate();
                }

                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CoreGMICMod.Instance?.LogError($"Failed to open exclusions config '{path}': {ex}");
            }
        }

        public static JsonExclusionConfigFile LoadOrCreate()
        {
            string path = ConfigPath;

            try
            {
                if (!File.Exists(path))
                {
                    JsonExclusionConfigFile defaults = new JsonExclusionConfigFile();
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllText(path, JsonConvert.SerializeObject(defaults, Formatting.Indented));
                    CoreGMICMod.Instance?.Log($"Created default exclusions config at '{path}'.");
                    return defaults;
                }

                string raw = File.ReadAllText(path);
                JsonExclusionConfigFile parsed = JsonConvert.DeserializeObject<JsonExclusionConfigFile>(raw);
                if (parsed == null)
                {
                    CoreGMICMod.Instance?.LogWarn($"'{path}' deserialized to null; using empty exclusion rules.");
                    return new JsonExclusionConfigFile();
                }

                parsed.Rules ??= new List<JsonExclusionRuleEntry>();
                return parsed;
            }
            catch (Exception ex)
            {
                CoreGMICMod.Instance?.LogError($"Failed to load exclusions config from '{path}': {ex}");
                return null;
            }
        }
    }
}