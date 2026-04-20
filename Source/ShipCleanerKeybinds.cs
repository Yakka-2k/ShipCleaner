using LethalCompanyInputUtils.Api;
using UnityEngine.InputSystem;

namespace ShipCleaner
{
    public class ShipCleanerKeybinds : LcInputActions
    {
        public static ShipCleanerKeybinds Instance { get; private set; }

        public ShipCleanerKeybinds()
        {
            Instance = this;
        }

        // Default binding: comma key
        [InputAction("<Keyboard>/comma")]
        public InputAction SortItemsKey { get; private set; }

        // Correct InputUtils-compatible press check
        public bool SortItemsKeyWasPressed =>
            SortItemsKey != null && SortItemsKey.triggered;
    }
}
