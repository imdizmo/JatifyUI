using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // The "New Recipes" log: Valheim's AddKnownRecipe / AddKnownPiece /
    // AddKnownItem are patched to note what was unlocked, what was picked up to cause it, which
    // station it needs, and when. One tab-separated file per character under config/jatify.ui/unlocks,
    // appended as things happen, newest last, capped at Max entries.
    internal static class Unlocks
    {
        internal const byte Recipe = 0, Piece = 1, Material = 2;
        internal const int Max = 2000;
        internal sealed class Entry
        {
            public byte Kind; public string Key, Cause, Station; public int Level, Day, Minute; public long Unix;
        }
        internal static readonly List<Entry> All = new List<Entry>();
        internal static int Seen, Version;
        private static string _file, _cause;

        private static string FileFor()
        {
            var prof = Game.instance != null ? Game.instance.GetPlayerProfile() : null; if (prof == null) return null;
            return Path.Combine(BepInEx.Paths.ConfigPath, "jatify.ui", "unlocks", prof.GetFilename() + ".tsv");
        }

        // load once per character
        internal static void Ensure()
        {
            var f = FileFor(); if (f == null || f == _file) return;
            _file = f; All.Clear(); Seen = 0; Version++;
            try
            {
                if (File.Exists(f + ".seen")) int.TryParse(File.ReadAllText(f + ".seen"), out Seen);
                if (!File.Exists(f)) return;
                foreach (var line in File.ReadLines(f))
                {
                    var c = line.Split('\t'); if (c.Length < 8) continue;
                    All.Add(new Entry { Kind = byte.Parse(c[0]), Key = c[1], Cause = c[2], Station = c[3], Level = int.Parse(c[4]), Day = int.Parse(c[5]), Minute = int.Parse(c[6]), Unix = long.Parse(c[7]) });
                }
                if (All.Count > Max) { int drop = All.Count - Max; All.RemoveRange(0, drop); Seen = Math.Max(0, Seen - drop); Rewrite(); }
            }
            catch (Exception e) { Log.Warn("unlocks: could not read " + f + ": " + e.Message); }
        }

        private static void Rewrite()
        {
            File.WriteAllLines(_file, All.Select(Line));
            File.WriteAllText(_file + ".seen", Seen.ToString());
        }

        private static string Line(Entry e) => string.Join("\t", e.Kind, e.Key, e.Cause ?? "", e.Station ?? "", e.Level, e.Day, e.Minute, e.Unix);

        internal static int Unseen => Math.Max(0, All.Count - Seen);

        internal static void Clear()
        {
            if (_file == null) return;
            All.Clear(); Seen = 0; Version++;
            try { Rewrite(); } catch (Exception e) { Log.Warn("unlocks: " + e.Message); }
        }

        internal static void MarkSeen()
        {
            if (_file == null || Seen == All.Count) return;
            Seen = All.Count;
            try { File.WriteAllText(_file + ".seen", Seen.ToString()); } catch (Exception e) { Log.Warn("unlocks: " + e.Message); }
        }

        private static void Add(byte kind, string key, string station, int level)
        {
            Ensure(); if (_file == null) return;
            var env = EnvMan.instance;
            var e = new Entry
            {
                Kind = kind, Key = key, Cause = _cause, Station = station, Level = level,
                Day = env != null ? env.GetDay() : 0, Minute = env != null ? (int)(env.GetDayFraction() * 1440f) : 0, Unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            All.Add(e); Version++;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                if (All.Count > Max) { All.RemoveAt(0); Seen = Math.Max(0, Seen - 1); Rewrite(); }
                else File.AppendAllText(_file, Line(e) + "\n");
            }
            catch (Exception ex) { Log.Warn("unlocks: could not write: " + ex.Message); }
        }

        private static Dictionary<string, string> _prefabOf;
        // prefab name; items made in code have no m_dropPrefab, so look the token up once
        internal static string ItemKey(ItemDrop.ItemData it)
        {
            if (it.m_dropPrefab != null) return it.m_dropPrefab.name;
            if (_prefabOf == null && ObjectDB.instance != null)
            {
                _prefabOf = new Dictionary<string, string>();
                foreach (var go in ObjectDB.instance.m_items) { var d = go ? go.GetComponent<ItemDrop>() : null; if (d != null && !_prefabOf.ContainsKey(d.m_itemData.m_shared.m_name)) _prefabOf[d.m_itemData.m_shared.m_name] = go.name; }
            }
            return _prefabOf != null && _prefabOf.TryGetValue(it.m_shared.m_name, out var n) ? n : it.m_shared.m_name;
        }

        internal static void OnRecipe(Recipe r)
        {
            var st = r.GetRequiredStation(1);
            Add(Recipe, r.m_item.gameObject.name, st != null ? st.m_name : null, st != null ? r.GetRequiredStationLevel(1) : 0);
        }
        internal static void OnPiece(global::Piece p) => Add(Piece, Utils.GetPrefabName(p.gameObject), p.m_craftingStation != null ? p.m_craftingStation.m_name : null, 0);
        internal static void OnItem(ItemDrop.ItemData it) => Add(Material, ItemKey(it), null, 0);
        internal static void Cause(string c) => _cause = c;

        // ---- names and icons for the page ----
        private static Dictionary<string, CraftingStation> _stations;
        internal static CraftingStation StationByName(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            if (_stations == null && ZNetScene.instance != null)
            {
                _stations = new Dictionary<string, CraftingStation>();
                foreach (var go in ZNetScene.instance.m_prefabs) { var cs = go ? go.GetComponent<CraftingStation>() : null; if (cs != null && !_stations.ContainsKey(cs.m_name)) _stations[cs.m_name] = cs; }
            }
            return _stations != null && _stations.TryGetValue(token, out var s) ? s : null;
        }

        internal static (string name, Sprite icon) Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return ("", null);
            var item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(key) : null;
            var drop = item != null ? item.GetComponent<ItemDrop>() : null;
            if (drop != null) return (Localization.instance.Localize(drop.m_itemData.m_shared.m_name), drop.m_itemData.GetIcon());
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(key) : null;
            var piece = go != null ? go.GetComponent<global::Piece>() : null;
            if (piece != null) return (Localization.instance.Localize(piece.m_name), piece.m_icon);
            var st = StationByName(key);
            if (st != null) return (Localization.instance.Localize(st.m_name), st.m_icon);
            return (Localization.instance.Localize(key), null);
        }

        internal static void Forget() { _file = null; All.Clear(); _stations = null; _cause = null; _prefabOf = null; }
    }

    [HarmonyPatch]
    internal static class UnlockPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        private static void RecipePre(Player __instance, Recipe recipe, out bool __state) => __state = recipe != null && recipe.m_item != null && __instance == Player.m_localPlayer && !__instance.IsRecipeKnown(recipe.m_item.m_itemData.m_shared.m_name);
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        private static void RecipePost(Recipe recipe, bool __state) { if (__state) Unlocks.OnRecipe(recipe); }

        [HarmonyPrefix, HarmonyPatch(typeof(Player), "AddKnownPiece")]
        private static void PiecePre(Player __instance, Piece piece, out bool __state) => __state = piece != null && __instance == Player.m_localPlayer && !__instance.IsRecipeKnown(piece.m_name);
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "AddKnownPiece")]
        private static void PiecePost(Piece piece, bool __state) { if (__state) Unlocks.OnPiece(piece); }

        // the item picked up is the "cause" of whatever it unlocks inside AddKnownItem
        [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        private static void ItemPre(Player __instance, ItemDrop.ItemData item, out bool __state)
        {
            __state = item != null && __instance == Player.m_localPlayer && !__instance.IsMaterialKnown(item.m_shared.m_name);
            if (__state) Unlocks.Cause(Unlocks.ItemKey(item));
        }
        [HarmonyPostfix, HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        private static void ItemPost(ItemDrop.ItemData item, bool __state) { if (!__state) return; Unlocks.Cause(null); Unlocks.OnItem(item); }

        [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.AddKnownStation))]
        private static void StationPre(Player __instance, CraftingStation station) { if (__instance == Player.m_localPlayer && station != null) Unlocks.Cause(station.m_name); }
        [HarmonyPostfix, HarmonyPatch(typeof(Player), nameof(Player.AddKnownStation))]
        private static void StationPost() => Unlocks.Cause(null);
    }
}
