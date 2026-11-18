using System;
using System.Collections.Generic;
using CoreGMIC.Core;
using Satchel.BetterMenus;
using SidePanelLib;
using UnityEngine;
using MenuButton = Satchel.BetterMenus.MenuButton;

namespace CoreGMIC.UI
{
    public static class ModMenu
    {
        private const string ResetButtonId = "reset_active_filter";
        private const string HeavyWarningPanelId = "heavy_warning_panel";
        private const string ExperimentalWarningPanelId = "experimental_warning_panel";
        private const string HeavyWarningOptionLabel = "<color=#FF3B30>[!] GPU HEAVY -></color>";
        private const string HeavyWarningBannerText = "<color=#FF3B30>GPU HEAVY SHADERS PAST THIS POINT</color>";
        private const string ExperimentalWarningOptionLabel = "<color=#FF9500>[!] EXPERIMENTAL -></color>";
        private const string ExperimentalWarningBannerText = "<color=#FF9500>EXPERIMENTAL SHADERS PAST THIS POINT</color>";

        private static Menu _menuRef;
        private static SidePanel _sidePanel;
        private static bool _isUpdatingUi;
        private static readonly List<IFilter> _filterSlots = new List<IFilter>();
        private static int _selectedOptionIndex;
        private static int _dummyWarningIndex = -1;
        private static int _dummyExperimentalIndex = -1;

        public static MenuScreen GetMenuScreen(MenuScreen modListMenu)
        {
            _isUpdatingUi = true;
            try
            {
                List<string> filterNames = RebuildFilterSlots();
                SyncSelectedOptionIndex();

                List<Element> elements = new List<Element>
                {
                    new HorizontalOption(
                        "Enable Post-Processing",
                        "Master toggle for all G'MIC visual filters.",
                        new[] { "Off", "On" },
                        index =>
                        {
                            bool enabled = index == 1;
                            FilterManager.IsEnabled = enabled;
                            CoreGMICMod.Settings.Enabled = enabled;
                        },
                        () => FilterManager.IsEnabled ? 1 : 0,
                        Id: "master_enable"
                    ),
                    new HorizontalOption(
                        "Active Filter",
                        "Select which G'MIC filter to apply to the game world.",
                        filterNames.ToArray(),
                        index =>
                        {
                            _selectedOptionIndex = index;
                            IFilter chosen = (index >= 0 && index < _filterSlots.Count) ? _filterSlots[index] : null;
                            FilterManager.SetActiveById(chosen?.Id ?? "none");
                            if (!_isUpdatingUi)
                            {
                                RefreshParameterVisibility();
                            }
                        },
                        () =>
                        {
                            SyncSelectedOptionIndexIfExternal();
                            return _selectedOptionIndex;
                        },
                        Id: "active_filter"
                    ),
                    new TextPanel(
                        HeavyWarningBannerText,
                        width: 1000f,
                        fontSize: 30,
                        Id: HeavyWarningPanelId
                    )
                    {
                        isVisible = IsDummyWarningSelected()
                    },
                    new TextPanel(
                        ExperimentalWarningBannerText,
                        width: 1000f,
                        fontSize: 30,
                        Id: ExperimentalWarningPanelId
                    )
                    {
                        isVisible = IsDummyExperimentalSelected()
                    },
                    new MenuButton(
                        "Reset Filter Defaults",
                        "Restore default parameters for the currently active filter.",
                        _ =>
                        {
                            IFilter active = FilterManager.ActiveFilter;
                            if (active == null)
                            {
                                return;
                            }

                            FilterManager.ResetFilterParameters(active);
                            RefreshFromManager();
                        },
                        proceed: false,
                        Id: ResetButtonId
                    )
                    {
                        isVisible = HasVisibleParameters(FilterManager.ActiveFilter)
                    }
                };

                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Parameters == null)
                    {
                        continue;
                    }

                    foreach (FilterParameter param in filter.Parameters)
                    {
                        Element elem = CreateParameterElement(filter.Id, param);
                        if (elem != null)
                        {
                            elem.isVisible = ShouldShowParam(filter, param);
                            elements.Add(elem);
                        }
                    }
                }

                _menuRef = new Menu("core.G'MIC", elements.ToArray());
                MenuScreen screen = _menuRef.GetMenuScreen(modListMenu);

