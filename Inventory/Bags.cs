using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Jatify.UI
{
    // Other mods' bags (Smoothbrain's Backpacks, Vapok's AdventureBackpacks, RustyMods' RustyBags): which worn item is a bag, what it
    // holds, and opening it the way its own key does. Each keeps the contents in the bag item's own data. Soft: by reflection from
    // each mod's own assembly; a mod that isn't there, or whose members moved, just has no tab.
    internal static class Bags
    {
        internal enum Kind { None, Backpacks, Adventure, Rusty }

        private static Assembly Asm(string guid) => BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(guid, out var i) && i.Instance != null ? i.Instance.GetType().Assembly : null;
        private static Assembly _bp, _ab, _rb; private static bool _bound;
        private static void Bind()
        {
            if (_bound) return; _bound = true;
            _bp = Asm("org.bepinex.plugins.backpacks"); _ab = Asm("vapok.mods.adventurebackpacks"); _rb = Asm("RustyMods.RustyBags");
        }
        internal static bool Any { get { Bind(); return _bp != null || _ab != null || _rb != null; } }

        internal static Kind KindOf(ItemDrop.ItemData it)
        {
            if (it == null) return Kind.None; Bind();
            var tn = it.GetType().FullName;
            if (_rb != null && (tn == "RustyBags.Bag" || tn == "RustyBags.Quiver")) return Kind.Rusty;
            if (_bp != null && it.m_customData.Keys.Any(k => k.StartsWith("org.bepinex.plugins.backpacks#"))) return Kind.Backpacks;
            var n = it.m_dropPrefab != null ? it.m_dropPrefab.name : "";
            if (_bp != null && n.StartsWith("bp_")) return Kind.Backpacks;
            if (_ab != null && AbCall("IsBackpack", it) is bool ab && ab) return Kind.Adventure;   // its own check (Judes' "Simple backpack" shares the name pattern)
            return Kind.None;
        }

        // the worn bags, in a steady order (Backpacks, AdventureBackpacks, RustyBags)
        internal static List<ItemDrop.ItemData> Worn(Player p, List<ItemDrop.ItemData> into)
        {
            into.Clear(); if (p == null || !Any) return into;
            ItemDrop.ItemData abWorn = null;   // AdventureBackpacks: "exactly one backpack may ever be equipped"; with two worn (ExtraSlots' slots) it opens only that one
            if (_ab != null && AbCall("GetEquippedBackpack", p) is object bp && bp != null) abWorn = AccessTools.Field(bp.GetType(), "ItemData")?.GetValue(bp) as ItemDrop.ItemData;
            foreach (var it in p.GetInventory().GetAllItems())
            {
                if (!p.IsItemEquiped(it)) continue; var k = KindOf(it);
                if (k == Kind.None || k == Kind.Adventure && it != abWorn) continue;
                into.Add(it);
            }
            into.Sort((a, b) => KindOf(a).CompareTo(KindOf(b)));
            return into;
        }

        private static object AbCall(string name, object arg)
        {
            try { var api = _ab.GetType("AdventureBackpacks.API.ABAPI"); var m = api != null ? AccessTools.Method(api, name, new[] { arg.GetType() == typeof(Player) ? typeof(Player) : typeof(ItemDrop.ItemData) }) : null; return m?.Invoke(null, new[] { arg }); }
            catch (Exception e) { Warn(name, e); return null; }
        }

        // what it holds (null when not known)
        internal static Inventory Contents(ItemDrop.ItemData it)
        {
            try
            {
                switch (KindOf(it))
                {
                    case Kind.Rusty: AccessTools.Method(it.GetType(), "Load")?.Invoke(it, null); return AccessTools.Field(it.GetType(), "inventory")?.GetValue(it) as Inventory;
                    case Kind.Adventure:
                    {
                        var api = _ab.GetType("AdventureBackpacks.API.ABAPI"); var args = new object[] { it, null };
                        return api != null && (bool)AccessTools.Method(api, "TryGetBackpackInventory").Invoke(null, args) ? args[1] as Inventory : null;
                    }
                    case Kind.Backpacks:
                    {   // its own ItemDataManager copy: item.Data().Get<ItemContainer>().Inventory
                        var data = AccessTools.Method(_bp.GetType("ItemDataManager.ItemExtensions"), "Data", new[] { typeof(ItemDrop.ItemData) })?.Invoke(null, new object[] { it });
                        var get = data != null ? AccessTools.Method(data.GetType(), "Get")?.MakeGenericMethod(_bp.GetType("Backpacks.ItemContainer")) : null;
                        var c = get?.Invoke(data, new object[] { "" }); return c != null ? AccessTools.Field(c.GetType(), "Inventory")?.GetValue(c) as Inventory : null;
                    }
                }
            }
            catch (Exception e) { Warn("contents", e); }
            return null;
        }

        // shown in the chest panel right now (AdventureBackpacks shows its pack through a stand-in chest: its own flag)
        internal static bool IsOpen(ItemDrop.ItemData it)
        {
            if (KindOf(it) == Kind.Adventure) { try { var api = _ab.GetType("AdventureBackpacks.API.ABAPI"); return api != null && AccessTools.Method(api, "IsBackpackOpen")?.Invoke(null, null) is bool b && b; } catch (Exception e) { Warn("open state", e); return false; } }
            var inv = Contents(it); return inv != null && ChestPanel.Foreign == inv;
        }

        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static void Warn(string what, Exception e) { if (_warned.Add(what)) Log.Warn($"bags: {what}: {e.GetBaseException().Message}"); }

        // opening it the way its own key does
        internal static bool Open(ItemDrop.ItemData it)
        {
            try { var ok = OpenWith(KindOf(it), it); if (!ok) Log.Info($"bags: {KindOf(it)} didn't open {it.m_shared.m_name}"); return ok; }
            catch (Exception e) { Warn("open", e); return false; }
        }
        private static bool OpenWith(Kind k, ItemDrop.ItemData it)
        {
            var g = InventoryGui.instance; var p = Player.m_localPlayer; if (g == null || p == null || k == Kind.None) return false;
            CloseAll(g);   // one bag at a time, and no chest open (RustyBags draws only then)
            switch (k)
            {
                case Kind.Backpacks:   // Backpacks.CustomContainer+OpenFakeItemsContainer.Open(InventoryGui, ItemData): any pack you pass. It is
                                       // also its UpdateContainer prefix, so false means shown ("skip the original")
                    return AccessTools.Method(_bp.GetType("Backpacks.CustomContainer+OpenFakeItemsContainer"), "Open")?.Invoke(null, new object[] { g, it }) is bool r && !r;
                case Kind.Adventure:   // ABAPI.OpenBackpack(Player, InventoryGui): the worn pack, through its stand-in chest
                    AccessTools.Method(_ab.GetType("AdventureBackpacks.API.ABAPI"), "OpenBackpack", new[] { typeof(Player), typeof(InventoryGui) })?.Invoke(null, new object[] { p, g }); return true;
                case Kind.Rusty:       // Bag.Load() then Bag.Open(): it draws the bag while no chest is open
                    AccessTools.Method(it.GetType(), "Load")?.Invoke(it, null); AccessTools.Method(it.GetType(), "Open")?.Invoke(it, null); return true;
            }
            return false;
        }

        private static readonly MethodInfo CloseContainer = AccessTools.Method(typeof(InventoryGui), "CloseContainer");
        // closes whichever bag (and chest) is open: Valheim's CloseContainer clears Backpacks' and AdventureBackpacks'; RustyBags' bag closes itself
        internal static void CloseAll(InventoryGui g)
        {
            CloseContainer?.Invoke(g, null);   // private in Valheim's assembly as we reference it
            if (_rb == null) return;
            try
            {
                var cur = AccessTools.Field(_rb.GetType("RustyBags.BagGui"), "m_currentBag")?.GetValue(null);
                if (cur != null) AccessTools.Method(cur.GetType(), "Close")?.Invoke(cur, null);
            }
            catch (Exception e) { Warn("close", e); }
        }
    }
}
