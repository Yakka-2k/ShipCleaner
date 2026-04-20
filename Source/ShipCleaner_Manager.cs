using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

namespace ShipCleaner
{
    public class ShipCleaner_Manager : MonoBehaviour
    {
        public static ShipCleaner_Manager Instance { get; private set; }

        private Transform _shipInterior = null!;

        private readonly List<GrabbableObject> _toolItems = new List<GrabbableObject>();
        private readonly List<GrabbableObject> _scrapItems = new List<GrabbableObject>();

        private float _floorWorldY = 0.3f;

        // Shelf node names under ObjectPlacements, ordered top → bottom
        private static readonly string[] ShelfNodeNames = new string[]
        {
            "Cube (3)",  // index 0 → top shelf
            "Cube (2)",  // index 1 → 2nd shelf
            "Cube (1)",  // index 2 → 3rd shelf
            "Cube",      // index 3 → bottom shelf
        };

        private const float ShelfItemSpacing = 0.28f;
        private const int MaxShelfItems = 6;

        // -0.10 pulls items just slightly below the ObjectPlacements node
        // so they appear to rest on the visual shelf surface
        private const float ShelfSurfaceCorrection = -0.05f; // just below ObjectPlacements node, resting on shelf surface

        // yRot=180 faces items toward the closet opening (confirmed correct last run)
        private const int ClosetItemYRot = 180;

        // ----------------------------------------------------------------
        // Floor grid — based on ship schematic
        //
        // SHIP ORIENTATION (overhead view, matching schematic):
        //   Front (doors/entrance):  lower Z (Z ~ -9 to -11)
        //   Back  (control desk):    higher Z (Z ~ -15 to -17)
        //   Left wall (closet side): lower X (X ~ 0)
        //   Right wall (terminal):   higher X (X ~ 4)
        //   Floor center X:          ~2.0
        //
        // SORTING LAYOUT (per schematic green zone):
        //   Two-handed items:   front of ship, near doors
        //                       Z starts at ~-10, spreads toward -12 (deeper)
        //   One-handed items:   middle of ship
        //                       Z starts at ~-13, spreads toward -16 (deeper)
        //
        // Both groups spread in -Z direction (from front toward back).
        // ----------------------------------------------------------------

        private const int MaxItemsPerRow = 4;          // fewer cols to keep items away from walls
        private const float OneHandedCellSize = 0.45f; // tight spacing, more items per row
        private const float TwoHandedCellSize = 0.85f; // tighter spacing for large items
        private const float OneHandedRowSpacing = 0.50f; // tight rows
        private const float TwoHandedRowSpacing = 1.00f;

        // Grid center X — midpoint of usable floor (confirmed ~2.0 from spawn data)
        private const float ShipFloorCenterX = 0.2f; // true center: (Flask X 5.39 + Engine X -4.96) / 2 ≈ 0.2
        private const float TwoHandedCenterX = 0.2f;  // center 2H items on floor, same as 1H

        // Two-handed: front of ship near doors
        // Ship pivot Z = -7.5. Offset -2.5 → baseZ = -10.0
        // Rows spread in -Z: row 0 = -10.0, row 1 = -11.2, etc.
        private const float TwoHandedStartZ = -8.0f; // 2H zone starts at Z -15.5 (near doors)

        // One-handed: middle of ship
        // Offset -5.5 → baseZ = -13.0
        // Rows spread in -Z: row 0 = -13.0, row 1 = -13.8, etc.
        private const float OneHandedStartZ = -6.77f; // baseZ=-14.27, exactly the Flask spawn position

        // yRot for floor items:
        //   Two-handed: yRot=90 orients item along Z axis (front-to-back),
        //               which is the natural orientation for long/large items near the door
        //   One-handed: yRot=0 is the neutral default for small items
        private const int TwoHandedFloorYRot = 90;
        private const int OneHandedFloorYRot = 0;

