using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Coin weight (user: a switch and an amount, zero allowed): with LighterCoins on, each coin weighs CoinWeight kg instead of
    // Valheim's 0.1. Computed on your game only, so it changes your own carry weight (and a cart you pull while it's yours); nothing is saved.
    [HarmonyPatch(typeof(ItemDrop.ItemData))]
    internal static class CoinWeight
    {
        internal static ConfigEntry<bool> On;
        internal static ConfigEntry<float> Each;
        private const string Coins = "$item_coins";
        private static readonly Action<Inventory> Recount = AccessTools.MethodDelegate<Action<Inventory>>(AccessTools.Method(typeof(Inventory), "UpdateTotalWeight"));

        internal static void Bind(ConfigFile c)
        {
            On = c.Bind("Inventory", "LighterCoins", true, "Coins weigh what CoinWeight says instead of Valheim's 0.1 each. Only your own carry weight.");
            Each = c.Bind("Inventory", "CoinWeight", 0.01f, new ConfigDescription("Weight of one coin in kg while LighterCoins is on (0 = weightless; Valheim's is 0.1).", new AcceptableValueRange<float>(0f, 0.1f)));
            On.SettingChanged += Changed; Each.SettingChanged += Changed;
        }

        private static void Changed(object _, EventArgs __) { var p = Player.m_localPlayer; if (p != null) Recount(p.GetInventory()); }
        private static float PerCoin => On == null || !On.Value ? -1f : Mathf.Clamp(Each.Value, 0f, 0.1f);

        [HarmonyPostfix, HarmonyPatch(nameof(ItemDrop.ItemData.GetWeight))]
        private static void Weight(ItemDrop.ItemData __instance, int stackOverride, ref float __result)
        {
            float each = PerCoin; if (each < 0f || __instance.m_shared.m_name != Coins) return;
            __result = each * (stackOverride >= 0 ? stackOverride : __instance.m_stack);
        }

        [HarmonyPostfix, HarmonyPatch(nameof(ItemDrop.ItemData.GetNonStackedWeight))]
        private static void One(ItemDrop.ItemData __instance, ref float __result) { float each = PerCoin; if (each >= 0f && __instance.m_shared.m_name == Coins) __result = each; }
    }
}
