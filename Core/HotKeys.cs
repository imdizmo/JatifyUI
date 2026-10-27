using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Jatify.UI
{
    // Keys for the inventory and chest actions: quick stack (P) and restock (L) work with
    // the inventory closed too; merge, sort and the chest transfers have keys you can set (none by default).
    // Never while you type, in a menu, the console or the big map.
    internal static class HotKeys
    {
        internal static ConfigEntry<KeyboardShortcut> Tracker, Ping, QuickStack, Restock, Merge, Sort, PutAll, PutSimilar, TakeAll, TakeSimilar, ChestSort;

        internal static void Bind(ConfigFile c)
        {
            Tracker = c.Bind("Keys", "Tracker", new KeyboardShortcut(KeyCode.U), "Folds the achievement tracker to its header and back (was Y, which EpicLoot's Spirit Animal uses).");
            Ping = c.Bind("Keys", "PingAtCrosshair", new KeyboardShortcut(KeyCode.Mouse2, KeyCode.LeftAlt), "Pings the spot under the crosshair for everyone, without opening the map.");
            QuickStack = c.Bind("Keys", "QuickStack", new KeyboardShortcut(KeyCode.P), "Puts what nearby chests already hold into them (Chest Range), inventory open or closed.");
            Restock = c.Bind("Keys", "Restock", new KeyboardShortcut(KeyCode.L), "Tops up your stacks from nearby chests, inventory open or closed.");
            Merge = c.Bind("Keys", "Merge", new KeyboardShortcut(KeyCode.None), "Merges split stacks in your inventory.");
            Sort = c.Bind("Keys", "Sort", new KeyboardShortcut(KeyCode.None), "Sorts your inventory (same order as the Sort button).");
            PutAll = c.Bind("Keys", "PutAll", new KeyboardShortcut(KeyCode.None), "Open chest: put everything in.");
            PutSimilar = c.Bind("Keys", "PutSimilar", new KeyboardShortcut(KeyCode.None), "Open chest: put in what it already holds.");
            TakeAll = c.Bind("Keys", "TakeAll", new KeyboardShortcut(KeyCode.None), "Open chest: take everything.");
            TakeSimilar = c.Bind("Keys", "TakeSimilar", new KeyboardShortcut(KeyCode.None), "Open chest: top up the stacks you carry.");
            ChestSort = c.Bind("Keys", "ChestSort", new KeyboardShortcut(KeyCode.None), "Open chest: sort it.");
        }

        internal static bool Free() =>
            !Typing.Active && (Chat.instance == null || !Chat.instance.HasFocus()) && !Console.IsVisible() && !TextInput.IsVisible()
            && !Menu.IsVisible() && !Minimap.IsOpen() && ConfigPage.Open == null && !StoreGui.IsVisible();

        private static bool Down(ConfigEntry<KeyboardShortcut> k) => k != null && k.Value.MainKey != KeyCode.None && k.Value.IsDown();

        // what the crosshair points at, up to 1 km
        private static void PingAhead()
        {
            var cam = GameCamera.instance; if (cam == null || Chat.instance == null) return;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");   // not characters: the ray would hit you first
            if (Physics.Raycast(ray, out var hit, 1000f, mask)) { Chat.instance.SendPing(hit.point); ActionToast.Show("Pinged the spot you aim at"); }
        }

        internal static void Tick()
        {
            var p = Player.m_localPlayer; if (p == null || !Free()) return;
            if (Down(Ping) && !InventoryGui.IsVisible()) PingAhead();
            if (Down(QuickStack)) { int n = InvActions.ToChests(p); ActionToast.Show(n > 0 ? $"Stacked {n} into nearby chests" : "Nothing to stack nearby", n > 0); }
            if (Down(Restock)) { int n = InvActions.Restock(p); ActionToast.Show(n > 0 ? $"Restocked {n} from nearby chests" : "Nothing to restock nearby", n > 0); }
            if (Down(Merge)) ActionToast.Show(InvActions.Merge(p) ? "Merged your stacks" : "Nothing to merge");
            if (Down(Sort)) { InvActions.Sort(p); ActionToast.Show("Sorted your inventory"); }
            if (ChestPanel.Open == null) return;
            string said = null;
            if (Down(PutAll)) said = ChestPanel.Put(false);
            if (Down(PutSimilar)) said = ChestPanel.Put(true);
            if (Down(TakeAll)) said = ChestPanel.Take(false);
            if (Down(TakeSimilar)) said = ChestPanel.Take(true);
            if (Down(ChestSort)) said = ChestPanel.DoSort();
            if (said != null) ActionToast.Show(said);
        }
    }

    // the ping's mouse button would also attack (middle mouse is Valheim's secondary attack): while the ping's
    // modifiers are held, that button doesn't reach the attacks
    [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class PingNoAttack
    {
        private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold)
        {
            if (__instance != Player.m_localPlayer || HotKeys.Ping == null) return;
            var k = HotKeys.Ping.Value; if (k.MainKey < KeyCode.Mouse0 || k.MainKey > KeyCode.Mouse2 || !k.Modifiers.Any() || !k.Modifiers.All(Input.GetKey)) return;
            if (k.MainKey == KeyCode.Mouse0) attack = attackHold = false;
            else if (k.MainKey == KeyCode.Mouse1) block = blockHold = false;
            else secondaryAttack = secondaryAttackHold = false;
        }
    }
}
