using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Configuration;
using UnityEngine;

namespace Jatify.UI
{
    // AzuExtendedPlayerInventory (AzuEPI): when it is installed it owns the player inventory (its rows, its equipment
    // and quick-slot cells at the bottom of the same grid, the save layout) and Jatify UI only draws it. Our own rows
    // (extra rows, Food / Tools / Meads, the hidden armour row) and everything that moves items for them stand aside:
    // two mods reshaping one grid drop items at login. Soft: its public API by reflection, nothing hard-referenced.
    internal static class Azu
    {
        internal const string Guid = "Azumatt.AzuExtendedPlayerInventory";
        private static bool? _on; private static MethodInfo _addedRows; private static ConfigEntryBase _eqRow, _extraRows, _oneUtility, _quickHud;
        internal static int ExtraRows { get { if (_on == null) Bind(); return _extraRows?.BoxedValue is int n ? n : 0; } }   // its Extra Inventory Rows
        private static int _rowsFrame = -1, _rows;

        internal static bool Drives { get { if (_on == null) Bind(); return _on ?? false; } }

        private static void Bind()
        {
            _on = false;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(Guid, out var info)) { if (!Epic.Started) _on = null; return; }   // plugins still loading
            if (info.Instance == null) { _on = null; return; }
            _on = true;   // installed: its grid, whether or not the API below binds (we never reshape it)
            var api = info.Instance.GetType().Assembly.GetType("AzuEPI.API");
            _addedRows = api?.GetMethod("GetAddedRows", new[] { typeof(int) });
            foreach (var kv in info.Instance.Config)
            {
                if (kv.Key.Section == "2 - Inventory" && kv.Key.Key == "Enable Equipment Row") _eqRow = kv.Value;
                if (kv.Key.Section == "2 - Inventory" && kv.Key.Key == "Extra Inventory Rows") _extraRows = kv.Value;
                if (kv.Key.Key == "One Utility Item At A Time") _oneUtility = kv.Value;
                if (kv.Key.Key == "Show Quick Slots on HUD") _quickHud = kv.Value;
            }
            UtilityRule();
            Log.Info($"AzuEPI {info.Metadata.Version} found: it owns the inventory rows and slots; Jatify UI's own rows stand aside"
                + (_addedRows == null ? " (its API has no GetAddedRows: every row counts as a normal row)" : ""));
        }

        // our UtilityTogether drives its One Utility Item At A Time (a server that syncs its config overrides it, as with any of its settings)
        internal static void UtilityRule()
        {
            if (_on != true || _oneUtility == null) return;
            try { var want = Enum.Parse(_oneUtility.SettingType, Cfg.UtilityTogether.Value ? "Off" : "On"); if (!Equals(_oneUtility.BoxedValue, want)) _oneUtility.BoxedValue = want; }
            catch (Exception e) { Log.Warn("AzuEPI One Utility Item At A Time: " + e.Message); }
        }

        // which of its slots a grid cell is (refreshed every 2 s: its slot set can change with its config)
        internal struct Slot { internal bool Known, Armour, Quick; internal string Name, Orig; }
        private static readonly string[] ArmourSlots = { "$azu_epi_helmet", "$azu_epi_chest", "$azu_epi_legs", "$azu_epi_shoulder", "$azu_epi_utility", "$azu_epi_trinket" };
        private static readonly System.Collections.Generic.Dictionary<int, Slot> _slots = new System.Collections.Generic.Dictionary<int, Slot>();
        private static MethodInfo _atPos, _desc; private static float _slotsAt;
        internal static Slot SlotAt(Inventory inv, int x, int y)
        {
            if (!Drives || inv == null) return default;
            if (Time.unscaledTime - _slotsAt > 2f) { _slotsAt = Time.unscaledTime; _slots.Clear(); }
            int key = y * 64 + x; if (_slots.TryGetValue(key, out var have)) return have;
            var r = default(Slot);
            try
            {
                if (_atPos == null) { var api = AccessTools.TypeByName("AzuEPI.API"); _atPos = AccessTools.Method(api, "TryGetSlotIndexAtGridPos"); _desc = AccessTools.Method(api, "TryGetSlotDescriptor", new[] { typeof(int), AccessTools.TypeByName("AzuEPI.SlotDescriptor").MakeByRefType() }); }
                var a = new object[] { inv, new Vector2i(x, y), 0 };
                if (_atPos != null && (bool)_atPos.Invoke(null, a))
                {
                    var d = new object[] { a[2], null };
                    if (_desc != null && (bool)_desc.Invoke(null, d) && d[1] != null)
                    {
                        var t = Traverse.Create(d[1]); var orig = t.Property("OriginalName").GetValue<string>() ?? "";
                        r = new Slot { Known = true, Quick = t.Property("IsQuickSlot").GetValue<bool>(), Armour = Array.IndexOf(ArmourSlots, orig) >= 0, Orig = orig, Name = Localization.instance.Localize(t.Property("Name").GetValue<string>() ?? "") };
                    }
                }
            }
            catch (Exception e) { if (_slots.Count == 0) Log.Warn("AzuEPI slot lookup: " + e.Message); }
            return _slots[key] = r;
        }

