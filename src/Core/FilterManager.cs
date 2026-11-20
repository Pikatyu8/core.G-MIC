using System;
using System.Collections.Generic;

namespace CoreGMIC.Core
{
    public static class FilterManager
    {
        private static readonly List<IFilter> _filters = new List<IFilter>();
        private static int _activeIndex = -1;
        private static bool _enableExperimentalShaders;

        public static bool IsEnabled { get; set; } = true;

        public static bool EnableExperimentalShaders
        {
            get => _enableExperimentalShaders;
            set
            {
                _enableExperimentalShaders = value;
                if (CoreGMICMod.Settings != null)
                {
                    CoreGMICMod.Settings.EnableExperimentalShaders = value;
                }

                if (!value && ActiveFilter != null && ActiveFilter.Category == FilterCategory.Experimental)
                {
                    SetActiveIndex(-1);
                }
            }
        }
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

            int insertIdx = _filters.FindIndex(f => (int)f.Category > (int)filter.Category);
            if (insertIdx >= 0)
            {
                _filters.Insert(insertIdx, filter);
            }
            else
            {
                _filters.Add(filter);
            }
        }

        public static void InitializeAll()
        {
            foreach (IFilter filter in _filters)
            {
                try
                {
                    filter.Initialize();
                    LoadFilterParameters(filter);
                }
                catch (Exception ex)
                {
                    CoreGMICMod.Instance?.LogError($"Failed to initialize filter '{filter.Id}': {ex}");
                }
            }
        }

        public static IFilter GetFilterById(string filterId)
        {
            if (string.IsNullOrEmpty(filterId))
            {
                return null;
            }

            return _filters.Find(f => f.Id == filterId);
        }

        public static FilterParameter GetFilterParameter(string filterId, string paramKey)
        {
            IFilter filter = GetFilterById(filterId);
            if (filter?.Parameters == null || string.IsNullOrEmpty(paramKey))
            {
                return null;
            }

            foreach (FilterParameter param in filter.Parameters)
            {
                if (param.Key == paramKey)
                {
                    return param;
                }
            }

            return null;
        }

        public static void SetActiveIndex(int index)
        {
            if (index < 0 || index >= _filters.Count ||
                (!_enableExperimentalShaders && _filters[index].Category == FilterCategory.Experimental))
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

        public static void LoadFilterParameters(IFilter filter)
        {
            if (filter == null || filter.Parameters == null || CoreGMICMod.Settings?.FilterParameters == null)
            {
                return;
            }

            if (!CoreGMICMod.Settings.FilterParameters.TryGetValue(filter.Id, out Dictionary<string, float> saved))
            {
                return;
            }

            foreach (FilterParameter param in filter.Parameters)
            {
                if (param.Kind == FilterParamKind.Button)
                {
                    continue;
                }

                if (saved != null && saved.TryGetValue(param.Key, out float val))
                {
                    param.Value = val;
                    param.ForceApply();
                }
            }
        }

        public static void SaveFilterParameters(IFilter filter)
        {
            if (filter == null || filter.Parameters == null || CoreGMICMod.Settings == null)
            {
                return;
            }

            CoreGMICMod.Settings.FilterParameters ??= new Dictionary<string, Dictionary<string, float>>();
            if (!CoreGMICMod.Settings.FilterParameters.TryGetValue(filter.Id, out Dictionary<string, float> dict) || dict == null)
            {
                dict = new Dictionary<string, float>();
                CoreGMICMod.Settings.FilterParameters[filter.Id] = dict;
            }

            foreach (FilterParameter param in filter.Parameters)
            {
                if (param.Kind == FilterParamKind.Button)
                {
                    continue;
                }

                dict[param.Key] = param.Value;
            }
        }

        public static void ResetFilterParameters(IFilter filter)
        {
            if (filter == null)
            {
                return;
            }

            filter.ResetParameters();
            SaveFilterParameters(filter);
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