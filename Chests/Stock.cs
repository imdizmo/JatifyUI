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
        // the other mod's own reach, so the panel counts exactly the chests it will take from (AzuCraftyBoxes: "Container Range", 20 m)
        private static BepInEx.Configuration.ConfigEntryBase _otherRange;
        private static float OtherRange()
        {
            if (_otherRange == null) _otherRange = AccessTools.Field(AccessTools.TypeByName("AzuCraftyBoxes.AzuCraftyBoxesPlugin"), "mRange")?.GetValue(null) as BepInEx.Configuration.ConfigEntryBase;
            try { return _otherRange != null ? System.Convert.ToSingle(_otherRange.BoxedValue) : Cfg.StackRange.Value; } catch { return Cfg.StackRange.Value; }
        }
        internal static bool Other
        {
            get
            {
                if (_other.HasValue) return _other.Value;
                var hit = Array.Find(Others, BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey);
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
            float r = Other ? OtherRange() : Cfg.StackRange.Value, r2 = r * r; long id = p.GetPlayerID(); var pos = p.transform.position;
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
            if (p == null || !(On || Other)) return 0;   // with another craft-from-chests mod we still count (the panel shows what it will use), it alone pays
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
        [HarmonyPrefix, HarmonyPatch("ConsumeResources")]
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
