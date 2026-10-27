using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Crafting and building from nearby chests (user, live design "Used for crafting"):
    //  * which chests: within Chest Range of you, ones you may open, not switched off ("Excluded from
    //    crafting"), and not open by someone else (a cart or ship only when it is yours right now);
    //  * counting: your bag + those chests, same world level rule as Valheim (Inventory.CountItems);
    //  * taking: your bag first, then the chests in turn, claiming each chest before it is changed.
    // Settings: CraftFromChests, BuildFromChests.
    internal static class Stock
    {
        private static readonly AccessTools.FieldRef<Container, ZNetView> View = AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly Func<Container, long, bool> Access = AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        private static readonly List<Container> _all = new List<Container>(), _near = new List<Container>();
        private static readonly Dictionary<string, int> _totals = new Dictionary<string, int>();
        private static float _scanAt = -99f; private static int _nearFrame = -1;
        internal static bool Building;   // inside Player.UpdatePlacement: the build menu asks

        internal static bool On => !Other && (Building ? Cfg.BuildFromChests.Value : Cfg.CraftFromChests.Value);

        // another craft-from-containers mod is installed: two of them both pay for the same craft (a lantern refuel
        // ate both lanterns and two cores, bug report 2026-10-26), so ours steps aside and leaves it to that one
        private static readonly string[] Others = { "Azumatt.AzuCraftyBoxes", "aedenthorn.CraftFromContainers" };   // ponytail: known GUIDs, add others as they're reported
        private static bool? _other;
        // AzuCraftyBoxes pays: the panel counts through its API, so its range, per-chest exclusions, item rules, pull mode (all /
        // mine / nothing) and its containers (drawers, backpacks) all apply. If the API is gone, the bag only (logged once).
        private static System.Reflection.MethodInfo _nearby, _count, _canPull, _prefabName, _prevent; private static bool _apiLooked, _apiBad;
        private static int _nearbyFrame = -1; private static System.Collections.IList _nearbyList; private static float _saidAt;
        private static readonly Dictionary<string, string> _prefabOf = new Dictionary<string, string>();
        private static int OtherCount(Player p, string name)
        {
            if (!_apiLooked)
            {
                _apiLooked = true;
                var api = AccessTools.TypeByName("AzuCraftyBoxes.API");
                _nearby = api?.GetMethod("GetNearbyContainers")?.MakeGenericMethod(typeof(Player));
                _count = api?.GetMethod("CountItemInContainer");
                _canPull = api?.GetMethod("CanItemBePulled", new[] { typeof(string), typeof(string) });
                _prefabName = AccessTools.TypeByName("AzuCraftyBoxes.IContainers.IContainer")?.GetMethod("GetPrefabName");
                _prevent = AccessTools.Method(AccessTools.TypeByName("AzuCraftyBoxes.Util.Functions.MiscFunctions"), "ShouldPrevent");
                if (_nearby == null || _count == null) { _apiBad = true; Log.Warn("AzuCraftyBoxes: its API was not found, the crafting panel counts the bag only (it still takes from chests)"); }
            }
            if (_apiBad) return 0;
            try
            {
                if (_prevent != null && (bool)_prevent.Invoke(null, null)) return 0;   // its pull mode is Off (Alt+O): it takes nothing, so nothing to count
                if (_nearbyFrame != Time.frameCount)
                {
                    _nearbyFrame = Time.frameCount;
                    var range = AccessTools.Field(AccessTools.TypeByName("AzuCraftyBoxes.AzuCraftyBoxesPlugin"), "mRange")?.GetValue(null) as BepInEx.Configuration.ConfigEntryBase;
                    float r = range != null ? Convert.ToSingle(range.BoxedValue) : Cfg.StackRange.Value;
                    Plugin.Timed("azu.nearby", () => _nearbyList = _nearby.Invoke(null, new object[] { p, r }) as System.Collections.IList);
                }
                if (_nearbyList == null || _nearbyList.Count == 0) return 0;
                string prefab = PrefabOf(name); int n = 0;
                Plugin.Timed("azu.count", () =>
                {
                    foreach (var c in _nearbyList)
                    {
                        if (c == null) continue;
                        if (_canPull != null && _prefabName != null && prefab != null && !(bool)_canPull.Invoke(null, new object[] { (string)_prefabName.Invoke(c, null), prefab })) continue;
                        n += (int)_count.Invoke(null, new object[] { c, name });
                    }
                });
#if JATIFY_DEV
                if (Time.unscaledTime >= _saidAt) { _saidAt = Time.unscaledTime + 5f; Log.Info($"cost: AzuCraftyBoxes sees {_nearbyList.Count} containers in range"); }
#endif
                return n;
            }
            catch (Exception e) { _apiBad = true; Log.Warn($"AzuCraftyBoxes: counting its chests failed ({e.GetType().Name}), the crafting panel counts the bag only (it still takes from chests)"); return 0; }
        }
        private static string PrefabOf(string sharedName)
        {
            if (_prefabOf.TryGetValue(sharedName, out var pf)) return pf;
            pf = null;
            foreach (var go in ObjectDB.instance.m_items) { var d = go != null ? go.GetComponent<ItemDrop>() : null; if (d != null && d.m_itemData.m_shared.m_name == sharedName) { pf = go.name; break; } }
            _prefabOf[sharedName] = pf; return pf;
        }

        internal static bool Other
        {
            get
            {
                if (_other.HasValue) return _other.Value;
                var hit = Array.Find(Others, BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey);
                if (hit == null && !Epic.Started) return false;   // plugins still loading: not final yet
                if (hit != null) Log.Warn($"{hit} is installed: it pays for crafts and builds from chests, Jatify UI's own steps aside (only one mod may pay)");
                _other = hit != null;
                return _other.Value;
            }
        }

        // all loaded chests, refreshed every 2 s (ponytail: a scene scan; a Container.Awake hook if it ever shows in profiles)
        private static List<Container> Near(Player p)
        {
            if (_nearFrame == Time.frameCount) return _near;
            _nearFrame = Time.frameCount;
            if (Time.unscaledTime - _scanAt > 2f) { _scanAt = Time.unscaledTime; _all.Clear(); _all.AddRange(UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)); }
            _near.Clear(); _totals.Clear();
            float r = Cfg.StackRange.Value, r2 = r * r; long id = p.GetPlayerID(); var pos = p.transform.position;
            foreach (var c in _all)
            {
                if (c == null || c.GetInventory() == null || c.GetComponentInParent<Player>() != null) continue;
                if ((c.transform.position - pos).sqrMagnitude > r2) continue;
                var nv = View(c); if (nv == null || !nv.IsValid()) continue;
                var z = nv.GetZDO();
                if (z.GetString(ChestPanel.NoCraftKey, "") == "1" || !Access(c, id) || !Ward(c)) continue;   // same gates as opening it by hand
                bool vehicle = c.GetComponentInParent<Ship>() != null || c.GetComponentInParent<Vagon>() != null;
                if (!nv.IsOwner() && (z.GetInt(ZDOVars.s_inUse, 0) == 1 || vehicle)) continue;   // someone else has it open
                _near.Add(c);
            }
            int wl = Game.m_worldLevel;
            foreach (var c in _near) foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_worldLevel >= wl) { _totals.TryGetValue(it.m_shared.m_name, out var n); _totals[it.m_shared.m_name] = n + it.m_stack; }
            return _near;
        }

        // chests around a point (a smelter, a fire) that you may use: the same gates as crafting from chests
        internal static void Around(Vector3 at, float range, long me, List<Container> into)
        {
            into.Clear();
            if (Time.unscaledTime - _scanAt > 2f) { _scanAt = Time.unscaledTime; _all.Clear(); _all.AddRange(UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)); }
            float r2 = range * range;
            foreach (var c in _all)
            {
                if (c == null || c.GetInventory() == null || c.GetComponentInParent<Player>() != null || (c.transform.position - at).sqrMagnitude > r2) continue;
                var nv = View(c); if (nv == null || !nv.IsValid()) continue;
                var z = nv.GetZDO();
                if (z.GetString(ChestPanel.NoCraftKey, "") == "1" || !Access(c, me) || !Ward(c)) continue;
                if (!nv.IsOwner() && (z.GetInt(ZDOVars.s_inUse, 0) == 1 || c.GetComponentInParent<Ship>() != null || c.GetComponentInParent<Vagon>() != null)) continue;
                into.Add(c);
            }
        }

        // take one of an item from a chest, claiming it first (as crafting does)
        internal static bool TakeOne(Container c, string name)
        {
            var nv = View(c); if (nv == null || !nv.IsValid()) return false;
            var inv = c.GetInventory(); int wl = Game.m_worldLevel;
            foreach (var it in inv.GetAllItems())
            {
                if (it.m_shared.m_name != name || it.m_worldLevel < wl || ChestPanel.IsLockedIn(c, it.m_gridPos.x, it.m_gridPos.y)) continue;
                if (!nv.IsOwner()) nv.ClaimOwnership();
                return inv.RemoveItem(it, 1);
            }
            return false;
        }

        // a chest inside someone else's ward cannot be opened by hand (Container.Interact), so not by crafting either
        internal static bool Ward(Container c) => !c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false, false);

        // Valheim's rule: upgrader resources (e.g. the Wooden Battle Idol) count only at an upgrader station,
        // and only there; everywhere else the recipe is made without them
        internal static bool Applies(Player p, Piece.Requirement r)
        {
            if (r == null || r.m_resItem == null) return false;
            var st = p != null ? p.GetCurrentCraftingStation() : null;
            return st != null ? st.m_upgrader == r.m_upgraderResource : !r.m_upgraderResource;
        }

        // in the chests only
        internal static int InChests(Player p, string name)
        {
            if (p == null) return 0;
            if (Other) return OtherCount(p, name);   // the other mod pays: count what it would take, by its own rules
            if (!On) return 0;
            Near(p); return _totals.TryGetValue(name, out var n) ? n : 0;
        }

        // bag + chests (what crafting may use)
        internal static int Count(Player p, string name) => p == null ? 0 : p.GetInventory().CountItems(name, -1, true) + InChests(p, name);

        internal static void Remove(Player p, string name, int amount, int quality)
        {
            var bag = p.GetInventory();
            int mine = Mathf.Min(bag.CountItems(name, quality, true), amount);
            if (mine > 0) { bag.RemoveItem(name, mine, quality, true); amount -= mine; }
            if (amount <= 0 || !On) return;
            foreach (var c in Near(p))
            {
                if (amount <= 0) break;
                var inv = c.GetInventory(); int k = Mathf.Min(inv.CountItems(name, quality, true), amount);
                if (k <= 0) continue;
                var nv = View(c); if (!Ward(c) || !Access(c, p.GetPlayerID())) continue;   // checked again right before taking
                if (!nv.IsOwner()) nv.ClaimOwnership();
                inv.RemoveItem(name, k, quality, true); amount -= k;
            }
            _nearFrame = -1;
            if (amount > 0) Log.Warn($"stock: {amount} x {name} could not be taken (gone meanwhile)");
        }

        // EpicLoot's enchanting table spends from the chests near you too. Stackable materials only: gear is what it edits
        // in place (enchant, disenchant, sockets), and an edit inside a chest can be undone when the chest reloads, after
        // the cost was paid (with sockets, the stones come back twice). EpicLoot names items by prefab or by shared name.
        private static string Shared(string n) => n == null || n.StartsWith("$") ? n : ObjectDB.instance?.GetItemPrefab(n)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? n;

        internal static List<ItemDrop.ItemData> TableItems()
        {
            var list = new List<ItemDrop.ItemData>(); var p = Player.m_localPlayer;
            if (p == null || !On) return list;
            int wl = Game.m_worldLevel;
            foreach (var c in Near(p)) foreach (var it in c.GetInventory().GetAllItems())
                if (it.m_shared.m_maxStackSize > 1 && it.m_worldLevel >= wl && !ChestPanel.IsLockedIn(c, it.m_gridPos.x, it.m_gridPos.y)) list.Add(it);
            return list;
        }

        internal static int TableCount(string name) { var p = Player.m_localPlayer; return p == null || !On ? 0 : InChests(p, Shared(name)); }

        internal static int TableTake(string name, int amount)
        {
            var p = Player.m_localPlayer; if (p == null || !On || amount <= 0) return 0;
            name = Shared(name); int taken = 0;
            foreach (var c in Near(p))
            {
                if (taken >= amount) break;
                var inv = c.GetInventory(); int k = Mathf.Min(inv.CountItems(name, -1, true), amount - taken);
                if (k <= 0 || !Ward(c) || !Access(c, p.GetPlayerID())) continue;
                var nv = View(c); if (!nv.IsOwner()) nv.ClaimOwnership();
                inv.RemoveItem(name, k, -1, true); taken += k;
            }
            _nearFrame = -1; return taken;
        }

        internal static int TableTakeExact(ItemDrop.ItemData it, int amount)
        {
            var c = Holder(it); if (c == null || amount <= 0) return 0;
            var nv = View(c); if (!nv.IsOwner()) nv.ClaimOwnership();
            int k = Mathf.Min(amount, it.m_stack); c.GetInventory().RemoveItem(it, k); _nearFrame = -1; return k;
        }

        // EpicLoot's save handler, as its docs show it: only the owner saves; never reload first
        internal static bool TableSave(ItemDrop.ItemData it)
        {
            var c = Holder(it); var nv = c != null ? View(c) : null;
            if (nv == null || !nv.IsOwner()) return false;
            InvChanged(c.GetInventory(), false, false); return true;
        }
        private static readonly Action<Inventory, bool, bool> InvChanged = AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(AccessTools.Method(typeof(Inventory), "Changed"));

        private static Container Holder(ItemDrop.ItemData it) { var p = Player.m_localPlayer; if (p == null || it == null) return null; foreach (var c in Near(p)) if (c.GetInventory().ContainsItem(it)) return c; return null; }
    }

    [HarmonyPatch(typeof(Player))]
    internal static class StockPatches
    {
        // crafting: may this recipe be made with bag + chests
        [HarmonyPrefix, HarmonyPatch("HaveRequirementItems")]
        private static bool HaveRequirementItems(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
        {
            if (discover || __instance != Player.m_localPlayer || !Stock.On) return true;
            var st = __instance.GetCurrentCraftingStation();
            foreach (var r in piece.m_resources)
            {
                if (r.m_resItem == null || (st != null && st.m_upgrader != r.m_upgraderResource) || (st == null && r.m_upgraderResource)) continue;
                int need = r.GetAmount(qualityLevel) * amount;
                int have = Stock.Count(__instance, r.m_resItem.m_itemData.m_shared.m_name);
                if (piece.m_requireOnlyOneIngredient) { if (have >= need) { __result = true; return false; } }
                else if (have < need) { __result = false; return false; }
            }
            __result = !piece.m_requireOnlyOneIngredient;
            return false;
        }

        // building: the piece's costs, counted with the chests (only the "can I afford it" part; the rest is vanilla)
        [HarmonyPostfix, HarmonyPatch(nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
        private static void HaveRequirementsPiece(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            if (__result || mode == Player.RequirementMode.IsKnown || __instance != Player.m_localPlayer || !Cfg.BuildFromChests.Value || piece == null) return;
            bool was = Stock.Building; Stock.Building = true;
            try
            {
                // vanilla said no: yes only if every material is covered by bag + chests and nothing else blocks it
                foreach (var r in piece.m_resources)
                {
                    if (r.m_resItem == null || r.m_amount <= 0) continue;
                    int have = Stock.Count(__instance, r.m_resItem.m_itemData.m_shared.m_name);
                    if (mode == Player.RequirementMode.CanAlmostBuild ? have <= 0 : have < r.m_amount) return;
                }
                if (piece.m_craftingStation != null && mode == Player.RequirementMode.CanBuild && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, __instance.transform.position) == null && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost)) return;
                if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc)) return;
                __result = true;
            }
            finally { Stock.Building = was; }
        }

        [HarmonyPrefix, HarmonyPatch("UpdatePlacement")] private static void PlacePre() => Stock.Building = true;
        [HarmonyPostfix, HarmonyPatch("UpdatePlacement")] private static void PlacePost() => Stock.Building = false;

        // paying: bag first, then chests
        [HarmonyPrefix, HarmonyPatch("ConsumeResources"), HarmonyAfter(Epic.Guid)]   // after EpicLoot's lucky craft has made the costs free
        private static bool ConsumeResources(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
        {
            if (__instance != Player.m_localPlayer || !Stock.On) return true;
            var st = __instance.GetCurrentCraftingStation();
            foreach (var r in requirements)
            {
                if (r.m_resItem == null || (st != null && st.m_upgrader != r.m_upgraderResource) || (st == null && r.m_upgraderResource)) continue;
                int n = r.GetAmount(qualityLevel) * multiplier;
                if (n > 0) Stock.Remove(__instance, r.m_resItem.m_itemData.m_shared.m_name, n, itemQuality);
            }
            return false;
        }
    }
}
