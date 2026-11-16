using System.Collections.Generic;
using CoreGMIC.Core;
using Satchel.BetterMenus;

namespace CoreGMIC.UI
{
    public static class ModMenu
    {
        private static Menu _menuRef;

        public static MenuScreen GetMenuScreen(MenuScreen modListMenu)
        {
            // Rebuild menu elements dynamically so newly registered filters always appear.
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
                    () => FilterManager.IsEnabled ? 1 : 0
                )
            };

            List<string> filterNames = new List<string> { "None" };
            foreach (IFilter filter in FilterManager.Filters)
            {
                string availability = filter.IsAvailable ? "" : " [N/A]";
                filterNames.Add($"{filter.DisplayName}{availability}");
            }

            elements.Add(new HorizontalOption(
                "Active Filter",
                "Select which G'MIC filter to apply to the game world.",
                filterNames.ToArray(),
                index =>
                {
                    // Index 0 corresponds to "None (Original)" (-1 in FilterManager).
                    FilterManager.SetActiveIndex(index - 1);
                },
                () => FilterManager.ActiveFilterIndex + 1
            ));

            _menuRef = new Menu("core.G'MIC", elements.ToArray());
            return _menuRef.GetMenuScreen(modListMenu);
        }
    }
}