        // Layer mask from GrabbableObject.GetItemFloorPosition
        private const int FloorLayerMask = 268437761;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void RunFullSort()
        {
            // Host-only: only the host has position authority over networked items.
            // Clients with the mod installed will simply skip the sort — they'll see
            // the sorted layout via normal Netcode sync from the host.
            if (!GameNetworkManager.Instance.isHostingGame)
            {
                Debug.Log("ShipCleaner: Not the host — skipping sort.");
                return;
            }

            Debug.Log("ShipCleaner: Cleaning the ship!");

            _shipInterior = GameObject.Find("Environment/HangarShip")?.transform;
            if (_shipInterior == null)
            {
                Debug.Log("ShipCleaner: Could not find Environment/HangarShip — aborting.");
                return;
            }

            _floorWorldY = ResolveFloorY();
            CollectAndClassifyItems();

            if (ShipCleanerConfig.ShouldSortTools)
            {
                if (ShipCleanerConfig.SortToolsToCloset)
                    SortToolsIntoCloset();
                else
                    SortToolsOnFloor();
            }

            if (ShipCleanerConfig.ShouldSortScrap)
                SortScrapOnFloorGrid();

            Debug.Log("ShipCleaner: Ship has been cleaned!");
        }

        // ----------------------------------------------------------------
        // Floor Y Resolution
        // ----------------------------------------------------------------

        private float ResolveFloorY()
        {
            var candidates = new List<float>();
            foreach (var item in FindObjectsOfType<GrabbableObject>())
            {
                if (item.isHeld) continue;
                bool onShip = item.isInShipRoom;
                if (!onShip)
                {
                    var local = _shipInterior.InverseTransformPoint(item.transform.position);
                    onShip = Mathf.Abs(local.x) < 15f && Mathf.Abs(local.y) < 10f && Mathf.Abs(local.z) < 15f;
                }
                if (!onShip) continue;
                float y = item.transform.position.y;
                if (y < -2f || y > 3f) continue;
                // Exclude items near the entrance ramp (Z < -14.5) which sits higher
                // than the main floor and would skew the floor Y sample upward
                float itemZ = item.transform.position.z;
                if (itemZ < -14.5f) continue;
                candidates.Add(y);
            }

            if (candidates.Count > 0)
            {
                candidates.Sort();
                // Use the minimum Y (true floor level).
                // Median was causing issues when items placed on elevated surfaces
                // (entrance ramp Y ~0.61) were skewing the sample upward.
                float minY = candidates[0];
                return minY;
            }

            Vector3 rayOrigin = _shipInterior.position + Vector3.up * 20f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 40f, FloorLayerMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return 0.3f;
        }

        // ----------------------------------------------------------------
        // Item Collection & Classification
        // ----------------------------------------------------------------

        private void CollectAndClassifyItems()
        {
            _toolItems.Clear();
            _scrapItems.Clear();

            foreach (var item in FindObjectsOfType<GrabbableObject>())
            {
                if (!IsOnShip(item) || item.isHeld) continue;
                // Exclude items the crew may want to leave in place,
                // and container/utility items that shouldn't be sorted
                string itemName = (item.itemProperties?.itemName ?? item.name).ToLowerInvariant();
                if (itemName.Contains("sticky note") || itemName.Contains("clipboard")) continue;
                if (itemName.Contains("peeper")) continue;
                if (itemName.Contains("wheelbarrow")) continue;
                if (itemName.Contains("shopping cart")) continue;
                // Faceless Stalker (Slenderman) mod pages — confirmed names: Page, Strange Page, Dirty Page, Mysterious Page
                if (itemName == "page" || itemName.EndsWith(" page")) continue;
                // Lethal Trading Cards mod — confirmed names from DawnLib log
                // Cards: "[Name] Card" pattern. Holofoils: "Holofoil [Name]" pattern.
                if (itemName.EndsWith(" card")) continue;          // all 16 cards
                if (itemName.StartsWith("holofoil ")) continue;    // all 16 holofoil variants
                if (itemName == "lethamon booster pack") continue;  // booster pack
                if (IsTool(item)) _toolItems.Add(item);
                else _scrapItems.Add(item);
            }

            _toolItems.Sort(CompareItems);
            _scrapItems.Sort(CompareItems);

        }

