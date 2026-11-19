using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class EntityExclusionManager
    {
        public const string PlayerRuleId = "player";
        public const string TiktikRuleId = "tiktik";

        private static readonly List<ExclusionRule> _rules = new List<ExclusionRule>();
        private static readonly Dictionary<string, ExclusionRule> _rulesById = new Dictionary<string, ExclusionRule>();

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