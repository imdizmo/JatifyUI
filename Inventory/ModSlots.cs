using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Wishbone + Wisplight slots and the key pouch (design's right column and key slot):
    // CustomStore way: a small Inventory of our own, saved in Player.m_customData, never in the bag.
    //  * Row 0: x0 Wishbone, x1 Wisplight. Their equip status effects are applied by hand and re-added
    //    after vanilla rebuilds the equipment effects (it drops ones it does not know).
    //  * Rows 1-3: a 4 x 3 key pouch. Keys found in the bag move in; doors accept pouch keys.
    //  * Death: just before the tombstone, everything moves into the bag, so the grave gets it like any
    //    other item (and world modifiers that keep or delete items apply to it as to the rest).
    //  * Without the mod the items stay in m_customData, unseen but kept; they come back with the mod.
    //  * Unload/reload writes the store to m_customData first. No path deletes an item; a move that fails
    //    leaves the item where it was.
    internal static class ModSlots
    {
        internal const string Key = "jatify.store";
        internal const int W = 4, H = 4, KeyRow = 1;
        internal static readonly string[] Utility = { "$item_wishbone", "$item_demister" };
        private static Inventory _inv;
        private static Player _for;
        private static readonly HashSet<StatusEffect> _applied = new HashSet<StatusEffect>();
        private static readonly AccessTools.FieldRef<Humanoid, SEMan> Seman = AccessTools.FieldRefAccess<Humanoid, SEMan>("m_seman");
        private static readonly AccessTools.FieldRef<Humanoid, HashSet<StatusEffect>> VanillaSe = AccessTools.FieldRefAccess<Humanoid, HashSet<StatusEffect>>("m_equipmentStatusEffects");
        private static HashSet<string> _keys;

        // the store for the local player, loaded from its save data on first use
        internal static Inventory For(Player p)
        {
            if (p == null) return null;
            if (_for == p && _inv != null) return _inv;
            _for = p; _applied.Clear();
            _inv = new Inventory("JatifySlots", null, W, H);
            if (p.m_customData.TryGetValue(Key, out var data) && data.Length > 0)
            {
                try { _inv.Load(new ZPackage(data)); }
                catch (Exception e) { Log.Warn("mod slots: the saved slots could not be read, left as they are: " + e.Message); _inv = null; _for = null; return null; }
            }
            _inv.m_onChanged = (Action)Delegate.Combine(_inv.m_onChanged, (Action)(() => Sync(_for)));   // vanilla drags items out too
            Sync(p);
            return _inv;
        }

        internal static ItemDrop.ItemData At(Player p, int x, int y) => For(p)?.GetItemAt(x, y);

        internal static bool Allows(int x, int y, ItemDrop.ItemData it)
        {
            if (it == null) return false;
            if (y == 0) return x < Utility.Length && it.m_shared.m_name == Utility[x];
            return y >= KeyRow && IsKey(it);
        }

        internal static bool IsKey(ItemDrop.ItemData it)
        {
            if (_keys == null && ZNetScene.instance != null)
            {   // every item some door asks for
                _keys = new HashSet<string>();
                foreach (var go in ZNetScene.instance.m_prefabs)
                {
                    var d = go ? go.GetComponentInChildren<Door>(true) : null;
                    if (d != null && d.m_keyItem != null) _keys.Add(d.m_keyItem.m_itemData.m_shared.m_name);
                }
            }
            return _keys != null && _keys.Contains(it.m_shared.m_name);
        }

        // bag -> slot. Added to the store first, removed from the bag only on success.
        internal static bool Put(Player p, ItemDrop.ItemData it, int x, int y)
        {
            var inv = For(p); var bag = p.GetInventory();
            if (inv == null || !Allows(x, y, it) || inv.GetItemAt(x, y) != null) return false;
            if (inv.ContainsItem(it)) { it.m_gridPos = new Vector2i(x, y); Sync(p); return true; }   // moved inside the store
            if (!bag.ContainsItem(it)) return false;
            if (p.IsItemEquiped(it)) p.UnequipItem(it, false);
            if (!inv.AddItem(it, new Vector2i(x, y))) return false;
            bag.RemoveItem(it);
            Sync(p);
            return true;
        }

        // slot -> bag. Vanilla's AddItem finds the cell; the store lets go only if that worked.
        internal static bool Take(Player p, int x, int y)
        {
            var inv = For(p); var it = inv?.GetItemAt(x, y);
            if (it == null || !p.GetInventory().AddItem(it)) return false;
            inv.RemoveItem(it);
            Sync(p);
            return true;
        }

        // Valheim drops whatever is in hand when it comes from another inventory and no chest is open
        // (InventoryGui.UpdateContainer), so an item lifted from our store first moves into a hidden cell of the
        // player's own inventory (eq row, last cell) and is lifted from there. Not placed anywhere: it goes back.
        private static readonly System.Reflection.MethodInfo SetupDrag = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> Carried = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        internal static Vector2i Stage => new Vector2i(7, SpecialRows.EqRow);
        private static ItemDrop.ItemData _staged; private static Vector2i _home;

        internal static bool PickUp(Player p, int x, int y)
        {
            var inv = For(p); var it = inv?.GetItemAt(x, y); var bag = p.GetInventory(); var g = InventoryGui.instance;
            if (it == null || g == null || bag.GetHeight() <= Stage.y || bag.GetItemAt(Stage.x, Stage.y) != null) return false;
            if (!bag.AddItem(it, Stage)) return false;   // into the player's inventory first; the store lets go only then
            inv.RemoveItem(it);
            _staged = it; _home = new Vector2i(x, y);
            Sync(p);
            SetupDrag.Invoke(g, new object[] { it, bag, it.m_stack });
            return true;
        }

        // every frame: a lifted item that was not placed anywhere goes back where it came from
        internal static void TickStaged()
        {
            if (_staged == null) return;
            var p = Player.m_localPlayer; var g = InventoryGui.instance;
            if (p == null) { _staged = null; return; }
            if (g != null && Carried(g) == _staged) return;                  // still in hand
            var bag = p.GetInventory(); var it = _staged; _staged = null;
            if (!bag.ContainsItem(it) || it.m_gridPos.x != Stage.x || it.m_gridPos.y != Stage.y) return;   // placed somewhere: done
            var inv = For(p); if (inv == null) return;
            var home = inv.GetItemAt(_home.x, _home.y) == null && Allows(_home.x, _home.y, it) ? _home : (IsKey(it) ? FreeKeyCell(inv) : new Vector2i(-1, -1));
            if (home.x < 0) return;                                            // no room: it stays in the hidden cell, still in the inventory
            if (inv.AddItem(it, home)) { bag.RemoveItem(it); Sync(p); }
        }

        internal static int KeyCount(Player p) { var inv = For(p); int n = 0; if (inv != null) foreach (var it in inv.GetAllItems()) if (it.m_gridPos.y >= KeyRow) n++; return n; }

        // a key picked up from the world lands in the bag; move it into a free pouch cell. Only on pickup,
        // so a key you drag out of the pouch stays where you put it.
        internal static void Collect(Player p, string name)
        {
            var inv = For(p); if (inv == null || name == null) return;
            foreach (var it in p.GetInventory().GetAllItems().ToArray())
            {
                if (it.m_shared.m_name != name || !IsKey(it) || p.IsItemEquiped(it)) continue;
                var cell = FreeKeyCell(inv); if (cell.x < 0) return;
                Put(p, it, cell.x, cell.y);
            }
        }

        private static Vector2i FreeKeyCell(Inventory inv)
        {
            for (int y = KeyRow; y < H; y++) for (int x = 0; x < W; x++) if (inv.GetItemAt(x, y) == null) return new Vector2i(x, y);
            return new Vector2i(-1, -1);
        }

        // status effects of what sits in row 0
        internal static void Sync(Player p)
        {
            if (p == null || _inv == null) return;
            var seman = Seman(p); var want = new HashSet<StatusEffect>();
            for (int x = 0; x < Utility.Length; x++) { var se = _inv.GetItemAt(x, 0)?.m_shared.m_equipStatusEffect; if (se != null) want.Add(se); }
            foreach (var se in want) if (!seman.HaveStatusEffect(se.NameHash())) seman.AddStatusEffect(se, false, 0, 0f);
            foreach (var se in _applied) if (!want.Contains(se) && !VanillaSe(p).Contains(se)) seman.RemoveStatusEffect(se.NameHash(), true);
            _applied.Clear(); _applied.UnionWith(want);
        }

        internal static void Save(Player p)
        {
            if (p == null || p != _for || _inv == null) return;
            if (_inv.NrOfItems() == 0) { p.m_customData.Remove(Key); return; }
            var pkg = new ZPackage(); _inv.Save(pkg);
            p.m_customData[Key] = Convert.ToBase64String(pkg.GetArray());
        }

        // just before the tombstone: into the bag so the grave takes it
        internal static void BeforeDeath(Player p)
        {
            var inv = For(p); if (inv == null || inv.NrOfItems() == 0) return;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathKeepInventory)) return;
            var bag = p.GetInventory();
            foreach (var it in inv.GetAllItems().ToArray())
            {
                it.m_customData[Home] = it.m_gridPos.x + "," + it.m_gridPos.y;   // its slot, for the grave pickup (Restore)
                if (!bag.AddItem(it))
                {   // bag full: on the ground beside the grave rather than lost
                    ItemDrop.DropItem(it, it.m_stack, p.transform.position + Vector3.up, p.transform.rotation);
                }
                inv.RemoveItem(it);
            }
            Sync(p);
            Save(p);   // the respawn save must not bring them back a second time
            Log.Info("mod slots: moved into the bag for the grave");
        }

        // grave picked up: what left the slots at death goes back to its slot (a key whose cell is taken: any free pouch cell)
        internal const string Home = "jatify.slot";
        internal static void Restore(Player p)
        {
            var inv = For(p); if (inv == null) return;
            foreach (var it in p.GetInventory().GetAllItems().Where(i => i.m_customData.ContainsKey(Home)).ToList())
            {
                var xy = it.m_customData[Home].Split(','); it.m_customData.Remove(Home);
                if (xy.Length != 2 || !int.TryParse(xy[0], out var x) || !int.TryParse(xy[1], out var y)) continue;
                if (inv.GetItemAt(x, y) == null && Put(p, it, x, y)) continue;
                if (IsKey(it)) { var cell = FreeKeyCell(inv); if (cell.x >= 0) Put(p, it, cell.x, cell.y); }
            }
        }

        // unload: keep everything in the save data; the next load reads it back
        internal static void Remove()
        {
            var p = Player.m_localPlayer;
            if (p != null && p == _for) { Save(p); foreach (var se in _applied) if (!VanillaSe(p).Contains(se)) Seman(p).RemoveStatusEffect(se.NameHash(), true); }
            _inv = null; _for = null; _applied.Clear(); _keys = null;
        }

        internal static void KeepEffects(Humanoid h)
        {
            if (h != _for || _inv == null) return;
            var seman = Seman(h);
            foreach (var se in _applied) if (!seman.HaveStatusEffect(se.NameHash())) seman.AddStatusEffect(se, false, 0, 0f);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class ModSlotPickupPatch
    {
        private static void Prefix(GameObject go, out string __state) => __state = go != null ? go.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name : null;
        private static void Postfix(Humanoid __instance, bool __result, string __state) { if (__result && __instance == Player.m_localPlayer) ModSlots.Collect((Player)__instance, __state); }
    }

    [HarmonyPatch(typeof(Humanoid), "UpdateEquipmentStatusEffects")]
    internal static class ModSlotEffectsPatch { private static void Postfix(Humanoid __instance) => ModSlots.KeepEffects(__instance); }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class ModSlotSavePatch { private static void Prefix(Player __instance) { if (__instance == Player.m_localPlayer) ModSlots.Save(__instance); } }

    [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
    internal static class ModSlotDeathPatch { private static void Prefix(Player __instance) { if (__instance == Player.m_localPlayer) ModSlots.BeforeDeath(__instance); } }

    // right click (use / equip) on a wishbone or wisplight in the bag: its own slot, not Valheim's single utility
    // slot (that one is the belt's; they swapped). One already in the slot goes to the bag.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class ModSlotEquipPatch
    {
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!(__instance is Player p) || p != Player.m_localPlayer || item == null || item.m_equipped) return true;   // m_equipped: a load re-equipping; left to Valheim
            int x = Array.IndexOf(ModSlots.Utility, item.m_shared.m_name); var inv = ModSlots.For(p);
            if (x < 0 || inv == null || !p.GetInventory().ContainsItem(item)) return true;
            if (inv.GetItemAt(x, 0) != null && !ModSlots.Take(p, x, 0)) return true;   // bag full: Valheim's way
            __result = ModSlots.Put(p, item, x, 0);
            return !__result;
        }
    }

    [HarmonyPatch(typeof(Door), "HaveKey")]
    internal static class ModSlotDoorPatch
    {
        private static void Postfix(Door __instance, Humanoid player, ref bool __result)
        {
            if (__result || __instance.m_keyItem == null || !(player is Player p) || p != Player.m_localPlayer) return;
            var inv = ModSlots.For(p);
            __result = inv != null && inv.HaveItem(__instance.m_keyItem.m_itemData.m_shared.m_name);
        }
    }
}

