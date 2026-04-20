using BepInEx.Configuration;

namespace ShipCleaner
{
    public enum SortingMode
    {
        ScrapToolsAndEquipment,  // Default — sort everything
        ScrapOnly,               // Only sort scrap to floor grid
        ToolsAndEquipmentOnly    // Only sort tools/equipment to closet (or floor)
    }

    public static class ShipCleanerConfig
    {
        // ----------------------------------------------------------------
        // Config entries
        // ----------------------------------------------------------------
        public static ConfigEntry<SortingMode> SortingModeConfig { get; private set; }
        public static ConfigEntry<bool>        UseStorageCloset  { get; private set; }

        // ----------------------------------------------------------------
        // Convenience properties (read by ShipCleaner_Manager at sort time)
        // ----------------------------------------------------------------
        public static bool ShouldSortScrap =>
            SortingModeConfig.Value == SortingMode.ScrapToolsAndEquipment ||
            SortingModeConfig.Value == SortingMode.ScrapOnly;

        public static bool ShouldSortTools =>
            SortingModeConfig.Value == SortingMode.ScrapToolsAndEquipment ||
            SortingModeConfig.Value == SortingMode.ToolsAndEquipmentOnly;

        public static bool SortToolsToCloset =>
            UseStorageCloset.Value;

        // ----------------------------------------------------------------
        // Initialise — called from Plugin.cs Awake()
        // ----------------------------------------------------------------
        public static void Init(ConfigFile config)
        {
            // --- Bind config entries (always runs, no LethalConfig dependency) ---

            SortingModeConfig = config.Bind(
                section:      "General",
                key:          "Sorting Mode",
                defaultValue: SortingMode.ScrapToolsAndEquipment,
                description:
                    "Controls what ShipCleaner sorts when the hotkey is pressed.\n" +
                    "  ScrapToolsAndEquipment — Sort everything (default).\n" +
                    "  ScrapOnly              — Only move scrap to the floor grid; leave tools untouched.\n" +
                    "  ToolsAndEquipmentOnly  — Only move tools/equipment; leave scrap untouched."
            );

            UseStorageCloset = config.Bind(
                section:      "General",
                key:          "Use Storage Closet",
                defaultValue: true,
                description:
                    "When true, tools and equipment are sorted into the Storage Closet shelves (default).\n" +
                    "When false, tools and equipment are arranged on the floor along the closet's default wall,\n" +
                    "useful for players who prefer floor-only organisation."
            );

            // --- Register with LethalConfig if it's installed (soft dependency) ---
            // Checked at runtime so the mod works fine without LethalConfig installed.
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("ainavt.lc.lethalconfig"))
                RegisterLethalConfigItems();
        }

        // Isolated method — LethalConfig types only resolved if the assembly
        // is confirmed present at runtime, preventing crashes without it.
        private static void RegisterLethalConfigItems()
        {
            LethalConfig.LethalConfigManager.AddConfigItem(
                new LethalConfig.ConfigItems.EnumDropDownConfigItem<SortingMode>(
                    SortingModeConfig,
                    new LethalConfig.ConfigItems.Options.EnumDropDownOptions
                    {
                        Name        = "Sorting Mode",
                        Description =
                            "Choose what ShipCleaner sorts when the hotkey is pressed:\n\n" +
                            "• Scrap, Tools & Equipment — Sort everything (default).\n" +
                            "• Scrap Only — Only move scrap items to the floor grid.\n" +
                            "• Tools & Equipment Only — Only move tools/equipment to the closet or wall.",
                        RequiresRestart = false
                    }
                )
            );

            LethalConfig.LethalConfigManager.AddConfigItem(
                new LethalConfig.ConfigItems.BoolCheckBoxConfigItem(
                    UseStorageCloset,
                    new LethalConfig.ConfigItems.Options.BoolCheckBoxOptions
                    {
                        Name        = "Use Storage Closet",
                        Description =
                            "ON  — Tools and equipment are sorted into the Storage Closet shelves (default).\n" +
                            "OFF — Tools and equipment are placed on the floor along the closet's default wall.\n" +
                            "      Useful for players who prefer floor-only organisation.",
                        RequiresRestart = false
                    }
                )
            );
        }
    }
}