                _sidePanel = SidePanel.Attach(
                        screen,
                        _ => _menuRef?.CancelAction?.Invoke(),
                        new SidePanelOptions { Name = "CoreGMIC" })
                    .AddToggle("Disable Player", () => EntityExclusionManager.DisablePlayer, v => EntityExclusionManager.DisablePlayer = v)
                    .AddToggle("Disable Tiktik", () => EntityExclusionManager.DisableTiktik, v => EntityExclusionManager.DisableTiktik = v)
                    .AddToggle("No FastNoise", () => CoreGMICMod.Settings?.NoFastNoise ?? false, v =>
                    {
                        if (CoreGMICMod.Settings != null)
                        {
                            CoreGMICMod.Settings.NoFastNoise = v;
                        }
                    })
                    .AddToggle("Experimental Shaders", () => FilterManager.EnableExperimentalShaders, v =>
                    {
                        FilterManager.EnableExperimentalShaders = v;
                        RefreshFromManager();
                    });

                return screen;
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        public static void RefreshFromManager()
        {
            if (_menuRef == null)
            {
                return;
            }

            _isUpdatingUi = true;
            try
            {
                bool wasHeavyBanner = IsDummyWarningSelected();
                bool wasExperimentalBanner = IsDummyExperimentalSelected();

                List<string> filterNames = RebuildFilterSlots();
                if (_menuRef.Find("active_filter") is HorizontalOption activeFilterOption)
                {
                    activeFilterOption.Values = filterNames.ToArray();
                }

                SyncSelectedOptionIndex(wasHeavyBanner, wasExperimentalBanner);
                ApplyParameterVisibilityFlags();
                _sidePanel?.Refresh();
                _menuRef.Update();
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private static List<string> RebuildFilterSlots()
        {
            _filterSlots.Clear();
            List<string> filterNames = new List<string>();

            // Slot 0: No active filter.
            _filterSlots.Add(null);
            filterNames.Add("None");

            // Light / LUT / Standard filters first.
            foreach (IFilter filter in FilterManager.Filters)
            {
                if (filter.Category == FilterCategory.Heavy || filter.Category == FilterCategory.Experimental)
                {
                    continue;
                }

                _filterSlots.Add(filter);
                string availability = filter.IsAvailable ? "" : " [N/A]";
                filterNames.Add($"{filter.DisplayName}{availability}");
            }

            // Dummy warning entry before GPU-heavy filters.
            _dummyWarningIndex = -1;
            bool hasHeavyFilters = false;
            foreach (IFilter filter in FilterManager.Filters)
            {
                if (filter.Category == FilterCategory.Heavy)
                {
                    hasHeavyFilters = true;
                    break;
                }
            }

            if (hasHeavyFilters)
            {
                _dummyWarningIndex = _filterSlots.Count;
                _filterSlots.Add(null);
                filterNames.Add(HeavyWarningOptionLabel);

                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Category != FilterCategory.Heavy)
                    {
                        continue;
                    }

                    _filterSlots.Add(filter);
                    string availability = filter.IsAvailable ? "" : " [N/A]";
                    filterNames.Add($"{filter.DisplayName}{availability}");
                }
            }

            // Experimental filters (only shown when enabled via side toggle).
            _dummyExperimentalIndex = -1;
            if (FilterManager.EnableExperimentalShaders)
            {
                bool hasExperimentalFilters = false;
                foreach (IFilter filter in FilterManager.Filters)
                {
                    if (filter.Category == FilterCategory.Experimental)
                    {
                        hasExperimentalFilters = true;
                        break;
                    }
                }

                if (hasExperimentalFilters)
                {
                    _dummyExperimentalIndex = _filterSlots.Count;
                    _filterSlots.Add(null);
                    filterNames.Add(ExperimentalWarningOptionLabel);

                    foreach (IFilter filter in FilterManager.Filters)
                    {
                        if (filter.Category != FilterCategory.Experimental)
                        {
                            continue;
                        }

                        _filterSlots.Add(filter);
                        string availability = filter.IsAvailable ? "" : " [N/A]";
                        filterNames.Add($"{filter.DisplayName}{availability}");
                    }
                }
            }

            return filterNames;
        }

        private static bool IsDummyWarningSelected()
        {
            return _dummyWarningIndex >= 0 && _selectedOptionIndex == _dummyWarningIndex;
        }

        private static bool IsDummyExperimentalSelected()
        {
            return _dummyExperimentalIndex >= 0 && _selectedOptionIndex == _dummyExperimentalIndex;
        }

        private static void SyncSelectedOptionIndex(bool preserveHeavyBanner = false, bool preserveExperimentalBanner = false)
        {
            IFilter active = FilterManager.ActiveFilter;
            if (active == null)
            {
                if (preserveHeavyBanner && _dummyWarningIndex >= 0)
                {
                    _selectedOptionIndex = _dummyWarningIndex;
                }
                else if (preserveExperimentalBanner && _dummyExperimentalIndex >= 0)
                {
                    _selectedOptionIndex = _dummyExperimentalIndex;
                }
                else
                {
                    _selectedOptionIndex = 0;
                }
                return;
            }

            int idx = _filterSlots.FindIndex(f => f == active);
            _selectedOptionIndex = idx >= 0 ? idx : 0;
        }

