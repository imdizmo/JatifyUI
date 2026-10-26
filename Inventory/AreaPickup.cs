using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Area pickup: hold the area key (Left Shift) and use a berry bush, crop,
    // mushroom or beehive: everything pickable of that kind (or any kind) around it is picked as if you used
    // each one. Beehives only inside wards you may use. Steps aside when PlantEasily is installed (it has
    // its own bulk harvest).
    [HarmonyPatch]
    internal static class AreaPickup
    {
        internal static ConfigEntry<bool> On, SameOnly;
        internal static ConfigEntry<float> Radius;
        internal static ConfigEntry<KeyboardShortcut> Key;
        private static readonly Func<Beehive, int> Honey = AccessTools.MethodDelegate<Func<Beehive, int>>(AccessTools.Method(typeof(Beehive), "GetHoneyLevel"));
        private static Collider[] _hits = new Collider[256];
        private static readonly HashSet<int> _seen = new HashSet<int>();
        private static readonly List<Interactable> _found = new List<Interactable>(64);
        private static int _mask; private static float _heldAt = -10f;
        private static bool? _plantEasily;

        internal static void Bind(ConfigFile c)
        {
            On = c.Bind("Automation", "AreaPickup", true, "Hold the area key and use a berry bush, crop, mushroom or beehive to pick everything around it.");
            Radius = c.Bind("Automation", "AreaRadius", 3f, new ConfigDescription("How far around the target area pickup reaches, in metres.", new AcceptableValueRange<float>(1f, 10f)));
            SameOnly = c.Bind("Automation", "AreaSameOnly", false, "Area pickup only takes the same kind as the one you use (only raspberries around a raspberry bush).");
            Key = c.Bind("Keys", "AreaPickup", new KeyboardShortcut(KeyCode.LeftShift), "Held with the use key for area pickup.");
        }

        private static bool Active
        {
            get
            {
                if (_plantEasily == null) _plantEasily = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("advize.PlantEasily");
                return On.Value && _plantEasily == false;
            }
        }

        private static bool Held() { var k = Key.Value; if (k.MainKey == KeyCode.None) return false; if (!Input.GetKey(k.MainKey)) return false; foreach (var m in k.Modifiers) if (!Input.GetKey(m)) return false; return true; }

        // the key may be let go a moment before the use key lands
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "Update")]
        private static void Watch(Player __instance) { if (__instance == Player.m_localPlayer && Held()) _heldAt = Time.unscaledTime; }

        [HarmonyPrefix, HarmonyPatch(typeof(Player), "Interact")]
        private static void Interact(Player __instance, GameObject go, bool hold, bool alt)
        {
            if (hold || go == null || __instance != Player.m_localPlayer || !Active) return;
            if ((!Held() && Time.unscaledTime - _heldAt > 0.15f) || __instance.InAttack() || __instance.InDodge()) return;
            var target = go.GetComponentInParent<Interactable>();
            if (!(target is Pickable) && !(target is Beehive)) return;
            Collect((Component)target);
            foreach (var f in _found) f.Interact(__instance, false, alt);   // the target itself is used by vanilla right after
            _found.Clear();
        }

        private static void Collect(Component target)
        {
            _found.Clear(); _seen.Clear();
            if (_mask == 0) _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "item");
            var at = target.transform.position; float r = Radius.Value;
            int n = Physics.OverlapSphereNonAlloc(at, r, _hits, _mask);
            while (n == _hits.Length && _hits.Length < 8192) { _hits = new Collider[_hits.Length * 2]; n = Physics.OverlapSphereNonAlloc(at, r, _hits, _mask); }
            string kind = SameOnly.Value ? Utils.GetPrefabName(target.gameObject) : null;
            _seen.Add(target.GetInstanceID());
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i]; _hits[i] = null; if (col == null) continue;
                var pick = col.GetComponentInParent<Pickable>(); var hive = pick == null ? col.GetComponentInParent<Beehive>() : null;
                Component it = pick != null ? (Component)pick : hive; if (it == null) continue;
                if (!_seen.Add(it.GetInstanceID()) || (kind != null && Utils.GetPrefabName(it.gameObject) != kind)) continue;
                if (pick != null) { if (pick.CanBePicked()) _found.Add(pick); }
                else if (Honey(hive) > 0 && PrivateArea.CheckAccess(hive.transform.position, 0f, false, false)) _found.Add(hive);
            }
        }

        // the hover card shows the area action too
        private static string Hint() => Localization.instance.Localize($"\n[<color=yellow><b>{Key.Value} + $KEY_Use</b></color>] Pick all around");

        [HarmonyPostfix, HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
        private static void PickHover(Pickable __instance, ref string __result) { if (Active && __instance.CanBePicked() && !string.IsNullOrEmpty(__result)) __result += Hint(); }

        [HarmonyPostfix, HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
        private static void HiveHover(Beehive __instance, ref string __result) { if (Active && Honey(__instance) > 0 && !string.IsNullOrEmpty(__result)) __result += Hint(); }
    }
}
