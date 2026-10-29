using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Smelter automation: hold the station key (Left Shift)
    // (ovens that burn fuel too: wood only, fed like fires)
    // and use a smelter, furnace, kiln, refinery or fire: a card switches Auto on for that station and picks which
    // of its ores and fuel it may take. Auto stations within 40 m of you top themselves up every 1.5 s, one item
    // each, from nearby chests (the crafting chests' rules: access, wards, Excluded from crafting, locked slots).
    // The switches live on the station (its ZDO), so everyone sees the same; your bag is never touched.
    // Nothing is lost: Valheim queues every add, at worst one over the limit when two players feed at once.
    // ponytail: no per-item keep / cap / stop rules; add when a station empties chests too eagerly.
    [HarmonyPatch]
    internal static class Smelters
    {
        internal static ConfigEntry<bool> On;
        internal static ConfigEntry<KeyboardShortcut> Key;
        internal const string AutoKey = "jatify.auto", OffKey = "jatify.off", ByKey = "jatify.autoby";
        private const float Reach = 40f;
        private static readonly AccessTools.FieldRef<Smelter, ZNetView> SmView = AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Fireplace, ZNetView> FireView = AccessTools.FieldRefAccess<Fireplace, ZNetView>("m_nview");
        private static readonly List<Smelter> _smelters = new List<Smelter>();
        private static readonly List<Fireplace> _fires = new List<Fireplace>();
        private static readonly List<CookingStation> _ovens = new List<CookingStation>();
        private static readonly List<Component> _others = new List<Component>();   // shield generators, ballistas, fermenters
        private static readonly List<Container> _chests = new List<Container>();
        private static float _scanAt = -99f, _feedAt;

        internal static void Bind(ConfigFile c)
        {
            On = c.Bind("Automation", "Smelters", true, "Stations switched to Auto (hold the station key and use one) top themselves up from nearby chests.");
            Key = c.Bind("Keys", "SmelterWindow", new KeyboardShortcut(KeyCode.LeftShift), "Held with the use key on a smelter, kiln, refinery or fire to open its Auto card.");
        }

        private static readonly AccessTools.FieldRef<CookingStation, ZNetView> OvenView = AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<ShieldGenerator, ZNetView> ShieldView = AccessTools.FieldRefAccess<ShieldGenerator, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Turret, ZNetView> TurretView = AccessTools.FieldRefAccess<Turret, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Fermenter, ZNetView> FermView = AccessTools.FieldRefAccess<Fermenter, ZNetView>("m_nview");
        internal static ZNetView ViewOf(Component s) => s is Smelter sm ? SmView(sm) : s is Fireplace f ? FireView(f) : s is CookingStation o ? OvenView(o)
            : s is ShieldGenerator g ? ShieldView(g) : s is Turret tu ? TurretView(tu) : s is Fermenter fe ? FermView(fe) : null;
        // the stations Auto works on: smelters (kilns, furnaces, refineries...), fires that burn fuel, ovens that burn fuel
        internal static Component StationOf(GameObject go)
        {
            if (go == null) return null;
            Component st = go.GetComponentInParent<Smelter>(); if (st != null) return st;
            var f = go.GetComponentInParent<Fireplace>(); if (f != null && !f.m_infiniteFuel && f.m_fuelItem != null) return f;
            var o = go.GetComponentInParent<CookingStation>(); if (o != null && o.m_useFuel && o.m_fuelItem != null) return o;
            Component other = go.GetComponentInParent<ShieldGenerator>(); if (other == null) other = go.GetComponentInParent<Turret>(); if (other == null) other = go.GetComponentInParent<Fermenter>();
            return other;
        }
        // a station's own Auto setting (its card): 0 follows its building type (Automation tab), 1 on, 2 off. Set here, it wins
        // over the tab (user: the individual override bypasses the general settings)
        internal const int Follow = 0, ForcedOn = 1, ForcedOff = 2;
        internal static int Mode(Component s) { var nv = ViewOf(s); return nv != null && nv.IsValid() ? nv.GetZDO().GetInt(AutoKey, Follow) : Follow; }
        // fed? set on here, or following a type that's on; by the player who set it (nobody else's chests are drawn on), within wards
        internal static bool IsAuto(Component s)
        {
            int m = Mode(s); return m == ForcedOn || (m == Follow && !Automation.Paused(s));
        }
        private static bool Feeds(Component s, long me)
        {
            var nv = ViewOf(s); if (nv == null || !nv.IsValid() || !Allowed(s)) return false;
            int m = nv.GetZDO().GetInt(AutoKey, Follow); long by = nv.GetZDO().GetLong(ByKey, 0L);
            return m == ForcedOn ? by == me : m == Follow && !Automation.Paused(s) && (by == 0L || by == me);
        }
        internal static bool Allowed(Component s) => PrivateArea.CheckAccess(s.transform.position, 0f, false, false);
        internal static bool IsOff(Component s, string prefab) { var nv = ViewOf(s); return nv != null && nv.IsValid() && ("," + nv.GetZDO().GetString(OffKey, "") + ",").Contains("," + prefab + ","); }

        internal static void SetAuto(Component s, bool on) => SetMode(s, on ? ForcedOn : Follow);
        internal static void SetMode(Component s, int mode)
        {
            var nv = ViewOf(s); var p = Player.m_localPlayer; if (nv == null || !nv.IsValid() || p == null || !Allowed(s)) return;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(AutoKey, mode); nv.GetZDO().Set(ByKey, mode == Follow ? 0L : p.GetPlayerID());
            Automation.Track(s, mode == ForcedOn);   // the Automation page's list
        }
        // may it take this item? Fermenters only what you switched on (their card lists ~25 bases); everything else unless switched off
        // an item on a station's card: set there (on or off), else its type's setting (Automation tab; everything starts off)
        internal const string OnKey = "jatify.forceon";
        internal static bool ForcedIn(Component s, string prefab) { var nv = ViewOf(s); return nv != null && nv.IsValid() && ("," + nv.GetZDO().GetString(OnKey, "") + ",").Contains("," + prefab + ","); }
        internal static bool SetHere(Component s, string prefab) => IsOff(s, prefab) || ForcedIn(s, prefab);
        internal static bool Takes(Component s, string prefab) => IsOff(s, prefab) ? false : ForcedIn(s, prefab) || Automation.TypeTakes(Automation.Cat(Utils.GetPrefabName(s.gameObject)), prefab);
        internal static bool StationTakes(Component s, string prefab) => Takes(s, prefab);
        // set here: on or off for this station; reset: back to following its type
        internal static void SetTakes(Component s, string prefab, bool on) { SetList(s, OffKey, prefab, !on); SetList(s, OnKey, prefab, on); }
        internal static void ResetTakes(Component s, string prefab) { SetList(s, OffKey, prefab, false); SetList(s, OnKey, prefab, false); }
        private static void SetList(Component s, string key, string prefab, bool add)
        {
            var nv = ViewOf(s); if (nv == null || !nv.IsValid() || !Allowed(s)) return;
            var list = new List<string>(nv.GetZDO().GetString(key, "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            list.Remove(prefab); if (add) list.Add(prefab);
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(key, string.Join(",", list));
        }

        // a station not loaded, switched from the Automation tab: only when you host (your game holds its saved state)
        internal static void SetAutoFar(ZDO z, ZDOID id, string prefab, bool on)
        {
            var p = Player.m_localPlayer; if (z == null || p == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            z.Set(AutoKey, on ? 1 : 0); z.Set(ByKey, on ? p.GetPlayerID() : 0L); if (!on) z.Set(ServerAuto.SrvKey, "");
            Automation.TrackFar(id, prefab, z.GetPosition(), on);
        }

        internal static void SetOff(Component s, string prefab, bool off)
        {
            var nv = ViewOf(s); if (nv == null || !nv.IsValid() || !Allowed(s)) return;
            var list = new List<string>(nv.GetZDO().GetString(OffKey, "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            list.Remove(prefab); if (off) list.Add(prefab);
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(OffKey, string.Join(",", list));
        }

        // the key actually used: AzuCraftyBoxes pulls a station's ore or fuel from chests on its key + use (on release; ours opens
        // on press, so one press did both). When its key is ours, ours moves to Left Alt and the hints follow.
        internal static KeyboardShortcut Effective
        {
            get
            {
                var k = Key.Value;
                if (k.MainKey == KeyCode.None || !Stock.Other || OtherFillKey() != k.MainKey) return k;
                if (!_moved) { _moved = true; Log.Warn($"AzuCraftyBoxes fills stations from chests on {k} + use: the Auto card opens on Left Alt + use instead"); }
                return new KeyboardShortcut(KeyCode.LeftAlt);
            }
        }
        private static bool _moved;
        private static KeyCode? _otherFill;
        private static KeyCode OtherFillKey()
        {
            if (_otherFill.HasValue) return _otherFill.Value;
            KeyCode code = KeyCode.None;
            try
            {
                var e = AccessTools.Field(Types.Find("AzuCraftyBoxes.AzuCraftyBoxesPlugin"), "fillAllModKey")?.GetValue(null) as ConfigEntryBase;
                if (e?.BoxedValue is KeyboardShortcut ks) code = ks.MainKey; else if (e?.BoxedValue is KeyCode kc) code = kc;
            }
            catch { }
            _otherFill = code; return code;
        }

        private static bool Held() { var k = Effective; if (k.MainKey == KeyCode.None || !Input.GetKey(k.MainKey)) return false; foreach (var m in k.Modifiers) if (!Input.GetKey(m)) return false; return true; }

        // the station key + use: our card instead of vanilla's add
        [HarmonyPrefix, HarmonyPatch(typeof(Player), "Interact")]
        private static bool Interact(Player __instance, GameObject go, bool hold)
        {
            if (!On.Value || hold || go == null || __instance != Player.m_localPlayer || !Held()) return true;
            var st = StationOf(go);
            if (st == null || ViewOf(st) == null || !ViewOf(st).IsValid()) return true;
            if (!PrivateArea.CheckAccess(st.transform.position, 0f, true, false)) return false;   // someone else's ward: vanilla's "no access" flash, no card
            SmelterCard.Show(st);
            return false;
        }

        private static readonly AccessTools.FieldRef<List<Piece>> AllPiecesRef = AccessTools.StaticFieldRefAccess<List<Piece>>(AccessTools.Field(typeof(Piece), "s_allPieces"));
        private static List<Piece> AllPieces() => AllPiecesRef() ?? new List<Piece>();

        // ---- feeding -------------------------------------------------------------------------------------
        internal static void Tick()
        {
            var p = Player.m_localPlayer;
            SmelterCard.Tick();
            if (!On.Value || p == null || Time.time < _feedAt) return;
            _feedAt = Time.time + 1.5f;
            if (Time.time - _scanAt > 4f)
            {
                _scanAt = Time.time; _smelters.Clear(); _fires.Clear(); _ovens.Clear(); _others.Clear();
                foreach (var piece in AllPieces())   // built pieces only (all stations are): Valheim's own list, not a scene-wide search (that cost ~2 ms a frame in a big base)
                {
                    if (piece == null) continue;
                    if (piece.TryGetComponent<Smelter>(out var sm)) _smelters.Add(sm);
                    if (piece.TryGetComponent<Fireplace>(out var fp)) _fires.Add(fp);
                    if (piece.TryGetComponent<CookingStation>(out var cs) && cs.m_useFuel && cs.m_fuelItem != null) _ovens.Add(cs);
                    if (piece.TryGetComponent<ShieldGenerator>(out var sg)) _others.Add(sg);
                    if (piece.TryGetComponent<Turret>(out var tu)) _others.Add(tu);
                    if (piece.TryGetComponent<Fermenter>(out var fe)) _others.Add(fe);
                }
                // the Automation page's list follows what the loaded stations say (switched elsewhere, or before the list existed)
                var known = new HashSet<ZDOID>(Automation.List(p).Select(e => e.Id)); long mine = p.GetPlayerID();
                foreach (var st in _smelters.Cast<Component>().Concat(_fires).Concat(_ovens).Concat(_others))
                {
                    var v = st != null ? ViewOf(st) : null; if (v == null || !v.IsValid()) continue;
                    bool want = v.GetZDO().GetInt(AutoKey, Follow) == ForcedOn && v.GetZDO().GetLong(ByKey, 0L) == mine;
                    if (want != known.Contains(v.GetZDO().m_uid)) Automation.Track(st, want);
                    ServerAuto.Release(st, v);                                   // what it made while nobody was near
                    ServerAuto.Leave(st, v, mine, Feeds(st, mine));              // what the server may feed it while nobody is
                }
            }
            var me = p.transform.position; long id = p.GetPlayerID();
            foreach (var s in _smelters) if (s != null && (s.transform.position - me).sqrMagnitude < Reach * Reach && Feeds(s, id)) Feed(s, id);
            foreach (var f in _fires) if (f != null && (f.transform.position - me).sqrMagnitude < Reach * Reach && Feeds(f, id)) FeedFire(f, id);
            foreach (var o in _ovens) if (o != null && (o.transform.position - me).sqrMagnitude < Reach * Reach && Feeds(o, id)) FeedOven(o, id);
            foreach (var x in _others) if (x != null && (x.transform.position - me).sqrMagnitude < Reach * Reach && Feeds(x, id)) FeedOther(x, id);
        }

        private static void Feed(Smelter s, long id)
        {
            var nv = SmView(s); if (nv == null || !nv.IsValid()) return;
            var z = nv.GetZDO();
            Stock.Around(s.transform.position, Cfg.StackRange.Value, id, _chests); if (_chests.Count == 0) return;
            if (s.m_fuelItem != null && Takes(s, s.m_fuelItem.gameObject.name) && z.GetFloat(ZDOVars.s_fuel, 0f) <= s.m_maxFuel - 1
                && Take(s.m_fuelItem.m_itemData.m_shared.m_name)) nv.InvokeRPC("RPC_AddFuel");
            if (z.GetInt(ZDOVars.s_queued, 0) >= s.m_maxOre) return;
            foreach (var conv in s.m_conversion)
            {
                if (conv.m_from == null || !Takes(s, conv.m_from.gameObject.name)) continue;
                if (Take(conv.m_from.m_itemData.m_shared.m_name)) { nv.InvokeRPC("RPC_AddOre", conv.m_from.gameObject.name, false); return; }
            }
        }

        private static void FeedFire(Fireplace f, long id)
        {
            var nv = FireView(f); if (nv == null || !nv.IsValid() || f.m_fuelItem == null || f.m_infiniteFuel || !Takes(f, f.m_fuelItem.gameObject.name)) return;
            if (!nv.IsOwner()) { nv.ClaimOwnership(); return; }   // a fire drops fuel past its cap: only feed one we own, so the count we check is current (fed next tick)
            if (nv.GetZDO().GetFloat(ZDOVars.s_fuel, 0f) > f.m_maxFuel - 1) return;
            Stock.Around(f.transform.position, Cfg.StackRange.Value, id, _chests);
            if (Take(f.m_fuelItem.m_itemData.m_shared.m_name)) nv.InvokeRPC("RPC_AddFuel");
        }

        // ovens: like fires, fuel past the cap is lost, so only one we own (current count) is fed
        private static void FeedOven(CookingStation o, long id)
        {
            var nv = OvenView(o); if (nv == null || !nv.IsValid() || !Takes(o, o.m_fuelItem.gameObject.name)) return;
            if (!nv.IsOwner()) { nv.ClaimOwnership(); return; }
            if (nv.GetZDO().GetFloat(ZDOVars.s_fuel, 0f) > o.m_maxFuel - 1) return;
            Stock.Around(o.transform.position, Cfg.StackRange.Value, id, _chests);
            if (Take(o.m_fuelItem.m_itemData.m_shared.m_name)) nv.InvokeRPC("RPC_AddFuel");
        }

        // shield generator (fuel), ballista (ammo, one kind loaded at a time), fermenter (a base when empty). Each applies an add
        // only on its owner's game, so we own it first: the state we check is current and no item is taken for nothing
        private static void FeedOther(Component st, long id)
        {
            var nv = ViewOf(st); if (nv == null || !nv.IsValid()) return;
            if (!nv.IsOwner()) { nv.ClaimOwnership(); return; }
            var z = nv.GetZDO();
            if (st is ShieldGenerator g)
            {
                if (z.GetFloat(ZDOVars.s_fuel, g.m_defaultFuel) > g.m_maxFuel - 1) return;
                Stock.Around(g.transform.position, Cfg.StackRange.Value, id, _chests);
                foreach (var fuel in g.m_fuelItems) if (fuel != null && Takes(g, fuel.gameObject.name) && Take(fuel.m_itemData.m_shared.m_name)) { nv.InvokeRPC("RPC_AddFuel"); return; }
            }
            else if (st is Turret tu)
            {
                int ammo = z.GetInt(ZDOVars.s_ammo); if (ammo >= tu.m_maxAmmo) return;
                string loaded = ammo > 0 ? z.GetString(ZDOVars.s_ammoType, "") : "";
                Stock.Around(tu.transform.position, Cfg.StackRange.Value, id, _chests);
                foreach (var a in tu.m_allowedAmmo)
                {
                    if (a.m_ammo == null) continue; var n = a.m_ammo.gameObject.name;
                    if ((loaded.Length > 0 && n != loaded) || !Takes(tu, n)) continue;
                    if (Take(a.m_ammo.m_itemData.m_shared.m_name)) { nv.InvokeRPC("RPC_AddAmmo", n); return; }
                }
            }
            else if (st is Fermenter fe)
            {
                if (z.GetInt(ZDOVars.s_content, 0) != 0) return;   // busy or ready to tap
                Stock.Around(fe.transform.position, Cfg.StackRange.Value, id, _chests);
                foreach (var cv in fe.m_conversion)
                {
                    if (cv.m_from == null || !Takes(fe, cv.m_from.gameObject.name)) continue;
                    if (Take(cv.m_from.m_itemData.m_shared.m_name)) { nv.InvokeRPC("RPC_AddItem", cv.m_from.gameObject.name.GetStableHashCode(), false); return; }
                }
            }
        }

        private static bool Take(string name) { foreach (var c in _chests) if (Stock.TakeOne(c, name)) return true; return false; }

        internal static int InChests(Component s, string name)
        {
            var p = Player.m_localPlayer; if (p == null) return 0;
            Stock.Around(s.transform.position, Cfg.StackRange.Value, p.GetPlayerID(), _chests);
            int n = 0, wl = Game.m_worldLevel;
            foreach (var c in _chests) foreach (var it in c.GetInventory().GetAllItems()) if (it.m_shared.m_name == name && it.m_worldLevel >= wl) n += it.m_stack;
            return n;
        }

        // the station's hover card says whether it's on Auto (and whose), or how to switch it on
        internal static string AutoLine(Component st)
        {
            if (On == null || !On.Value || st == null) return "";
            var nv = ViewOf(st); var p = Player.m_localPlayer; if (nv == null || !nv.IsValid() || p == null) return "";
            int m = Mode(st); long by = nv.GetZDO().GetLong(ByKey, 0L);
            string state = m == ForcedOn ? (by != 0L && by != p.GetPlayerID() ? "Auto: on (another player's)" : "Auto: on (set here)")
                         : m == ForcedOff ? "Auto: off (set here)" : Automation.Paused(st) ? "Auto: off (its type is off)" : "Auto: on (by its type)";
            var k = Effective; return "\n" + state + (k.MainKey == KeyCode.None ? "" : $"\n[<color=yellow><b>{k} + $KEY_Use</b></color>] Auto settings");
        }
        [HarmonyPostfix, HarmonyPatch(typeof(Smelter), "OnHoverAddOre")] private static void HoverOre(Smelter __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += Localization.instance.Localize(AutoLine(__instance)); }
        [HarmonyPostfix, HarmonyPatch(typeof(Smelter), "OnHoverAddFuel")] private static void HoverFuel(Smelter __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += Localization.instance.Localize(AutoLine(__instance)); }
        [HarmonyPostfix, HarmonyPatch(typeof(CookingStation), "OnHoverFuelSwitch")] private static void HoverOven(CookingStation __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += Localization.instance.Localize(AutoLine(__instance)); }
        [HarmonyPostfix, HarmonyPatch(typeof(ShieldGenerator), nameof(ShieldGenerator.GetHoverText))] private static void HoverShield(ShieldGenerator __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += Localization.instance.Localize(AutoLine(__instance)); }
        [HarmonyPostfix, HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))] private static void HoverTurret(Turret __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += Localization.instance.Localize(AutoLine(__instance)); }
        [HarmonyPostfix, HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))] private static void HoverFerm(Fermenter __instance, ref string __result) { if (!string.IsNullOrEmpty(__result)) __result += Localization.instance.Localize(AutoLine(__instance)); }
        [HarmonyPostfix, HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))] private static void HoverFire(Fireplace __instance, ref string __result) { if (!string.IsNullOrEmpty(__result) && !__instance.m_infiniteFuel) __result += Localization.instance.Localize(AutoLine(__instance)); }

        internal static void Clear() { SmelterCard.Close(); _smelters.Clear(); _fires.Clear(); _ovens.Clear(); _others.Clear(); }
    }

    // the station's Auto card, centre screen; the station key + use opens it, E / Esc / walking away close it
    internal static class SmelterCard
    {
        internal static bool Open => _root != null;
        private static RectTransform _root; private static Component _for; private static float _openedAt; private static bool _fromTab;

        // fromTab: opened from the Automation tab (any distance, over the inventory)
        internal static void Show(Component st, bool fromTab = false)
        {
            Close(); _for = st; _openedAt = Time.unscaledTime; _fromTab = fromTab;
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-smelter"); _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            Notices.OnTop(_root); _root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();   // its own canvas: without this no click reached the switches
            Fill(t);
        }

        private static void Fill(ThemeData t)
        {
            foreach (Transform c in _root) Object.Destroy(c.gameObject);
            var piece = _for.GetComponent<Piece>();
            var rows = new List<(string prefab, Sprite icon, string name, Sprite to)>();
            ItemDrop fuel = null;
            if (_for is Smelter sm)
            {
                fuel = sm.m_fuelItem;
                foreach (var cv in sm.m_conversion) if (cv.m_from != null) rows.Add((cv.m_from.gameObject.name, cv.m_from.m_itemData.GetIcon(), Localization.instance.Localize(cv.m_from.m_itemData.m_shared.m_name), cv.m_to != null ? cv.m_to.m_itemData.GetIcon() : null));
            }
            else if (_for is Fireplace f && !f.m_infiniteFuel) fuel = f.m_fuelItem;
            else if (_for is CookingStation o) fuel = o.m_fuelItem;
            else if (_for is ShieldGenerator g) foreach (var fi in g.m_fuelItems) { if (fi != null) rows.Add((fi.gameObject.name, fi.m_itemData.GetIcon(), Localization.instance.Localize(fi.m_itemData.m_shared.m_name), null)); }
            else if (_for is Turret tu) foreach (var a in tu.m_allowedAmmo) { if (a.m_ammo != null) rows.Add((a.m_ammo.gameObject.name, a.m_ammo.m_itemData.GetIcon(), Localization.instance.Localize(a.m_ammo.m_itemData.m_shared.m_name), null)); }
            else if (_for is Fermenter fe)
                foreach (var cv in fe.m_conversion)   // only the bases in your chests and the ones already switched on: the full list is ~25
                    if (cv.m_from != null && (Smelters.Takes(fe, cv.m_from.gameObject.name) || Smelters.SetHere(fe, cv.m_from.gameObject.name) || Smelters.InChests(fe, cv.m_from.m_itemData.m_shared.m_name) > 0))
                        rows.Add((cv.m_from.gameObject.name, cv.m_from.m_itemData.GetIcon(), Localization.instance.Localize(cv.m_from.m_itemData.m_shared.m_name), cv.m_to != null ? cv.m_to.m_itemData.GetIcon() : null));
            if (fuel != null) rows.Insert(0, (fuel.gameObject.name, fuel.m_itemData.GetIcon(), Localization.instance.Localize(fuel.m_itemData.m_shared.m_name) + "  <size=80%><color=#9a9a9a>fuel</color></size>", null));
            const float W = 380; float h = 70 + 46 + rows.Count * 40 + 44;
            _root.sizeDelta = new Vector2(W, h);
            var (bg, _) = Kit.Box(_root, "Bg", Theme.C(t.glass), Theme.C(t.glassLine), 12); bg.rectTransform.Fill(); bg.raycastTarget = true;
            if (piece != null && piece.m_icon != null) { var ic = Kit.Img(_root, "Icon", piece.m_icon, Color.white, false); ic.rectTransform.At(16, 14, 36, 36); ic.preserveAspect = true; }
            var title = Kit.Text(_root, "Title", Localization.instance.Localize(piece != null ? piece.m_name : _for.name), Fonts.Norse, 22f, Color.white); title.rectTransform.At(62, 14, W - 110, 22);
            var sub = Kit.Text(_root, "Sub", "Tops up from chests within " + Cfg.StackRange.Value + " m", Fonts.Medium, 12f, Theme.C(t.mute)); sub.rectTransform.At(62, 36, W - 110, 18);
            Button(_root, "×", W - 42, 14, 28, () => Close(), t);
            // Auto switch
            int mode = Smelters.Mode(_for);
            var al = Kit.Text(_root, "AutoL", "Auto top-up", Fonts.Bold, 14f, Color.white); al.rectTransform.At(16, 72, 120, 28); al.alignment = TextAlignmentOptions.MidlineLeft;
            bool typeOn = !Automation.Paused(_for);
            float sx = W - 16 - 3 * 64;   // Type (its building type decides) / On / Off: set here, it wins over the Automation tab
            foreach (var (label, m) in new[] { ($"Type · {(typeOn ? "on" : "off")}", Smelters.Follow), ("On", Smelters.ForcedOn), ("Off", Smelters.ForcedOff) })
            {
                bool sel = mode == m; int mm = m;
                var (b, f) = Kit.Box(_root, "Mode", sel ? Theme.C(t.accentSoft) : Theme.C(t.raised), sel ? Theme.C(t.accent) : Theme.C(t.line2), 7); b.rectTransform.At(sx, 72, 62, 28); b.raycastTarget = true;
                var bt = Kit.Text(b.rectTransform, "T", label, Fonts.Medium, 11.5f, sel ? Color.white : Theme.C(t.dim)); bt.alignment = TextAlignmentOptions.Center; bt.rectTransform.Fill(); bt.enableAutoSizing = true; bt.fontSizeMin = 9f; bt.fontSizeMax = 11.5f;
                b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { Smelters.SetMode(_for, mm); Fill(t); });
                sx += 64;
            }
            var line = Kit.Img(_root, "Line", null, Theme.C(t.line2)); line.rectTransform.At(16, 112, W - 32, 1);
            Knots.Divider(line, false, Theme.C(t.accent));   // design .kdiv: between the switches and the inputs
            float y = 122;
            foreach (var (prefab, icon, name, to) in rows)
            {
                var ii = Kit.Img(_root, "I", icon, Color.white, false); ii.rectTransform.At(16, y + 4, 28, 28); ii.preserveAspect = true;
                if (to != null) { var ar = Kit.Text(_root, "Arrow", "→", Fonts.Medium, 13f, Theme.C(t.mute)); ar.rectTransform.At(48, y, 16, 36); ar.alignment = TextAlignmentOptions.Center; var ti = Kit.Img(_root, "To", to, Color.white, false); ti.rectTransform.At(66, y + 8, 20, 20); ti.preserveAspect = true; }
                float nx = to != null ? 94 : 54;
                var nm = Kit.Text(_root, "N", name, Fonts.Medium, 13f, Color.white); nm.rectTransform.At(nx, y + 2, 180, 18); nm.overflowMode = TextOverflowModes.Ellipsis;
                var shared = ShareName(prefab);
                var have = Kit.Text(_root, "H", shared != null ? $"{Smelters.InChests(_for, shared)} in chests" : "", Fonts.Mono, 10.5f, Theme.C(t.mute)); have.rectTransform.At(nx, y + 19, 180, 15);
                var pf = prefab;
                // the switch shows what it takes now; flipping it sets it here (wins over the type); "reset" goes back to the type
                Switch(_root, W - 62, y + 7, Smelters.Takes(_for, prefab), () => { Smelters.SetTakes(_for, pf, !Smelters.Takes(_for, pf)); Fill(t); }, t);
                if (Smelters.SetHere(_for, prefab))
                {
                    var rs = Kit.Text(_root, "Reset", "set here · <u>reset</u>", Fonts.Medium, 10.5f, Theme.C(t.accent)); rs.richText = true; rs.alignment = TextAlignmentOptions.MidlineRight; rs.rectTransform.At(W - 62 - 130, y + 8, 124, 20); rs.raycastTarget = true;
                    rs.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { Smelters.ResetTakes(_for, pf); Fill(t); });
                }
                else { var bytype = Kit.Text(_root, "ByType", "by type", Fonts.Medium, 10.5f, Theme.C(t.mute)); bytype.alignment = TextAlignmentOptions.MidlineRight; bytype.rectTransform.At(W - 62 - 130, y + 8, 124, 20); }
                y += 40;
            }
            var hint = Kit.Text(_root, "Hint", "E or Esc to close", Fonts.Mono, 10.5f, Theme.C(t.mute)); hint.rectTransform.At(16, h - 32, W - 32, 18);
        }

        private static string ShareName(string prefab) { var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null; var d = go != null ? go.GetComponent<ItemDrop>() : null; return d != null ? d.m_itemData.m_shared.m_name : null; }

        private static void Button(RectTransform p, string label, float x, float y, float w, System.Action click, ThemeData t)
        {
            var (b, _) = Kit.Box(p, "Btn", Theme.C(t.raised), Theme.C(t.line2), 7); b.rectTransform.At(x, y, w, 28); b.raycastTarget = true;
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Bold, 15f, Color.white); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
            b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => click());
        }

        private static void Switch(RectTransform p, float x, float y, bool on, System.Action flip, ThemeData t)
        {
            var track = Kit.Panel(p, "Sw", on ? Theme.C(t.accent) : new Color(1f, 1f, 1f, 0.14f), 11); track.rectTransform.At(x, y, 44, 22); track.raycastTarget = true;
            var knob = Kit.Img(track.rectTransform, "K", Kit.Circle(), Color.white, false); knob.rectTransform.At(on ? 24 : 3, 3, 16, 16);
            track.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => flip());
        }

        internal static void Tick()
        {
            if (_root == null) return;
            var p = Player.m_localPlayer;
            bool away = p == null || _for == null || (!_fromTab && (_for.transform.position - p.transform.position).sqrMagnitude > 8f * 8f);
            bool key = Time.unscaledTime - _openedAt > 0.3f && (Input.GetKeyDown(KeyCode.Escape) || (!_fromTab && Input.GetKeyDown(KeyCode.E)));
            if (away || key || (!_fromTab && InventoryGui.IsVisible()) || (_fromTab && !InventoryGui.IsVisible()) || Menu.IsVisible()) Close();
        }

        internal static void Close() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; _for = null; }
    }
}
