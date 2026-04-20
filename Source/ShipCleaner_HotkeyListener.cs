using UnityEngine;

namespace ShipCleaner
{
    public class ShipCleaner_HotkeyListener : MonoBehaviour
    {
        private void Update()
        {
            if (ShipCleanerKeybinds.Instance != null &&
                ShipCleanerKeybinds.Instance.SortItemsKeyWasPressed)
            {
                ShipCleaner.Log.LogInfo("Cleaning Ship");
                ShipCleaner_Manager.Instance?.RunFullSort();
            }
        }
    }
}
