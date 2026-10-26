using HarmonyLib;

namespace Jatify.UI
{
    // Instant equip (user's choice): Valheim queues armour with an equip duration behind a progress bar;
    // with InstantEquip on, the local player equips / unequips at once instead, like drag-to-equip.
    [HarmonyPatch(typeof(Player), "QueueEquipAction")]
    internal static class InstantEquipPatch
    {
        private static bool Prefix(Player __instance, ItemDrop.ItemData item)
        {
            if (item == null || __instance != Player.m_localPlayer || !Cfg.InstantEquip.Value) return true;
            __instance.EquipItem(item, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "QueueUnequipAction")]
    internal static class InstantUnequipPatch
    {
        private static bool Prefix(Player __instance, ItemDrop.ItemData item)
        {
            if (item == null || __instance != Player.m_localPlayer || !Cfg.InstantEquip.Value) return true;
            __instance.UnequipItem(item, true);
            return false;
        }
    }
}
