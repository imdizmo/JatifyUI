using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Auto stations keep working with nobody near (user, i87): the game that is the server (yours when you host, or a
    // dedicated server running Jatify) runs smelter-type stations and fermenters that nobody has loaded, straight on their
    // saved data. It burns fuel and finishes ore the way Smelter.UpdateSmelter does, taps ready mead and starts the next
    // batch, and refills both from the chests around them. What they make waits on the station (HeldKey) and drops at its
    // output when someone comes by (Release). Valheim alone stops an unloaded station once its queue (10 ore) is done.
    //
    // A station's game only knows its Automation tab settings on the player's own game, so the player feeding it leaves a
    // copy on the station (SrvKey: "player|prefab,prefab"); the server feeds what that copy allows, from chests built by that
    // player. Windmill-powered stations are skipped (their power is the wind's, which only a loaded station knows).
    internal static class ServerAuto
    {
        internal const string SrvKey = "jatify.srv", HeldKey = "jatify.held";
        private const float Every = 10f, Rescan = 60f;
        private static float _at, _scanAt = -999f;
        private static readonly List<ZDO> _stations = new List<ZDO>(), _near = new List<ZDO>();
        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> All = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

        // ---- the server ------------------------------------------------------------------------------------------
        internal static void Tick()
        {
            if (!Smelters.On.Value || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null || Time.time < _at) return;
            _at = Time.time + Every;
            if (Time.time - _scanAt > Rescan)
            {   // which stations carry a copy (a full pass over the world's objects, once a minute)
                _scanAt = Time.time; _stations.Clear();
                foreach (var z in All(ZDOMan.instance).Values) if (z != null && z.GetString(SrvKey, "").Length > 0) _stations.Add(z);
            }
            var now = ZNet.instance.GetTime();
            foreach (var z in _stations) RunOne(z, now);
        }

        internal static void RunOne(ZDO z, DateTime now)
        {
            if (z == null || !z.IsValid() || Loaded(z)) return;   // loaded somewhere: its owner's game runs it
            int mode = z.GetInt(Smelters.AutoKey, Smelters.Follow); if (mode == Smelters.ForcedOff) return;
            var go = ZNetScene.instance.GetPrefab(z.GetPrefab()); if (go == null) return;
            var copy = z.GetString(SrvKey, "").Split('|'); if (copy.Length != 2 || !long.TryParse(copy[0], out long by) || by == 0L) return;
            var takes = new HashSet<string>(copy[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            var sm = go.GetComponent<Smelter>(); var fe = go.GetComponent<Fermenter>();
            try
            {
                if (sm != null && sm.m_windmill == null) RunSmelter(z, sm, by, takes, now);
                else if (fe != null) RunFermenter(z, fe, by, takes, now);
            }
            finally { SaveChests(); }
        }

        // loaded = its owner's game has it in its active area (Valheim's own test). Ownership only moves when someone else
        // comes near, so a station you walked away from still names you as its owner
        private static readonly Func<ZDOMan, Vector3, long, bool> InPeerArea = AccessTools.MethodDelegate<Func<ZDOMan, Vector3, long, bool>>(AccessTools.Method(typeof(ZDOMan), "IsInPeerActiveArea"));
        internal static bool Loaded(ZDO z) => z.HasOwner() && InPeerArea(ZDOMan.instance, z.GetPosition(), z.GetOwner());

        // Smelter.UpdateSmelter on the saved data, refilling as it empties (roof and smoke as they were when last loaded)
        private static void RunSmelter(ZDO z, Smelter sm, long by, HashSet<string> takes, DateTime now)
        {
            double delta = (now - new DateTime(z.GetLong(ZDOVars.s_startTime, now.Ticks))).TotalSeconds;
            z.Set(ZDOVars.s_startTime, now.Ticks);
            float acc = Mathf.Min(3600f, z.GetFloat(ZDOVars.s_accTime) + (float)Math.Max(0.0, delta));
            Refill(z, sm, by, takes);
            while (acc >= 1f)
            {
                acc--;
                float fuel = z.GetFloat(ZDOVars.s_fuel); int queued = Mathf.Clamp(z.GetInt(ZDOVars.s_queued), 0, Mathf.Max(0, sm.m_maxOre));   // saved data any game can write: never loop past the queue's size
                if ((sm.m_maxFuel != 0 && fuel <= 0f) || (sm.m_maxOre != 0 && queued == 0)) { if (!Refill(z, sm, by, takes)) continue; fuel = z.GetFloat(ZDOVars.s_fuel); queued = z.GetInt(ZDOVars.s_queued); }
                if ((sm.m_maxFuel != 0 && fuel <= 0f) || (sm.m_maxOre != 0 && queued == 0) || sm.m_secPerProduct <= 0f) continue;
                if (sm.m_maxFuel > 0) { fuel -= 1f / (sm.m_secPerProduct / sm.m_fuelPerProduct); z.Set(ZDOVars.s_fuel, fuel < 0.0001f ? 0f : fuel); }
                float bake = z.GetFloat(ZDOVars.s_bakeTimer) + 1f;
                if (bake < sm.m_secPerProduct) { z.Set(ZDOVars.s_bakeTimer, bake); continue; }
                z.Set(ZDOVars.s_bakeTimer, 0f);
                string ore = z.GetString(ZDOVars.s_item0);
                for (int i = 0; i < queued; i++) z.Set("item" + i, z.GetString("item" + (i + 1)));
                z.Set(ZDOVars.s_queued, queued - 1);
                Hold(z, ore, 1);
            }
            z.Set(ZDOVars.s_accTime, acc);
        }

        // fuel up to its cap, ore up to its queue: false when it still can't run
        private static bool Refill(ZDO z, Smelter sm, long by, HashSet<string> takes)
        {
            if (sm.m_maxFuel > 0 && sm.m_fuelItem != null && takes.Contains(sm.m_fuelItem.gameObject.name))
                while (z.GetFloat(ZDOVars.s_fuel) <= sm.m_maxFuel - 1 && Take(z, by, sm.m_fuelItem.gameObject.name)) z.Set(ZDOVars.s_fuel, z.GetFloat(ZDOVars.s_fuel) + 1f);
            if (sm.m_maxOre > 0)
            {
                bool any = true;
                while (any && z.GetInt(ZDOVars.s_queued) < sm.m_maxOre)
                {
                    any = false;
                    foreach (var cv in sm.m_conversion)
                    {
                        if (cv.m_from == null || !takes.Contains(cv.m_from.gameObject.name) || !Take(z, by, cv.m_from.gameObject.name)) continue;
                        int q = z.GetInt(ZDOVars.s_queued); z.Set("item" + q, cv.m_from.gameObject.name); z.Set(ZDOVars.s_queued, q + 1); any = true; break;
                    }
                }
            }
            return (sm.m_maxFuel == 0 || z.GetFloat(ZDOVars.s_fuel) > 0f) && (sm.m_maxOre == 0 || z.GetInt(ZDOVars.s_queued) > 0);
        }

        // ready mead is tapped onto the station's held output, then the next base goes in (Valheim ferments by the clock)
        private static void RunFermenter(ZDO z, Fermenter fe, long by, HashSet<string> takes, DateTime now)
        {
            int content = z.GetInt(ZDOVars.s_content);
            if (content != 0)
            {
                if ((now - new DateTime(z.GetLong(ZDOVars.s_startTime, 0L))).TotalSeconds <= fe.m_fermentationDuration) return;
                var from = ZNetScene.instance.GetPrefab(content); if (from == null) return;
                Hold(z, from.name, 1);
                z.Set(ZDOVars.s_content, 0); z.Set(ZDOVars.s_startTime, 0L); z.Set(ZDOVars.s_cheatedQueued, false);
            }
            foreach (var cv in fe.m_conversion)
            {
                if (cv.m_from == null || !takes.Contains(cv.m_from.gameObject.name) || !Take(z, by, cv.m_from.gameObject.name)) continue;
                z.Set(ZDOVars.s_content, cv.m_from.gameObject.name.GetStableHashCode()); z.Set(ZDOVars.s_startTime, now.Ticks); return;
            }
        }

        // ---- chests: the ones around the station built by its player, read and written on their saved data -------------
        private static readonly Dictionary<ZDO, Inventory> _open = new Dictionary<ZDO, Inventory>();   // this station's chests, saved after it
        private static ZDO _openFor;

        private static bool Take(ZDO station, long by, string prefab)
        {
            if (_openFor != station) { SaveChests(); _openFor = station; Around(station, by); }
            int wl = Game.m_worldLevel;
            foreach (var kv in _open)
            {
                string locked = "," + kv.Key.GetString(ChestPanel.LockKey, "") + ",";
                foreach (var it in kv.Value.GetAllItems())
                {
                    if (it.m_dropPrefab == null || it.m_dropPrefab.name != prefab || it.m_worldLevel < wl || locked.Contains("," + it.m_gridPos.x + "." + it.m_gridPos.y + ",")) continue;
                    if (kv.Value.RemoveItem(it, 1)) { _dirty.Add(kv.Key); return true; }
                }
            }
            return false;
        }

        private static readonly HashSet<ZDO> _dirty = new HashSet<ZDO>();

        private static void Around(ZDO station, long by)
        {
            _open.Clear(); _near.Clear();
            var at = station.GetPosition(); float r = Cfg.StackRange.Value;
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(at), new SimulationDistance(1, 0, true), _near);
            foreach (var c in _near)
            {
                if (c == null || !c.IsValid() || Loaded(c) || (c.GetPosition() - at).sqrMagnitude > r * r || c.GetLong(ZDOVars.s_creator, 0L) != by) continue;
                if (c.GetString(ChestPanel.NoCraftKey, "") == "1" || c.GetInt(ZDOVars.s_inUse, 0) == 1) continue;
                var go = ZNetScene.instance.GetPrefab(c.GetPrefab()); var box = go != null ? go.GetComponent<Container>() : null;
                if (box == null || go.GetComponent<Ship>() != null || go.GetComponent<Vagon>() != null || go.GetComponent<TombStone>() != null) continue;
                var bytes = c.GetByteArray(ZDOVars.s_items); if (bytes == null) continue;
                var inv = new Inventory(box.m_name, null, box.m_width, box.m_height); inv.Load(new ZPackage(bytes));
                _open[c] = inv;
            }
        }

        private static void SaveChests()
        {
            foreach (var c in _dirty) if (c.IsValid() && _open.TryGetValue(c, out var inv)) { var pkg = new ZPackage(); inv.Save(pkg); c.Set(ZDOVars.s_items, pkg.GetArray()); }
            _dirty.Clear(); _open.Clear(); _openFor = null;
        }

        // ---- held output: "prefab:count;prefab:count", by what went in (ore, mead base) ----------------------------------
        private static void Hold(ZDO z, string from, int n)
        {
            var held = Held(z); held.TryGetValue(from, out int had); held[from] = had + n;
            z.Set(HeldKey, string.Join(";", held.Select(kv => kv.Key + ":" + kv.Value)));
        }

        internal static Dictionary<string, int> Held(ZDO z)
        {
            var d = new Dictionary<string, int>();
            foreach (var part in z.GetString(HeldKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int k = part.LastIndexOf(':'); if (k > 0 && int.TryParse(part.Substring(k + 1), out int n) && n > 0) d[part.Substring(0, k)] = n;
            }
            return d;
        }

        // ---- a player's game ------------------------------------------------------------------------------------------
        private static readonly System.Reflection.MethodInfo SmSpawn = AccessTools.Method(typeof(Smelter), "Spawn");

        // a loaded station we own drops what it made while nobody was near, at its output (as Valheim drops it)
        internal static void Release(Component st, ZNetView nv)
        {
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var z = nv.GetZDO(); if (z.GetString(HeldKey, "").Length == 0) return;
            var held = Held(z); z.Set(HeldKey, "");
            // any game can write a station's data, so a count is capped at what a day of running could make (a vanilla client
            // that edits saved data can spawn items anyway; this only stops a bad value flooding the area with drops)
            foreach (var k in held.Keys.ToList()) held[k] = Mathf.Min(held[k], st is Fermenter ? 40 : 3000);
            foreach (var kv in held)
            {
                if (st is Smelter sm)
                {
                    var cv = sm.m_conversion.FirstOrDefault(c => c.m_from != null && c.m_from.gameObject.name == kv.Key); if (cv == null || cv.m_to == null) continue;
                    int max = Mathf.Max(1, cv.m_to.m_itemData.m_shared.m_maxStackSize);
                    for (int left = kv.Value; left > 0; left -= max) SmSpawn.Invoke(sm, new object[] { kv.Key, Mathf.Min(max, left) });
                }
                else if (st is Fermenter fe)
                {
                    var cv = fe.m_conversion.FirstOrDefault(c => c.m_from != null && c.m_from.gameObject.name == kv.Key); if (cv == null || cv.m_to == null) continue;
                    for (int i = 0; i < kv.Value * cv.m_producedItems; i++)
                        ItemDrop.OnCreateNew(UnityEngine.Object.Instantiate(cv.m_to, fe.m_outputPoint.position + Vector3.up * 0.3f, Quaternion.identity), false);
                }
            }
            Log.Info($"auto: {Utils.GetPrefabName(st.gameObject)} dropped what it made while nobody was near ({string.Join(", ", held.Select(kv => kv.Key + " ×" + kv.Value))})");
        }

        // the copy the server works from: who feeds it and what it may take (fuel and ores, or mead bases), as this game sees it
        internal static void Leave(Component st, ZNetView nv, long me, bool feeds)
        {
            if (nv == null || !nv.IsValid() || !(st is Smelter || st is Fermenter)) return;
            var z = nv.GetZDO(); string want = "";
            if (feeds)
            {
                var names = new List<string>();
                if (st is Smelter sm) { if (sm.m_fuelItem != null) names.Add(sm.m_fuelItem.gameObject.name); names.AddRange(sm.m_conversion.Where(c => c.m_from != null).Select(c => c.m_from.gameObject.name)); }
                else if (st is Fermenter fe) names.AddRange(fe.m_conversion.Where(c => c.m_from != null).Select(c => c.m_from.gameObject.name));
                want = me + "|" + string.Join(",", names.Distinct().Where(n => Smelters.Takes(st, n)));
            }
            var had = z.GetString(SrvKey, "");
            if (had == want || (!feeds && !had.StartsWith(me + "|"))) return;   // someone else's copy stays theirs
            if (!nv.IsOwner()) { nv.ClaimOwnership(); return; }   // written next scan, as the owner
            z.Set(SrvKey, want);
        }
    }
}
