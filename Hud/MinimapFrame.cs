using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The design's minimap box (top right): glass frame, the map, then biome + coordinates and
    // day/time + wind. The map itself stays Valheim's (terrain, fog, pins, player arrow): its image is
    // moved and resized into our frame each frame and its own frame graphics are switched off.
    // Restore() puts every moved rect and every switched-off graphic back.
    internal sealed class MinimapFrame
    {
        private const float W = 190f, Map = 174f, Pad = 8f;
        private readonly RectTransform _root, _hole;
        private readonly TextMeshProUGUI _biome, _coords, _day;
        private readonly RectTransform _windArrow;
        private readonly Image[] _bars = new Image[5];
        private readonly ThemeData _t;
        private int _cx = int.MinValue, _cz, _dayN = -1, _min = -1, _windN = -1, _real = -2;
        private Heightmap.Biome _biomeShown = (Heightmap.Biome)(-1);

        internal MinimapFrame(Transform parent, ThemeData t)
        {
            _t = t;
            var (line, fill) = Kit.Box(parent, "Minimap", Theme.C(t.glass), Theme.C(t.glassLine), 12);
            _root = line.rectTransform; _panelLine = line; _panelFill = fill;
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-28f, -24f); _root.sizeDelta = new Vector2(W, Pad + Map + 6 + 18 + 4 + 18 + Pad);
            _hole = Kit.Node(_root, "MapArea").At(Pad, Pad, Map, Map);  // where Valheim's map image is placed
            float y = Pad + Map + 6;
            _biome = Kit.Text(_root, "Biome", "", Fonts.Bold, 13f, Theme.C(t.text)); _biome.rectTransform.At(Pad, y, 110, 18); _biome.alignment = TextAlignmentOptions.MidlineLeft;
            _coords = Kit.Text(_root, "Coords", "", Fonts.Mono, 11f, Theme.C(t.dim)); _coords.rectTransform.At(W - Pad - 90, y, 90, 18); _coords.alignment = TextAlignmentOptions.MidlineRight;
            y += 22;
            _day = Kit.Text(_root, "Day", "", Fonts.Mono, 11f, Theme.C(t.dim)); _day.rectTransform.At(Pad, y, W - 2 * Pad - 56, 18); _day.alignment = TextAlignmentOptions.MidlineLeft;
            _day.enableAutoSizing = true; _day.fontSizeMin = 8f; _day.fontSizeMax = 11f;   // ends before the wind: the real clock ran under it (#22)
            // wind: an arrow pointing where the wind blows, relative to the camera, then five strength bars
            var arrow = Kit.Node(_root, "Wind").At(W - Pad - 52, y + 3, 12, 12);
            var icon = Kit.IconImg(arrow, "arrow-up", Theme.C(t.dim), 14f); icon.rectTransform.At(-1, -1, 14, 14);   // a whole arrow (user: the stick and square read as broken)
            arrow.pivot = new Vector2(0.5f, 0.5f); arrow.anchoredPosition += new Vector2(6f, -6f);
            _windArrow = arrow;
            for (int i = 0; i < 5; i++)
            {
                float h = 4 + i * 2;
                _bars[i] = Kit.Panel(_root, "Bar" + i, Theme.C(t.mute), 2);
                _bars[i].rectTransform.At(W - Pad - 34 + i * 7, y + 15 - h, 4, h);
            }
        }

        // MinimapPanel off (the default, user): no glass card behind the map and its lines, as in the Minimal look; switched live
        private readonly Image _panelLine, _panelFill; private int _panel = -1, _windPanel = -1; private Material[] _mat0;
        private readonly System.Collections.Generic.List<GameObject> _shades = new System.Collections.Generic.List<GameObject>(), _windShades = new System.Collections.Generic.List<GameObject>();
        private sealed class TextShade : MonoBehaviour
        {   // a shade copy follows its line's text and size
            internal TMP_Text Src, Me;
            private void LateUpdate() { if (Src == null || Me == null) return; if (Me.text != Src.text) Me.text = Src.text; if (Me.fontSize != Src.fontSize) Me.fontSize = Src.fontSize; }
        }
        private void TickPanel()
        {
            int on = Cfg.MinimapPanel != null && Cfg.MinimapPanel.Value ? 1 : 0; if (on == _panel) return; _panel = on;
            _panelLine.color = on == 1 ? Theme.C(_t.glassLine) : Color.clear; _panelFill.color = on == 1 ? Theme.C(_t.glass) : Color.clear;
            // with no card behind them the lines read against the world (user: unreadable; a strip behind them, no): white with a
            // solid dark outline and shadow
            bool bare = on == 0;
            foreach (var (x, font, dim) in new[] { (_biome, Fonts.Bold, false), (_coords, Fonts.Mono, true), (_day, Fonts.Mono, true) })
            {
                if (_mat0 == null) _mat0 = new Material[3];
                int k = x == _biome ? 0 : x == _coords ? 1 : 2; if (_mat0[k] == null) _mat0[k] = x.fontSharedMaterial;
                x.fontSharedMaterial = _mat0[k];
                x.color = bare ? Color.white : Theme.C(dim ? _t.dim : _t.text);
            }
            // the edge: four black copies of each line a pixel off it, drawn behind (TMP's own outline is too thin on these fonts)
            if (_shades.Count == 0)
                foreach (var x in new[] { _biome, _coords, _day })
                    foreach (var d in new[] { new Vector2(1.2f, -1.2f), new Vector2(-1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
                    {
                        var c = Object.Instantiate(x.gameObject, x.transform.parent); c.name = x.name + "-shade";
                        var ct = c.GetComponent<TextMeshProUGUI>(); ct.color = new Color(0f, 0f, 0f, 0.85f); ct.raycastTarget = false;
                        var crt = (RectTransform)c.transform; crt.anchoredPosition = x.rectTransform.anchoredPosition + d; c.transform.SetSiblingIndex(x.transform.GetSiblingIndex());
                        var f = c.AddComponent<TextShade>(); f.Src = x; f.Me = ct; _shades.Add(c);
                    }
            foreach (var c in _shades) if (c != null) c.SetActive(bare);
            foreach (var b in _bars) if (b != null) b.color = bare ? Color.white : Theme.C(_t.mute);
            // the wind arrow and bars: crisp, one soft drop shadow each (user: an outline all round smudged them)
            var arrow = _windArrow.GetComponentInChildren<Image>(); if (arrow != null) arrow.color = bare ? Color.white : Theme.C(_t.dim);
            if (_windShades.Count == 0)
                foreach (var g in _bars.Cast<Image>().Append(arrow))
                {
                    if (g == null) continue;
                    var c = Object.Instantiate(g.gameObject, g.transform.parent); c.name = g.name + "-shade";
                    var ci = c.GetComponent<Image>(); ci.color = new Color(0f, 0f, 0f, 0.7f); ci.raycastTarget = false;
                    var crt = (RectTransform)c.transform; crt.anchoredPosition = g.rectTransform.anchoredPosition + new Vector2(1f, -1f); c.transform.SetSiblingIndex(g.transform.GetSiblingIndex());
                    _windShades.Add(c);
                }
            foreach (var c in _windShades) if (c != null) c.SetActive(bare);
        }

        internal void Tick(Player p)
        {
            TickPanel();
            var map = Minimap.instance;
            bool cover = Menu.IsVisible() || ConfigPage.Open != null || InventoryGui.IsVisible();   // design: no minimap over the pause menu or the inventory
            bool show = map != null && map.m_smallRoot != null && map.m_smallRoot.activeInHierarchy && !cover;
            if (map != null && map.m_smallRoot != null)
            {   // vanilla's small map too (faded, not deactivated: vanilla reads its active state)
                var g = map.m_smallRoot.GetComponent<CanvasGroup>() ?? map.m_smallRoot.AddComponent<CanvasGroup>();
                float a = cover ? 0f : 1f; if (g.alpha != a) { g.alpha = a; g.blocksRaycasts = !cover; }
            }
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;
            var pos = p.transform.position;
            int cx = Mathf.RoundToInt(pos.x), cz = Mathf.RoundToInt(pos.z);
            if (cx != _cx || cz != _cz) { _cx = cx; _cz = cz; _coords.text = cx + ", " + cz; }
            var biome = p.GetCurrentBiome();
            if (biome != _biomeShown) { _biomeShown = biome; _biome.text = Localization.instance.Localize("$biome_" + biome.ToString().ToLowerInvariant()); }
            var env = EnvMan.instance;
            if (env != null)
            {
                int day = env.GetDay(), min = Mathf.FloorToInt(env.GetDayFraction() * 1440f);
                int real = Cfg.RealClock.Value == Cfg.Clock.Off ? -1 : System.DateTime.Now.Hour * 60 + System.DateTime.Now.Minute;
                if (day != _dayN || min != _min || real != _real)
                {
                    _dayN = day; _min = min; _real = real;
                    var now = System.DateTime.Now;
                    string clock = real < 0 ? "" : Cfg.RealClock.Value == Cfg.Clock.H24 ? $" · {now:HH:mm}" : $" · {now:h:mm tt}";
                    _day.text = $"Day {day} · {min / 60:00}:{min % 60:00}{clock}";
                }
                var cam = GameCamera.instance;
                var wd = env.GetWindDir();
                _windArrow.localEulerAngles = new Vector3(0f, 0f, -Quaternion.LookRotation(wd).eulerAngles.y);   // as Valheim's marker on this north-up map (user: ours pointed the other way)
                int n = Mathf.Clamp(Mathf.CeilToInt(env.GetWindIntensity() * 5f), 0, 5);
                if (n != _windN || _panel != _windPanel) { _windN = n; _windPanel = _panel; for (int i = 0; i < 5; i++) _bars[i].color = _panel == 0 ? (i < n ? Color.white : new Color(0f, 0f, 0f, 0.4f)) : Theme.C(i < n ? _t.text : _t.mute); }   // no panel: lit white, the rest dark (user: read as a smudge)
            }
        }

        internal RectTransform Hole => _root.gameObject.activeInHierarchy ? _hole : null;
        internal void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); }
    }

    // Moves Valheim's small map image (and pin layers that are not inside it) onto our frame's map area,
    // and switches off the vanilla frame graphics around it. Everything changed is recorded and undone.
    internal static class VanillaMinimap
    {
        private struct Saved { public RectTransform Rt; public Vector2 AMin, AMax, Pivot, Pos, Size; }
        private static readonly List<Saved> _moved = new List<Saved>();
        private static readonly List<Graphic> _off = new List<Graphic>();
        private static Minimap _for;
        private static readonly Vector3[] _c = new Vector3[4];

        internal static void Tick(RectTransform hole)
        {
            var map = Minimap.instance;
            if (map == null) { _moved.Clear(); _off.Clear(); _for = null; return; }
            if (hole == null) { if (_for != null) Restore(); return; }
            var img = map.m_mapImageSmall.rectTransform;
            if (_for != map)
            {
                Restore(); _for = map;
                foreach (var rt in new[] { img, map.m_pinRootSmall, map.m_pinNameRootSmall })
                    if (rt != null && (rt == img || !rt.IsChildOf(img))) _moved.Add(new Saved { Rt = rt, AMin = rt.anchorMin, AMax = rt.anchorMax, Pivot = rt.pivot, Pos = rt.anchoredPosition, Size = rt.sizeDelta });
                foreach (var g in map.m_smallRoot.GetComponentsInChildren<Graphic>(true))
                {
                    var t = g.transform;
                    bool keep = t.IsChildOf(img) || (map.m_pinRootSmall != null && t.IsChildOf(map.m_pinRootSmall)) || (map.m_pinNameRootSmall != null && t.IsChildOf(map.m_pinNameRootSmall))
                                || (map.m_smallMarker != null && t.IsChildOf(map.m_smallMarker));
                    if (map.m_windMarker != null && t.IsChildOf(map.m_windMarker)) keep = false;   // ours sits under the map (user: Valheim's still showed)
                    if (!keep && g.enabled) { g.enabled = false; _off.Add(g); }
                }
            }
            hole.GetWorldCorners(_c); // both canvases are screen-space overlays, so world space is screen space
            foreach (var s in _moved)
            {
                var par = (RectTransform)s.Rt.parent;
                Vector2 bl = par.InverseTransformPoint(_c[0]), tr = par.InverseTransformPoint(_c[2]);
                s.Rt.anchorMin = s.Rt.anchorMax = s.Rt.pivot = new Vector2(0.5f, 0.5f);
                s.Rt.localPosition = (bl + tr) / 2f;
                s.Rt.sizeDelta = tr - bl;
            }
            foreach (var g in _off) if (g != null && g.enabled) g.enabled = false; // the game may switch some back on
            var mapImg = map.m_mapImageSmall; bool paused = Menu.IsVisible() || ConfigPage.Open != null;
            if (mapImg != null && mapImg.enabled == paused) mapImg.enabled = !paused;   // the minimap belongs to the HUD, not the pause screen
        }

        internal static void Restore()
        {
            foreach (var s in _moved) if (s.Rt != null) { s.Rt.anchorMin = s.AMin; s.Rt.anchorMax = s.AMax; s.Rt.pivot = s.Pivot; s.Rt.anchoredPosition = s.Pos; s.Rt.sizeDelta = s.Size; }
            foreach (var g in _off) if (g != null) g.enabled = true;
            if (Minimap.instance != null && Minimap.instance.m_mapImageSmall != null) Minimap.instance.m_mapImageSmall.enabled = true;
            _moved.Clear(); _off.Clear(); _for = null;
        }
    }
}