namespace Jatify.UI
{
    // ponytail: temporary, dev only (user: the wisp is released now and then on opening/closing the inventory) - who removes its effect
    [HarmonyLib.HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(int), typeof(bool) })]
    internal static class WispTrace
    {
        private static void Prefix(SEMan __instance, int nameHash)
        {
            if (!Dev.Build || Player.m_localPlayer == null || __instance != Player.m_localPlayer.GetSEMan()) return;
            var se = __instance.GetStatusEffect(nameHash); if (se == null || !se.name.ToLowerInvariant().Contains("demister")) return;
            Log.Info($"wisp effect removed (inventory {(InventoryGui.IsVisible() ? "open" : "closed")}) from:\n" + string.Join("\n", System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(new System.Diagnostics.StackTrace(1).GetFrames(), 8), f => "  " + f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name)));
        }
    }
}

namespace Jatify.UI
{
    // ponytail: temporary, dev only - the wisp message came with no removal logged: whatever ends the effect, log who
    [HarmonyLib.HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Stop))]
    internal static class WispStopTrace
    {
        private static void Prefix(StatusEffect __instance)
        {
            if (!Dev.Build || __instance == null || !__instance.name.ToLowerInvariant().Contains("demister")) return;
            Log.Info($"wisp effect stopped (inventory {(InventoryGui.IsVisible() ? "open" : "closed")}) from:\n" + string.Join("\n", System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(new System.Diagnostics.StackTrace(1).GetFrames(), 9), f => "  " + f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name)));
        }
    }
}
