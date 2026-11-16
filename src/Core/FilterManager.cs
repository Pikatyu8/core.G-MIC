using System;
using System.Collections.Generic;

namespace CoreGMIC.Core
{
    public static class FilterManager
    {
        private static readonly List<IFilter> _filters = new List<IFilter>();
        private static int _activeIndex = -1;

        public static bool IsEnabled { get; set; } = true;
        public static IReadOnlyList<IFilter> Filters => _filters;
        public static IFilter ActiveFilter => (_activeIndex >= 0 && _activeIndex < _filters.Count) ? _filters[_activeIndex] : null;
        public static int ActiveFilterIndex => _activeIndex;

        public static void Register(IFilter filter)
        {
            if (filter == null)
            {
                return;
            }

            if (_filters.Exists(f => f.Id == filter.Id))
            {
                return;
            }

            _filters.Add(filter);
        }

        public static void InitializeAll()
        {
            foreach (IFilter filter in _filters)
            {
                try
                {
                    filter.Initialize();
                }
                catch (Exception ex)
                {
                    CoreGMICMod.Instance?.LogError($"Failed to initialize filter '{filter.Id}': {ex}");
                }
            }
        }

        public static void SetActiveIndex(int index)
        {
            if (index < 0 || index >= _filters.Count)
            {
                _activeIndex = -1;
                if (CoreGMICMod.Settings != null)
                {
                    CoreGMICMod.Settings.SelectedFilterId = "none";
                }
                return;
            }

            _activeIndex = index;
            if (CoreGMICMod.Settings != null)
            {
                CoreGMICMod.Settings.SelectedFilterId = _filters[index].Id;
            }
        }

        public static void SetActiveById(string filterId)
        {
            if (string.IsNullOrEmpty(filterId) || filterId == "none")
            {
                SetActiveIndex(-1);
                return;
            }

            int idx = _filters.FindIndex(f => f.Id == filterId);
            SetActiveIndex(idx);
        }

        public static void DisposeAll()
        {
            foreach (IFilter filter in _filters)
            {
                try
                {
                    filter.Dispose();
                }
                catch (Exception ex)
                {
                    CoreGMICMod.Instance?.LogError($"Failed to dispose filter '{filter.Id}': {ex}");
                }
            }

            _filters.Clear();
            _activeIndex = -1;
        }
    }
}