        private bool IsOnShip(GrabbableObject item)
        {
            if (item.isInShipRoom) return true;
            var local = _shipInterior.InverseTransformPoint(item.transform.position);
            return Mathf.Abs(local.x) < 15f && Mathf.Abs(local.y) < 10f && Mathf.Abs(local.z) < 15f;
        }

        private bool IsTool(GrabbableObject item)
        {
            var props = item.itemProperties;
            if (props == null) return false;
            string name = (props.itemName ?? item.name).ToLowerInvariant();
            // Some items are flagged isScrap by the game but should be treated as tools.
            // Check name first to catch these overrides.
            bool nameMatchesTool =
                name.Contains("flashlight") || name.Contains("key") || name.Contains("walkie") ||
                name.Contains("portable teleporter") || name.Contains("advanced portable teleporter") ||
                name.Contains("pokeball") || name.Contains("great ball") || name.Contains("ultra ball") || name.Contains("master ball") ||
                name.Contains("shotgun") || name.Contains("ammo") || name.Contains("grenade") ||
                name.Contains("flashbang") || name.Contains("zap") || name.Contains("stun") ||
                name.Contains("rifle") || name.Contains("magazine") ||
                name.Contains("belt") || name.Contains("spray") || name.Contains("weed") ||
                name.Contains("tzp") || name.Contains("inhalant") || name.Contains("lockpicker") ||
                name.Contains("whoopie") || name.Contains("diving kit") || name.Contains("helmet") ||
                name.Contains("boombox") || name.Contains("ladder") || name.Contains("jetpack") ||
                name.Contains("radar") || name.Contains("shovel");
            if (nameMatchesTool) return true;
            // For everything else, respect the isScrap flag
            if (props.isScrap) return false;
            if (name.Contains("flashlight") || name.Contains("key") || name.Contains("walkie")) return true;
            if (name.Contains("shotgun") || name.Contains("ammo") || name.Contains("grenade") ||
                name.Contains("flashbang") || name.Contains("zap") || name.Contains("stun") ||
                name.Contains("rifle") || name.Contains("magazine")) return true;
            if (name.Contains("belt") || name.Contains("spray") || name.Contains("weed") ||
                name.Contains("tzp") || name.Contains("inhalant") || name.Contains("lockpicker") ||
                name.Contains("whoopie")) return true;
            if (name.Contains("boombox") || name.Contains("ladder") || name.Contains("jetpack") ||
                name.Contains("radar") || name.Contains("shovel")) return true;
            return false;
        }

        private bool IsTwoHanded(GrabbableObject item) =>
            item.itemProperties != null && item.itemProperties.twoHanded;

        private int CompareItems(GrabbableObject a, GrabbableObject b)
        {
            string an = a.itemProperties?.itemName ?? StripClone(a.name);
            string bn = b.itemProperties?.itemName ?? StripClone(b.name);
            return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
        }

        private string StripClone(string name)
        {
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }

        // ----------------------------------------------------------------
        // Closet / Tool Sorting
        //
        //   Shelf 0 (top):    Flashlight, Pro-flashlight, Key, Walkie-talkie
        //   Shelf 1 (2nd):    Shotgun, Ammo, Stun grenade, Flashbang, Zap gun
        //   Shelf 2 (3rd):    Belt bag, Spray paint, Weed killer, TZP-Inhalant, Lockpicker
        //   Shelf 3 (bottom): Boombox, Extension ladder, Jetpack, Radar Booster, Shovel
        // ----------------------------------------------------------------

        private int GetShelfIndexForTool(GrabbableObject item)
        {
            string name = (item.itemProperties?.itemName ?? item.name).ToLowerInvariant();
            if (name.Contains("flashlight") || name.Contains("key") || name.Contains("walkie") ||
                name.Contains("portable teleporter") || name.Contains("advanced portable teleporter") ||
                name.Contains("pokeball") || name.Contains("great ball") || name.Contains("ultra ball") || name.Contains("master ball"))
                return 0;
            // Shelf 1 (2nd from top, more vertical space): consumables/tall items
            if (name.Contains("belt") || name.Contains("spray") || name.Contains("weed") ||
                name.Contains("tzp") || name.Contains("inhalant") || name.Contains("lockpicker") ||
                name.Contains("whoopie"))
                return 1;
            // Shelf 2 (3rd from top): weapons & explosives
            if (name.Contains("shotgun") || name.Contains("ammo") || name.Contains("grenade") ||
                name.Contains("flashbang") || name.Contains("zap") || name.Contains("stun") ||
                name.Contains("rifle") || name.Contains("magazine"))
                return 2;
            return 3;
        }

