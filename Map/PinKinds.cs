using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // The design's extra pin kinds (Ore, Dungeon, Cave, ...). The pin itself stays a vanilla Pin (Icon3), so
    // without the mod nothing breaks and nothing is lost; which pin wears which kind is kept per character in
    // m_customData["jatify.pins"] ("x:z:kind;..." by rounded position).
    internal static class PinKinds
    {
        internal static readonly string[] Names = { "Ore", "Dungeon", "Cave", "Ship", "Loot", "Farm", "Berries", "Fish", "Danger", "Waypoint" };   // kind = index + 1
        private const string Key = "jatify.pins";
        private static readonly Dictionary<long, int> _kind = new Dictionary<long, int>();
        internal static readonly HashSet<int> Hidden = new HashSet<int>();   // kinds switched off on the map (session)
        private static Player _for;

        internal static Sprite Icon(int kind) => kind > 0 && kind <= Names.Length ? Kit.Icon("pin-" + Names[kind - 1].ToLowerInvariant()) : null;

        private static long KeyOf(Vector3 p) => ((long)Mathf.RoundToInt(p.x) << 32) ^ (uint)Mathf.RoundToInt(p.z);

        internal static int Of(Minimap.PinData p)
        {
            if (p == null || p.m_type != Minimap.PinType.Icon3) return 0;
            Ensure();
            return _kind.TryGetValue(KeyOf(p.m_pos), out var k) ? k : 0;
        }

        internal static void Set(Minimap.PinData p, int kind)
        {
            if (p == null) return;
            Ensure();
            var k = KeyOf(p.m_pos);
            if (kind > 0) { p.m_type = Minimap.PinType.Icon3; _kind[k] = kind; } else _kind.Remove(k);
            Save();
            Paint(p);
        }

        // a pin of yours went away; shared ones are left alone (vanilla re-syncs those by removing and re-adding)
        internal static void Forget(Minimap.PinData p) { if (p == null || p.m_type != Minimap.PinType.Icon3 || !p.m_save || p.m_ownerID != 0L) return; Ensure(); if (_kind.Remove(KeyOf(p.m_pos))) Save(); }

        // the kind's icon on the pin (vanilla draws m_icon; we swap it after vanilla made the marker)
        internal static void Paint(Minimap.PinData p)
        {
            var sp = Icon(Of(p)); if (sp == null) return;
            if (p.m_icon != sp) p.m_icon = sp;
            if (p.m_iconElement != null && p.m_iconElement.sprite != sp) p.m_iconElement.sprite = sp;
        }

        private static void Ensure()
        {
            var me = Player.m_localPlayer; if (me == _for) return;
            _for = me; _kind.Clear(); if (me == null) return;
            if (me.m_customData.TryGetValue(Key, out var ours)) Parse(ours);
        }

        private static void Parse(string data)
        {
            foreach (var entry in data.Split(';'))
            {
                var f = entry.Split(':');
                if (f.Length < 3 || !int.TryParse(f[0], out var x) || !int.TryParse(f[1], out var z) || !int.TryParse(f[2], out var k)) continue;
                if (k > 0 && k <= Names.Length) _kind[((long)x << 32) ^ (uint)z] = k;
            }
        }

        private static void Save()
        {
            if (_for == null) return;
            var sb = new StringBuilder();
            foreach (var kv in _kind) { if (sb.Length > 0) sb.Append(';'); sb.Append((int)(kv.Key >> 32)).Append(':').Append((int)(kv.Key & 0xFFFFFFFFu)).Append(':').Append(kv.Value); }
            _for.m_customData[Key] = sb.ToString();
        }
    }

    [HarmonyPatch(typeof(Minimap))]
    internal static class PinKindPatches
    {
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        // after vanilla placed / refreshed the markers: our icons (which markers show is MapClusters' job)
        [HarmonyPostfix, HarmonyPatch("UpdatePins")]
        private static void AfterPins(Minimap __instance)
        {
            foreach (var p in Pins(__instance)) if (PinKinds.Of(p) > 0) PinKinds.Paint(p);
        }

        [HarmonyPostfix, HarmonyPatch(nameof(Minimap.RemovePin), typeof(Minimap.PinData))]
        private static void Removed(Minimap.PinData pin) => PinKinds.Forget(pin);
    }
}
