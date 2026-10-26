using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Your grave (settings under Grave, user: several options). Picking it up puts you back as you were (ReequipGear, ReequipWhat):
    // Valheim already returns every item to its cell (Inventory.MoveAll), but it takes everything off before making the grave, so
    // worn items are marked just before that and put on again once the grave's items are back. PickupOnTouch takes it when you
    // walk into it; CompassMarker is in Compass.
    [HarmonyPatch]
    internal static class Graves
    {
        internal const string Mark = "jatify.eq";
        private static readonly AccessTools.FieldRef<Character, float> SwimTimer = AccessTools.FieldRefAccess<Character, float>("m_swimTimer");

        [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        private static void MarkWorn(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            foreach (var it in __instance.GetInventory().GetAllItems())
                if (it.m_equipped) it.m_customData[Mark] = "1";
                else it.m_customData.Remove(Mark);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Container), "RPC_TakeAllResponse")]
        private static void Reequip(Container __instance, bool granted)
        {
            var p = Player.m_localPlayer;
            if (!granted || p == null || __instance.GetComponent<TombStone>() == null) return;
            float swim = SwimTimer(p); SwimTimer(p) = 999f;   // Valheim won't equip while swimming
            try
            {
                foreach (var it in p.GetInventory().GetAllItems().Where(i => i.m_customData.ContainsKey(Mark)).ToList())
                {
                    it.m_customData.Remove(Mark);
                    if (!Cfg.GraveReequip.Value || !Wanted(it) || p.IsItemEquiped(it)) continue;
                    try { p.EquipItem(it, false); }
                    catch (System.Exception e) { Log.Warn($"grave: putting {it.m_shared.m_name} back on failed ({e.Message}); left in the bag"); }
                }
            }
            finally { SwimTimer(p) = swim; }
            ModSlots.Restore(p);   // wishbone, wisplight and keys back into our slots
        }

        private static bool Wanted(ItemDrop.ItemData it)
        {
            var what = Cfg.GraveReequipWhat.Value; if (what == Cfg.Regear.Everything) return true;
            switch (it.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Helmet: case ItemDrop.ItemData.ItemType.Chest: case ItemDrop.ItemData.ItemType.Legs: case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shoulder: case ItemDrop.ItemData.ItemType.Utility: case ItemDrop.ItemData.ItemType.Trinket: return true;
                default: return what == Cfg.Regear.ArmourAndWeapons;
            }
        }

        // PickupOnTouch: your own grave within reach is taken as if you used it (Valheim's own TombStone.Interact), once a second at most
        private static float _next; private static readonly Collider[] _hits = new Collider[32];
        private static bool Mine(TombStone t)
        {
            var nv = t.GetComponent<ZNetView>(); return nv != null && nv.IsValid() && Game.instance != null && nv.GetZDO().GetLong(ZDOVars.s_owner) == Game.instance.GetPlayerProfile().GetPlayerID();
        }

        internal static void Tick()
        {
            if (!Cfg.GraveTouch.Value || Time.time < _next) return; _next = Time.time + 0.5f;
            var p = Player.m_localPlayer; if (p == null || p.IsDead() || p.InPlaceMode() || InventoryGui.IsVisible()) return;
            int n = Physics.OverlapSphereNonAlloc(p.transform.position + Vector3.up * 0.5f, 1.6f, _hits);
            for (int i = 0; i < n; i++)
            {
                var tomb = _hits[i] != null ? _hits[i].GetComponentInParent<TombStone>() : null;
                if (tomb == null || !Mine(tomb)) continue;
                tomb.Interact(p, false, false); _next = Time.time + 1.5f; return;
            }
        }
    }
}