        private void SortToolsIntoCloset()
        {
            if (_toolItems.Count == 0) return;

            var closetGO = GameObject.Find("Environment/HangarShip/StorageCloset");
            if (closetGO == null)
            {
                Debug.Log("ShipCleaner: StorageCloset not found — placing tools on floor grid.");
                _scrapItems.InsertRange(0, _toolItems);
                return;
            }

            Transform objectPlacements = null;
            foreach (Transform t in closetGO.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "ObjectPlacements") { objectPlacements = t; break; }
            }

            if (objectPlacements == null)
            {
                Debug.Log("ShipCleaner: ObjectPlacements not found — placing tools on floor grid.");
                _scrapItems.InsertRange(0, _toolItems);
                return;
            }

            var shelfTransforms = new Dictionary<string, Transform>();
            foreach (Transform t in objectPlacements)
                shelfTransforms[t.name] = t;

            var buckets = new Dictionary<int, List<GrabbableObject>>();
            for (int i = 0; i < ShelfNodeNames.Length; i++)
                buckets[i] = new List<GrabbableObject>();

            foreach (var item in _toolItems)
                buckets[GetShelfIndexForTool(item)].Add(item);

            var overflow = new List<GrabbableObject>();

            foreach (var kvp in buckets)
            {
                var items = kvp.Value;
                if (items.Count == 0) continue;

                string nodeName = ShelfNodeNames[kvp.Key];
                if (!shelfTransforms.TryGetValue(nodeName, out Transform shelfNode))
                {
                    Debug.Log($"ShipCleaner: Shelf node '{nodeName}' not found — overflowing to floor.");
                    overflow.AddRange(items);
                    continue;
                }

                int count = Mathf.Min(items.Count, MaxShelfItems);
                float totalWidth = ShelfItemSpacing * (count - 1);

                // Spread items along the shelf's own right axis (local X of the shelf node's
                // parent ObjectPlacements transform). This makes left-to-right order correct
                // regardless of where the player has moved the closet in the ship.
                // ObjectPlacements has a consistent local right = world right in default placement,
                // but using TransformDirection future-proofs it if the closet is rotated.
                Vector3 shelfRight = objectPlacements.TransformDirection(Vector3.right).normalized;
                // Push items back along the shelf's depth so they don't clip the closet doors.
                // The closet forward (into the shelf) is objectPlacements local -Z in world space.
                // The closet opens toward +X (world). Shelf depth runs in world -Z direction.
                // We push items slightly in -Z to keep them away from the closet door opening.
                Vector3 shelfDepth = Vector3.back; // world -Z = into the shelf
                float depthOffset = 0.12f; // push 0.12 units back from front edge

                for (int i = 0; i < count; i++)
                {
                    float vOffset = items[i].itemProperties?.verticalOffset ?? 0f;
                    float offset = -totalWidth * 0.5f + i * ShelfItemSpacing;

                    Vector3 worldPos = shelfNode.position
                        + shelfRight * offset
                        + shelfDepth * depthOffset
                        + Vector3.up * (vOffset + ShelfSurfaceCorrection);

                    // Shelf 0 (flashlights/keys/walkies): yRot=0
                    // Shelves 1 and 2 (weapons/consumables): yRot=90
                    // Shelf 3 (shovel): yRot=180
                    // Per-item overrides for items that clip or fit better rotated differently
                    int itemYRot = (kvp.Key == 3) ? 180 : (kvp.Key == 0) ? 0 : 90;
                    string closetItemName = (items[i].itemProperties?.itemName ?? items[i].name).ToLowerInvariant();
                    if (closetItemName.Contains("rifle"))
                        itemYRot = 0;   // orient front-to-back to avoid clipping doors
                    else if (closetItemName.Contains("magazine"))
                        itemYRot = 90;  // lay flat along shelf width
                    MoveItem(items[i], worldPos, itemYRot);
                }

                if (items.Count > MaxShelfItems)
                    overflow.AddRange(items.Skip(MaxShelfItems));
            }

