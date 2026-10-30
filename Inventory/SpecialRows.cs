using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Food / Tools / Meads (design: three labelled rows of 6 under the inventory):
    // they are reserved rows at the bottom of the same player inventory, so vanilla cells, drag and drop,
    // right-click use and equipping all keep working, and ExtraRows' save packing protects them.
    // Guards: pickups and auto-placement never land in a reserved row, empty reserved
    // cells do not count as free space, and a row only accepts its kind of item.
    internal static class SpecialRows
    {
        internal const int Count = 3, Width = 6, Rows = Count + 1;   // + the hidden equipment row
        internal static int EqRow => Start + Count;
        // Food and Meads keep 3 slots, Tools 6 (user): what sits further along those two rows is moved to the bag (ExtraRows.Rescue)
        internal const int ShortWidth = 3;
        internal static int RowWidth(int band) => band == 1 ? Width : ShortWidth;
        internal static readonly string[] Names = { "FOOD", "TOOLS", "MEADS" };
        internal static bool On => !SlotHost.Other;   // AzuEPI or ExtraSlots owns the grid: no rows of ours (it has its own equipment and quick-slot cells)
        internal static int Start => On ? ExtraRows.Base + ExtraRows.Extra.Value : Es.Drives ? Es.NormalRows : Azu.NormalRows;   // first reserved row (AzuEPI / ExtraSlots: their first slot row; the bag is above either way)
        internal static bool Is(Inventory inv, int y) => On && IsPlayer(inv) && y >= Start && y < Start + Rows;
        internal static bool IsPlayer(Inventory inv) => inv != null && Player.m_localPlayer != null && inv == Player.m_localPlayer.GetInventory();

        // tools as players mean it, also other mods' (by type and skill, not by name): hammer / hoe / cultivator, pickaxes,
        // axes, the fishing rod, torches. They land in a free Tools slot when picked up or crafted.
        internal static bool IsTool(ItemDrop.ItemData it)
        {
            if (it == null) return false; var s = it.m_shared; var k = s.m_itemType;
            return k == ItemDrop.ItemData.ItemType.Tool || k == ItemDrop.ItemData.ItemType.Torch || s.m_buildPieces != null
                || s.m_skillType == Skills.SkillType.Pickaxes || s.m_skillType == Skills.SkillType.Axes || s.m_skillType == Skills.SkillType.Fishing;
        }

        internal static bool Accepts(int row, ItemDrop.ItemData it)
        {
            if (it == null) return false;
            var s = it.m_shared; var k = s.m_itemType;
            bool food = k == ItemDrop.ItemData.ItemType.Consumable && (s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f);
            switch (row)
            {
                case 0: return food;
                case 1: return true;   // the Tools row takes anything you put there (user): a knife, a circlet, a weapon to swap to by key; IsTool goes there on its own
                case 2: return k == ItemDrop.ItemData.ItemType.Consumable && !food;
                default: return false;
            }
        }

        // keys for the reserved slots: food Z / X / C (its quick slots), meads Shift + Z / X / C (V is auto pickup), tools Alt + Z X C V B N
        // (its action slots; user: our quick slots are the tools); a key uses the item there (eat, drink, equip) as a right click would
        internal static BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut>[,] Keys;
        internal static void BindKeys(BepInEx.Configuration.ConfigFile c)
        {
            Keys = new BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut>[Count, Width];
            KeyCode[] row = { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N };
            string[] rows = { "Food", "Tool", "Mead" };
            BepInEx.Configuration.KeyboardShortcut Def(int r, int i) =>
                r == 0 && i < 3 ? new BepInEx.Configuration.KeyboardShortcut(row[i]) :
                r == 2 && i < 3 ? new BepInEx.Configuration.KeyboardShortcut(row[i], KeyCode.LeftShift) :
                r == 1 && i < row.Length ? new BepInEx.Configuration.KeyboardShortcut(row[i], KeyCode.LeftAlt) : new BepInEx.Configuration.KeyboardShortcut(KeyCode.None);
            for (int r = 0; r < Count; r++)
                for (int i = 0; i < Width; i++)
                    Keys[r, i] = c.Bind("Keys", $"{rows[r]}Slot{i + 1}", Def(r, i), $"Uses the item in {rows[r].ToLowerInvariant()} slot {i + 1}.");
            // a config still on the first defaults (meads Z / X / C, nothing else) moves to these once; anything you set stays
            bool first = true;
            for (int r = 0; r < Count && first; r++)
                for (int i = 0; i < Width && first; i++)
                {
                    var was = r == 2 && i < 3 ? row[i] : KeyCode.None; var k = Keys[r, i].Value;
                    first = k.MainKey == was && !k.Modifiers.Any();
                }
            if (first) for (int r = 0; r < Count; r++) for (int i = 0; i < Width; i++) Keys[r, i].Value = Def(r, i);
            // meads on V / B / N (a default for an hour; V is Valheim's auto pickup) move to Shift + Z / X / C
            bool vbn = true; for (int i = 0; i < 3; i++) { var k = Keys[2, i].Value; vbn &= k.MainKey == row[i + 3] && !k.Modifiers.Any(); }
            if (vbn) for (int i = 0; i < 3; i++) Keys[2, i].Value = Def(2, i);
        }

        internal static void TickKeys()
        {
            var p = Player.m_localPlayer;
            if (!On || Keys == null || p == null || InventoryGui.IsVisible() || Menu.IsVisible() || Chat.instance != null && Chat.instance.HasFocus() || Console.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen()) return;
            for (int r = 0; r < Count; r++)
                for (int i = 0; i < RowWidth(r); i++)
                {
                    var k = Keys[r, i].Value; if (!Pressed(k)) continue;
                    var it = p.GetInventory().GetItemAt(i, Start + r);
                    if (it != null) p.UseItem(p.GetInventory(), it, true);
                }
        }

        // the main key goes down with its modifiers held, whatever else is (BepInEx's IsDown fails while any other key is
        // held, so it missed while running). Several keys on one main key (Z, Shift Z, Alt Z): the one asking the most wins,
        // Alt and Ctrl over Shift, since Shift is also run (user: Shift + WASD then Alt X sat down)
        private static int Score(BepInEx.Configuration.KeyboardShortcut k)
        {
            int n = 0;
            foreach (var m in k.Modifiers)
            {
                bool held = ZInput.GetKey(m, false) || m == KeyCode.LeftAlt && ZInput.GetKey(KeyCode.RightAlt, false) || m == KeyCode.LeftShift && ZInput.GetKey(KeyCode.RightShift, false) || m == KeyCode.LeftControl && ZInput.GetKey(KeyCode.RightControl, false);
                if (!held) return -1;
                n += m == KeyCode.LeftShift || m == KeyCode.RightShift ? 1 : 2;
            }
            return n;
        }
        private static bool Mods(BepInEx.Configuration.KeyboardShortcut k)
        {
            int mine = Score(k); if (mine < 0) return false;
            for (int r = 0; r < Count; r++)
                for (int i = 0; i < Width; i++) { var o = Keys[r, i].Value; if (o.MainKey == k.MainKey && Score(o) > mine) return false; }
            return true;
        }
        private static bool Pressed(BepInEx.Configuration.KeyboardShortcut k) => k.MainKey != KeyCode.None && ZInput.GetKeyDown(k.MainKey, false) && Mods(k);

        // Valheim's own actions on a key we use (chords): while a chord's modifiers are held with its key, or a bare
        // key's slot holds something, Valheim's buttons bound to that key read as not pressed, until the key is let go
        private static readonly HashSet<KeyCode> _latched = new HashSet<KeyCode>();
        private static readonly HashSet<string> _blocked = new HashSet<string>();
        private static readonly Dictionary<KeyCode, string[]> _names = new Dictionary<KeyCode, string[]>();   // ponytail: cached per load; a vanilla rebind mid-session is picked up after a reload
        private static uint _frame = uint.MaxValue;
        private static readonly AccessTools.FieldRef<ZInput, Dictionary<string, ZInput.ButtonDef>> Buttons = AccessTools.FieldRefAccess<ZInput, Dictionary<string, ZInput.ButtonDef>>("m_buttons");
        private static readonly System.Reflection.MethodInfo ToPath = AccessTools.Method(typeof(ZInput), "KeyCodeToPath");

        internal static bool Blocks(string name)
        {
            if (!On || Keys == null || name == null) return false;   // AzuEPI's quick-slot keys are its own
            if (_frame != UnityEngine.InputSystem.LowLevel.InputState.updateCount) { _frame = UnityEngine.InputSystem.LowLevel.InputState.updateCount; Claim(); }   // per input update, not per frame: Valheim updates input mid-frame
            return _blocked.Count > 0 && _blocked.Contains(name);
        }

        private static void Claim()
        {
            _blocked.Clear(); _latched.RemoveWhere(k => !ZInput.GetKey(k, false));
            var p = Player.m_localPlayer; if (p == null) { _latched.Clear(); return; }
            for (int r = 0; r < Count; r++)
                for (int i = 0; i < RowWidth(r); i++)
                {
                    var k = Keys[r, i].Value; if (k.MainKey == KeyCode.None || _latched.Contains(k.MainKey) || !ZInput.GetKey(k.MainKey, false) || !Mods(k)) continue;
                    if ((k.Modifiers.Any() || p.GetInventory().GetItemAt(i, Start + r) != null) && _latched.Add(k.MainKey) && Dev.Build) Log.Info($"slot keys: {k} claims {k.MainKey}");
                }
            foreach (var k in _latched) foreach (var n in ButtonsOn(k)) _blocked.Add(n);
        }

        private static string[] ButtonsOn(KeyCode key)
        {
            if (_names.TryGetValue(key, out var v)) return v;
            var list = new List<string>(); var all = ZInput.instance != null ? Buttons(ZInput.instance) : null;
            var path = ToPath?.Invoke(null, new object[] { key, false }) as string;
            if (all != null && !string.IsNullOrEmpty(path))
                foreach (var kv in all)
                {
                    if (kv.Key.StartsWith("Joy")) continue;
                    string ap; try { ap = kv.Value.GetActionPath(true); } catch { continue; }
                    if (string.Equals(ap, path, System.StringComparison.OrdinalIgnoreCase)) list.Add(kv.Key);
                }
            if (Dev.Build) Log.Info($"slot keys: {key} ({path}) blocks {string.Join(", ", list)}");
            return _names[key] = list.ToArray();
        }
    }

    [HarmonyPatch(typeof(ZInput))]
    internal static class SlotKeysBlock
    {
        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetButtonDown))] private static bool Down(string name, ref bool __result) => Let(name, ref __result);
        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetButton))] private static bool Held(string name, ref bool __result) => Let(name, ref __result);
        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetButtonUp))] private static bool Up(string name, ref bool __result) => Let(name, ref __result);
        // and one level down (ZInput's own lookup): the three above are small enough to be inlined into Valheim's code
        // that was compiled before a hot reload patched them
        [HarmonyPrefix, HarmonyPatch("TryGetButtonState")] private static bool Lookup(string name, ref bool __result) => Let(name, ref __result);
        private static float _said;
        private static bool Let(string name, ref bool __result)
        {
            if (!SpecialRows.Blocks(name)) return true; __result = false;
            if (Dev.Build && Time.unscaledTime - _said > 0.5f) { _said = Time.unscaledTime; Log.Info($"slot keys: blocked {name}"); }
            return false;
        }
    }

    // Worn armour leaves the grid (user's request): head, chest, legs, cape, belt and trinket
    // sit in a hidden reserved row of the same player inventory while equipped, so Valheim's own equip
    // logic, saving (ExtraRows packs this row like the others), death and a game without the mod all keep
    // working. Taking one off puts it in the first free normal cell; with no room it waits in its hidden
    // cell, still in the inventory, and comes out as soon as there is space. Hand items stay on the hotbar.
    internal static class EqRow
    {
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData>[] Worn =
        {
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem"), AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem"),
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem"), AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem"),
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem"), AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_trinketItem"),
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_ammoItem"),   // the equipped ammo stack too (it shows on the doll; spares stay in the bag)
        };   // cells 0-6; the last one (7) is ModSlots' lift cell
        private static readonly System.Action<Inventory> Changed = inv => AccessTools.Method(typeof(Inventory), "Changed").Invoke(inv, new object[] { false, false });
        private static float _next;

        private static bool Ready(Player p) => SpecialRows.On && p != null && p.GetInventory().GetHeight() >= SpecialRows.Start + SpecialRows.Rows;

        // every worn piece into its cell; anything unworn in the row back out
        internal static void Park(Player p)
        {
            if (!Ready(p)) return;
            var inv = p.GetInventory(); int y = SpecialRows.EqRow; bool moved = false;
            for (int x = 0; x < Worn.Length; x++)
            {
                var it = Worn[x](p);
                if (it == null || !inv.ContainsItem(it) || (it.m_gridPos.x == x && it.m_gridPos.y == y)) continue;
                var there = inv.GetItemAt(x, y);
                if (there != null) there.m_gridPos = it.m_gridPos;          // a stale occupant swaps into the worn piece's old cell
                it.m_gridPos = new Vector2i(x, y); moved = true;
            }
            foreach (var it in inv.GetAllItems())
            {
                if (it.m_gridPos.y != y || p.IsItemEquiped(it) || it.m_gridPos.x == ModSlots.Stage.x) continue;   // the last cell is ModSlots' lift cell
                var free = FreeCell(inv);
                if (free.x < 0) continue;                                 // no room: it waits here, still in the inventory
                it.m_gridPos = free; moved = true;
            }
            if (moved) Changed(inv);
        }

        // bag rows first, the hotbar last; never a locked cell or a reserved row
        private static Vector2i FreeCell(Inventory inv)
        {
            for (int n = 0; n < SpecialRows.Start; n++)
            {
                int y = n + 1 < SpecialRows.Start ? n + 1 : 0;
                for (int x = 0; x < inv.GetWidth(); x++) if (inv.GetItemAt(x, y) == null && !InvActions.Locked(x, y)) return new Vector2i(x, y);
            }
            return new Vector2i(-1, -1);
        }

        internal static void Tick()
        {
            var p = Player.m_localPlayer; if (p == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Park(p);
            if (Ready(p) && ExtraRows.Rescue(p.GetInventory()) > 0) Changed(p.GetInventory());   // items a reserved row does not take go to the bag as soon as there is room
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class EqRowEquipPatch { private static void Postfix(Humanoid __instance, bool __result) { if (__result && __instance == Player.m_localPlayer) EqRow.Park((Player)__instance); } }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    internal static class EqRowUnequipPatch
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {   // ponytail: temporary, dev only (user: the wisp comes off now and then on opening/closing the inventory) - who took it off
            if (Dev.Build && item != null && __instance == Player.m_localPlayer && __instance.IsItemEquiped(item))
                Log.Info($"unequip {item.m_shared.m_name} (inventory {(InventoryGui.IsVisible() ? "open" : "closed")}) from:\n{string.Join("\n", new System.Diagnostics.StackTrace(1).GetFrames().Take(8).Select(f => "  " + f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name))}");
        }
        private static void Postfix(Humanoid __instance) { if (__instance == Player.m_localPlayer) EqRow.Park((Player)__instance); }
    }

    [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
    internal static class FindEmptySlotPatch
    {
        internal enum Order { Vanilla, UnderHotbar }
        internal static BepInEx.Configuration.ConfigEntry<Order> Pickup;

        [System.ThreadStatic] internal static ItemDrop.ItemData Adding;   // the item Inventory.AddItem is placing (AddingPatch)

        private static void Postfix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            var me = Player.m_localPlayer;
            if (Adding != null && SpecialRows.IsPlayer(__instance) && SpecialRows.On && SpecialRows.IsTool(Adding))
            {   // a tool picked up or crafted: a free Tools slot first
                int ty = SpecialRows.Start + 1;
                for (int x = 0; x < SpecialRows.Width; x++) if (__instance.GetItemAt(x, ty) == null && !InvActions.Locked(x, ty)) { __result = new Vector2i(x, ty); return; }
            }
            if (Pickup != null && Pickup.Value == Order.UnderHotbar && me != null && __instance == me.GetInventory())
            {
                if (!SpecialRows.On && __result.y >= SpecialRows.Start) return;   // AzuEPI chose one of its equipment / quick cells (it equips there): its call   // PickupOrder: the bag rows first, the hotbar last, never a locked slot or a reserved row
                int rows = SpecialRows.Start; __result = new Vector2i(-1, -1);
                for (int n = 1; n <= rows; n++)
                {
                    int y = n % rows;   // 1, 2, ..., rows-1, then 0 (the hotbar)
                    for (int x = 0; x < __instance.GetWidth(); x++)
                        if (__instance.GetItemAt(x, y) == null && !InvActions.Locked(x, y)) { __result = new Vector2i(x, y); return; }
                }
                return;
            }
            if (__result.x < 0 || !SpecialRows.Is(__instance, __result.y)) return;
            int top = SpecialRows.Start;                                    // search the normal rows only
            __result = new Vector2i(-1, -1);
            for (int n = 0; n < top; n++)
            {
                int y = topFirst ? n : top - 1 - n;
                for (int x = 0; x < __instance.GetWidth(); x++)
                    if (__instance.GetItemAt(x, y) == null) { __result = new Vector2i(x, y); return; }
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
    internal static class AddingPatch
    {
        private static void Prefix(ItemDrop.ItemData item) => FindEmptySlotPatch.Adding = item;
        private static System.Exception Finalizer(System.Exception __exception) { FindEmptySlotPatch.Adding = null; return __exception; }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
    internal static class EmptySlotsPatch
    {
        private static void Postfix(Inventory __instance, ref int __result)
        {
            if (!SpecialRows.IsPlayer(__instance)) return;
            int reservedEmpty = 0;   // reserved rows and locked cells: empty, but no item is ever placed there
            for (int y = 0; y < __instance.GetHeight(); y++)
                for (int x = 0; x < __instance.GetWidth(); x++)
                    if (__instance.GetItemAt(x, y) == null && (SpecialRows.On ? y >= SpecialRows.Start || InvActions.Locked(x, y) : y < SpecialRows.Start && InvActions.Locked(x, y))) reservedEmpty++;   // AzuEPI counts its own cells itself
            __result = Mathf.Max(0, __result - reservedEmpty);
        }
    }

    // Valheim's room check counts every empty cell, ours included, so crafting started with a full bag and
    // then dropped the result (stackables) or made nothing. For the player: free stack space + empty normal
    // cells only (GetEmptySlots is patched above).
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
    internal static class CanAddItemPatch
    {
        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
        {
            if (!__result || item == null || !SpecialRows.IsPlayer(__instance)) return;
            if (stack <= 0) stack = item.m_stack;
            __result = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel) + __instance.GetEmptySlots() * item.m_shared.m_maxStackSize >= stack;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
    internal static class HaveEmptySlotPatch
    {
        private static void Postfix(Inventory __instance, ref bool __result) { if (__result && SpecialRows.IsPlayer(__instance)) __result = __instance.GetEmptySlots() > 0; }
    }

    // a drop onto a taken cell is a swap, done by Valheim in three steps (take ours out, move theirs to our old cell, put ours
    // in). The row guard below refuses a step, but by then the first item was already out: it vanished. So the swap is
    // checked whole, both ways, before it starts: refused, nothing moves (and the item stays in hand).
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class SwapIntoSpecialPatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
        {
            var to = __instance.GetInventory(); if (to == null || item == null) return true;
            var there = to.GetItemAt(pos.x, pos.y);
            bool ok = Fits(to, pos.x, pos.y, item) && (there == null || there == item || Fits(fromInventory, item.m_gridPos.x, item.m_gridPos.y, there));
            if (ok) return true;
            __result = false; return false;
        }

        private static bool Fits(Inventory inv, int x, int y, ItemDrop.ItemData it) => !SpecialRows.Is(inv, y) || (x < SpecialRows.RowWidth(y - SpecialRows.Start) && SpecialRows.Accepts(y - SpecialRows.Start, it));
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
    internal static class MoveIntoSpecialPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int x, int y, ref bool __result)
        {
            if (!SpecialRows.Is(__instance, y)) return true;
            if (x < SpecialRows.RowWidth(y - SpecialRows.Start) && SpecialRows.Accepts(y - SpecialRows.Start, item)) return true;
            __result = false; return false;                                  // not this row's kind: nothing moves
        }
    }
}
