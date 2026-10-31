using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Extra inventory rows, layered on vanilla's own row system (Player.SetInventorySize / "invrows").
    // Safety rules:
    //  * The inventory is vanilla rows + our extra rows; the save keeps storing the VANILLA row count.
    //  * Just before the character is saved, items in our rows move into free vanilla cells, tagged with
    //    their slot in the item's own data; right after the save they move back. The file on disk thus
    //    has everything inside vanilla's rows: a login without the mod shows it all, and vanilla's
    //    "drop items outside the inventory" at login finds nothing to drop.
    //  * Loading with the mod: after vanilla applies its rows, the inventory grows and tagged items go
    //    back to their slots. Unloading packs the same way and shrinks without dropping anything.
    //  * Every step only changes an item's grid position. Nothing is created, removed or dropped here.
    internal static class ExtraRows
    {
        internal const string Tag = "jatify.slot";
        internal static ConfigEntry<int> Extra;
        internal static int Base = 4;                 // vanilla rows, as vanilla last set them
        private static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> Items = AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");
        private static readonly Action<Inventory> Changed = inv => AccessTools.Method(typeof(Inventory), "Changed").Invoke(inv, new object[] { false, false });

        internal static void Bind(ConfigFile cfg)
        {
            Extra = cfg.Bind("Inventory", "ExtraRows", 2, new ConfigDescription(
                "Rows Jatify UI adds under Valheim's own inventory rows. Their items are saved inside the vanilla rows, so the character stays safe without the mod.", new AcceptableValueRange<int>(0, 4)));
            Extra.SettingChanged += OnExtraChanged;
        }

        internal static void Unbind() { if (Extra != null) Extra.SettingChanged -= OnExtraChanged; _applied = -1; }

        // The setting changed in game (the settings page): move every row's items to the new layout, then resize.
        //  * Food / Tools / Meads and the worn-armour row keep their cells, in their new rows.
        //  * Items in extra rows that go away move to free, unlocked normal cells.
        //  * Not enough room: the change is refused (the setting goes back) and nothing moves. Nothing is dropped.
        private static bool _busy;
        private static void OnExtraChanged(object _, EventArgs __)
        {
            var p = Player.m_localPlayer;
            if (_busy || p == null || _applied < 0 || Extra.Value == _applied || SlotHost.Other) return;   // AzuEPI: its own Extra Inventory Rows
            var inv = p.GetInventory(); int oldStart = Base + _applied, newStart = Base + Extra.Value, bands = SpecialRows.Rows;
            int newH = newStart + bands;
            var moves = new List<(ItemDrop.ItemData it, Vector2i to)>();
            var leaving = new List<ItemDrop.ItemData>();
            foreach (var it in Items(inv))
            {
                int y = it.m_gridPos.y;
                if (y >= oldStart && y < oldStart + bands) moves.Add((it, new Vector2i(it.m_gridPos.x, newStart + (y - oldStart))));   // reserved rows keep their cells
                else if (y >= newStart && y < oldStart) leaving.Add(it);                                                                // extra rows that go away
            }
            // free normal cells in the new layout (bag rows first, hotbar last), not locked, not taken by an item that stays
            var taken = new HashSet<Vector2i>();
            foreach (var it in Items(inv)) if (it.m_gridPos.y < newStart && it.m_gridPos.y < oldStart && !leaving.Contains(it)) taken.Add(it.m_gridPos);
            var free = new List<Vector2i>();
            for (int n = 1; n <= newStart && free.Count < leaving.Count; n++)
            {
                int y = n < newStart ? n : 0;
                for (int x = 0; x < inv.GetWidth() && free.Count < leaving.Count; x++)
                {
                    var c = new Vector2i(x, y);
                    if (!taken.Contains(c) && !InvActions.Locked(x, y)) free.Add(c);
                }
            }
            if (free.Count < leaving.Count)
            {
                int need = leaving.Count - free.Count;
                Log.Warn($"extra rows: {need} item(s) in the rows to remove have no free cell; keeping {_applied} extra rows");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Make room for {need} more item(s) before removing rows", 0, Kit.Icon("box"));
                _busy = true; Extra.Value = _applied; _busy = false;
                return;
            }
            for (int i = 0; i < leaving.Count; i++) moves.Add((leaving[i], free[i]));
            if (newH > inv.GetHeight()) inv.SetHeight(newH);                  // grow first so every target cell exists
            foreach (var (it, to) in moves) it.m_gridPos = to;
            inv.SetHeight(newH);
            InventoryGui.instance?.SetInventorySize(newH);
            _applied = Extra.Value;
            Changed(inv);
            Log.Info($"extra rows: now {_applied}; {moves.Count} item(s) moved with their rows");
        }

        // After a (re)load while a player is already in the world.
        internal static void Apply()
        {
            var p = Player.m_localPlayer; if (p == null || SlotHost.Other) return;   // AzuEPI owns the grid
            var inv = p.GetInventory();
            Base = Mathf.Clamp(inv.GetHeight(), 1, 9);
            if (p.TryGetUniqueKeyValue("invrows", out var v) && int.TryParse(v, out var r)) Base = Mathf.Clamp(r, 1, 9);
            Grow(p);
        }

        private static int _applied = -1;     // the Extra value the inventory is laid out for

        private static void Grow(Player p)
        {
            var inv = p.GetInventory(); _applied = Extra.Value;
            inv.SetHeight(Base + Extra.Value + SpecialRows.Rows);
            InventoryGui.instance?.SetInventorySize(Base + Extra.Value + SpecialRows.Rows);
            int back = Unpack(inv);
            int rescued = Rescue(inv);
            if (back > 0 || rescued > 0) { Changed(inv); Log.Info($"extra rows: {back} item(s) back in their slots" + (rescued > 0 ? $", {rescued} rescued from outside the grid" : "")); }
        }

        // Items outside the grid (rows that no longer exist, e.g. fewer extra rows than when they were placed), or in
        // a reserved row that does not take them:
        // into a free unlocked normal cell, else a reserved cell that takes them. Valheim would drop them at login.
        internal static int Rescue(Inventory inv)
        {
            int n = 0, w = inv.GetWidth(), h = inv.GetHeight();
            foreach (var it in Items(inv).ToArray())
            {
                int band = it.m_gridPos.y - SpecialRows.Start;
                bool inGrid = it.m_gridPos.x < w && it.m_gridPos.y < h && it.m_gridPos.x >= 0 && it.m_gridPos.y >= 0;
                bool wrongBand = inGrid && band >= 0 && band < SpecialRows.Count && (!SpecialRows.Accepts(band, it) || it.m_gridPos.x >= SpecialRows.RowWidth(band));   // left there by an older layout (6 food / mead slots before)
                if (inGrid && !wrongBand) continue;
                Vector2i cell = new Vector2i(-1, -1);
                for (int k = 1; k <= SpecialRows.Start && cell.x < 0; k++)
                {   // the bag rows first, the 1-8 bar last (it was getting the food from a closed slot)
                    int y = k % SpecialRows.Start;
                    for (int x = 0; x < w && cell.x < 0; x++) if (inv.GetItemAt(x, y) == null && !InvActions.Locked(x, y)) cell = new Vector2i(x, y);
                }
                for (int b = 0; b < SpecialRows.Count && cell.x < 0; b++)
                    for (int x = 0; x < SpecialRows.RowWidth(b) && cell.x < 0; x++) if (inv.GetItemAt(x, SpecialRows.Start + b) == null && SpecialRows.Accepts(b, it)) cell = new Vector2i(x, SpecialRows.Start + b);
                if (cell.x < 0 && wrongBand && band != 1 && it.m_gridPos.x >= SpecialRows.RowWidth(band) && Player.m_localPlayer is Player me && inv == me.GetInventory())
                {   // a food / mead slot that closed, the bag full: on the ground at your feet (user), never kept out of sight
                    Log.Info($"extra rows: {it.m_shared.m_name} x{it.m_stack} from a closed {(band == 0 ? "food" : "mead")} slot, bag full: dropped");
                    if (me.DropItem(inv, it, it.m_stack)) n++;
                    continue;
                }
                if (cell.x < 0) { if (!inGrid) Log.Warn($"extra rows: no room for {it.m_shared.m_name} outside the grid; it stays in the inventory list"); continue; }   // misplaced but visible: waits for room
                it.m_gridPos = cell; n++;
            }
            return n;
        }

        // Before unload: everything into vanilla rows, then the vanilla size (nothing dropped).
        internal static void Remove()
        {
            var p = Player.m_localPlayer; if (p == null || SlotHost.Other) return;   // AzuEPI: shrinking would cut off its rows
            var inv = p.GetInventory();
            int left = Pack(inv);
            inv.SetHeight(Base);
            InventoryGui.instance?.SetInventorySize(Base);
            Changed(inv);
            if (left > 0) Log.Warn($"extra rows: {left} item(s) did not fit in the vanilla rows and stay where they are");
        }

        // Items in our rows -> free vanilla cells (bottom rows first, the hotbar row last), tagged.
        // Returns how many could not be moved (no free cell).
        internal static int Pack(Inventory inv)
        {
            int left = 0;
            foreach (var it in Items(inv))
            {
                if (it.m_gridPos.y < Base) continue;
                var cell = FreeVanillaCell(inv);
                if (cell.x < 0) { left++; continue; }
                it.m_customData[Tag] = it.m_gridPos.x + "," + it.m_gridPos.y;
                it.m_gridPos = cell;
            }
            return left;
        }

        // Tagged items -> their slot, when that slot is inside the inventory and free.
        internal static int Unpack(Inventory inv)
        {
            int moved = 0;
            foreach (var it in Items(inv))
            {
                if (!it.m_customData.TryGetValue(Tag, out var s)) continue;
                it.m_customData.Remove(Tag);
                var parts = s.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y)) continue;
                if (x < 0 || y < 0 || x >= inv.GetWidth() || y >= inv.GetHeight() || inv.GetItemAt(x, y) != null) continue;
                it.m_gridPos = new Vector2i(x, y); moved++;
            }
            return moved;
        }

        private static Vector2i FreeVanillaCell(Inventory inv)
        {
            for (int y = Base - 1; y >= 1; y--)
                for (int x = inv.GetWidth() - 1; x >= 0; x--)
                    if (inv.GetItemAt(x, y) == null) return new Vector2i(x, y);
            for (int x = inv.GetWidth() - 1; x >= 0; x--)
                if (inv.GetItemAt(x, 0) == null) return new Vector2i(x, 0);
            return new Vector2i(-1, -1);
        }

        internal static int InOurRows(Inventory inv) { int n = 0; foreach (var it in Items(inv)) if (it.m_gridPos.y >= Base) n++; return n; }
    }

    // A brand-new character: Valheim never resizes its inventory at login, so it played its whole first session without the
    // extra rows and the Tools, Food and Meads rows (found in the 0.3.0 release tests). On spawn, a grid shorter than vanilla
    // + ours gets them, as a reload would; one that has them is left alone.
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class SpawnRowsPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || SlotHost.Other) return;   // AzuEPI sizes the grid
            int rows = __instance.TryGetUniqueKeyValue("invrows", out var v) && int.TryParse(v, out var r) ? Mathf.Clamp(r, 1, 9) : 4;
            if (__instance.GetInventory().GetHeight() >= rows + ExtraRows.Extra.Value + SpecialRows.Rows) return;
            __instance.AddUniqueKeyValue("invrows", rows.ToString());
            ExtraRows.Apply();
            Log.Info($"extra rows: a new character's inventory grown to {__instance.GetInventory().GetHeight()} rows");
        }
    }

    // Vanilla's resize (login, console "inventorysize"): keep its row count in the save, give the
    // inventory vanilla + extra rows, then put tagged items back.
    [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
    internal static class SetInventorySizePatch
    {
        private static bool Prefix(Player __instance, int rows)
        {
            if (Player.m_localPlayer != null && __instance != Player.m_localPlayer) return true;
            rows = Mathf.Clamp(rows, 0, 9);
            ExtraRows.Base = rows;
            if (SlotHost.Other) return true;   // AzuEPI sizes the grid (vanilla + its rows + its equipment rows)
            __instance.AddUniqueKeyValue("invrows", rows.ToString());
            var inv = __instance.GetInventory();
            inv.SetHeight(rows + ExtraRows.Extra.Value + SpecialRows.Rows);
            InventoryGui.instance?.SetInventorySize(rows + ExtraRows.Extra.Value + SpecialRows.Rows);
            __instance.DropInvalidItems();             // vanilla's rule, now only beyond OUR rows
            ExtraRows.Unpack(inv);
            return false;
        }
    }

    // Saving the character: pack before, unpack after (the save itself sees only vanilla rows).
    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class PlayerSavePatch
    {
        private static void Prefix(Player __instance, out bool __state)
        {
            __state = false;
            if (__instance != Player.m_localPlayer || SlotHost.Other) return;   // AzuEPI saves its own layout
            var inv = __instance.GetInventory();
            int before = ExtraRows.InOurRows(inv);
            if (before == 0) return;
            int left = ExtraRows.Pack(inv);
            __state = true;
            Log.Info($"save: {before - left} item(s) from the extra rows saved inside the vanilla rows" + (left > 0 ? $", {left} did not fit" : ""));
            if (left > 0) MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"{left} item(s) in Jatify UI's extra rows have no free vanilla slot. Without the mod they would drop at login: make room.");
        }

        private static void Postfix(Player __instance, bool __state)
        {
            if (__state) ExtraRows.Unpack(__instance.GetInventory());
        }
    }
}