            if (overflow.Count > 0)
                _scrapItems.InsertRange(0, overflow);
        }

        // ----------------------------------------------------------------
        // Floor Grid (Scrap) Sorting
        // ----------------------------------------------------------------

        // 1H grid: 1.0 X steps (room for large items), 0.5 Z steps (tighter rows)
        //   7 rows per column (Z -13 to -16), 9 columns (X 6 to -2) = 63 cells
        // 2H grid: 1.0 steps both axes
        //   4 rows per column (Z -16 to -13), 3 columns (X -3 to -5) = 12 cells
        private const int OneHRowsPerCol = 7;  // Z: -13, -13.5, -14, -14.5, -15, -15.5, -16
        private const int OneHNumCols = 9;  // X: 6, 5, 4, 3, 2, 1, 0, -1, -2
        private const int TwoHRowsPerCol = 4;  // Z: -16, -15, -14, -13
        private const int TwoHNumCols = 3;  // X: -3, -4, -5

        // ----------------------------------------------------------------
        // Floor-based Tool Sorting (used when Storage Closet is disabled)
        // Tools fill a single row along Z=-13, X=-5 to X=2 in 0.5 steps.
        // Overflow rows step in -Z (toward doors): -13, -13.5, -14, etc.
        // ----------------------------------------------------------------
        private void SortToolsOnFloor()
        {
            if (_toolItems.Count == 0) return;

            var avoidanceZones = BuildAvoidanceZones();
            const float toolStartX = -5f;   // East wall side
            const float toolEndX = 2f;   // stop before 1H scrap zone
            const float toolStartZ = -12.5f; // first row Z, against the East wall
            const float toolStepX = 0.5f; // spacing along the row
            const float toolStepZ = 0.5f; // spacing between rows

            // Number of positions per row: X from -5 to 2 in 0.5 steps = 15 positions
            int posPerRow = Mathf.RoundToInt((toolEndX - toolStartX) / toolStepX) + 1;

            int cellIndex = 0;
            foreach (var item in _toolItems)
            {
                for (int skip = 0; skip < 50; skip++)
                {
                    int col = cellIndex % posPerRow;
                    int row = cellIndex / posPerRow;
                    float tx = toolStartX + col * toolStepX;
                    float tz = toolStartZ - row * toolStepZ;
                    if (IsCellBlocked(tx, tz, avoidanceZones) || IsCellElevated(tx, tz, _floorWorldY))
                        cellIndex++;
                    else
                        break;
                }

                int finalCol = cellIndex % posPerRow;
                int finalRow = cellIndex / posPerRow;
                float worldX = toolStartX + finalCol * toolStepX;
                float worldZ = toolStartZ - finalRow * toolStepZ;
                float vOff = item.itemProperties?.verticalOffset ?? 0f;

                Vector3 candidate = new Vector3(worldX, _floorWorldY + 2f, worldZ);
                Vector3 worldPos;
                if (Physics.Raycast(candidate, Vector3.down, out RaycastHit hit, 5f, FloorLayerMask, QueryTriggerInteraction.Ignore))
                    worldPos = hit.point + Vector3.up * vOff;
                else
                    worldPos = new Vector3(worldX, _floorWorldY + vOff, worldZ);

                MoveItem(item, worldPos, 0);
                cellIndex++;
            }
        }


        private static float CellToWorldX(int cellIndex, bool isTwoHanded, float oneHStartX, float twoHStartX)
        {
            if (!isTwoHanded)
            {
                int col = (cellIndex / OneHRowsPerCol) % OneHNumCols;
                return oneHStartX - col * 1.0f; // 1.0 X steps: 6, 5, 4...
            }
            else
            {
                int col = (cellIndex / TwoHRowsPerCol) % TwoHNumCols;
                return twoHStartX - col * 1.0f; // 1.0 X steps: -3, -4, -5
            }
        }

