using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    public sealed class ExclusionRule
    {
        private readonly List<Renderer> _cachedRenderers = new List<Renderer>();
        private readonly HashSet<Renderer> _seenCache = new HashSet<Renderer>();
        private readonly Action<ExclusionRule> _refreshCache;
        private readonly Action<ExclusionRule> _onInitialize;
        private readonly Action<ExclusionRule> _onDispose;
        private readonly Func<ExclusionRule, bool> _needsRefresh;

        public string Id { get; }
        public string DisplayName { get; }
        public bool Enabled { get; internal set; }

        public ExclusionRule(
            string id,
            string displayName,
            Action<ExclusionRule> refreshCache,
            Action<ExclusionRule> onInitialize = null,
            Action<ExclusionRule> onDispose = null,
            Func<ExclusionRule, bool> needsRefresh = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            _refreshCache = refreshCache;
            _onInitialize = onInitialize;
            _onDispose = onDispose;
            _needsRefresh = needsRefresh;
        }

        internal void Initialize()
        {
            _onInitialize?.Invoke(this);
        }

        internal void Dispose()
        {
            _onDispose?.Invoke(this);
            ClearCache();
        }

        public void ClearCache()
        {
            _cachedRenderers.Clear();
            _seenCache.Clear();
        }

        public void AddRenderer(Renderer renderer)
        {
            if (renderer != null && _seenCache.Add(renderer))
            {
                _cachedRenderers.Add(renderer);
            }
        }

        public void AddRenderersInChildren(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                AddRenderer(r);
            }
        }

        public void RefreshCache()
        {
            ClearCache();
            _refreshCache?.Invoke(this);
        }

        public void CollectActiveRenderers(List<Renderer> output)
        {
            if (!Enabled)
            {
                return;
            }

            if (_needsRefresh != null && _needsRefresh(this))
            {
                RefreshCache();
            }

            for (int i = _cachedRenderers.Count - 1; i >= 0; i--)
            {
                Renderer r = _cachedRenderers[i];
                if (r == null)
                {
                    _seenCache.Remove(r);
                    _cachedRenderers.RemoveAt(i);
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