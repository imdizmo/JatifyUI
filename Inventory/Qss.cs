using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Goldenrevolver's Quick Stack Store. Installed, its actions own the jobs Jatify's buttons do (user: theirs take priority):
    //  * its own slot check is told which cells are ours (the Food / Tools / Meads rows, worn armour, another mod's slot rows,
    //    locked cells), so its sort, quick stack, store all and quick trash leave them alone;
    //  * its floating buttons, star and trash can are hidden: To chests, Restock, Sort and our trash call its actions instead;
    //  * a key bound in both mods is its key (one press, one action).
    // Soft: its members by reflection from its own assembly; whatever is missing stays Jatify's, with one log line.
    internal static class Qss
    {
        internal const string Guid = "goldenrevolver.quick_stack_store";
        private static bool? _on; private static Assembly _asm;
        private static MethodInfo _quickStack, _restock, _sort, _flag, _quickTrash, _favMode;
        private static FieldInfo[] _buttons = new FieldInfo[0]; private static FieldInfo _trashRoot;
        private static bool _inQuickTrash;

        internal static bool On
        {
            get
            {
                if (_on != null) return _on.Value;
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(Guid, out var i) || i.Instance == null) { if (Epic.Started) _on = false; return false; }
                _asm = i.Instance.GetType().Assembly; _on = true; return true;
            }
        }

        private static Type T(string n) => _asm.GetType("QuickStackStore." + n);

        // from Plugin.Start: every plugin's Awake has run
        internal static void Hook(Harmony h)
        {
            if (!On) return;
            var compat = T("CompatibilitySupport"); var missing = new System.Collections.Generic.List<string>();
            void Post(Type t, string m, string mine) { var mi = t != null ? AccessTools.Method(t, m) : null; if (mi == null) { missing.Add(m); return; } h.Patch(mi, postfix: new HarmonyMethod(typeof(Qss), mine)); }
            Post(compat, "IsEquipOrQuickSlot", nameof(GuardPost));
            Post(compat, "IsEquipSlot", nameof(FixedPost));
            Post(T("UserConfig"), "IsSlotFavorited", nameof(TrashGuardPost));
            var qt = AccessTools.Method(T("TrashModule"), "QuickTrash");
            if (qt != null) h.Patch(qt, prefix: new HarmonyMethod(typeof(Qss), nameof(QuickTrashPre)), finalizer: new HarmonyMethod(typeof(Qss), nameof(QuickTrashFin))); else missing.Add("QuickTrash");
            Post(T("ButtonRenderer+MainButtonUpdate"), "UpdateInventoryGuiButtons", nameof(HideUi));
            Post(T("TrashModule+TrashItemsPatches"), "UpdateTrashCanUI", nameof(HideUi));
            _quickStack = AccessTools.Method(T("QuickStackModule"), "DoQuickStack");
            _restock = AccessTools.Method(T("RestockModule"), "DoRestock");
            _sort = AccessTools.Method(T("SortModule"), "DoSort");
            _flag = AccessTools.Method(T("TrashModule"), "TrashOrTrashFlagItem");
            _quickTrash = AccessTools.Method(T("TrashModule"), "AttemptQuickTrash");
            _favMode = AccessTools.Method(T("FavoritingMode"), "IsInFavoritingMode");
            var br = T("ButtonRenderer");
            _buttons = new[] { "favoritingTogglingButton", "quickStackAreaButton", "sortInventoryButton", "restockAreaButton", "quickStackToContainerButton", "storeAllButton", "sortContainerButton", "restockFromContainerButton" }
                .Select(n => br != null ? AccessTools.Field(br, n) : null).Where(f => f != null).ToArray();
            _trashRoot = AccessTools.Field(T("TrashModule"), "trashRoot");
            foreach (var (m, n) in new[] { (_quickStack, "DoQuickStack"), (_restock, "DoRestock"), (_sort, "DoSort"), (_flag, "TrashOrTrashFlagItem"), (_quickTrash, "AttemptQuickTrash") }) if (m == null) missing.Add(n);
            if (missing.Count == 0) Log.Info("Quick Stack Store found: its sort, quick stack and quick trash leave Jatify's rows, worn gear and locked cells alone; To chests, Restock, Sort and the trash use its actions; its own buttons are hidden");
            else Log.Warn("Quick Stack Store found, but not all of it: " + string.Join(", ", missing) + " missing; those parts stay Jatify's (or unguarded: favourite those slots in it with Alt + right click)");
        }

        // a cell of ours: another mod's or our reserved rows, worn armour, a locked cell (player inventory only)
        private static bool Ours(int height, Vector2i at)
        {
            var p = Player.m_localPlayer; if (p == null || height != p.GetInventory().GetHeight()) return false;
            return at.y >= SpecialRows.Start || InvActions.Locked(at.x, at.y);
        }
        private static void GuardPost(int inventoryHeight, Vector2i itemPos, ref bool __result) { if (!__result && Ours(inventoryHeight, itemPos)) __result = true; }
        // restock: may top up Food and Meads stacks, never the worn row or the hidden cells beyond a row's width
        private static void FixedPost(int inventoryHeight, Vector2i itemPos, ref bool __result)
        {
            if (__result || !SpecialRows.On) return;
            var p = Player.m_localPlayer; if (p == null || inventoryHeight != p.GetInventory().GetHeight()) return;
            if (itemPos.y == SpecialRows.EqRow || itemPos.y >= SpecialRows.Start && itemPos.x >= SpecialRows.RowWidth(itemPos.y - SpecialRows.Start)) __result = true;
        }
        // its quick trash goes by "favourited slot": for that one call, our cells count as favourited
        private static void QuickTrashPre() => _inQuickTrash = true;
        private static Exception QuickTrashFin(Exception __exception) { _inQuickTrash = false; return __exception; }
        private static void TrashGuardPost(Vector2i position, ref bool __result)
        {
            if (!_inQuickTrash || __result || Player.m_localPlayer == null) return;
            __result = Ours(Player.m_localPlayer.GetInventory().GetHeight(), position);
        }

        // its floating buttons, star and trash can: ours do those jobs
        private static void HideUi()
        {
            foreach (var f in _buttons) if (f.GetValue(null) is Component c && c != null && c.gameObject.activeSelf) c.gameObject.SetActive(false);
            if (_trashRoot?.GetValue(null) is Transform tr && tr != null && tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
        }
        // every frame the inventory shows: it brings them back on its own (its Show postfix runs after ours, and it re-lays them)
        internal static void Tick() { if (_on == true && _buttons.Length > 0 && InventoryGui.IsVisible()) HideUi(); }
        internal static void Unload()
        {
            if (_trashRoot?.GetValue(null) is Transform tr && tr != null) tr.gameObject.SetActive(true);   // it spawns it once and never looks again
        }

        // its actions, args filled with their defaults (an optional parameter added later doesn't break the call)
        private static bool Call(MethodInfo m, Player p = null)
        {
            if (m == null) return false;
            var ps = m.GetParameters(); var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) args[i] = i == 0 && p != null ? p : ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            try { m.Invoke(null, args); return true; } catch (Exception e) { Log.Warn("Quick Stack Store " + m.Name + ": " + e.GetBaseException().Message); return false; }
        }
        internal static bool QuickStack(Player p) => On && Call(_quickStack, p);
        internal static bool Restock(Player p) => On && Call(_restock, p);
        internal static bool Sort(Player p) => On && Call(_sort, p);
        internal static bool FavouritingMode => On && _favMode != null && _favMode.Invoke(null, null) is bool b && b;
        internal static bool Flag() => On && Call(_flag);             // a held item dropped on the trash in favouriting mode (Alt held): marked as trash
        internal static bool QuickTrash() => On && Call(_quickTrash); // the trash clicked empty-handed: everything marked as trash goes (its confirm)

        // a key it binds too: its key
        internal static bool OwnsKey(BepInEx.Configuration.KeyboardShortcut k)
        {
            if (!On || k.MainKey == KeyCode.None) return false;
            var cfg = T("QSSConfig"); if (cfg == null) return false;
            foreach (var t in cfg.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (f.GetValue(null) is BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> e && f.Name.EndsWith("Keybind") && !f.Name.StartsWith("FavoritingModifier")
                        && e.Value.MainKey == k.MainKey && e.Value.Modifiers.SequenceEqual(k.Modifiers)) return true;
            return false;
        }
    }
}