        private static float CellToWorldZ(int cellIndex, bool isTwoHanded, float oneHStartZ, float twoHStartZ)
        {
            if (!isTwoHanded)
            {
                int row = cellIndex % OneHRowsPerCol;
                return oneHStartZ - row * 0.5f; // 0.5 Z steps: -13, -13.5, -14...
            }
            else
            {
                int row = cellIndex % TwoHRowsPerCol;
                return twoHStartZ + row * 1.0f; // 1.0 Z steps: -16, -15, -14, -13
            }
        }

        // Furniture avoidance zones — (object name, X radius, Z radius)
        // Names confirmed from "item awake:" log entries.
        // Radii in world units: 1.0 = avoids 2 grid columns (1.0 X step) or 4 rows (0.5 Z step).
        private static readonly (string name, float rx, float rz)[] FurniturePaths = new[]
        {
            // Built-in ship furniture (no Clone suffix)
            ("StorageCloset",              1.5f, 1.0f),
            ("Terminal",                   1.2f, 0.8f),
            ("Bunkbeds",                   1.5f, 1.2f),
            ("FileCabinet",                0.8f, 0.6f),
            ("LightSwitchContainer",       0.5f, 0.5f),
            // Unlockable furniture (confirmed names from log)
            ("Teleporter(Clone)",          0.8f, 0.8f),
            ("InverseTeleporter(Clone)",   0.8f, 0.8f),
            ("Shower(Clone)",              1.0f, 1.0f),
            ("DogHouse(Clone)",            1.2f, 1.2f),
            ("ChargingStationStorage(Clone)", 0.8f, 0.6f),
            ("FridgeContainer(Clone)",     1.0f, 0.8f),
            ("FishBowlContainer(Clone)",   0.6f, 0.6f), // confirmed name
            ("PumpkinUnlockableContainer(Clone)", 0.6f, 0.6f), // confirmed name
            ("ShipHorn(Clone)",            1.5f, 1.2f), // wider X radius to cover grid boundary at X=6
            ("MicrowaveContainer(Clone)",  0.8f, 0.7f),
            ("PlushiePJManContainer(Clone)", 0.9f, 0.9f),
            ("RecordPlayerContainer(Clone)", 1.0f, 1.0f), // confirmed name
            ("RomanticTable(Clone)",       1.0f, 1.0f),
            ("SignalTranslator(Clone)",    0.8f, 0.6f),
            ("SofaChairContainer(Clone)",  0.6f, 0.6f), // height check handles items landing on seat
            ("Television(Clone)",          1.0f, 1.0f),
            ("Toilet(Clone)",              0.7f, 0.7f),
            ("Mirror(Clone)",              1.0f, 1.0f),
            ("ElectricChair(Clone)",       0.6f, 0.6f), // small base footprint; height check handles top
        };

        private List<(Vector3 pos, float rx, float rz)> BuildAvoidanceZones()
        {
            // Build a name→GameObject lookup using all GameObjects in the scene,
            // including inactive ones (GameObject.Find skips inactive objects).
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            var lookup = new Dictionary<string, GameObject>();
            foreach (var go in allObjects)
            {
                if (!lookup.ContainsKey(go.name))
                    lookup[go.name] = go;
            }

            var zones = new List<(Vector3, float, float)>();
            foreach (var (name, rx, rz) in FurniturePaths)
            {
                if (lookup.TryGetValue(name, out var found))
                {
                    Vector3 pos = found.transform.position;
                    // Skip items at origin — not yet placed or invalid position
                    if (pos == Vector3.zero) continue;
                    zones.Add((pos, rx, rz));
                }
            }
            return zones;
        }

        private bool IsCellBlocked(float worldX, float worldZ, List<(Vector3 pos, float rx, float rz)> zones)
        {
            foreach (var (pos, rx, rz) in zones)
            {
                if (Mathf.Abs(worldX - pos.x) < rx && Mathf.Abs(worldZ - pos.z) < rz)
                    return true;
            }
            return false;
        }

