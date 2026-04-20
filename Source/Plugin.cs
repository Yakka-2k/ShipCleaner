using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace ShipCleaner
{
    [BepInPlugin("com.yakka_productions.shipcleaner", "ShipCleaner", "1.0.0")]
    [BepInDependency("com.rune580.LethalCompanyInputUtils")]
    [BepInDependency("ainavt.lc.lethalconfig", BepInDependency.DependencyFlags.SoftDependency)]
    public class ShipCleaner : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            // Initialize config (must happen before manager is created)
            ShipCleanerConfig.Init(Config);

            Log.LogInfo("ShipCleaner loaded.");

            // Initialize InputUtils keybinds (constructor sets Instance)
            _ = new ShipCleanerKeybinds();

            // Create manager object
            var obj = new GameObject("ShipCleaner_Manager");
            DontDestroyOnLoad(obj);

            // Hide the manager object from the hierarchy
            obj.hideFlags = HideFlags.HideAndDontSave;

            // Attach the manager
            obj.AddComponent<ShipCleaner_Manager>();

            // Attach the listener
            obj.AddComponent<ShipCleaner_HotkeyListener>();

            Log.LogInfo("ShipCleaner_Manager created.");
        }
    }
}
