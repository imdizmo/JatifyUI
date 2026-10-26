using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Every station you switched to Auto (smelters, kilns, refineries, fires, hearths...), kept on your character
    // (m_customData, "zdoid|prefab|x|z;..."), because your game only knows the stations loaded around you. The
    // Automation page lists them: where, how far, and, when loaded, what it may burn and what it holds now.
    internal static class Automation
    {
        private const string Key = "jatify.autos", Max = "40";   // ponytail: at most 40 kept, oldest dropped
        internal struct Entry { public ZDOID Id; public string Prefab; public Vector3 Pos; }

        internal static List<Entry> List(Player p)
        {
            var list = new List<Entry>();
            if (p == null || !p.m_customData.TryGetValue(Key, out var s) || string.IsNullOrEmpty(s)) return list;
            foreach (var row in s.Split(';'))
            {
                var f = row.Split('|'); if (f.Length != 4) continue;
                var id = f[0].Split(':'); if (id.Length != 2 || !long.TryParse(id[0], out var u) || !uint.TryParse(id[1], out var n)) continue;
                if (!float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) continue;
                list.Add(new Entry { Id = new ZDOID(u, n), Prefab = f[1], Pos = new Vector3(x, 0f, z) });
            }
            return list;
        }

        private static void Save(Player p, List<Entry> list)
        {
            if (list.Count > int.Parse(Max)) list.RemoveRange(0, list.Count - int.Parse(Max));
            p.m_customData[Key] = string.Join(";", list.Select(e => $"{e.Id.UserID}:{e.Id.ID}|{e.Prefab}|{e.Pos.x.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}|{e.Pos.z.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}"));
            if (list.Count == 0) p.m_customData.Remove(Key);
        }

        // Auto switched on / off by you, or found on / off when loaded
        internal static void Track(Component station, bool on)
        {
            var p = Player.m_localPlayer; var nv = Smelters.ViewOf(station); if (p == null || nv == null || !nv.IsValid()) return;
            var id = nv.GetZDO().m_uid; var list = List(p); int i = list.FindIndex(e => e.Id == id);
            if (on && i < 0) list.Add(new Entry { Id = id, Prefab = Utils.GetPrefabName(station.gameObject), Pos = station.transform.position });
            else if (!on && i >= 0) list.RemoveAt(i);
            else return;
            Save(p, list);
        }

        // pauses you set on the Automation page (on your character, so they hold for far stations too): "cat:<name>" or "id:<uid>:<id>"
        private const string PauseKey = "jatify.autopause";
        private static HashSet<string> Pauses(Player p) => p != null && p.m_customData.TryGetValue(PauseKey, out var s) && s.Length > 0 ? new HashSet<string>(s.Split(';')) : new HashSet<string>();
        private static void SetPause(string token, bool paused)
        {
            var p = Player.m_localPlayer; if (p == null) return; var set = Pauses(p);
            if (paused) set.Add(token); else set.Remove(token);
            if (set.Count == 0) p.m_customData.Remove(PauseKey); else p.m_customData[PauseKey] = string.Join(";", set);
        }
        internal static string Cat(string prefab)
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null; if (go == null) return prefab;
            var piece = go.GetComponent<Piece>(); return piece != null ? Localization.instance.Localize(piece.m_name) : prefab;
        }
        private static string IdToken(ZDOID id) => $"id:{id.UserID}:{id.ID}";
        // a building type is off until switched on in the Automation tab (user: everything off by default)
        internal static bool CatPaused(string cat) => !Pauses(Player.m_localPlayer).Contains("type:" + cat);
        internal static bool IdPaused(ZDOID id) => Pauses(Player.m_localPlayer).Contains(IdToken(id));
        internal static void PauseCat(string cat, bool paused) => SetPause("type:" + cat, !paused);
        internal static void PauseId(ZDOID id, bool paused) => SetPause(IdToken(id), paused);
        // the feeder asks: this station paused, by itself or by its kind?
        internal static bool Paused(Component st)
        {
            var nv = Smelters.ViewOf(st); if (nv == null || !nv.IsValid()) return false;
            return CatPaused(Cat(Utils.GetPrefabName(st.gameObject)));   // per station it's the Auto switch itself
        }

        // a station far away switched from the tab (hosting): kept in the list like one switched by hand
        internal static void TrackFar(ZDOID id, string prefab, Vector3 pos, bool on)
        {
            var p = Player.m_localPlayer; if (p == null) return; var list = List(p); int i = list.FindIndex(e => e.Id == id);
            if (on && i < 0) list.Add(new Entry { Id = id, Prefab = prefab, Pos = pos }); else if (!on && i >= 0) list.RemoveAt(i); else return;
            Save(p, list);
        }

        // what a whole building type may take (Automation tab), on your character: "on:<type>|<item>"; everything is off
        // until switched on (user)
        private const string ItemsKey = "jatify.autoitems";
        private static HashSet<string> Items(Player p) => p != null && p.m_customData.TryGetValue(ItemsKey, out var s) && s.Length > 0 ? new HashSet<string>(s.Split(';')) : new HashSet<string>();
        internal static bool TypeTakes(string cat, string prefab) => Items(Player.m_localPlayer).Contains($"on:{cat}|{prefab}");
        internal static void SetTypeTakes(string cat, string prefab, bool takes)
        {
            var p = Player.m_localPlayer; if (p == null) return; var set = Items(p);
            var token = $"on:{cat}|{prefab}";
            if (takes) set.Add(token); else set.Remove(token);
            if (set.Count == 0) p.m_customData.Remove(ItemsKey); else p.m_customData[ItemsKey] = string.Join(";", set);
        }

        // what a type of station can take, from its prefab: (item prefab, icon, name, what it makes, is fuel)
        internal static List<(string prefab, Sprite icon, string name, Sprite to, bool fuel)> Inputs(string stationPrefab)
        {
            var list = new List<(string, Sprite, string, Sprite, bool)>(); var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(stationPrefab) : null; if (go == null) return list;
            void Add(ItemDrop d, ItemDrop to, bool fuel) { if (d != null && !list.Any(x => x.Item1 == d.gameObject.name)) list.Add((d.gameObject.name, d.m_itemData.GetIcon(), Localization.instance.Localize(d.m_itemData.m_shared.m_name), to != null ? to.m_itemData.GetIcon() : null, fuel)); }
            var sm = go.GetComponent<Smelter>(); if (sm != null) { Add(sm.m_fuelItem, null, true); foreach (var c in sm.m_conversion) Add(c.m_from, c.m_to, false); }
            var f = go.GetComponent<Fireplace>(); if (f != null) Add(f.m_fuelItem, null, true);
            var o = go.GetComponent<CookingStation>(); if (o != null && o.m_useFuel) Add(o.m_fuelItem, null, true);
            var g = go.GetComponent<ShieldGenerator>(); if (g != null) foreach (var d in g.m_fuelItems) Add(d, null, true);
            var tu = go.GetComponent<Turret>(); if (tu != null) foreach (var a in tu.m_allowedAmmo) Add(a.m_ammo, null, false);
            var fe = go.GetComponent<Fermenter>(); if (fe != null) foreach (var c in fe.m_conversion) Add(c.m_from, c.m_to, false);
            return list;
        }

        internal static void Forget(ZDOID id) { var p = Player.m_localPlayer; if (p == null) return; var list = List(p); if (list.RemoveAll(e => e.Id == id) > 0) Save(p, list); }
    }

    internal sealed class AutomationPage
    {
        internal readonly RectTransform Root;
        private readonly ThemeData _t;
        private readonly RectTransform _list;
        private readonly TextMeshProUGUI _count;
        private string _key; private float _next;

        // every prefab that can go on Auto (smelters, fires and ovens that burn fuel, shields, ballistas, fermenters)
        private static Dictionary<int, string> _kinds;
        internal static Dictionary<int, string> Kinds()
        {
            if (_kinds != null || ZNetScene.instance == null) return _kinds;
            _kinds = new Dictionary<int, string>();
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (go == null || go.GetComponent<Piece>() == null) continue;
                var f = go.GetComponent<Fireplace>(); var o = go.GetComponent<CookingStation>();
                bool ok = go.GetComponent<Smelter>() != null || (f != null && !f.m_infiniteFuel && f.m_fuelItem != null) || (o != null && o.m_useFuel && o.m_fuelItem != null)
                          || go.GetComponent<ShieldGenerator>() != null || go.GetComponent<Turret>() != null || go.GetComponent<Fermenter>() != null;
                if (ok) _kinds[go.name.GetStableHashCode()] = go.name;
            }
            return _kinds;
        }
        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> All = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
        internal static IEnumerable<ZDO> AllZdos() => ZDOMan.instance != null ? All(ZDOMan.instance).Values : (IEnumerable<ZDO>)new ZDO[0];

        private static readonly Dictionary<string, string> _cats = new Dictionary<string, string>();   // prefab -> building type name (one per prefab)
        private static string CatOf(string prefab) { if (!_cats.TryGetValue(prefab, out var c)) _cats[prefab] = c = Automation.Cat(prefab); return c; }

        private struct Row { public ZDOID Id; public ZDO Zdo; public string Prefab; public Vector3 Pos; public bool Auto, Mine, FromServer, Built; public GameObject Go; }

        internal AutomationPage(RectTransform parent, ThemeData t)
        {
            _t = t;
            Root = Kit.Node(parent, "Automation").Fill();
            var title = Kit.Text(Root, "Title", "Automation", Fonts.Norse, 24f, Theme.C(t.text)); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 0, 642, 38);
            Kit.Heading(title, 6f, Theme.C(t.accent));   // design .htitle: the knots, lines fading out to the panel's edges
            var hl = Kit.Img(Root, "Line", null, Theme.C(t.glassLine)); hl.rectTransform.At(-14, 44, 670, 1);
            Knots.Divider(hl, false, Theme.C(t.accent));   // design .kdiv: a small knot in the section line
            _count = Kit.Text(Root, "Count", "", Fonts.Mono, 11f, Theme.C(t.mute)); _count.characterSpacing = 8f; _count.richText = true; _count.rectTransform.At(18, 60, 606, 14);
            _list = Pages.Scroll(Root, 18, 84, 606, 640);
            var hint = Kit.Text(Root, "Hint", "Everything is off until you switch it on: a type, then what it may take. A station's own settings (hold Left Shift and use it) win over these. Stations on Auto top themselves up from nearby chests while you're within 40 m.", Fonts.Medium, 12f, Theme.C(t.mute));
            hint.textWrappingMode = TextWrappingModes.Normal; hint.rectTransform.At(18, 734, 606, 40);
        }

        // what the game knows (every saved station when you host; the ones loaded near you on a server) and the Auto ones you remember
        private static List<Row> Collect(Player p)
        {
            var rows = new List<Row>(); var kinds = Kinds(); var zm = ZDOMan.instance; if (kinds == null || zm == null) return rows;
            var seen = new HashSet<ZDOID>(); long me = p.GetPlayerID();
            foreach (var z in All(zm).Values)
            {
                if (z == null || !kinds.TryGetValue(z.GetPrefab(), out var prefab)) continue;
                seen.Add(z.m_uid);
                int mode = z.GetInt(Smelters.AutoKey, Smelters.Follow); bool typeOn = !Automation.CatPaused(CatOf(prefab));   // set on the station, else its type
                rows.Add(new Row { Id = z.m_uid, Zdo = z, Prefab = prefab, Pos = z.GetPosition(), Auto = mode == Smelters.ForcedOn || (mode == Smelters.Follow && typeOn),
                                   Mine = mode != Smelters.ForcedOn || z.GetLong(Smelters.ByKey, 0L) == me, Built = z.GetLong(ZDOVars.s_creator, 0L) == me, Go = ZNetScene.instance.FindInstance(z.m_uid) });
            }
            if (ServerLink.Remote != null)   // the server runs Jatify: its list covers the whole world
                foreach (var st in ServerLink.Remote) if (seen.Add(st.Id)) rows.Add(new Row { Id = st.Id, Prefab = st.Prefab, Pos = st.Pos, Auto = st.Auto, Mine = st.By == me, Built = st.Creator == me, FromServer = true });
            foreach (var e in Automation.List(p)) if (!seen.Contains(e.Id)) rows.Add(new Row { Id = e.Id, Prefab = e.Prefab, Pos = e.Pos, Auto = true, Mine = true });   // out of reach: as last known
            return rows;
        }

        internal void Tick()
        {
            var p = Player.m_localPlayer; if (p == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f; ServerLink.Ask();   // a server with Jatify sends the whole world's stations
            var rows = Collect(p); var me = p.transform.position;
            var key = new System.Text.StringBuilder();
            foreach (var r in rows) key.Append(r.Id).Append(r.Auto).Append(r.Go != null).Append(Mathf.RoundToInt(Utils.DistanceXZ(me, r.Pos) / 10f)).Append(r.Go != null && r.Auto ? State(r.Go) : "").Append(';');
            p.m_customData.TryGetValue("jatify.autopause", out var pz); p.m_customData.TryGetValue("jatify.autoitems", out var pi); key.Append(pz).Append(pi).Append(string.Join(",", _open));
            if (key.ToString() == _key) return; _key = key.ToString();
            Fill(rows, me);
        }

        // a loaded station now: what it holds, what it may take
        private static string State(GameObject go)
        {
            var st = Smelters.StationOf(go); var nv = st != null ? Smelters.ViewOf(st) : null; if (nv == null || !nv.IsValid()) return "";
            var z = nv.GetZDO(); var parts = new List<string>();
            if (st is Smelter sm)
            {
                if (sm.m_maxOre > 0) parts.Add($"{z.GetInt(ZDOVars.s_queued)}/{sm.m_maxOre} queued");
                if (sm.m_fuelItem != null && sm.m_maxFuel > 0) parts.Add($"{Localization.instance.Localize(sm.m_fuelItem.m_itemData.m_shared.m_name)} {Mathf.FloorToInt(z.GetFloat(ZDOVars.s_fuel))}/{sm.m_maxFuel}");
            }
            else if (st is Fireplace fp) parts.Add($"{Localization.instance.Localize(fp.m_fuelItem.m_itemData.m_shared.m_name)} {Mathf.FloorToInt(z.GetFloat(ZDOVars.s_fuel))}/{fp.m_maxFuel:0}");
            else if (st is CookingStation ov) parts.Add($"{Localization.instance.Localize(ov.m_fuelItem.m_itemData.m_shared.m_name)} {Mathf.FloorToInt(z.GetFloat(ZDOVars.s_fuel))}/{ov.m_maxFuel}");
            else if (st is ShieldGenerator g) parts.Add($"fuel {Mathf.FloorToInt(z.GetFloat(ZDOVars.s_fuel, g.m_defaultFuel))}/{g.m_maxFuel}");
            else if (st is Turret tu) parts.Add($"ammo {z.GetInt(ZDOVars.s_ammo)}/{tu.m_maxAmmo}");
            else if (st is Fermenter) parts.Add(z.GetInt(ZDOVars.s_content, 0) == 0 ? "empty" : "fermenting / ready");
            return string.Join("  ·  ", parts);
        }

        // general settings only (user): one row per building type, its counts and one switch for the whole type; a click opens
        // what that type may take, with a switch each. Each station's own Auto and items are on its card (station key + use)
        internal static readonly HashSet<string> _open = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private void Fill(List<Row> rows, Vector3 me)
        {
            var t = _t; var accent = ColorUtility.ToHtmlStringRGB(Theme.C(t.accent));
            foreach (Transform c in _list) Object.Destroy(c.gameObject);
            int auto = rows.Count(r => r.Auto && r.Mine);
            _count.text = rows.Count == 0 ? "NO STATIONS KNOWN" : $"{rows.Count} STATIONS  ·  <color=#{accent}>{auto} ON AUTO</color>";
            float y = 0f;
            foreach (var grp in rows.GroupBy(r => Automation.Cat(r.Prefab)).OrderByDescending(g => g.Count(r => r.Auto && r.Mine)).ThenBy(g => g.Key))
            {
                string cat = grp.Key; bool catOn = !Automation.CatPaused(cat), open = _open.Contains(cat); int on = grp.Count(r => r.Auto && r.Mine);
                int feeding = catOn ? grp.Count(r => r.Auto && r.Mine && r.Go != null && Utils.DistanceXZ(me, r.Pos) <= 40f) : 0;
                string stationPrefab = grp.First().Prefab; var prefab0 = ZNetScene.instance.GetPrefab(stationPrefab); var icon = prefab0 != null ? prefab0.GetComponent<Piece>()?.m_icon : null;
                var (row, _) = Kit.Box(_list, "Kind", Theme.C(t.raised), feeding > 0 || open ? Theme.C(t.accent) : Theme.C(t.glassLine), 10); row.rectTransform.At(0, y, 606, 56); row.raycastTarget = true;
                var g = row.gameObject.AddComponent<CanvasGroup>(); g.alpha = catOn ? 1f : 0.6f;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (!_open.Remove(cat)) _open.Add(cat); _key = null; _next = 0f; });
                if (icon != null) { var hi = Kit.Img(row.rectTransform, "I", icon, Color.white, false); hi.rectTransform.At(12, 10, 36, 36); hi.preserveAspect = true; }
                var hn = Kit.Text(row.rectTransform, "N", cat, Fonts.Bold, 15f, Color.white); hn.rectTransform.At(60, 9, 380, 20);
                string line = $"{grp.Count()} built  ·  " + (on == 0 ? "none on Auto" : $"<color=#{accent}>{on} on Auto</color>") + (!catOn ? "  ·  off" : feeding > 0 ? $"  ·  {feeding} feeding now" : "") + (open ? "" : "  ·  click for what it takes");
                var sub = Kit.Text(row.rectTransform, "S", line, Fonts.Medium, 12f, Theme.C(t.dim)); sub.rectTransform.At(60, 30, 470, 18);
                Switch(row.rectTransform, 606 - 12 - 44, 17, catOn, () => Automation.PauseCat(cat, catOn), t);
                y += 62f;
                if (!open) continue;
                var inputs = Automation.Inputs(stationPrefab);
                foreach (var (prefab, ic, name, to, fuel) in inputs)
                {
                    var (ir, _) = Kit.Box(_list, "Item", new Color(0f, 0f, 0f, 0.2f), Theme.C(t.glassLine), 8); ir.rectTransform.At(20, y, 586, 40);
                    var ii = Kit.Img(ir.rectTransform, "I", ic, Color.white, false); ii.rectTransform.At(10, 6, 28, 28); ii.preserveAspect = true;
                    float nx = 48f;
                    if (to != null) { var ar = Kit.Text(ir.rectTransform, "A", "→", Fonts.Medium, 13f, Theme.C(t.mute)); ar.alignment = TextAlignmentOptions.Center; ar.rectTransform.At(40, 0, 18, 40); var ti = Kit.Img(ir.rectTransform, "To", to, Color.white, false); ti.rectTransform.At(60, 10, 20, 20); ti.preserveAspect = true; nx = 90f; }
                    var nm = Kit.Text(ir.rectTransform, "N", name + (fuel ? $"  <size=80%><color=#{ColorUtility.ToHtmlStringRGB(Theme.C(t.mute))}>fuel</color></size>" : ""), Fonts.Medium, 13f, Color.white); nm.rectTransform.At(nx, 0, 380, 40); nm.alignment = TextAlignmentOptions.MidlineLeft;
                    bool takes = Automation.TypeTakes(cat, prefab); var pf = prefab;
                    Switch(ir.rectTransform, 586 - 12 - 44, 9, takes, () => Automation.SetTypeTakes(cat, pf, !takes), t);
                    y += 44f;
                }
                y += 8f;
            }
            if (rows.Count == 0) { var none = Kit.Text(_list, "None", "No smelter, kiln, fire, oven, shield, ballista or fermenter known yet.", Fonts.Medium, 14f, Theme.C(t.mute)); none.rectTransform.At(0, 6, 600, 22); y = 34f; }
            _list.sizeDelta = new Vector2(0f, y + 10f);
        }

        private void Switch(RectTransform p, float x, float y, bool on, System.Action flip, ThemeData t)
        {
            var track = Kit.Panel(p, "Sw", on ? Theme.C(t.accent) : new Color(1f, 1f, 1f, 0.14f), 11); track.rectTransform.At(x, y, 44, 22); track.raycastTarget = true;
            var knob = Kit.Img(track.rectTransform, "K", Kit.Circle(), Color.white, false); knob.rectTransform.At(on ? 24 : 3, 3, 16, 16);
            track.gameObject.AddComponent<Button>().onClick.AddListener(() => { flip(); _key = null; _next = 0f; });
        }
    }
}