        // our doll cell that stands for one of its slots (its equipment cells sit there, worn or not), or null
        internal static string DollSlot(Slot s)
        {
            if (!s.Known) return null;
            switch (s.Orig)
            {
                case "$azu_epi_helmet": return "Head"; case "$azu_epi_chest": return "Chest"; case "$azu_epi_legs": return "Legs"; case "$azu_epi_shoulder": return "Cape";
                case "$azu_epi_utility": return "Belt"; case "$azu_epi_trinket": return "Trinket"; case "$item_wishbone": return "Wishbone"; case "$item_demister": return "Wisplight";
                default: return null;
            }
        }
        internal static readonly string[] DollNames = { "Head", "Chest", "Legs", "Cape", "Belt", "Trinket" };   // our doll cells its own cells replace

        // its favourites (Alt + click an item name, Alt + right click a slot): what our movers must leave alone, as our locks.
        // Its own unless AzuAutoStore or Quick Stack Store is installed (they own favourites then). By reflection, bound once.
        internal static bool Favouriting => Drives && !BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("Azumatt.AzuAutoStore") && !BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("goldenrevolver.quick_stack_store");
        private static MethodInfo _favCfg, _favSlot, _favItem; private static bool _favBound; private static object _favFor; private static long _favId;
        internal static bool Favourite(Inventory inv, int x, int y)
        {
            var p = Player.m_localPlayer; if (p == null || inv == null || !Favouriting) return false;
            if (!_favBound)
            {
                _favBound = true; var t = AccessTools.TypeByName("AzuEPI.Game.Favoriting.UserConfig");
                _favCfg = AccessTools.Method(t, "GetPlayerConfig"); _favSlot = AccessTools.Method(t, "IsSlotFavorited"); _favItem = AccessTools.Method(t, "IsItemNameOrSlotFavorited");
                if (_favCfg == null || _favSlot == null || _favItem == null) Log.Warn("AzuEPI favourites: not found, only Jatify's locks keep items in place");
            }
            if (_favCfg == null || _favSlot == null || _favItem == null) return false;
            try
            {
                if (_favFor == null || _favId != p.GetPlayerID()) { _favId = p.GetPlayerID(); _favFor = _favCfg.Invoke(null, new object[] { _favId }); }
                var it = inv.GetItemAt(x, y);
                return it != null ? (bool)_favItem.Invoke(_favFor, new object[] { it }) : (bool)_favSlot.Invoke(_favFor, new object[] { new Vector2i(x, y) });
            }
            catch (Exception e) { Log.Warn("AzuEPI favourites: " + e.Message); _favCfg = null; return false; }
        }

        internal static bool QuickOnHud => _quickHud == null || _quickHud.BoxedValue?.ToString() != "Off";   // its Show Quick Slots on HUD (our tools bar shows them)

        // its equipment and quick-slot cells sit in rows at the bottom of the grid (when its equipment row is on)
        internal static bool EquipmentRow => _eqRow == null || _eqRow.BoxedValue?.ToString() == "On";