        private static void SyncSelectedOptionIndexIfExternal()
        {
            if (_selectedOptionIndex < 0 || _selectedOptionIndex >= _filterSlots.Count ||
                _filterSlots[_selectedOptionIndex] != FilterManager.ActiveFilter)
            {
                SyncSelectedOptionIndex();
            }
        }

        private static Element CreateParameterElement(string filterId, FilterParameter templateParam)
        {
            string fId = filterId;
            string pKey = templateParam.Key;
            string elemId = GetParamElementId(fId, pKey);

            switch (templateParam.Kind)
            {
                case FilterParamKind.Choice:
                    return new HorizontalOption(
                        templateParam.DisplayName,
                        templateParam.Description,
                        templateParam.Choices ?? new[] { "Default" },
                        index =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = index;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                            if (!_isUpdatingUi)
                            {
                                RefreshParameterVisibility();
                            }
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.IntValue ?? templateParam.IntValue,
                        Id: elemId
                    );

                case FilterParamKind.Bool:
                    return Blueprints.HorizontalBoolOption(
                        templateParam.DisplayName,
                        templateParam.Description,
                        val =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = val ? 1f : 0f;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                            if (!_isUpdatingUi)
                            {
                                RefreshParameterVisibility();
                            }
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.BoolValue ?? templateParam.BoolValue,
                        "On",
                        "Off",
                        Id: elemId
                    );

                case FilterParamKind.Int:
                    return new FilterSlider(
                        templateParam.DisplayName,
                        val =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = val;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.Value ?? templateParam.Value,
                        templateParam.MinValue,
                        templateParam.MaxValue,
                        wholeNumbers: true,
                        id: elemId
                    );

                case FilterParamKind.Float:
                default:
                    return new FilterSlider(
                        templateParam.DisplayName,
                        val =>
                        {
                            FilterParameter param = FilterManager.GetFilterParameter(fId, pKey);
                            if (param == null)
                            {
                                return;
                            }

                            param.Value = val;
                            FilterManager.SaveFilterParameters(FilterManager.GetFilterById(fId));
                        },
                        () => FilterManager.GetFilterParameter(fId, pKey)?.Value ?? templateParam.Value,
                        templateParam.MinValue,
                        templateParam.MaxValue,
                        wholeNumbers: false,
                        id: elemId
                    );
            }
        }

        private static void RefreshParameterVisibility()
        {
            RefreshFromManager();
        }

        private static void ApplyParameterVisibilityFlags()
        {
            if (_menuRef == null)
            {
                return;
            }

            Element warningPanel = _menuRef.Find(HeavyWarningPanelId);
            if (warningPanel != null)
            {
                warningPanel.isVisible = IsDummyWarningSelected();
            }

            Element experimentalPanel = _menuRef.Find(ExperimentalWarningPanelId);
            if (experimentalPanel != null)
            {
                experimentalPanel.isVisible = IsDummyExperimentalSelected();
            }

            Element resetBtn = _menuRef.Find(ResetButtonId);
            if (resetBtn != null)
            {
                resetBtn.isVisible = HasVisibleParameters(FilterManager.ActiveFilter);
            }

            foreach (IFilter filter in FilterManager.Filters)
            {
                if (filter.Parameters == null)
                {
                    continue;
                }

                foreach (FilterParameter param in filter.Parameters)
                {
                    Element elem = _menuRef.Find(GetParamElementId(filter.Id, param.Key));
                    if (elem != null)
                    {
                        elem.isVisible = ShouldShowParam(filter, param);
                    }
                }
            }
        }

        private static bool HasVisibleParameters(IFilter filter)
        {
            return filter != null && filter.Parameters != null && filter.Parameters.Count > 0;
        }

        private static bool ShouldShowParam(IFilter filter, FilterParameter param)
        {
            if (FilterManager.ActiveFilter != filter)
            {
                return false;
            }

            return param.IsVisible == null || param.IsVisible();
        }

        private static string GetParamElementId(string filterId, string paramKey)
        {
            return $"param:{filterId}:{paramKey}";
        }

        private sealed class FilterSlider : CustomSlider
        {
            public FilterSlider(
                string name,
                Action<float> storeValue,
                Func<float> loadValue,
                float minValue,
                float maxValue,
                bool wholeNumbers = false,
                string id = "__UseName")
                : base(name, storeValue, loadValue, minValue, maxValue, wholeNumbers, id)
            {
            }

            public override void Update()
            {
                if (valueLabel != null)
                {
                    valueLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                    valueLabel.verticalOverflow = VerticalWrapMode.Overflow;
                }

                base.Update();
            }
        }
    }
}