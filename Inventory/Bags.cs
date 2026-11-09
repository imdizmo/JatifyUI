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

        // Backpacks opens your worn pack whenever the inventory opens (its Auto Open Backpack, on by default): with Jatify its tab
        // opens it in one click, so it stays closed until then (user). A chest you open, and other mods' bags, are left alone
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyAfter("org.bepinex.plugins.backpacks")]
        private static class NoAutoOpen
        {
            private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> Grid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
            private static readonly List<ItemDrop.ItemData> _w = new List<ItemDrop.ItemData>();
            private static void Postfix(InventoryGui __instance, Container container)
            {
                Bind(); if (container != null || _bp == null || __instance == null) return;
                var shown = Grid(__instance)?.GetInventory(); if (shown == null) return;
                foreach (var it in Worn(Player.m_localPlayer, _w))
                    if (KindOf(it) == Kind.Backpacks && Contents(it) == shown) { CloseContainer?.Invoke(__instance, null); return; }
            }
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

    // Hypnogoogic's Advanced_Backpacks (#28): a worn Enhanced or better backpack opens its portal and ship slots as a stand-in
    // chest each time the inventory opens; its rows and those slots live in the backpack item's own data (adv_bp, adv_bpx). No
    // calls into it: its GUID, its prefab names and its data keys only.
    internal static class AdvPacks
    {
        private static int _on = -1;
        internal static bool On { get { if (_on < 0) _on = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("p0kahontazz.advancedbackpacks") ? 1 : 0; return _on == 1; } }
        internal static bool IsPack(ItemDrop.ItemData it) => On && it?.m_dropPrefab != null && it.m_dropPrefab.name.StartsWith("AdvancedBackpack", StringComparison.Ordinal);

        // its stand-in chest: a Container on the player (a real chest never is); "Portal and ship" tells it from AdventureBackpacks'
        internal static bool IsDock(Container c) => On && c != null && c.gameObject.name == "AB_BackpackProxy" && c.m_name == "Portal and ship" && c.GetComponentInParent<Player>() != null;

        // the backpack's own names, for chest stock (which counts by name)
        private static HashSet<string> _names;
        internal static bool IsPackName(string name)
        {
            if (!On || ObjectDB.instance == null) return false;
            if (_names == null || _names.Count == 0)
            {
                _names = new HashSet<string>();
                foreach (var go in ObjectDB.instance.m_items) if (go != null && go.name.StartsWith("AdvancedBackpack", StringComparison.Ordinal)) { var d = go.GetComponent<ItemDrop>(); if (d != null) _names.Add(d.m_itemData.m_shared.m_name); }
            }
            return _names.Contains(name);
        }

        // how many items it holds where they would be lost with it: its portal / ship slots always, its rows unless worn (taking a
        // worn one off releases its rows into the bag)
        internal static int Held(ItemDrop.ItemData it)
        {
            if (!IsPack(it)) return 0;
            int n = Count(it, "adv_bpx"); if (!it.m_equipped) n += Count(it, "adv_bp");
            return n;
        }
        private static int Count(ItemDrop.ItemData it, string key)
        {
            if (!it.m_customData.TryGetValue(key, out var b64) || string.IsNullOrEmpty(b64)) return 0;
            try { var inv = new Inventory("", null, 8, 32); inv.Load(new ZPackage(b64)); return inv.GetAllItems().Sum(i => i.m_stack); }
            catch (Exception e) { Log.Warn($"Advanced_Backpacks: {key} unreadable ({e.Message}), treated as not empty"); return 1; }
        }
    }
}