        // Returns true if the floor at this cell is more than 0.3 units above the
        // ship floor level — meaning furniture geometry is occupying the cell.
        private bool IsCellElevated(float worldX, float worldZ, float floorY)
        {
            Vector3 origin = new Vector3(worldX, floorY + 3f, worldZ);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f, FloorLayerMask, QueryTriggerInteraction.Ignore))
                return hit.point.y > floorY + 0.15f; // tighter threshold catches low platforms like teleporter pads
            return false;
        }

        private void SortScrapOnFloorGrid()
        {
            if (_scrapItems.Count == 0) return;

            // GRID LAYOUT (absolute world coordinates from in-game HUD):
            //
            // 1H zone: columns X=6 down to X=-2, rows Z=-13 to Z=-16
            //   Fill order: X=6,Z=-13 → X=6,Z=-14 → X=6,Z=-15 → X=6,Z=-16
            //               then X=5,Z=-13 → ... and so on westward
            //
            // 2H zone: columns X=-3 down to X=-5, rows Z=-16 to Z=-13
            //   Fill order: X=-3,Z=-16 → X=-3,Z=-15 → X=-3,Z=-14 → X=-3,Z=-13
            //               then X=-4 → X=-5
            //
            // Buffer: X=-2 (1H) to X=-3 (2H) — 1 unit gap between zones

            const float GridY = 0f;   // floor Y (overridden by raycast)
            const float OneHStartX = 6f;  // 1H first column (West side)
            const float OneHEndX = -2f;  // 1H last column (East buffer)
            const float OneHStartZ = -13f; // 1H first row
            const float OneHEndZ = -16f; // 1H last row
            const float TwoHStartX = -3f;  // 2H first column
            const float TwoHEndX = -5f;  // 2H last column
            const float TwoHStartZ = -16f; // 2H first row
            const float TwoHEndZ = -13f; // 2H last row


            // Special items (Yield sign, Stop sign) get dedicated spots at X=9
            // outside the normal grid, so they don't displace other items.
            const float SignColumnX = 9f;
            var signItems = _scrapItems
                .Where(i => {
                    string n = (i.itemProperties?.itemName ?? i.name).ToLowerInvariant();
                    return n.Contains("yield") || n.Contains("stop sign");
                })
                .OrderBy(i => i.itemProperties?.itemName ?? i.name)
                .ToList();

            float[] signZPositions = { -13f, -14f, -15f };
            for (int s = 0; s < signItems.Count && s < signZPositions.Length; s++)
            {
                float vOff = signItems[s].itemProperties?.verticalOffset ?? 0f;
                Vector3 signCandidate = new Vector3(SignColumnX, _floorWorldY + 2f, signZPositions[s]);
                Vector3 signPos;
                if (Physics.Raycast(signCandidate, Vector3.down, out RaycastHit signHit, 5f, FloorLayerMask, QueryTriggerInteraction.Ignore))
                    signPos = signHit.point + Vector3.up * vOff;
                else
                    signPos = new Vector3(SignColumnX, _floorWorldY + vOff, signZPositions[s]);
                MoveItem(signItems[s], signPos, 0);
            }

            // Remove signs from scrap list so they don't also appear in the normal grid
            _scrapItems.RemoveAll(i => {
                string n = (i.itemProperties?.itemName ?? i.name).ToLowerInvariant();
                return n.Contains("yield") || n.Contains("stop sign");
            });

            // Sort scrap groups by value descending so most valuable items
            // fill the grid first (toward the back/monitors for 1H, toward doors for 2H).
            // Within same-name groups, items are also sorted by value descending.
            // Build avoidance zones once for all furniture present on the ship
            var avoidanceZones = BuildAvoidanceZones();

            var groups = _scrapItems
                .GroupBy(i => i.itemProperties?.itemName ?? StripClone(i.name))
                .OrderByDescending(g => g.Max(i => i.scrapValue))
                .ToList();

            // Global cell counters — each stack of same-type items occupies one cell.
            // 1H cells fill: X=6→-2 (step -1), within each X column Z=-13→-16 (step -1)
            // 2H cells fill: X=-3→-5 (step -1), within each X column Z=-16→-13 (step +1)
            const int MaxStackSize = 6;
            int oneHandedCellsUsed = 0;
            int twoHandedCellsUsed = 0;

            foreach (var group in groups)
            {
                var items = group.OrderByDescending(i => i.scrapValue).ToList();
                bool isTwoHanded = IsTwoHanded(items[0]);
                int itemYRot = isTwoHanded ? TwoHandedFloorYRot : OneHandedFloorYRot;
                System.Random rng = new System.Random(items[0].GetHashCode());

                for (int i = 0; i < items.Count; i++)
                {
                    int stackIndex = i / MaxStackSize;
                    int stackPos = i % MaxStackSize;

                    int baseCell = (isTwoHanded ? twoHandedCellsUsed : oneHandedCellsUsed) + stackIndex;

                    // Advance cell index past any cells blocked by furniture footprint
                    // or elevated geometry (items landing on top of chairs, sofas, etc.)
                    int cellIndex = baseCell;
                    for (int skip = 0; skip < 50; skip++)
                    {
                        float tx = CellToWorldX(cellIndex, isTwoHanded, OneHStartX, TwoHStartX);
                        float tz = CellToWorldZ(cellIndex, isTwoHanded, OneHStartZ, TwoHStartZ);
                        if (IsCellBlocked(tx, tz, avoidanceZones) || IsCellElevated(tx, tz, _floorWorldY))
                            cellIndex++;
                        else
                            break;
                    }

                    float worldX = CellToWorldX(cellIndex, isTwoHanded, OneHStartX, TwoHStartX);
                    float worldZ = CellToWorldZ(cellIndex, isTwoHanded, OneHStartZ, TwoHStartZ);

                    Vector3 candidate = new Vector3(worldX, _floorWorldY + 2f, worldZ);
                    float vOffset = items[i].itemProperties?.verticalOffset ?? 0f;
                    Vector3 worldPos;

                    if (Physics.Raycast(candidate, Vector3.down, out RaycastHit hit, 5f, FloorLayerMask, QueryTriggerInteraction.Ignore))
                        worldPos = hit.point + Vector3.up * vOffset;
                    else
                    {
                        Vector3 nudged = new Vector3(candidate.x, candidate.y, candidate.z + 0.5f);
                        if (Physics.Raycast(nudged, Vector3.down, out RaycastHit hit2, 5f, FloorLayerMask, QueryTriggerInteraction.Ignore))
                            worldPos = hit2.point + Vector3.up * vOffset;
                        else
                            worldPos = new Vector3(worldX, _floorWorldY + vOffset, worldZ);
                    }

                    int finalYRot = itemYRot + (stackPos == 0 ? 0 : rng.Next(-15, 16));
                    string label = items[i].itemProperties?.itemName ?? items[i].name;

                    MoveItem(items[i], worldPos, finalYRot);
                }

                // Advance cell counter by number of stacks this group used
                int stacksUsed = Mathf.CeilToInt((float)items.Count / MaxStackSize);
                if (isTwoHanded) twoHandedCellsUsed += stacksUsed;
                else oneHandedCellsUsed += stacksUsed;
            }
        }

        // ----------------------------------------------------------------
        // Item Movement
        // ----------------------------------------------------------------

        private void MoveItem(GrabbableObject item, Vector3 worldPos, int yRot)
        {
            if (item == null) return;

            var player = GameNetworkManager.Instance?.localPlayerController;
            if (player == null) return;

            Vector3 shipLocalPos = _shipInterior.InverseTransformPoint(worldPos);

            item.targetFloorPosition = shipLocalPos;
            item.transform.position = worldPos;
            item.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
            item.fallTime = 1f;

            player.SetObjectAsNoLongerHeld(
                droppedInElevator: true,
                droppedInShipRoom: true,
                targetFloorPosition: shipLocalPos,
                dropObject: item,
                floorYRot: yRot
            );

            var rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                StartCoroutine(ReEnablePhysics(rb));
            }
        }

        private System.Collections.IEnumerator ReEnablePhysics(Rigidbody rb)
        {
            yield return new WaitForFixedUpdate();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}