using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Craft queue: the Queue button beside Craft adds the selected recipe with the amount
    // from the stepper; while nothing is crafting the next one is selected and crafted through Valheim's own
    // Craft (its timer, sounds, requirement checks and multi-craft). An entry that can't be made any more is
    // dropped with a note. Closing the panel or changing station clears the queue.
    internal static class CraftQueue
    {
        internal static ConfigEntry<bool> On;
        internal static readonly List<(Recipe recipe, int qty)> Items = new List<(Recipe, int)>();
        internal static int Forced;   // the running entry's amount (DetailCard uses it instead of its stepper)
        private static readonly AccessTools.FieldRef<InventoryGui, float> Timer = AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");
        private static readonly System.Reflection.FieldInfo Available = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
        private static readonly System.Reflection.MethodInfo SetRecipe = AccessTools.Method(typeof(InventoryGui), "SetRecipe");
        private static readonly System.Reflection.MethodInfo Press = AccessTools.Method(typeof(InventoryGui), "OnCraftPressed");
        private static CraftingStation _station; private static bool _was;

        internal static void Bind(ConfigFile c) => On = c.Bind("Crafting", "CraftQueue", true, "A Queue button beside Craft: recipes are crafted one after another.");

        internal static void Add(Recipe r, int qty)
        {
            if (r == null) return;
            if (Items.Count > 0 && Items[Items.Count - 1].recipe == r) { var last = Items[Items.Count - 1]; Items[Items.Count - 1] = (r, last.qty + qty); }
            else Items.Add((r, Mathf.Max(1, qty)));
            ActionToast.Show($"Queued {Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name)} ×{qty}");
        }

        internal static void Clear() { Items.Clear(); Forced = 0; }

        internal static void Tick()
        {
            var g = InventoryGui.instance; var p = Player.m_localPlayer;
            bool open = g != null && p != null && InventoryGui.IsVisible() && g.InCraftTab();
            var st = p != null ? p.GetCurrentCraftingStation() : null;
            if (Items.Count > 0 && (!open || st != _station)) { if (_was) ActionToast.Show("Craft queue cleared", false); Clear(); }
            _station = st; _was = Items.Count > 0;
            if (!On.Value || !open || Items.Count == 0 || Timer(g) >= 0f) { if (g != null && Timer(g) < 0f) Forced = 0; return; }
            var (recipe, qty) = Items[0]; Items.RemoveAt(0);
            int index = IndexOf(g, recipe);
            if (index < 0) { ActionToast.Show($"{Name(recipe)} can't be made here any more", false); return; }
            SetRecipe.Invoke(g, new object[] { index, true });
            Forced = qty; g.m_multiCraftAmount = qty; AccessTools.FieldRefAccess<InventoryGui, bool>("m_touchMultiCrafting")(g) = qty > 1;
            Press.Invoke(g, null);
            if (Timer(g) < 0f) { Forced = 0; ActionToast.Show($"Not enough for {Name(recipe)} ×{qty}", false); }   // vanilla said no (materials, room)
        }

        private static int IndexOf(InventoryGui g, Recipe r)
        {
            var list = Available.GetValue(g) as System.Collections.IList; if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                var t = Traverse.Create(list[i]);
                if (t.Property<Recipe>("Recipe").Value == r && t.Property<ItemDrop.ItemData>("ItemData").Value == null) return i;
            }
            return -1;
        }

        private static string Name(Recipe r) => Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name);
    }
}
