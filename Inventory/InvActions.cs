using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Inventory actions from the design header and slot locks:
    //  * To chests / Restock: chests within range that nobody else has open; ownership is claimed first;
    //    each whole stack moves with Valheim's own AddItem and leaves the source only if that succeeded.
    //  * Merge: tops up partial stacks of the same item, quality and world level.
    //  * Sort: by category then name, into the free unlocked cells of the main rows (not the hotbar).
    //  * Locks: Alt + click toggles; saved with the character (Player.m_customData "jatify.locks").
    //    Locked cells are skipped by every action and by auto-placement.
    internal static class InvActions
    {
        internal const string LockKey = "jatify.locks";
        internal static readonly HashSet<int> Locks = new HashSet<int>();
        private static Player _locksFor;
        private static readonly AccessTools.FieldRef<Container, ZNetView> NView = AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly System.Func<Container, long, bool> Access = AccessTools.MethodDelegate<System.Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        internal static string[] SortModes = { "Category", "Name", "Weight" };
        internal static int SortMode;

        internal static bool Locked(int x, int y) { Sync(); return Locks.Contains(y * 128 + x); }

        private static void Sync()
        {
            var p = Player.m_localPlayer;
            if (p == _locksFor) return;
            _locksFor = p; Locks.Clear();
            if (p != null && p.m_customData.TryGetValue(LockKey, out var s))
                foreach (var part in s.Split(','))
                {
                    var xy = part.Split('.');
                    if (xy.Length == 2 && int.TryParse(xy[0], out var x) && int.TryParse(xy[1], out var y)) Locks.Add(y * 128 + x);
                }
        }

        internal static void ToggleLock(int x, int y)
        {
            Sync(); var p = Player.m_localPlayer; if (p == null) return;
            int k = y * 128 + x; if (!Locks.Remove(k)) Locks.Add(k);
            p.m_customData[LockKey] = string.Join(",", Locks.Select(v => (v % 128) + "." + (v / 128)));
        }

        // cells an action may take items from / put items into: main rows, not the hotbar, not locked, not reserved
        private static bool Free(Inventory inv, ItemDrop.ItemData it, bool hotbar = false)
            => it != null && !it.m_equipped && (hotbar || it.m_gridPos.y >= 1) && it.m_gridPos.y < SpecialRows.Start && !Locked(it.m_gridPos.x, it.m_gridPos.y);

        private static List<Container> Nearby(Player p)
        {
            var list = new List<Container>(); float r = Cfg.StackRange.Value;
            foreach (var c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (c == null || c.GetInventory() == null || (c.transform.position - p.transform.position).sqrMagnitude > r * r) continue;
                var nv = NView(c); if (nv == null || !nv.IsValid()) continue;
                if (!Access(c, p.GetPlayerID()) || !Stock.Ward(c)) continue;   // the chest's own lock and the ward, like opening it
                if (c.GetComponentInParent<Ship>() != null || c.GetComponentInParent<Vagon>() != null) continue; // not carts and ships
                if (!nv.IsOwner()) { if (c.IsInUse()) continue; nv.ClaimOwnership(); }
                list.Add(c);
            }
            return list;
        }

        private static int Move(Inventory from, Inventory to, List<ItemDrop.ItemData> items)
        {
            int n = 0;
            foreach (var it in items) if (to.AddItem(it)) { from.RemoveItem(it); n++; }
            return n;
        }

        internal static int ToChests(Player p)
        {
            var inv = p.GetInventory(); int n = 0;
            foreach (var c in Nearby(p).OrderBy(x => ChestAssign.Of(x).Length > 0 ? 0 : 1))   // assigned chests get their item first
            {
                var names = new HashSet<string>(c.GetInventory().GetAllItems().Select(i => i.m_shared.m_name));
                var assigned = ChestAssign.SharedName(c); if (assigned != null) names.Add(assigned);   // an assigned chest takes its item even when empty
                n += Move(inv, c.GetInventory(), inv.GetAllItems().Where(i => Free(inv, i) && names.Contains(i.m_shared.m_name) && ChestAssign.Allows(c, i)).ToList());
            }
            return n;
        }

        internal static int Restock(Player p)
        {
            var inv = p.GetInventory(); int n = 0;
            var mine = new HashSet<string>(inv.GetAllItems().Where(i => i.m_shared.m_maxStackSize > 1).Select(i => i.m_shared.m_name));
            foreach (var c in Nearby(p))
                n += Move(c.GetInventory(), inv, c.GetInventory().GetAllItems().Where(i => mine.Contains(i.m_shared.m_name)).ToList());
            return n;
        }

        internal static bool Merge(Player p)
        {
            var inv = p.GetInventory(); bool changed = false;
            var c = inv.GetAllItems().Where(i => Free(inv, i, true) && i.m_shared.m_maxStackSize > 1).ToList();
            for (int a = 0; a < c.Count; a++)
                for (int b = 0; b < c.Count && c[a] != null && c[a].m_stack < c[a].m_shared.m_maxStackSize; b++)
                {
                    if (a == b || c[b] == null || c[b].m_shared.m_name != c[a].m_shared.m_name || c[b].m_quality != c[a].m_quality || c[b].m_worldLevel != c[a].m_worldLevel) continue;
                    int k = Mathf.Min(c[a].m_shared.m_maxStackSize - c[a].m_stack, c[b].m_stack);
                    c[a].m_stack += k; c[b].m_stack -= k; changed |= k > 0;
                    if (c[b].m_stack <= 0) { inv.RemoveItem(c[b]); c[b] = null; }
                }
            return changed;
        }

        internal static void Sort(Player p)
        {
            Merge(p);
            var inv = p.GetInventory();
            var items = inv.GetAllItems().Where(i => Free(inv, i)).ToList();
            var cells = new List<Vector2i>();
            for (int y = 1; y < SpecialRows.Start; y++)
                for (int x = 0; x < inv.GetWidth(); x++)
                {
                    if (Locked(x, y)) continue;
                    var at = inv.GetItemAt(x, y);
                    if (at == null || items.Contains(at)) cells.Add(new Vector2i(x, y));
                }
            IEnumerable<ItemDrop.ItemData> sorted = SortMode == 1 ? items.OrderBy(i => Localization.instance.Localize(i.m_shared.m_name))
                : SortMode == 2 ? items.OrderByDescending(i => i.GetWeight())
                : items.OrderBy(i => Kind(i)).ThenBy(i => Localization.instance.Localize(i.m_shared.m_name));
            int k = 0;
            foreach (var it in sorted) it.m_gridPos = cells[k++];   // same number of cells as items: only positions change
            AccessTools.Method(typeof(Inventory), "Changed").Invoke(inv, new object[] { false, false });
        }

        private static int Kind(ItemDrop.ItemData i)
        {
            var k = i.m_shared.m_itemType;
            if (k == ItemDrop.ItemData.ItemType.OneHandedWeapon || k == ItemDrop.ItemData.ItemType.TwoHandedWeapon || k == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || k == ItemDrop.ItemData.ItemType.Bow || k == ItemDrop.ItemData.ItemType.Shield || k == ItemDrop.ItemData.ItemType.Ammo) return 0;
            if (k == ItemDrop.ItemData.ItemType.Helmet || k == ItemDrop.ItemData.ItemType.Chest || k == ItemDrop.ItemData.ItemType.Legs || k == ItemDrop.ItemData.ItemType.Shoulder || k == ItemDrop.ItemData.ItemType.Utility || k == ItemDrop.ItemData.ItemType.Trinket) return 1;
            if (k == ItemDrop.ItemData.ItemType.Tool || k == ItemDrop.ItemData.ItemType.Torch) return 2;
            if (k == ItemDrop.ItemData.ItemType.Consumable) return 3;
            return 4;
        }

        internal static void Say(string s) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, s);
    }

    // Alt + left click on a player slot toggles its lock (instead of picking the item up).
    [HarmonyPatch(typeof(InventoryGrid), "OnLeftDown")]
    internal static class LockClickPatch
    {
        private static bool Prefix(InventoryGrid __instance, UIInputHandler clickHandler)
        {
            // Valheim's own input: Unity's legacy Input missed Alt on the second click
            if (!(ZInput.GetKey(KeyCode.LeftAlt, true) || ZInput.GetKey(KeyCode.RightAlt, true))) return true;
            var el = clickHandler.GetComponentInParent<InventoryElement>(); if (el == null) return true;
            var open = ChestPanel.Open;
            if (open != null && __instance.GetInventory() == open.GetInventory()) { ChestPanel.ToggleLock(el.Position.x, el.Position.y); return false; }   // chest slots: locks live on the chest
            if (!SpecialRows.IsPlayer(__instance.GetInventory())) return true;
            if (SpecialRows.Is(__instance.GetInventory(), el.Position.y)) return false;   // reserved rows are not lockable
            InvActions.ToggleLock(el.Position.x, el.Position.y);
            return false;
        }
    }

    // Auto-placement skips locked empty cells too.
    [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
    internal static class LockedEmptySlotPatch
    {
        [HarmonyPriority(Priority.Low)]   // after the reserved-rows postfix
        private static void Postfix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            if (__result.x < 0 || !SpecialRows.IsPlayer(__instance) || !InvActions.Locked(__result.x, __result.y)) return;
            int top = SpecialRows.Start; __result = new Vector2i(-1, -1);
            for (int n = 0; n < top; n++)
            {
                int y = topFirst ? n : top - 1 - n;
                for (int x = 0; x < __instance.GetWidth(); x++)
                    if (__instance.GetItemAt(x, y) == null && !InvActions.Locked(x, y)) { __result = new Vector2i(x, y); return; }
            }
        }
    }
}
