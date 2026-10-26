using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Ore detection (off by default): deposits you pass within
    // the radius get a saved map pin of the Ore kind named after the ore, dungeon entrances a Dungeon pin, unless
    // a pin is already that close. A deposit mined away while you're near crosses its pin off.
    // ponytail: no "share of ore left" label and no line-of-sight check; add them by reading hit points and a raycast.
    [HarmonyPatch]
    internal static class OreScout
    {
        internal static ConfigEntry<bool> On, Dungeons;
        internal static ConfigEntry<float> Radius;
        private const string Key = "jatify.ores";   // the pins we placed, "x:z;..." (to cross them off later)
        private static readonly HashSet<Component> _rocks = new HashSet<Component>();
        private static readonly HashSet<Location> _places = new HashSet<Location>();
        private static readonly Dictionary<Component, string> _names = new Dictionary<Component, string>();
        private static float _next, _settleUntil; private static Player _seenPlayer; private static Vector3 _lastPos;

        internal static void Bind(ConfigFile c)
        {
            On = c.Bind("Map", "OreDetect", false, "Ore deposits you pass get a map pin named after the ore; crossed off when mined away.");
            Dungeons = c.Bind("Map", "DungeonDetect", false, "Dungeon entrances you pass get a Dungeon pin.");
            Radius = c.Bind("Map", "OreRadius", 25f, new ConfigDescription("How close a deposit must be to be found, in metres.", new AcceptableValueRange<float>(10f, 60f)));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MineRock5), "Awake")] private static void Rock5(MineRock5 __instance) { if (On.Value) _rocks.Add(__instance); }
        [HarmonyPostfix, HarmonyPatch(typeof(MineRock), "Start")] private static void Rock(MineRock __instance) { if (On.Value) _rocks.Add(__instance); }
        [HarmonyPostfix, HarmonyPatch(typeof(Destructible), "Awake")] private static void Deposit(Destructible __instance) { if (On.Value && OreName(__instance) != null) _rocks.Add(__instance); }
        [HarmonyPostfix, HarmonyPatch(typeof(Location), "Awake")] private static void Place(Location __instance) { if (Dungeons.Value && __instance.m_hasInterior) _places.Add(__instance); }

        private static bool IsOre(string shared) { var s = shared.ToLowerInvariant(); return s.EndsWith("ore") || s.Contains("ore_") || s.EndsWith("scrap"); }

        private static string FromTable(DropTable t)
        {
            if (t?.m_drops == null) return null;
            foreach (var d in t.m_drops) { var drop = d.m_item != null ? d.m_item.GetComponent<ItemDrop>() : null; if (drop != null && IsOre(drop.m_itemData.m_shared.m_name)) return Localization.instance.Localize(drop.m_itemData.m_shared.m_name); }
            return null;
        }

        private static string OreName(Component c)
        {
            if (c is Destructible d)
            {
                var ht = d.GetComponent<HoverText>(); var txt = ht != null ? ht.m_text : "";
                var low = (txt ?? "").ToLowerInvariant();
                if (low.Contains("deposit") && !low.Contains("obsidian") && !low.Contains("flint")) return Localization.instance.Localize(txt);
                var dod = d.GetComponent<DropOnDestroyed>(); return dod != null ? FromTable(dod.m_dropWhenDestroyed) : null;
            }
            if (c is MineRock5 m5) return FromTable(m5.m_dropItems);
            if (c is MineRock m) return FromTable(m.m_dropItems);
            return null;
        }

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        internal static void Tick()
        {
            var p = Player.m_localPlayer; var mm = Minimap.instance;
            if (p != _seenPlayer || (p != null && (p.transform.position - _lastPos).sqrMagnitude > 100f * 100f)) { _seenPlayer = p; _settleUntil = Time.time + 6f; }   // spawned or teleported: rocks may not be loaded yet
            if (p != null) _lastPos = p.transform.position;
            if (!On.Value && !Dungeons.Value) { _rocks.Clear(); _places.Clear(); _names.Clear(); }   // off: hold nothing
            if (p == null || mm == null || (!On.Value && !Dungeons.Value) || Time.time < _next) return;
            _next = Time.time + 1.5f;
            _rocks.RemoveWhere(r => r == null); _places.RemoveWhere(l => l == null);
            var me = p.transform.position; float r = Radius.Value;
            var pins = Pins(mm);
            if (On.Value)
            {
                foreach (var rock in _rocks)
                {
                    if ((rock.transform.position - me).sqrMagnitude > r * r) continue;
                    if (!_names.TryGetValue(rock, out var name)) _names[rock] = name = OreName(rock);
                    if (name == null || PinNear(pins, rock.transform.position, 12f)) continue;
                    Add(p, mm, rock.transform.position, name, 1);   // kind 1 = Ore
                }
                if (Time.time > _settleUntil) CrossOffMined(p, pins, me);
            }
            if (Dungeons.Value)
                foreach (var loc in _places)
                {
                    var at = loc.transform.position; if (at.y > 3000f || Utils.DistanceXZ(at, me) > r + loc.m_exteriorRadius) continue;
                    if (PinNear(pins, at, loc.m_exteriorRadius + 8f)) continue;
                    Add(p, mm, at, Localization.instance.Localize(loc.GetComponentInChildren<Teleport>()?.m_enterText ?? "Dungeon"), 2);   // kind 2 = Dungeon
                }
            foreach (var k in _names.Keys.Where(k => k == null).ToList()) _names.Remove(k);   // bounded: only loaded rocks
        }

        private static bool PinNear(List<Minimap.PinData> pins, Vector3 at, float d) { foreach (var q in pins) if (q.m_save && Utils.DistanceXZ(q.m_pos, at) < d) return true; return false; }

        private static void Add(Player p, Minimap mm, Vector3 at, string name, int kind)
        {
            var pin = mm.AddPin(at, Minimap.PinType.Icon3, name, true, false, 0L);
            if (pin == null) return;
            PinKinds.Set(pin, kind);
            if (kind == 1) { var list = Placed(p); list.Add(Rounded(at)); p.m_customData[Key] = string.Join(";", list); }
            ActionToast.Show($"Found {name}");
        }

        private static string Rounded(Vector3 v) => Mathf.RoundToInt(v.x) + ":" + Mathf.RoundToInt(v.z);
        private static List<string> Placed(Player p) => p.m_customData.TryGetValue(Key, out var s) && s.Length > 0 ? s.Split(';').ToList() : new List<string>();

        // a pin we placed, near you (its rock would be loaded), with no deposit left there: crossed off
        private static void CrossOffMined(Player p, List<Minimap.PinData> pins, Vector3 me)
        {
            var list = Placed(p); if (list.Count == 0) return; bool changed = false;
            foreach (var pin in pins)
            {
                if (!pin.m_save || pin.m_checked || Utils.DistanceXZ(pin.m_pos, me) > 40f || !list.Contains(Rounded(pin.m_pos))) continue;
                bool left = false; foreach (var rock in _rocks) if (Utils.DistanceXZ(rock.transform.position, pin.m_pos) < 3f) { left = true; break; }
                if (left) continue;
                pin.m_checked = true; list.Remove(Rounded(pin.m_pos)); changed = true;
            }
            if (changed) p.m_customData[Key] = string.Join(";", list);
        }

        internal static void Clear() { _rocks.Clear(); _places.Clear(); _names.Clear(); }
    }
}
