using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Time-left lines in the hover card: plants (grown in), fermenters (ready in),
    // beehives (next honey in), smelters / kilns / refineries (next one in, all done in), cooking stations and ovens
    // (ready in, burns in). Read-only.
    [HarmonyPatch]
    internal static class Timers
    {
        internal static ConfigEntry<bool> On;
        private static readonly Func<Plant, double> Since = AccessTools.MethodDelegate<Func<Plant, double>>(AccessTools.Method(typeof(Plant), "TimeSincePlanted"));
        private static readonly Func<Plant, float> GrowTime = AccessTools.MethodDelegate<Func<Plant, float>>(AccessTools.Method(typeof(Plant), "GetGrowTime"));
        private static readonly Func<Fermenter, double> Fermented = AccessTools.MethodDelegate<Func<Fermenter, double>>(AccessTools.Method(typeof(Fermenter), "GetFermentationTime"));
        private static readonly AccessTools.FieldRef<Plant, ZNetView> PView = AccessTools.FieldRefAccess<Plant, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Beehive, ZNetView> BView = AccessTools.FieldRefAccess<Beehive, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Smelter, ZNetView> SView = AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Fermenter, ZNetView> FView = AccessTools.FieldRefAccess<Fermenter, ZNetView>("m_nview");

        internal static void Bind(ConfigFile c) => On = c.Bind("HUD", "Timers", true, "Time left for plants, fermenters, beehives and smelters, in their hover card.");

        private static string Span(double s)
        {
            if (s <= 0) return "a moment";
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";
        }

        private static bool Ok(ZNetView nv) => On.Value && nv != null && nv.IsValid() && ZNet.instance != null;

        [HarmonyPostfix, HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
        private static void PlantHover(Plant __instance, ref string __result)
        {
            if (!Ok(PView(__instance)) || string.IsNullOrEmpty(__result)) return;
            double left = GrowTime(__instance) - Since(__instance);
            __result += "\nGrown in " + Span(left);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
        private static void FermenterHover(Fermenter __instance, ref string __result)
        {
            if (!Ok(FView(__instance)) || string.IsNullOrEmpty(__result)) return;
            double done = Fermented(__instance); if (done < 0) return;   // empty
            double left = __instance.m_fermentationDuration - done; if (left <= 0) return;
            __result += "\nReady in " + Span(left);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
        private static void HiveHover(Beehive __instance, ref string __result)
        {
            var nv = BView(__instance); if (!Ok(nv) || string.IsNullOrEmpty(__result)) return;
            var z = nv.GetZDO(); if (z.GetInt(ZDOVars.s_level) >= __instance.m_maxHoney) return;
            __result += "\nNext honey in " + Span(__instance.m_secPerUnit - z.GetFloat(ZDOVars.s_product));
        }

        private static readonly AccessTools.FieldRef<CookingStation, ZNetView> CView = AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");
        private static readonly System.Reflection.MethodInfo Conversion = AccessTools.Method(typeof(CookingStation), "GetItemConversion");

        // cooking station, stone oven, iron cooking station: the soonest ready, the soonest to burn (counts only while it heats)
        [HarmonyPostfix, HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
        private static void CookHover(CookingStation __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += CookLines(__instance); }

        // ovens hover through their food switch (fixed text, CookingStation.GetHoverText says nothing): the same lines there,
        // plus the Auto status
        [HarmonyPostfix, HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
        private static void OvenSwitchHover(Switch __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            var cs = __instance.GetComponentInParent<CookingStation>(); if (cs == null || cs.m_addFoodSwitch != __instance) return;
            __result = Localization.instance.Localize(__result) + CookLines(cs) + Localization.instance.Localize(Smelters.AutoLine(cs));
        }

        private static string CookLines(CookingStation __instance)
        {
            var nv = CView(__instance); if (!Ok(nv)) return "";
            string __result = "";
            var z = nv.GetZDO(); double ready = double.MaxValue, burn = double.MaxValue;
            for (int i = 0; i < __instance.m_slots.Length; i++)
            {
                var name = z.GetString("slot" + i); if (string.IsNullOrEmpty(name)) continue;
                var conv = Conversion.Invoke(__instance, new object[] { name }) as CookingStation.ItemConversion; if (conv == null) continue;
                float cooked = z.GetFloat("slot" + i); int status = z.GetInt("slotstatus" + i);   // 0 cooking, 1 done, 2 burnt
                if (status == 0) ready = Math.Min(ready, conv.m_cookTime - cooked);
                else if (status == 1 && __instance.m_canOvercookItems) burn = Math.Min(burn, conv.m_cookTime * 2f - cooked);
            }
            if (ready < double.MaxValue) __result += "\nReady in " + Span(ready);
            if (burn < double.MaxValue) __result += "\nBurns in " + Span(burn);
            return __result;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Smelter), "OnHoverAddOre")]
        private static void SmelterHover(Smelter __instance, ref string __result)
        {
            var nv = SView(__instance); if (!Ok(nv) || string.IsNullOrEmpty(__result)) return;
            var z = nv.GetZDO(); int queued = z.GetInt(ZDOVars.s_queued); if (queued <= 0) return;
            double next = __instance.m_secPerProduct - z.GetFloat(ZDOVars.s_bakeTimer);
            __result += "\nNext in " + Span(next) + (queued > 1 ? " · all in " + Span(next + (queued - 1) * __instance.m_secPerProduct) : "");
        }
    }
}
