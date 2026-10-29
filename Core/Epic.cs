using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // EpicLoot (randyknapp.mods.epicloot), soft: only its public API, by reflection. Every call is a no-op without it,
    // and a member a given EpicLoot build lacks turns just that feature off, with one log line.
    internal static class Epic
    {
        internal const string Guid = "randyknapp.mods.epicloot";
        private static bool? _on; private static Type _api, _main;
        private static readonly Dictionary<string, MethodInfo> _m = new Dictionary<string, MethodInfo>();
        private static readonly HashSet<string> _missing = new HashSet<string>();

        internal static bool Started;
        internal static bool On { get { if (_on == null) Bind(); return _on ?? false; } }

        private static void Bind()
        {
            _on = false;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(Guid, out var info)) { if (!Started) _on = null; return; }   // BepInEx lists plugins as it loads them: before our Start, absent may just mean later
            if (info.Instance == null) { _on = null; return; }   // installed but not loaded yet (we load first: j before r): ask again later
            var asm = info.Instance.GetType().Assembly;
            _api = asm.GetType("EpicLoot.API"); _main = asm.GetType("EpicLoot.EpicLoot");
            _on = _api != null;
            Log.Info(_on.Value ? $"EpicLoot {info.Metadata.Version} found: using its API" : "EpicLoot found but its API is missing: no integration");
        }

        // a static API method by name and parameter types; null (logged once) when this EpicLoot build lacks it
        private static MethodInfo M(string name, params Type[] args)
        {
            if (!On) return null;
            var key = name + "/" + args.Length;
            if (_m.TryGetValue(key, out var m)) return m;
            try { m = AccessTools.Method(_api, name, args.Length == 0 ? null : args); } catch (AmbiguousMatchException) { m = null; }
            if (m == null && _missing.Add(key)) Log.Warn($"EpicLoot has no API.{name}: that part of the integration is off");
            return _m[key] = m;
        }

        internal static object Call(string name, Type[] types, params object[] args)
        {
            var m = M(name, types); if (m == null) return null;
            try { return m.Invoke(null, args); }
            catch (Exception e) { if (_missing.Add(name + "!")) Log.Warn($"EpicLoot API.{name} failed: {e.InnerException?.Message ?? e.Message}"); return null; }
        }

        private static readonly Type[] Any = new Type[0];   // by name: EpicLoot's register calls have one overload each

        // what Jatify keeps where EpicLoot can't see: worn utility items in its mod slots (they count as equipped: effects,
        // set pieces, abilities), and locked bag cells (never offered for sacrifice)
        internal static void Hook(Harmony harmony)
        {
            if (!On) return;
            if (Harmony.GetPatchInfo(EpicSocketTarget.Target())?.Postfixes.Any(x => x.owner == harmony.Id) != true)   // EpicLoot loads after us: its patches go on here, at Start
                try { harmony.CreateClassProcessor(typeof(EpicSocketTarget)).Patch(); } catch (Exception e) { Log.Warn("EpicLoot socket shortcut on worn gear: " + e.Message); }
            Call("RegisterEquipmentProvider", Any, "jatify.ui.slots", (Func<Player, List<ItemDrop.ItemData>>)ModSlots.Worn);
            Call("RegisterSacrificeFilter", Any, "jatify.ui.locks", (Func<ItemDrop.ItemData, bool>)(it => !InvActions.LockedItem(it)));
            Call("SetTooltipOverride", Any, "jatify.ui", true);   // our item card draws its tooltips (ItemTip): EpicLoot's own box would show behind it
            if (Stock.Other) return;   // a craft-from-chests mod is installed: it registers its own chests
            Call("RegisterInventoryProvider", Any, "jatify.ui.chests", (Func<List<ItemDrop.ItemData>>)Stock.TableItems, (Func<string, int>)Stock.TableCount, (Func<string, int, int>)Stock.TableTake, (Func<ItemDrop.ItemData, int, int>)Stock.TableTakeExact);
            Call("RegisterInventoryProviderSaveHandler", Any, "jatify.ui.chests", (Func<ItemDrop.ItemData, bool>)Stock.TableSave);
        }

        internal static void Unhook()
        {
            if (_on != true) return;
            Call("UnregisterEquipmentProvider", Any, "jatify.ui.slots");
            Call("UnregisterSacrificeFilter", Any, "jatify.ui.locks");
            Call("UnregisterInventoryProvider", Any, "jatify.ui.chests");
            Call("SetTooltipOverride", Any, "jatify.ui", false);
        }

        // after a mod slot changed: EpicLoot recounts effects only on vanilla equip / unequip
        internal static void Refresh(Player p) { if (_on == true && p != null) Call("InvalidatePlayerEffectCache", new[] { typeof(Player) }, p); }

        // ---- tooltips: EpicLoot's data for an item (its schema 1), and the vanilla text without its lines ----------------
        // public: Unity's JsonUtility skips lists of non-public classes
        [Serializable] public class Tip { public bool IsMagic; public int MaterialRarity = -1; public string Title = "", DisplayName = "", RarityName = "", RarityColor = "", ItemTypeName = ""; public List<Fx> Effects = new List<Fx>(); public Sockets Sockets; public List<Set> Sets = new List<Set>(); public List<Line> Extra = new List<Line>(); }
        [Serializable] public class Fx { public string Text = ""; }
        [Serializable] public class Sockets { public int Count, Used; public List<Fx> Entries = new List<Fx>(); }
        [Serializable] public class Set { public string Name = ""; public int Equipped, Full; public List<Bonus> Bonuses = new List<Bonus>(); }
        [Serializable] public class Bonus { public int Count; public string Text = ""; public bool Active; }
        [Serializable] public class Line { public string Text = ""; }

        // the colour of its rarity: a magic item's, or a rarity material's (runestones, dust, shards) from its title
        private static readonly System.Text.RegularExpressions.Regex TitleColor = new System.Text.RegularExpressions.Regex("<color=(#[0-9a-fA-F]{6,8})>");
        internal static string Color(Tip t)
        {
            if (t == null) return null;
            if (t.IsMagic && !string.IsNullOrEmpty(t.RarityColor)) return t.RarityColor.StartsWith("#") ? t.RarityColor : "#" + t.RarityColor;
            if (t.MaterialRarity < 0) return null;
            var m = TitleColor.Match(t.Title ?? ""); return m.Success ? m.Groups[1].Value : null;
        }

        // Newtonsoft, which EpicLoot itself needs (always there with it): Unity's JsonUtility drops the nested lists when the
        // plugin was loaded from bytes (ScriptEngine)
        private static readonly MethodInfo Parse = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json")?.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });

        internal static Tip TipData(ItemDrop.ItemData it, int quality)
        {
            if (!On || it == null) return null;
            var json = Call("GetItemTooltipData", Any, it, quality, false, true) as string;
            if (string.IsNullOrEmpty(json)) return null;
            try { return Parse != null ? (Tip)Parse.Invoke(null, new object[] { json, typeof(Tip) }) : UnityEngine.JsonUtility.FromJson<Tip>(json); } catch (Exception e) { if (_missing.Add("tipjson")) Log.Warn("EpicLoot tooltip data unreadable: " + e.Message); return null; }
        }

        internal static string TextWithout(ItemDrop.ItemData it, int quality, bool crafting) =>
            On ? Call("GetItemTooltipWithoutEpicLoot", Any, it, quality, crafting, (float)it.m_worldLevel, -1) as string : null;

        // its magic-item cell background (knotwork fading up in the rarity colour), so our own cells match its grid cells
        internal static Sprite MagicBg()
        {
            if (!On || _main == null) return null;
            try { return AccessTools.Method(_main, "GetMagicItemBgSprite")?.Invoke(null, null) as Sprite; } catch { return null; }
        }

        // EpicLoot's two map pin types (bounties, treasure maps); -1 until its minimap has registered them
        internal static int PinType(string which)
        {
            if (!On || _main == null) return -1;
            var t = Traverse.Create(_main);
            var v = t.Property(which).PropertyExists() ? t.Property(which).GetValue() : t.Field(which).FieldExists() ? t.Field(which).GetValue() : null;
            return v == null ? -1 : Convert.ToInt32(v);
        }

        // not for sale here (Sell all would take them): EpicLoot's currencies and its magic items; its own trader panel buys those
        private static readonly string[] Currency = { "ForestToken", "IronBountyToken", "GoldBountyToken" };
        internal static bool Keep(ItemDrop.ItemData it) => On && it != null && (Array.IndexOf(Currency, it.m_dropPrefab != null ? it.m_dropPrefab.name : "") >= 0 || IsMagic(it) || IsOwnItem(it));

        internal static bool IsMagic(ItemDrop.ItemData it) => it != null && Call("IsMagicItem", new[] { typeof(ItemDrop.ItemData) }, it) is bool b && b;
        internal static bool IsOwnItem(ItemDrop.ItemData it) => it != null && Call("IsEpicLootItem", new[] { typeof(ItemDrop.ItemData) }, it) is bool b && b;
    }

    // EpicLoot's socket shortcut (Ctrl + E) looks for the item under the pointer in Valheim's grid only: over our
    // equipment doll it finds nothing, so worn gear's sockets couldn't be opened without taking it off
    [HarmonyPatch]
    internal static class EpicSocketTarget
    {
        private static bool Prepare() => Epic.On && Types.Find("EpicLoot.ShardStones.SocketOverlayInput") != null;
        internal static MethodBase Target() => AccessTools.Method(Types.Find("EpicLoot.ShardStones.SocketOverlayInput"), "TryGetPressTarget");
        private static MethodBase TargetMethod() => Target();
        private static void Postfix(bool __result, ref ItemDrop.ItemData item, bool gamepad)
        {
            if (!__result || gamepad || item != null) return;
            var worn = Equipment.UnderPointer(); if (worn != null) item = worn;
        }
    }
}
