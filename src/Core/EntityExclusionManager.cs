using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class EntityExclusionManager
    {
        public const string PlayerRuleId = "player";
        public const string TiktikRuleId = "tiktik";

        private static readonly List<ExclusionRule> _rules = new List<ExclusionRule>();
        private static readonly Dictionary<string, ExclusionRule> _rulesById = new Dictionary<string, ExclusionRule>();
        private static readonly List<string> _jsonRuleIds = new List<string>();

        private static HeroController _cachedHero;
        private static bool _builtInRegistered;

        public static IReadOnlyList<ExclusionRule> Rules => _rules;

        public static bool DisablePlayer
        {
            get => IsEnabled(PlayerRuleId);
            set => SetEnabled(PlayerRuleId, value);
        }

        public static bool DisableTiktik
        {
            get => IsEnabled(TiktikRuleId);
            set => SetEnabled(TiktikRuleId, value);
        }

        public static bool HasAnyExclusion
        {
            get
            {
                for (int i = 0; i < _rules.Count; i++)
                {
                    if (_rules[i].Enabled)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static void Initialize(ModSettings settings)
        {
            RegisterBuiltInRules();

            if (settings != null)
            {
                settings.ExclusionRules ??= new Dictionary<string, bool>();

                // Migrate legacy boolean settings on first load.
                if (!settings.ExclusionRules.ContainsKey(PlayerRuleId) && settings.DisablePlayer)
                {
                    settings.ExclusionRules[PlayerRuleId] = true;
                }

                if (!settings.ExclusionRules.ContainsKey(TiktikRuleId) && settings.DisableTiktik)
                {
                    settings.ExclusionRules[TiktikRuleId] = true;
                }
            }

            foreach (ExclusionRule rule in _rules)
            {
                bool enabled = settings != null &&
                               settings.ExclusionRules != null &&
                               settings.ExclusionRules.TryGetValue(rule.Id, out bool saved) &&
                               saved;
                rule.Enabled = enabled;
                rule.Initialize();
            }

            ReloadJsonRules(applyJsonEnabledState: false);
            RefreshAll();
        }

        public static void RegisterRule(ExclusionRule rule)
        {
            if (rule == null || _rulesById.ContainsKey(rule.Id))
            {
                return;
            }

            _rules.Add(rule);
            _rulesById[rule.Id] = rule;
        }

        public static void UnregisterRule(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId) || !_rulesById.TryGetValue(ruleId, out ExclusionRule rule))
            {
                return;
            }

            rule.Dispose();
            _rulesById.Remove(ruleId);
            _rules.Remove(rule);
        }

        public static void ReloadJsonRules(bool applyJsonEnabledState)
        {
            JsonExclusionConfigFile config = JsonExclusionConfig.LoadOrCreate();
            if (config == null)
            {
                return;
            }

            for (int i = 0; i < _jsonRuleIds.Count; i++)
            {
                UnregisterRule(_jsonRuleIds[i]);
            }
            _jsonRuleIds.Clear();

            ModSettings settings = CoreGMICMod.Settings;
            if (settings != null)
            {
                settings.ExclusionRules ??= new Dictionary<string, bool>();
            }

            if (config.Rules == null)
            {
                return;
            }

            for (int i = 0; i < config.Rules.Count; i++)
            {
                JsonExclusionRuleEntry entry = config.Rules[i];
                if (entry == null)
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Id : entry.DisplayName.Trim();
                string ruleId = string.IsNullOrWhiteSpace(entry.Id) ? BuildFallbackId(displayName) : entry.Id.Trim();

                if (string.IsNullOrEmpty(ruleId) || string.IsNullOrEmpty(displayName))
                {
                    CoreGMICMod.Instance?.LogWarn($"Skipped JSON exclusion entry #{i} with empty Id/DisplayName.");
                    continue;
                }

                if (_rulesById.ContainsKey(ruleId))
                {
                    CoreGMICMod.Instance?.LogWarn($"Skipped duplicate JSON exclusion rule Id '{ruleId}'.");
                    continue;
                }

                HashSet<string> exactSet = BuildExactSet(entry.ExactNames);
                List<string> containsList = BuildCleanList(entry.NameContains);
                List<string> sceneList = BuildCleanList(entry.SceneContains);

                if (exactSet.Count == 0 && containsList.Count == 0)
                {
                    CoreGMICMod.Instance?.LogWarn($"Skipped JSON exclusion rule '{ruleId}': ExactNames and NameContains are both empty.");
                    continue;
                }

                bool includeChildren = entry.IncludeChildren;
                float pollInterval = Mathf.Max(0f, entry.PollIntervalSeconds);
                float nextPollTime = 0f;

                ExclusionRule rule = new ExclusionRule(
                    id: ruleId,
                    displayName: displayName,
                    refreshCache: r =>
                    {
                        CollectJsonRuleRenderers(r, exactSet, containsList, sceneList, includeChildren);
                        if (pollInterval > 0f)
                        {
                            nextPollTime = Time.realtimeSinceStartup + pollInterval;
                        }
                    },
                    needsRefresh: pollInterval > 0f
                        ? _ => Time.realtimeSinceStartup >= nextPollTime
                        : null);

                bool enabled = entry.Enabled;
                if (!applyJsonEnabledState && settings != null && settings.ExclusionRules.TryGetValue(ruleId, out bool saved))
                {
                    enabled = saved;
                }
                else if (settings != null)
                {
                    settings.ExclusionRules[ruleId] = enabled;
                }

                rule.Enabled = enabled;
                RegisterRule(rule);
                _jsonRuleIds.Add(ruleId);
                rule.Initialize();

                if (enabled)
                {
                    rule.RefreshCache();
                }
            }

            CoreGMICMod.Instance?.Log($"Loaded {_jsonRuleIds.Count} JSON exclusion rule(s).");
        }

        public static bool IsEnabled(string ruleId)
        {
            return !string.IsNullOrEmpty(ruleId) &&
                   _rulesById.TryGetValue(ruleId, out ExclusionRule rule) &&
                   rule.Enabled;
        }

        public static void SetEnabled(string ruleId, bool enabled)
        {
            if (string.IsNullOrEmpty(ruleId) || !_rulesById.TryGetValue(ruleId, out ExclusionRule rule))
            {
                return;
            }

            rule.Enabled = enabled;

            ModSettings settings = CoreGMICMod.Settings;
            if (settings != null)
            {
                settings.ExclusionRules ??= new Dictionary<string, bool>();
                settings.ExclusionRules[ruleId] = enabled;

                if (ruleId == PlayerRuleId)
                {
                    settings.DisablePlayer = enabled;
                }
                else if (ruleId == TiktikRuleId)
                {
                    settings.DisableTiktik = enabled;
                }
            }

            if (enabled)
            {
                rule.RefreshCache();
            }
        }

        public static void RefreshAll()
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                _rules[i].RefreshCache();
            }
        }

        public static void CollectActiveExcludedRenderers(List<Renderer> output)
        {
            output.Clear();
            if (!HasAnyExclusion)
            {
                return;
            }

            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Enabled)
                {
                    _rules[i].CollectActiveRenderers(output);
                }
            }
        }

        public static void DisposeAll()
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                _rules[i].Dispose();
            }
        }

        private static void RegisterBuiltInRules()
        {
            if (_builtInRegistered)
            {
                return;
            }

            _builtInRegistered = true;

            RegisterRule(new ExclusionRule(
                id: PlayerRuleId,
                displayName: "Disable Player",
                refreshCache: CollectHeroRenderers,
                onDispose: _ => _cachedHero = null,
                needsRefresh: _ => HeroController.UnsafeInstance != _cachedHero));

            RegisterRule(new ExclusionRule(
                id: TiktikRuleId,
                displayName: "Disable Tiktik",
                refreshCache: CollectTiktikRenderers,
                onInitialize: _ =>
                {
                    On.Climber.Start -= OnClimberStart;
                    On.Climber.Start += OnClimberStart;
                },
                onDispose: _ => On.Climber.Start -= OnClimberStart));
        }

        private static void CollectHeroRenderers(ExclusionRule rule)
        {
            HeroController hero = HeroController.UnsafeInstance;
            _cachedHero = hero;
            if (hero == null)
            {
                return;
            }

            // 1. Player body sprite renderers only (on the Knight root GameObject).
            foreach (Renderer r in hero.GetComponents<Renderer>())
            {
                rule.AddRenderer(r);
            }

            // 2. Normal nail slashes.
            AddSlashRenderers(rule, hero.normalSlash);
            AddSlashRenderers(rule, hero.alternateSlash);
            AddSlashRenderers(rule, hero.upSlash);
            AddSlashRenderers(rule, hero.downSlash);
            AddSlashRenderers(rule, hero.wallSlash);

            // 3. Nail Art slashes (Great Slash, Dash Slash, Cyclone Slash).
            foreach (Transform t in hero.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                {
                    continue;
                }

                string n = t.name;
                if (n == "Great Slash" || n == "Dash Slash" || n == "Cyclone Slash")
                {
                    rule.AddRenderersInChildren(t.gameObject);
                }
            }
        }

        private static void AddSlashRenderers(ExclusionRule rule, NailSlash slash)
        {
            if (slash != null)
            {
                rule.AddRenderersInChildren(slash.gameObject);
            }
        }

        private static void CollectTiktikRenderers(ExclusionRule rule)
        {
            foreach (Climber climber in UnityEngine.Object.FindObjectsOfType<Climber>())
            {
                if (climber != null && IsTiktikObject(climber.gameObject))
                {
                    rule.AddRenderersInChildren(climber.gameObject);
                }
            }
        }

        private static void CollectJsonRuleRenderers(
            ExclusionRule rule,
            HashSet<string> exactNames,
            List<string> nameContains,
            List<string> sceneContains,
            bool includeChildren)
        {
            string activeScene = GameManager.UnsafeInstance != null ? GameManager.UnsafeInstance.sceneName : null;

            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null)
                {
                    continue;
                }

                if (sceneContains.Count > 0)
                {
                    string scene = !string.IsNullOrEmpty(activeScene) ? activeScene : r.gameObject.scene.name;
                    if (!MatchesContains(scene, sceneContains))
                    {
                        continue;
                    }
                }

                if (MatchesObjectHierarchy(r.transform, exactNames, nameContains, includeChildren))
                {
                    rule.AddRenderer(r);
                }
            }
        }

        private static bool MatchesObjectHierarchy(
            Transform target,
            HashSet<string> exactNames,
            List<string> nameContains,
            bool includeChildren)
        {
            if (target == null)
            {
                return false;
            }

            if (!includeChildren)
            {
                return MatchesName(target.gameObject.name, exactNames, nameContains);
            }

            for (Transform cur = target; cur != null; cur = cur.parent)
            {
                if (MatchesName(cur.gameObject.name, exactNames, nameContains))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesName(string objName, HashSet<string> exactNames, List<string> nameContains)
        {
            if (string.IsNullOrEmpty(objName))
            {
                return false;
            }

            if (exactNames.Count > 0 && exactNames.Contains(objName))
            {
                return true;
            }

            return MatchesContains(objName, nameContains);
        }

        private static bool MatchesContains(string value, List<string> patterns)
        {
            if (string.IsNullOrEmpty(value) || patterns.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < patterns.Count; i++)
            {
                if (value.IndexOf(patterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> BuildExactSet(List<string> source)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return set;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(source[i]))
                {
                    set.Add(source[i].Trim());
                }
            }

            return set;
        }

        private static List<string> BuildCleanList(List<string> source)
        {
            List<string> list = new List<string>();
            if (source == null)
            {
                return list;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(source[i]))
                {
                    list.Add(source[i].Trim());
                }
            }

            return list;
        }

        private static string BuildFallbackId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            StringBuilder sb = new StringBuilder("json_");
            for (int i = 0; i < displayName.Length; i++)
            {
                char ch = displayName[i];
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
                else if (ch == ' ' || ch == '_' || ch == '-')
                {
                    sb.Append('_');
                }
            }

            return sb.Length > 5 ? sb.ToString() : null;
        }

        private static void OnClimberStart(On.Climber.orig_Start orig, Climber self)
        {
            orig(self);

            if (self != null && IsTiktikObject(self.gameObject) &&
                _rulesById.TryGetValue(TiktikRuleId, out ExclusionRule rule))
            {
                rule.AddRenderersInChildren(self.gameObject);
            }
        }

        private static bool IsTiktikObject(GameObject go)
        {
            return go != null && go.name.IndexOf("Climber", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}