        // the rows that are the bag (the action bar row included): above AzuEPI's equipment / quick-slot rows. Once a frame.
        internal static int NormalRows
        {
            get
            {
                if (_rowsFrame == Time.frameCount) return _rows;
                _rowsFrame = Time.frameCount;
                var p = Player.m_localPlayer; if (p == null) return _rows = 4;
                var inv = p.GetInventory(); int added = 0;
                if (EquipmentRow && _addedRows != null) try { added = (int)_addedRows.Invoke(null, new object[] { inv.GetWidth() }); } catch (Exception e) { Log.Warn("AzuEPI GetAddedRows: " + e.Message); _addedRows = null; }
                return _rows = Mathf.Max(1, inv.GetHeight() - added);
            }
        }
    }

    // A character from Jatify UI alone, loaded with AzuEPI for the first time: AzuEPI's resize moves every item below
    // Valheim's rows into its equipment area (or, with no room, drops it). Ours were saved inside Valheim's rows
    // (tagged); only items that did not fit then are still below. Just before AzuEPI's resize: tags off, those items
    // into free bag cells, and with no room into a hold kept in the character's save, released as room appears.
    // Never dropped.
    internal static class AzuHold
    {
        internal const string Key = "jatify.hold", AzuLayout = "AzuEPI.SlotLayout";
        private static Inventory _hold; private static Player _for; private static float _next, _said;

        internal static Inventory For(Player p)
        {
            if (p == null) return null;
            if (_for == p && _hold != null) return _hold;
            _for = p; _hold = new Inventory("JatifyHold", null, 8, 8);
            if (p.m_customData.TryGetValue(Key, out var data) && data.Length > 0)
                try { _hold.Load(new ZPackage(data)); } catch (Exception e) { Log.Warn("hold: the saved hold could not be read, left as it is: " + e.Message); _hold = null; _for = null; return null; }
            return _hold;
        }

        internal static void Save(Player p)
        {
            if (p == null || p != _for || _hold == null) return;
            if (_hold.NrOfItems() == 0) { p.m_customData.Remove(Key); return; }
            var pkg = new ZPackage(); _hold.Save(pkg); p.m_customData[Key] = Convert.ToBase64String(pkg.GetArray());
        }

        // once per character and session, just before AzuEPI lays the loaded inventory out (its slot remap reads every item
        // below the bag rows as one of its slots, and parks what doesn't fit where it is later dropped). rows: the bag rows
        // as AzuEPI counts them then (Valheim's rows + its Extra Inventory Rows)
        private static readonly System.Collections.Generic.HashSet<long> _done = new System.Collections.Generic.HashSet<long>();
        internal static void Migrate(Player p, int rows)
        {
            if (!Azu.Drives || p == null || !_done.Add(p.GetPlayerID())) return;
            var inv = p.GetInventory(); int tags = 0, moved = 0, held = 0;
            foreach (var it in inv.GetAllItems()) if (it.m_customData.Remove(ExtraRows.Tag)) tags++;
            if (!p.m_customData.Keys.Any(k => k.StartsWith("AzuEPI")))   // AzuEPI never saved this character: anything below the bag rows is ours (worn armour, rows it would misread)
            {
                // worn first: a worn item needs a bag cell to stay worn (Valheim equips it in this load and AzuEPI moves it into its
                // slot, freeing the cell). With the bag full, an unworn bag item makes way for it, into the hold.
                foreach (var it in inv.GetAllItems().Where(i => i.m_gridPos.y >= rows).OrderBy(i => i.m_equipped ? 0 : 1).ToArray())
                {
                    var cell = Free(inv, rows);
                    if (cell.x < 0 && it.m_equipped) { var make = MakeWay(p, inv, rows); if (make.x >= 0) { cell = make; held++; } }
                    if (cell.x >= 0) { it.m_gridPos = cell; moved++; continue; }   // worn stays worn
                    if (Hold(p, it)) { inv.RemoveItem(it); held++; }
                }
                // our Wishbone / Wisplight slots: into the bag, worn (so AzuEPI's own slots take them); rings and keys into the bag or the hold
                var store = ModSlots.For(p);
                if (store != null) foreach (var it in store.GetAllItems().Where(i => i.m_gridPos.y == 0).OrderBy(i => i.m_gridPos.x).ToArray())
                {
                    bool wear = Array.IndexOf(ModSlots.Utility, it.m_shared.m_name) >= 0;
                    var cell = Free(inv, rows);
                    if (cell.x < 0 && wear) { var make = MakeWay(p, inv, rows); if (make.x >= 0) { cell = make; held++; } }
                    it.m_equipped = wear;
                    if (cell.x >= 0 && inv.AddItem(it, cell)) { store.RemoveItem(it); moved++; }   // into the bag first, out of the store only then
                    else { it.m_equipped = false; if (Hold(p, it)) { store.RemoveItem(it); held++; } }
                }
            }
            if (tags + moved + held > 0) Log.Info($"AzuEPI: first load of this character with it: {tags} save tag(s) removed, {moved} item(s) moved into the bag, {held} into the hold");
        }

        // a full bag: its last unworn, unlocked item goes into the hold, and its cell is free
        private static Vector2i MakeWay(Player p, Inventory inv, int rows)
        {
            var out_ = inv.GetAllItems().Where(i => !i.m_equipped && i.m_gridPos.y >= 1 && i.m_gridPos.y < rows && !InvActions.Locked(i.m_gridPos.x, i.m_gridPos.y))
                .OrderByDescending(i => i.m_gridPos.y).ThenByDescending(i => i.m_gridPos.x).FirstOrDefault();
            if (out_ == null) return new Vector2i(-1, -1);
            var cell = out_.m_gridPos;
            if (!Hold(p, out_)) return new Vector2i(-1, -1);
            inv.RemoveItem(out_);
            return cell;
        }

        private static bool Hold(Player p, ItemDrop.ItemData it) { var h = For(p); if (h == null) return false; it.m_equipped = false; return h.AddItem(it); }

        private static Vector2i Free(Inventory inv, int rows)
        {
            for (int y = 1; y <= rows; y++) { int r = y % rows; for (int x = 0; x < inv.GetWidth(); x++) if (inv.GetItemAt(x, r) == null && !InvActions.Locked(x, r)) return new Vector2i(x, r); }   // bag rows, the action bar last
            return new Vector2i(-1, -1);
        }

        // every 2 s: what waits in the hold goes into the bag as room appears; a notice while something waits
        internal static void Tick()
        {
            var p = Player.m_localPlayer; if (p == null || Time.unscaledTime < _next) return;   // releases even if AzuEPI is gone later
            _next = Time.unscaledTime + 2f;
            var hold = For(p); if (hold == null || hold.NrOfItems() == 0) return;
            var inv = p.GetInventory(); int rows = Azu.Drives ? Azu.NormalRows : SpecialRows.Start;
            foreach (var it in hold.GetAllItems().ToArray())
            {   // into a free bag cell by position: no pickup path, so nothing is equipped or swapped on its own
                var cell = Free(inv, rows); if (cell.x < 0) break;
                if (inv.AddItem(it, cell)) hold.RemoveItem(it);
            }
            int left = hold.NrOfItems();
            if (left > 0 && Time.unscaledTime - _said > 120f) { _said = Time.unscaledTime; MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"{left} item(s) wait in Jatify UI's hold: make room in your bag and they come back"); }
        }

        internal static void Remove() { var p = Player.m_localPlayer; if (p != null && p == _for) Save(p); _hold = null; _for = null; }
    }

    [HarmonyLib.HarmonyPatch(typeof(Player), "EquipInventoryItems")]
    internal static class AzuMigratePatch
    {
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First), HarmonyLib.HarmonyBefore(Azu.Guid)]
        private static void Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || !Azu.Drives) return;
            int vanilla = __instance.TryGetUniqueKeyValue("invrows", out var v) && int.TryParse(v, out var r) ? r : 4;
            AzuHold.Migrate(__instance, Mathf.Clamp(vanilla, 1, 9) + Azu.ExtraRows);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class AzuHoldSavePatch { private static void Prefix(Player __instance) { if (__instance == Player.m_localPlayer) AzuHold.Save(__instance); } }
}
