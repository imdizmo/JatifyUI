using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Live marks on the world map (user): every portal, ship
    // and cart the world knows, as unsaved pins, but only where you have explored (nothing is revealed that
    // you have not seen) and not where you already pinned that portal yourself. Portals carry their tag; the
    // portal under the cursor or picked in the list shows where it leads with a flowing dashed line and the
    // distance. Ships and carts move with their objects. Nothing here is saved; on unload the pins go.
    internal static class MapMarks
    {
        internal static readonly Minimap.PinType Vehicle = (Minimap.PinType)System.Enum.GetValues(typeof(Minimap.PinType)).Length;   // past vanilla's types
        private sealed class Mark { internal ZDO Zdo; internal Minimap.PinData Pin; internal bool Portal; internal Sprite Icon; }
        private static readonly Dictionary<ZDOID, Mark> _marks = new Dictionary<ZDOID, Mark>();
        private static readonly Dictionary<int, (string name, Sprite icon)> _vehicles = new Dictionary<int, (string, Sprite)>();
        private static readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();
        private static readonly AccessTools.FieldRef<ZDOMan, List<ZDO>[]> Sectors = AccessTools.FieldRefAccess<ZDOMan, List<ZDO>[]>("m_objectsBySector");
        private static readonly AccessTools.FieldRef<Minimap, bool[]> Visible = AccessTools.FieldRefAccess<Minimap, bool[]>("m_visibleIconTypes");
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        private static readonly System.Reflection.MethodInfo ToMap = AccessTools.Method(typeof(Minimap), "WorldToMapPoint");
        private static int _sector; private static bool _scanning; private static float _nextPass;

        internal static bool Is(Minimap.PinData p) { foreach (var m in _marks.Values) if (m.Pin == p) return true; return false; }
        internal static int VehicleCount { get { int n = 0; foreach (var m in _marks.Values) if (!m.Portal) n++; return n; } }

        // ---- finding them -------------------------------------------------------------------------
        internal static void Tick(Minimap mm)
        {
            if (mm == null || ZDOMan.instance == null || ZNetScene.instance == null || Player.m_localPlayer == null) return;
            if (_vehicles.Count == 0) Init();
            Extend(mm);
            if (!_scanning && Time.unscaledTime >= _nextPass) { _scanning = true; _sector = 0; _seen.Clear(); }
            if (_scanning) Scan(mm);
            foreach (var m in _marks.Values)
            {   // vehicles move; a renamed portal follows its tag
                if (m.Pin == null || !m.Zdo.IsValid()) continue;
                if (!m.Portal) { var at = m.Zdo.GetPosition(); if ((at - m.Pin.m_pos).sqrMagnitude > 1f) m.Pin.m_pos = at; }
                else { var tag = m.Zdo.GetString(ZDOVars.s_tag, ""); var want = tag.Length > 0 ? tag : Localization.instance.Localize("$piece_portal"); if (m.Pin.m_name != want) m.Pin.m_name = want; }
                if (m.Icon != null && m.Pin.m_icon != m.Icon) m.Pin.m_icon = m.Icon;
            }
        }

        private static void Init()
        {
            foreach (var go in ZNetScene.instance.m_prefabs)
            {
                if (go == null || (go.GetComponent<Ship>() == null && go.GetComponent<Vagon>() == null)) continue;
                var piece = go.GetComponent<Piece>();
                _vehicles[go.name.GetStableHashCode()] = (Localization.instance.Localize(piece != null && !string.IsNullOrEmpty(piece.m_name) ? piece.m_name : go.name), piece != null ? piece.m_icon : null);
            }
        }

        // vanilla sizes its per-type arrays to its own enum: one more slot for our Vehicle type
        private static void Extend(Minimap mm)
        {
            var v = Visible(mm); int need = (int)Vehicle + 1;
            if (v != null && v.Length < need) { var n = new bool[need]; System.Array.Copy(v, n, v.Length); for (int i = v.Length; i < need; i++) n[i] = true; Visible(mm) = n; }
        }

        // a few hundred sectors a frame, then the portal list; every 2 s
        private static void Scan(Minimap mm)
        {
            var all = Sectors(ZDOMan.instance); if (all == null) { _scanning = false; return; }
            int done = 0;
            while (_sector < all.Length && done < 250)
            {
                var list = all[_sector++]; if (list == null) continue; done++;
                foreach (var z in list) if (z != null && _vehicles.TryGetValue(z.GetPrefab(), out var k)) Keep(mm, z, false, k.name, k.icon);
            }
            if (_sector < all.Length) return;
            foreach (var z in ZDOMan.instance.GetPortalList()) if (z != null) Keep(mm, z, true, null, null);
            var gone = new List<ZDOID>();
            foreach (var kv in _marks) if (!_seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) Drop(mm, id);
            // marks left by an earlier load of the mod: unsaved portal / vehicle pins are only ever ours
            var mine = new HashSet<Minimap.PinData>(); foreach (var mk in _marks.Values) mine.Add(mk.Pin);
            var pins = Pins(mm);
            for (int i = pins.Count - 1; i >= 0; i--) { var p = pins[i]; if (!p.m_save && (p.m_type == Vehicle || p.m_type == Minimap.PinType.Icon4) && !mine.Contains(p)) mm.RemovePin(p); }
            _scanning = false; _nextPass = Time.unscaledTime + 2f;
        }

        private static void Keep(Minimap mm, ZDO z, bool portal, string name, Sprite icon)
        {
            var pos = z.GetPosition();
            if (!MapStyle.KnownAt(mm, pos) || (portal && PinnedByHand(mm, pos))) return;   // only what you have seen; no twin of your own portal pin
            _seen.Add(z.m_uid);
            if (_marks.ContainsKey(z.m_uid)) return;
            var pin = mm.AddPin(pos, portal ? Minimap.PinType.Icon4 : Vehicle, portal ? "" : name, false, false, 0L);
            if (pin == null) return;
            if (icon != null) pin.m_icon = icon;
            _marks[z.m_uid] = new Mark { Zdo = z, Pin = pin, Portal = portal, Icon = icon };
        }

        private static bool PinnedByHand(Minimap mm, Vector3 pos)
        {
            foreach (var p in Pins(mm)) if (p.m_save && p.m_type == Minimap.PinType.Icon4 && Utils.DistanceXZ(p.m_pos, pos) < 12f) return true;
            return false;
        }

        private static void Drop(Minimap mm, ZDOID id) { if (_marks.TryGetValue(id, out var m)) { if (m.Pin != null) mm.RemovePin(m.Pin); _marks.Remove(id); } }

        internal static void Clear()
        {
            var mm = Minimap.instance;
            if (mm != null) foreach (var m in _marks.Values) if (m.Pin != null) mm.RemovePin(m.Pin);
            _marks.Clear(); _vehicles.Clear(); _scanning = false; _nextPass = 0f;
            LinkView.Destroy();
        }

        // ---- where a portal leads -----------------------------------------------------------------
        private static ZDOID TargetOf(ZDO z) { var x = z.GetZDOID("XPortal_TargetId"); return x != ZDOID.None ? x : z.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal); }   // XPortal's target if that mod set one

        // the portals this one connects to: vanilla's connection either way, else the one other portal with its tag
        internal static List<Minimap.PinData> LinkedTo(Minimap.PinData pin)
        {
            var into = new List<Minimap.PinData>();
            Mark me = null; foreach (var m in _marks.Values) if (m.Pin == pin && m.Portal) { me = m; break; }
            ZDO zdo = me?.Zdo;
            if (zdo == null && pin != null && pin.m_type == Minimap.PinType.Icon4)
            {   // your own portal pin: the portal object under it
                foreach (var z in ZDOMan.instance.GetPortalList()) if (z != null && Utils.DistanceXZ(z.GetPosition(), pin.m_pos) < 12f) { zdo = z; break; }
            }
            if (zdo == null || !zdo.IsValid()) return into;
            var target = TargetOf(zdo); var portals = ZDOMan.instance.GetPortalList();
            foreach (var z in portals)
                if (z != null && z != zdo && (z.m_uid == target || TargetOf(z) == zdo.m_uid)) { var p = PinAt(z); if (p != null && !into.Contains(p)) into.Add(p); }
            if (into.Count > 0) return into;
            var tag = zdo.GetString(ZDOVars.s_tag, ""); if (tag.Length == 0) return into;
            ZDO only = null; int n = 0;
            foreach (var z in portals) if (z != null && z != zdo && z.GetString(ZDOVars.s_tag, "") == tag) { only = z; n++; }
            if (n == 1) { var p = PinAt(only); if (p != null) into.Add(p); }
            return into;
        }

        // the pin standing for a portal object: our live mark, or your own portal pin there
        private static Minimap.PinData PinAt(ZDO z)
        {
            if (_marks.TryGetValue(z.m_uid, out var m) && m.Pin != null) return m.Pin;
            var mm = Minimap.instance; if (mm == null) return null;
            foreach (var p in Pins(mm)) if (p.m_type == Minimap.PinType.Icon4 && Utils.DistanceXZ(p.m_pos, z.GetPosition()) < 12f) return p;
            return null;
        }

        internal static bool IsPortal(Minimap.PinData p) => p != null && p.m_type == Minimap.PinType.Icon4;

        // a world position in the big map's pin space (where vanilla puts the pin markers)
        private static readonly object[] _mp = new object[3];
        internal static Vector2 OnMap(Minimap mm, Vector3 world)
        {
            _mp[0] = world; ToMap.Invoke(mm, _mp); float mx = (float)_mp[1], my = (float)_mp[2];
            var img = mm.m_mapImageLarge; var uv = img.uvRect; var r = img.rectTransform.rect;
            return new Vector2((mx - uv.xMin) / uv.width * r.width, (my - uv.yMin) / uv.height * r.height);
        }

        // ---- the line(s) ----------------------------------------------------------------------------
        internal static class LinkView
        {
            private static RectTransform _host;
            private static readonly List<(RawImage line, TextMeshProUGUI km)> _lines = new List<(RawImage, TextMeshProUGUI)>();
            private static Texture2D _dash;
            private static Minimap.PinData _from; private static List<Minimap.PinData> _to = new List<Minimap.PinData>();
            private static float _t0, _nextLook;

            internal static void Destroy() { if (_host != null) Object.Destroy(_host.gameObject); _host = null; _lines.Clear(); _from = null; }

            internal static void Show(Minimap mm, Minimap.PinData from, ThemeData t)
            {
                if (mm == null || mm.m_mode != Minimap.MapMode.Large || !IsPortal(from)) { Hide(); return; }
                if (from != _from || Time.unscaledTime >= _nextLook) { if (from != _from) _t0 = Time.unscaledTime; _from = from; _to = LinkedTo(from); _nextLook = Time.unscaledTime + 0.5f; }
                if (_to.Count == 0) { Hide(); return; }
                if (_host == null)
                {   // inside the map's mask, in the pins' own space, under the pins
                    _host = Kit.Node(mm.m_pinRootLarge, "jatify-portallinks"); _host.anchorMin = _host.anchorMax = Vector2.zero; _host.pivot = Vector2.zero; _host.anchoredPosition = Vector2.zero; _host.sizeDelta = Vector2.zero;
                    _host.SetAsFirstSibling();
                }
                if (!_host.gameObject.activeSelf) _host.gameObject.SetActive(true);
                var a = OnMap(mm, from.m_pos);
                float grow = Mathf.Clamp01((Time.unscaledTime - _t0) / 0.45f); grow = 1f - (1f - grow) * (1f - grow);   // the line draws itself out
                for (int i = 0; i < _to.Count; i++)
                {
                    if (i >= _lines.Count) _lines.Add(MakeLine(t));
                    var (line, km) = _lines[i]; line.gameObject.SetActive(true); km.gameObject.SetActive(true);
                    var b = OnMap(mm, _to[i].m_pos); var d = b - a; float len = d.magnitude * grow;
                    var rt = line.rectTransform; rt.anchoredPosition = a; rt.sizeDelta = new Vector2(len, 4f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    line.uvRect = new Rect(-Time.unscaledTime * 1.6f, 0f, len / 16f, 1f);   // dashes flow toward where it leads
                    km.gameObject.SetActive(false);   // the distance is on the card at the cursor (user)
                }
                for (int i = _to.Count; i < _lines.Count; i++) { _lines[i].line.gameObject.SetActive(false); _lines[i].km.gameObject.SetActive(false); }
            }

            internal static void Hide() { if (_host != null && _host.gameObject.activeSelf) _host.gameObject.SetActive(false); _from = null; }

            private static (RawImage, TextMeshProUGUI) MakeLine(ThemeData t)
            {
                if (_dash == null)
                {   // 16 x 4: 10 px of dash, 6 of gap, soft edges; repeats along the line
                    _dash = new Texture2D(16, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "jatify-dash" };
                    var px = new Color32[64];
                    for (int y = 0; y < 4; y++) for (int x = 0; x < 16; x++) px[y * 16 + x] = new Color32(255, 255, 255, (byte)(x < 10 && y > 0 && y < 3 ? 255 : 0));
                    _dash.SetPixels32(px); _dash.Apply(false, true);
                }
                var go = new GameObject("Line", typeof(RectTransform)); go.layer = 5; go.transform.SetParent(_host, false);
                var line = go.AddComponent<RawImage>(); line.texture = _dash; line.color = Theme.C(t.accent); line.raycastTarget = false;
                var rt = line.rectTransform; rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0f, 0.5f);
                var km = Kit.Text(_host, "Km", "", Fonts.Mono, 11f, Color.white); km.alignment = TextAlignmentOptions.Center; km.outlineWidth = 0.25f; km.outlineColor = new Color32(0, 0, 0, 200);
                var kr = km.rectTransform; kr.anchorMin = kr.anchorMax = Vector2.zero; kr.pivot = new Vector2(0.5f, 0.5f); kr.sizeDelta = new Vector2(80f, 18f);
                return (line, km);
            }
        }
    }
}
