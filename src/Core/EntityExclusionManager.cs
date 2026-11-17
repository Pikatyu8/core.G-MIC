using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    public static class EntityExclusionManager
    {
        private static bool _disablePlayer;
        private static bool _disableTiktik;

        private static HeroController _cachedHero;
        private static readonly List<Renderer> _playerRenderers = new List<Renderer>();
        private static readonly List<Renderer> _tiktikRenderers = new List<Renderer>();
        private static readonly HashSet<Renderer> _seenCache = new HashSet<Renderer>();

        public static bool DisablePlayer
        {
            get => _disablePlayer;
            set
            {
                _disablePlayer = value;
                if (CoreGMICMod.Settings != null)
                {
                    CoreGMICMod.Settings.DisablePlayer = value;
                }
                if (value)
                {
                    RefreshHeroCache(force: true);
                }
            }
        }

        public static bool DisableTiktik
        {
            get => _disableTiktik;
            set
            {
                _disableTiktik = value;
                if (CoreGMICMod.Settings != null)
                {
                    CoreGMICMod.Settings.DisableTiktik = value;
                }
                if (value)
                {
                    RefreshTiktikCache();
                }
            }
        }

        public static bool HasAnyExclusion => _disablePlayer || _disableTiktik;

        public static void Initialize(ModSettings settings)
        {
            _disablePlayer = settings != null && settings.DisablePlayer;
            _disableTiktik = settings != null && settings.DisableTiktik;

            On.Climber.Start -= OnClimberStart;
            On.Climber.Start += OnClimberStart;

            RefreshAll();
        }

        public static void RefreshAll()
        {
            RefreshHeroCache(force: true);
            RefreshTiktikCache();
        }

        public static void CollectActiveExcludedRenderers(List<Renderer> output)
        {
            output.Clear();
            if (!HasAnyExclusion)
            {
                return;
            }

            if (_disablePlayer)
            {
                RefreshHeroCache(force: false);
                AppendActive(_playerRenderers, output);
            }

            if (_disableTiktik)
            {
                AppendActive(_tiktikRenderers, output);
            }
        }

        private static void OnClimberStart(On.Climber.orig_Start orig, Climber self)
        {
            orig(self);

            if (self != null && IsTiktikObject(self.gameObject))
            {
                AddTiktikRenderers(self.gameObject);
            }
        }

        private static void RefreshHeroCache(bool force)
        {
            HeroController hero = HeroController.UnsafeInstance;
            if (!force && hero == _cachedHero)
            {
                return;
            }

            _cachedHero = hero;
            _playerRenderers.Clear();
            if (hero == null)
            {
                return;
            }

            _seenCache.Clear();

            // 1. Player body sprite renderers only (on the Knight root GameObject).
            foreach (Renderer r in hero.GetComponents<Renderer>())
            {
                AddUnique(_playerRenderers, r);
            }

            // 2. Normal nail slashes.
            AddSlashRenderers(hero.normalSlash);
            AddSlashRenderers(hero.alternateSlash);
            AddSlashRenderers(hero.upSlash);
            AddSlashRenderers(hero.downSlash);
            AddSlashRenderers(hero.wallSlash);

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
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        AddUnique(_playerRenderers, r);
                    }
                }
            }

            _seenCache.Clear();
        }

        private static void AddSlashRenderers(NailSlash slash)
        {
            if (slash == null)
            {
                return;
            }

            foreach (Renderer r in slash.GetComponentsInChildren<Renderer>(true))
            {
                AddUnique(_playerRenderers, r);
            }
        }

        private static void RefreshTiktikCache()
        {
            _tiktikRenderers.Clear();
            _seenCache.Clear();

            foreach (Climber climber in UnityEngine.Object.FindObjectsOfType<Climber>())
            {
                if (climber != null && IsTiktikObject(climber.gameObject))
                {
                    foreach (Renderer r in climber.GetComponentsInChildren<Renderer>(true))
                    {
                        AddUnique(_tiktikRenderers, r);
                    }
                }
            }

            _seenCache.Clear();
        }

        private static void AddTiktikRenderers(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null && !_tiktikRenderers.Contains(r))
                {
                    _tiktikRenderers.Add(r);
                }
            }
        }

        private static bool IsTiktikObject(GameObject go)
        {
            return go != null && go.name.IndexOf("Climber", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddUnique(List<Renderer> list, Renderer r)
        {
            if (r != null && _seenCache.Add(r))
            {
                list.Add(r);
            }
        }

        private static void AppendActive(List<Renderer> source, List<Renderer> output)
        {
            for (int i = source.Count - 1; i >= 0; i--)
            {
                Renderer r = source[i];
                if (r == null)
                {
                    source.RemoveAt(i);
                    continue;
                }

                if (r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff)
                {
                    output.Add(r);
                }
            }
        }
    }
}