using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // World map in the live design's layout (vanilla map, pins and input; our frame around them):
    // a glass window with a sidebar (day and time, pin search, what to show with counts, every pin by
    // distance), the map beside it with "under cursor", zoom, a scale bar and a card for sharing, the
    // new pin's type and pin / name sizes, and a hint bar under it. Vanilla's own panels stay alive but
    // invisible (their toggles still work); everything through the ledger.
    internal static class MapStyle
    {
        private static readonly Ledger _l = new Ledger();
        private static Minimap _for;
        private static ThemeData _t;

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        private static readonly AccessTools.FieldRef<Minimap, bool[]> Visible = AccessTools.FieldRefAccess<Minimap, bool[]>("m_visibleIconTypes");
        private static readonly AccessTools.FieldRef<Minimap, bool> PinUpdate = AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");
        private static readonly AccessTools.FieldRef<Minimap, Vector3> Offset = AccessTools.FieldRefAccess<Minimap, Vector3>("m_mapOffset");
        private static readonly AccessTools.FieldRef<Minimap, Minimap.PinType> Selected = AccessTools.FieldRefAccess<Minimap, Minimap.PinType>("m_selectedType");
        private static readonly AccessTools.FieldRef<Minimap, bool> SharedOn = AccessTools.FieldRefAccess<Minimap, bool>("m_showSharedMapData");
        private static readonly AccessTools.FieldRef<Minimap, System.Collections.BitArray> Explored = AccessTools.FieldRefAccess<Minimap, System.Collections.BitArray>("m_explored");
        private static readonly AccessTools.FieldRef<Minimap, System.Collections.BitArray> ExploredOthers = AccessTools.FieldRefAccess<Minimap, System.Collections.BitArray>("m_exploredOthers");
        private static readonly System.Reflection.MethodInfo ToPixel = AccessTools.Method(typeof(Minimap), "WorldToPixel");
        private static readonly System.Reflection.MethodInfo ToWorld = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint");
        private static readonly System.Reflection.MethodInfo SelectIcon = AccessTools.Method(typeof(Minimap), "SelectIcon");
        private static readonly System.Reflection.MethodInfo SpriteOf = AccessTools.Method(typeof(Minimap), "GetSprite");

        // window and its parts, in design px (1920 x 1080)
        private const float WX = 40, WY = 30, WW = 1840, WH = 1000, Side = 320, Bar = 44;
        private const float MX = WX + Side, MY = WY, MW = WW - Side, MH = WH - Bar;

        // what the Show chips filter: vanilla types (kind 0) and our pin kinds (PinKinds, worn by vanilla Pins)
        private static readonly List<(string label, Minimap.PinType[] types, int kind)> Kinds = BuildKinds();
        private static List<(string, Minimap.PinType[], int)> BuildKinds()
        {
            var k = new List<(string, Minimap.PinType[], int)>
            {
                ("Fire", new[] { Minimap.PinType.Icon0 }, 0), ("House", new[] { Minimap.PinType.Icon1 }, 0),
                ("Hammer", new[] { Minimap.PinType.Icon2 }, 0), ("Pin", new[] { Minimap.PinType.Icon3 }, 0),
                ("Portal", new[] { Minimap.PinType.Icon4 }, 0),
            };
            for (int i = 0; i < PinKinds.Names.Length; i++) k.Add((PinKinds.Names[i], new[] { Minimap.PinType.Icon3 }, i + 1));
            k.Add(("Boss", new[] { Minimap.PinType.Boss }, 0)); k.Add(("Hildir", new[] { Minimap.PinType.Hildir1, Minimap.PinType.Hildir2, Minimap.PinType.Hildir3 }, 0));
            k.Add(("Death", new[] { Minimap.PinType.Death }, 0)); k.Add(("Bed", new[] { Minimap.PinType.Bed }, 0)); k.Add(("Vehicles", MapMarks.VehicleTypes, 0));
            k.Add(("Bounties", MapMarks.BountyTypes, 0)); k.Add(("Treasure", MapMarks.TreasureTypes, 0));   // EpicLoot's adventure pins (its own toggles sit in the vanilla panel we hide); their types are filled in once its map has them, so without it no pin matches and the chips never show
            k.Add(("Players", new[] { Minimap.PinType.Player }, 0));
            return k;
        }
        // design PLACEABLE: the five vanilla icons, then our kinds
        private static readonly List<(Minimap.PinType type, int kind)> NewTypes = BuildNew();
        private static List<(Minimap.PinType, int)> BuildNew()
        {
            var n = new List<(Minimap.PinType, int)> { (Minimap.PinType.Icon0, 0), (Minimap.PinType.Icon1, 0), (Minimap.PinType.Icon2, 0), (Minimap.PinType.Icon3, 0), (Minimap.PinType.Icon4, 0) };
            for (int i = 0; i < PinKinds.Names.Length; i++) n.Add((Minimap.PinType.Icon3, i + 1));
            return n;
        }
        private static int _newKind;   // the kind the next pin gets (0: plain vanilla type)
        private static Minimap.PinData _kindedPin;
        private const float AddH = 52f + 3 * 36f + 44f;

        private static TextMeshProUGUI _sub, _pinCount, _cursorBiome, _cursorXY, _scaleText, _allBtn;
        private static RectTransform _cursor, _list, _scaleBar, _window, _card;
        private static TMP_InputField _search;
        private static readonly List<(Image bg, Image line, TextMeshProUGUI name, TextMeshProUGUI count, Image icon, int kind)> _chips = new List<(Image, Image, TextMeshProUGUI, TextMeshProUGUI, Image, int)>();
        private static readonly List<(Image bg, Minimap.PinType type, int kind)> _newPin = new List<(Image, Minimap.PinType, int)>();
        private static readonly AccessTools.FieldRef<Minimap, Minimap.PinData> NamePin = AccessTools.FieldRefAccess<Minimap, Minimap.PinData>("m_namePin");
        private static RectTransform _pinsLabel, _pinsView, _addCard, _nameField, _cone, _portalTip;
        private static TextMeshProUGUI _tipName, _tipTo;
        private static RectTransform _groupCard, _groupRows; private static TextMeshProUGUI _groupTitle;
        private static MapClusters.Group _cardGroup; private static float _cardKeep; private static bool _cardPinned;
        private static TextMeshProUGUI _undo;
        private static Minimap.PinData _sel;
        private static string _chipKey;
        // pins you removed by hand, newest last (design "Undo"); ponytail: kept for the session, 20 at most
        internal static readonly List<(Vector3 pos, Minimap.PinType type, string name, bool isChecked, long owner, Splatform.PlatformUserID author, int kind)> Removed = new List<(Vector3, Minimap.PinType, string, bool, long, Splatform.PlatformUserID, int)>();
        private static (Image track, RectTransform knob) _swPublic, _swShared;
        private static string _q = "", _listKey;
        private static float _nextList;

        internal static bool AppliedTo(Minimap m) => _for == m && m != null;

        internal static void Restore() { MapMarks.Clear(); MapClusters.Clear(); _cardGroup = null; _l.Undo(); _for = null; _chips.Clear(); _newPin.Clear(); _listKey = null; _chipKey = null; _sel = null; }

        internal static void Apply(ThemeData t)
        {
            var m = Minimap.instance; if (m == null || m.m_largeRoot == null) return;
            Restore(); _for = m; _t = t;
            var large = (RectTransform)m.m_largeRoot.transform;   // m_largeRoot is the "large" panel itself
            _l.Rect(large, r => { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; });   // the whole screen; our window draws the frame
            _l.Off(large.GetComponent<Graphic>());
            foreach (Transform c in large)
            {   // vanilla's panels: invisible and out of the way, still alive (their toggles keep working)
                var n = c.name; if (n == "map" || n == "OutsideMapClick" || n == "GamepadCrosshair" || n == "NameField" || c == m.m_pinNameRootLarge) continue;
                var g = c.GetComponent<CanvasGroup>() ?? _l.Added(c.gameObject.AddComponent<CanvasGroup>());
                var (a0, b0, i0) = (g.alpha, g.blocksRaycasts, g.interactable);
                _l.Do(() => { g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false; }, () => { if (g != null) { g.alpha = a0; g.blocksRaycasts = b0; g.interactable = i0; } });
            }
            if (Cfg.MapZoomIn.Value > 1) { float z0 = m.m_minZoom; _l.Do(() => m.m_minZoom = z0 / Cfg.MapZoomIn.Value, () => { if (m != null) m.m_minZoom = z0; }); }   // closer than vanilla (ZoomIn)
            var dim = Kit.Img(large, "jatify-dim", null, new Color(0.02f, 0.02f, 0.024f, 0.6f)); _l.Own(dim); dim.rectTransform.Fill(); dim.transform.SetSiblingIndex(1);   // design: the world dimmed behind the window
            var mat = m.m_mapImageLarge.material;   // user: the map stays vanilla, only the fog of war darker
            if (mat != null && mat.HasProperty("_FogLayerTex") && mat.GetTexture("_FogLayerTex") is Texture fog0)
            {
                if (fog0.name != "fog_layer") foreach (var tx in Resources.FindObjectsOfTypeAll<Texture2D>()) if (tx.name == "fog_layer") { fog0 = tx; break; }   // always from vanilla's own, never a copy left by an earlier load   // the fog is drawn from this texture (_FogColor does nothing visible): a darkened copy, its grain kept
                var dark = Darken(fog0, new Color(0.27f, 0.3f, 0.4f));
                _l.Do(() => mat.SetTexture("_FogLayerTex", dark), () => { if (mat != null) mat.SetTexture("_FogLayerTex", fog0); if (dark != null) Object.Destroy(dark); });
            }
            var (win, winFill) = Kit.Box(large, "jatify-window", Theme.C(t.glass), Theme.C(t.glassLine), 14); _l.Own(win);
            _window = win.rectTransform.At(WX, WY, WW, WH); win.transform.SetSiblingIndex(2);   // behind the map, above the dim and the outside-click catcher
            win.raycastTarget = true;   // a click on the sidebar's empty space must not reach vanilla's "outside the map closes it"
            var map = (RectTransform)m.m_mapImageLarge.transform;
            _l.Rect(map, r => r.At(MX, MY, MW, MH));
            _l.Rect(m.m_pinNameRootLarge, r => r.At(MX, MY, MW, MH));
            var sep = Kit.Img(_window, "Sep", null, Theme.C(t.line2)); sep.rectTransform.At(Side, 0, 1, WH);
            var barLine = Kit.Img(_window, "BarLine", null, Theme.C(t.line2)); barLine.rectTransform.At(Side, WH - Bar, WW - Side, 1);
            Sidebar(_window, t);
            Overlay(large, t);
            HintBar(_window, t);
            var corners = Kit.Node(large, "jatify-knotwork"); _l.Own(corners); corners.Fill();   // over the map image, which covers the window's top and right edges
            Knots.Corners(win, winFill, 14, Theme.C(t.accent), corners);
            NameField(large, t);
            Cone(m, t);
        }

        // a copy of a (likely unreadable) texture, its colour multiplied; read back once through the GPU
        private static Texture2D Darken(Texture src, Color k)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var was = RenderTexture.active;
            Graphics.Blit(src, rt); RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = "jatify-fog", wrapMode = src.wrapMode, filterMode = src.filterMode };
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color32((byte)(c.r * k.r), (byte)(c.g * k.g), (byte)(c.b * k.b), c.a); }
            tex.SetPixels32(px); tex.Apply(true, true);   // no CPU copy kept
            return tex;
        }

        // ---- sidebar ------------------------------------------------------------------------------
        private static void Sidebar(RectTransform w, ThemeData t)
        {
            var title = Kit.Text(w, "Title", "World map", Fonts.Norse, 21f, Color.white); title.rectTransform.At(15, 16, 120, 22);
            _sub = Kit.Text(w, "Sub", "", Fonts.Medium, 12f, Theme.C(t.mute)); _sub.rectTransform.At(15 + Kit.W(title) + 8, 18, 160, 20);
            Pages.T = Pages.T ?? t;
            var sb = Kit.Node(w, "jatify-search").At(0, 0, Side, 90);
            _search = Pages.Search(sb, "Search pins", 15, 46, Side - 30, q => { _q = (q ?? "").Trim().ToLowerInvariant(); _listKey = null; });
            var show = Kit.Text(w, "Show", "SHOW", Fonts.Mono, 10f, Theme.C(t.mute)); show.characterSpacing = 8f; show.rectTransform.At(15, 92, 100, 16);
            _allBtn = Kit.Text(w, "All", "NONE", Fonts.Mono, 10f, Theme.C(t.accent)); _allBtn.alignment = TextAlignmentOptions.MidlineRight; _allBtn.rectTransform.At(Side - 115, 90, 100, 20);
            _allBtn.raycastTarget = true; _allBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => { bool any = AnyShown(); for (int i = 0; i < Kinds.Count; i++) SetShown(i, !any); });
            for (int i = 0; i < Kinds.Count; i++)
            {
                int k = i;   // placed by LayoutChips: only the kinds you have pins of (design)
                var (line, fill) = Kit.Box(w, "Chip " + Kinds[i].label, Theme.C(t.raised), Theme.C(t.line2), 7); line.rectTransform.At(15, 112, ChipW, 30); line.raycastTarget = true;
                line.gameObject.AddComponent<Button>().onClick.AddListener(() => SetShown(k, !Shown(k)));
                var ic = Kit.Img(line.rectTransform, "I", Kinds[i].kind > 0 ? PinKinds.Icon(Kinds[i].kind) : Kinds[i].types[0] == MapMarks.Vehicle ? ZNetScene.instance?.GetPrefab("Cart")?.GetComponent<Piece>()?.m_icon : Sprite(Kinds[i].types[0]), Color.white, false); ic.rectTransform.At(10, 7, 16, 16); ic.preserveAspect = true;
                var nm = Kit.Text(line.rectTransform, "N", Kinds[i].label, Fonts.Medium, 12.5f, Color.white); nm.rectTransform.At(32, 0, 70, 30); nm.alignment = TextAlignmentOptions.MidlineLeft;
                var ct = Kit.Text(line.rectTransform, "C", "", Fonts.Mono, 10.5f, Theme.C(t.mute)); ct.rectTransform.At(ChipW - 48, 0, 38, 30); ct.alignment = TextAlignmentOptions.MidlineRight;
                _chips.Add((fill, line, nm, ct, ic, k));
            }
            var pins = Kit.Text(w, "Pins", "PINS", Fonts.Mono, 10f, Theme.C(t.mute)); pins.characterSpacing = 8f; _pinsLabel = pins.rectTransform;
            _pinCount = Kit.Text(pins.rectTransform, "PinCount", "", Fonts.Mono, 10f, Theme.C(t.mute)); _pinCount.alignment = TextAlignmentOptions.MidlineRight; _pinCount.rectTransform.At(Side - 130, -2, 100, 20);
            var view = _pinsView = Kit.Node(w, "PinList");
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            _list = Kit.Node(view, "Content"); _list.anchorMin = new Vector2(0f, 1f); _list.anchorMax = Vector2.one; _list.pivot = new Vector2(0.5f, 1f); _list.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = _list; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            LayoutChips(new bool[Kinds.Count]);
        }

        private const float ChipW = (Side - 36) / 2f;

        // the chips you have pins of in a two-column grid (Players always), then the pin list under them
        private static void LayoutChips(bool[] has)
        {
            int n = 0;
            foreach (var c in _chips)
            {
                bool on = has[c.kind] || Kinds[c.kind].label == "Players";
                if (on && c.icon.sprite == null && Kinds[c.kind].kind == 0) c.icon.sprite = Sprite(Kinds[c.kind].types[0]);   // a mod's pin type known only after the panel was built
                c.line.gameObject.SetActive(on); if (!on) continue;
                c.line.rectTransform.At(15 + (n % 2) * (ChipW + 6), 112 + (n / 2) * 35, ChipW, 30); n++;
            }
            float py = 112 + ((n + 1) / 2) * 35 + 8;
            _pinsLabel.At(15, py, Side - 30, 16);
            _pinsView.At(0, py + 22, Side, WH - py - 30);
        }

        private static bool Shown(int i)
        {
            var (_, types, kind) = Kinds[i];
            if (kind > 0) return !PinKinds.Hidden.Contains(kind);
            var v = Visible(_for); return v != null && (int)types[0] >= 0 && (int)types[0] < v.Length && v[(int)types[0]];   // Vehicle's slot exists once MapMarks has grown the array
        }
        private static bool AnyShown() { for (int i = 0; i < Kinds.Count; i++) if (Shown(i)) return true; return false; }
        private static void SetShown(int i, bool on)
        {
            var (_, types, kind) = Kinds[i];
            if (kind > 0) { if (on) PinKinds.Hidden.Remove(kind); else PinKinds.Hidden.Add(kind); }
            else
            {
                var v = Visible(_for); if (v == null) return;
                foreach (var ty in types)
                {
                    if ((int)ty < 0 || (int)ty >= v.Length || v[(int)ty] == on) continue;
                    if (ty == MapMarks.BountyTypes[0] || ty == MapMarks.TreasureTypes[0]) ToggleFilter.Invoke(_for, new object[] { ty });   // through vanilla's toggle: EpicLoot follows it to keep its own filter state
                    else v[(int)ty] = on;
                }
            }
            PinUpdate(_for) = true; _listKey = null;
        }

        private static readonly System.Reflection.MethodInfo ToggleFilter = AccessTools.Method(typeof(Minimap), "ToggleIconFilter");
        private static Sprite Sprite(Minimap.PinType ty) => (int)ty < 0 ? null : SpriteOf.Invoke(_for, new object[] { ty }) as Sprite;

        // the pins we list: what you placed or found, not live markers (players, pings, events)
        private static bool Listed(Minimap.PinData p) => p.m_type != MapMarks.Vehicle && p.m_type != Minimap.PinType.Player && p.m_type != Minimap.PinType.Ping && p.m_type != Minimap.PinType.Shout && p.m_type != Minimap.PinType.RandomEvent && p.m_type != Minimap.PinType.EventArea && p.m_type != Minimap.PinType.None;

        private static string NameOf(Minimap.PinData p)
        {
            if (!string.IsNullOrEmpty(p.m_name)) return p.m_ownerID != 0L || MapMarks.IsPortal(p) ? Kit.Plain(p.m_name) : Localization.instance.Localize(p.m_name);   // others' text: literal
            return TypeName(p);
        }

        private static string TypeName(Minimap.PinData p)
        {
            int pk = PinKinds.Of(p); if (pk > 0) return PinKinds.Names[pk - 1];
            foreach (var k in Kinds) foreach (var ty in k.types) if (ty == p.m_type) return k.label;
            return p.m_type.ToString();
        }

        private static void TickList(Player me)
        {
            var all = Pins(_for); var vis = Visible(_for);
            var pos = me != null ? me.transform.position : Vector3.zero;
            var key = $"{all.Count}|{_q}|{Mathf.RoundToInt(pos.x / 20f)},{Mathf.RoundToInt(pos.z / 20f)}|{string.Join("", System.Array.ConvertAll(vis, b => b ? "1" : "0"))}";
            foreach (var p in all) if (p.m_checked) key += "c";
            key += "|" + (_sel != null ? _sel.GetHashCode() : 0) + "|" + string.Join(",", PinKinds.Hidden);
            if (key == _listKey) return;
            _listKey = key;
            var rows = new List<(Minimap.PinData p, string name, float d)>();
            int listed = 0;
            foreach (var p in all)
            {
                if (!Listed(p)) continue; listed++;
                int pk = PinKinds.Of(p);
                if ((int)p.m_type >= vis.Length || !vis[(int)p.m_type] || (pk > 0 && PinKinds.Hidden.Contains(pk))) continue;
                var name = NameOf(p);
                if (_q.Length > 0 && !name.Replace("\u200B", "").ToLowerInvariant().Contains(_q)) continue;
                rows.Add((p, name, Utils.DistanceXZ(pos, p.m_pos)));
            }
            rows.Sort((a, b) => a.d.CompareTo(b.d));
            _pinCount.text = listed.ToString();
            if (_search != null && _search.placeholder is TMP_Text ph) ph.text = $"Search {listed} pins";
            foreach (Transform c in _list) Object.Destroy(c.gameObject);
            const int Cap = 300;   // ponytail: plain rows, no recycling; a few hundred pins at most in practice
            float y = 0f;
            for (int i = 0; i < rows.Count && i < Cap; i++)
            {
                var (p, name, d) = rows[i];
                bool picked = p == _sel;
                var row = Kit.Img(_list, "Row", Kit.Rounded(8), Color.white); row.rectTransform.At(10, y, Side - 24, 35); row.raycastTarget = true;
                var b = row.gameObject.AddComponent<Button>(); var cb = ColorBlock.defaultColorBlock; cb.normalColor = picked ? Theme.C(_t.accentSoft) : Color.clear; cb.highlightedColor = new Color(1f, 1f, 1f, 0.06f); cb.pressedColor = new Color(1f, 1f, 1f, 0.1f); cb.selectedColor = Color.clear; b.colors = cb;
                var pin = p; b.onClick.AddListener(() => FlyTo(pin));
                float a = p.m_checked ? 0.45f : 1f;
                var ic = Kit.Img(row.rectTransform, "I", p.m_icon, new Color(1f, 1f, 1f, a), false); ic.rectTransform.At(10, 8, 19, 19); ic.preserveAspect = true;
                bool unnamed = string.IsNullOrEmpty(p.m_name);   // design: no name -> the kind, in italics, quieter
                var nm = Kit.Text(row.rectTransform, "N", name, Fonts.Medium, 13.5f, unnamed ? Theme.C(_t.mute) : new Color(1f, 1f, 1f, p.m_checked ? 0.5f : 1f)); if (unnamed) nm.fontStyle = FontStyles.Italic; nm.alignment = TextAlignmentOptions.MidlineLeft; nm.overflowMode = TextOverflowModes.Ellipsis;
                float nw = Mathf.Min(Kit.W(nm), 170f); nm.rectTransform.At(38, 0, nw + 2, 35);
                if (p.m_ownerID != 0L)
                {   // from a cartography table
                    var (bl, _) = Kit.Box(row.rectTransform, "Shared", new Color(0.36f, 0.55f, 1f, 0.12f), new Color(0.36f, 0.55f, 1f, 0.6f), 3); bl.rectTransform.At(38 + nw + 6, 11, 50, 14);
                    var st = Kit.Text(bl.rectTransform, "T", "SHARED", Fonts.Mono, 8.5f, new Color(0.55f, 0.7f, 1f)); st.alignment = TextAlignmentOptions.Center; st.rectTransform.Fill();
                }
                if (MapMarks.IsPortal(p))
                {   // where it leads, or that it leads nowhere
                    var to = MapMarks.LinkedTo(p); float lx = 38 + nw + (p.m_ownerID != 0L ? 62 : 6);
                    var lt = Kit.Text(row.rectTransform, "Link", to.Count > 0 ? $"\u2192 {Km(Utils.DistanceXZ(p.m_pos, to[0].m_pos))}" : "not linked", Fonts.Medium, 11.5f, to.Count > 0 ? Theme.C(_t.accent) : Theme.C(_t.mute));
                    lt.alignment = TextAlignmentOptions.MidlineLeft; lt.overflowMode = TextOverflowModes.Ellipsis; lt.rectTransform.At(lx, 0, Mathf.Max(20f, Side - 24 - 84 - lx), 35);
                }
                row.gameObject.AddComponent<RowHover>().Pin = p;
                var dt = Kit.Text(row.rectTransform, "D", d >= 10000f ? $"{d / 1000f:0.0} km" : $"{d:0} m", Fonts.Mono, 11f, Theme.C(_t.mute)); dt.alignment = TextAlignmentOptions.MidlineRight; dt.rectTransform.At(Side - 24 - 80, 0, 70, 35);
                y += 35f;
            }
            if (rows.Count > Cap) { var more = Kit.Text(_list, "More", $"+{rows.Count - Cap} more · search to narrow", Fonts.Medium, 12f, Theme.C(_t.mute)); more.rectTransform.At(20, y + 6, 260, 20); y += 32f; }
            if (rows.Count == 0) { var none = Kit.Text(_list, "None", _q.Length > 0 ? $"No pin matches “{_q}”." : "No pins to show.", Fonts.Medium, 12.5f, Theme.C(_t.mute)); none.rectTransform.At(20, 8, 270, 20); y = 40f; }
            _list.sizeDelta = new Vector2(0f, y + 10f);
        }

        // centre the map on a pin (vanilla keeps the map centred on you plus this offset)
        private static void FlyTo(Minimap.PinData p)
        {
            var me = Player.m_localPlayer; if (me == null) return;
            var o = p.m_pos - me.transform.position; o.y = 0f;
            Offset(_for) = o; PinUpdate(_for) = true; _sel = p; _listKey = null;
        }

        // ---- over the map -------------------------------------------------------------------------
        private static void Overlay(RectTransform large, ThemeData t)
        {
            var o = _l.Own(Kit.Node(large, "jatify-overlay")); o.Fill(); o.SetAsLastSibling();
            // crowded pins: the card listing a group (you can move into it)
            var (gc, _) = Kit.Box(o, "GroupCard", Theme.C(t.glass), Theme.C(t.glassLine), 10); _groupCard = gc.rectTransform; gc.raycastTarget = true;
            _groupCard.anchorMin = _groupCard.anchorMax = _groupCard.pivot = new Vector2(0f, 1f);
            _groupTitle = Kit.Text(_groupCard, "T", "", Fonts.Mono, 10f, Theme.C(t.mute)); _groupTitle.characterSpacing = 6f; _groupTitle.rectTransform.At(14, 10, 240, 16);
            _groupRows = Kit.Node(_groupCard, "Rows").At(0, 30, 280, 10);
            _groupCard.gameObject.SetActive(false);
            // a hovered portal: its name and where it leads, at the cursor
            var (pt, _) = Kit.Box(o, "PortalTip", Theme.C(t.glass), Theme.C(t.glassLine), 8); _portalTip = pt.rectTransform; _portalTip.anchorMin = _portalTip.anchorMax = _portalTip.pivot = new Vector2(0f, 1f);
            _tipName = Kit.Text(_portalTip, "N", "", Fonts.Bold, 13f, Color.white); _tipName.rectTransform.At(12, 8, 260, 20); _tipName.alignment = TextAlignmentOptions.MidlineLeft;
            _tipTo = Kit.Text(_portalTip, "T", "", Fonts.Medium, 12f, Theme.C(t.accent)); _tipTo.rectTransform.At(12, 28, 260, 18); _tipTo.alignment = TextAlignmentOptions.MidlineLeft;
            _portalTip.gameObject.SetActive(false);
            // under cursor
            var (cb, _) = Kit.Box(o, "Cursor", Theme.C(t.glass), Theme.C(t.glassLine), 8); _cursor = cb.rectTransform.At(MX + 16, MY + 16, 250, 30);
            var ul = Kit.Text(_cursor, "L", "Under cursor", Fonts.Medium, 11.5f, Theme.C(t.mute)); ul.rectTransform.At(12, 0, 90, 30); ul.alignment = TextAlignmentOptions.MidlineLeft;
            _cursorBiome = Kit.Text(_cursor, "B", "", Fonts.Bold, 12f, Color.white); _cursorBiome.alignment = TextAlignmentOptions.MidlineLeft;
            _cursorXY = Kit.Text(_cursor, "XY", "", Fonts.Mono, 11.5f, Theme.C(t.dim)); _cursorXY.alignment = TextAlignmentOptions.MidlineLeft;
            // zoom
            ZoomButton(o, "+", MY + 18, 0.7f, t); ZoomButton(o, "−", MY + 56, 1.4f, t);
            var (me, _) = Kit.Box(o, "Me", Theme.C(t.glass), Theme.C(t.glassLine), 8); me.rectTransform.At(MX + MW - 48, MY + 94, 32, 32); me.raycastTarget = true;   // design: centre on me
            me.gameObject.AddComponent<Button>().onClick.AddListener(() => { Offset(_for) = Vector3.zero; PinUpdate(_for) = true; _sel = null; _listKey = null; });
            var mi = Kit.Img(me.rectTransform, "I", Sprite(Minimap.PinType.Player), Color.white, false); mi.rectTransform.At(7, 7, 18, 18); mi.preserveAspect = true;
            // scale bar
            var (sbx, _) = Kit.Box(o, "Scale", Theme.C(t.glass), Theme.C(t.glassLine), 8); var sr = sbx.rectTransform.At(MX + 16, MY + MH - 46, 90, 30);
            _scaleBar = Kit.Node(sr, "Bar").At(12, 11, 20, 8);
            var bb = Kit.Img(_scaleBar, "B", null, Color.white); bb.rectTransform.anchorMin = Vector2.zero; bb.rectTransform.anchorMax = new Vector2(1f, 0f); bb.rectTransform.offsetMin = Vector2.zero; bb.rectTransform.offsetMax = new Vector2(0f, 1.5f);
            var bl = Kit.Img(_scaleBar, "L", null, Color.white); bl.rectTransform.anchorMin = Vector2.zero; bl.rectTransform.anchorMax = new Vector2(0f, 1f); bl.rectTransform.offsetMin = Vector2.zero; bl.rectTransform.offsetMax = new Vector2(1.5f, 0f);
            var br = Kit.Img(_scaleBar, "R", null, Color.white); br.rectTransform.anchorMin = new Vector2(1f, 0f); br.rectTransform.anchorMax = Vector2.one; br.rectTransform.offsetMin = new Vector2(-1.5f, 0f); br.rectTransform.offsetMax = Vector2.zero;
            _scaleText = Kit.Text(sr, "T", "", Fonts.Mono, 11.5f, Color.white); _scaleText.alignment = TextAlignmentOptions.MidlineLeft;
            // the card: sharing and sizes (design), each row with its icon
            var (card, _) = Kit.Box(o, "Card", Theme.C(t.glass), Theme.C(t.glassLine), 10); var c = _card = card.rectTransform.At(MX + MW - 306, MY + MH - 156, 290, 140); card.raycastTarget = true;
            var carto = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_cartographytable")?.GetComponent<Piece>()?.m_icon : null;
            _swPublic = Switch(c, Sprite(Minimap.PinType.Player), "Visible to other players", 10, () => { var tg = _for.m_publicPosition; if (tg != null) tg.isOn = !tg.isOn; }, t);   // vanilla's toggle calls OnTogglePublicPosition
            _swShared = Switch(c, carto, "Cartography table", 40, () => _for.OnToggleSharedMapData(), t);
            SizeSlider(c, Sprite(Minimap.PinType.Icon3), "Pins", 74, Cfg.MapPinSize, 40, 150, t);
            SizeSlider(c, null, "Names", 104, Cfg.MapNameSize, 0, 150, t);
        }

        private static void ZoomButton(RectTransform o, string label, float y, float k, ThemeData t)
        {
            var (b, _) = Kit.Box(o, "Zoom", Theme.C(t.glass), Theme.C(t.glassLine), 8); b.rectTransform.At(MX + MW - 48, y, 32, 32); b.raycastTarget = true;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => { _for.LargeZoom = _for.LargeZoom * k; PinUpdate(_for) = true; });
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Bold, 17f, Color.white); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
        }

        private static void RowIcon(RectTransform c, Sprite icon, float y)
        {
            if (icon != null) { var i = Kit.Img(c, "I", icon, Color.white, false); i.rectTransform.At(14, y + 6, 16, 16); i.preserveAspect = true; }
            else { var aa = Kit.Text(c, "I", "Aa", Fonts.Bold, 11f, Theme.C(_t.dim)); aa.rectTransform.At(12, y, 22, 28); aa.alignment = TextAlignmentOptions.Center; }
        }

        private static (Image, RectTransform) Switch(RectTransform c, Sprite icon, string label, float y, System.Action flip, ThemeData t)
        {
            RowIcon(c, icon, y);
            var l = Kit.Text(c, "L", label, Fonts.Medium, 12.5f, Color.white); l.rectTransform.At(38, y, 200, 28); l.alignment = TextAlignmentOptions.MidlineLeft;
            var track = Kit.Panel(c, "Sw", Theme.C(t.raised), 10); track.rectTransform.At(290 - 14 - 38, y + 4, 38, 20); track.raycastTarget = true;
            track.gameObject.AddComponent<Button>().onClick.AddListener(() => flip());
            var knob = Kit.Img(track.rectTransform, "K", Kit.Circle(), Color.white, false);
            return (track, knob.rectTransform);
        }

        private static void SizeSlider(RectTransform c, Sprite icon, string label, float y, BepInEx.Configuration.ConfigEntry<int> e, int min, int max, ThemeData t)
        {
            RowIcon(c, icon, y - 2);
            var l = Kit.Text(c, "L", label, Fonts.Medium, 12.5f, Color.white); l.rectTransform.At(38, y, 70, 24); l.alignment = TextAlignmentOptions.MidlineLeft;
            var track = Kit.Panel(c, "Track", new Color(1f, 1f, 1f, 0.12f), 3); track.rectTransform.At(104, y + 9, 128, 6); track.raycastTarget = true;
            var fill = Kit.Panel(track.rectTransform, "Fill", Theme.C(t.accent), 3); var fr = fill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(Mathf.InverseLerp(min, max, e.Value), 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            var area = Kit.Node(track.rectTransform, "Area").Fill(); var handle = Kit.Img(area, "Handle", Kit.Circle(), Color.white, false); handle.rectTransform.sizeDelta = new Vector2(12f, 12f);
            var val = Kit.Text(c, "V", $"{e.Value}%", Fonts.Mono, 11f, Color.white); val.alignment = TextAlignmentOptions.MidlineRight; val.rectTransform.At(236, y, 40, 24);
            var s = track.gameObject.AddComponent<Slider>(); s.minValue = min; s.maxValue = max; s.wholeNumbers = true; s.fillRect = fr; s.handleRect = handle.rectTransform; s.transition = Selectable.Transition.None; s.value = e.Value;
            s.onValueChanged.AddListener(x => { e.Value = (int)x; val.text = $"{(int)x}%"; });
        }

        private static void HintBar(RectTransform w, ThemeData t)
        {
            var bar = Kit.Node(w, "Hints").At(Side, WH - Bar, WW - Side, Bar);
            float x = 16f;
            var hints = new List<(string, string)> { ("Double-click", "pin"), ("Click", "cross off"), ("Right-click", "remove"), ("Middle", "ping"), ("Drag", "move"), ("Wheel", "zoom") };
#if JATIFY_DEV
            if (Dev.Build) hints.Add(("Alt + right", "teleport"));
#endif
            foreach (var (k, what) in hints)
                x += HoverCard.Row(bar, k, what, x, 11f, t) + 18f;
            var (ub, _) = Kit.Box(bar, "Undo", Theme.C(t.raised), Theme.C(t.line2), 7); ub.rectTransform.At(x + 6, 8, 92, 28); ub.raycastTarget = true;   // design: bring back the last removed pin
            ub.gameObject.AddComponent<Button>().onClick.AddListener(UndoRemove);
            _undo = Kit.Text(ub.rectTransform, "T", "", Fonts.Medium, 12f, Color.white); _undo.alignment = TextAlignmentOptions.Center; _undo.rectTransform.Fill();
            var close = Kit.Node(bar, "Close").At(WW - Side - 90, 0, 90, Bar);
            HoverCard.Row(close, "M", "close", 0f, 11f, t);
        }

        private static void UndoRemove()
        {
            if (Removed.Count == 0 || _for == null) return;
            var r = Removed[Removed.Count - 1]; Removed.RemoveAt(Removed.Count - 1);
            var back = _for.AddPin(r.pos, r.type, r.name, true, r.isChecked, r.owner, r.author); if (r.kind > 0) PinKinds.Set(back, r.kind); _listKey = null;
        }

        // vanilla's pin name field, in our style
        private static void NameField(RectTransform large, ThemeData t)
        {
            var f = _nameField = large.Find("NameField") as RectTransform; if (f == null) return;   // by name: its type lives in an assembly we don't reference
            // a raised box that reads as a field on the card, and a placeholder that says what it's for (#33: dark on the dark card,
            // it went unseen)
            _l.Img(f.GetComponent<Image>(), Kit.Rounded(8), Theme.C(t.raised));
            foreach (var x in f.GetComponentsInChildren<TMP_Text>(true))
            {
                _l.Txt(x, Fonts.Medium, 14f, x.name == "Placeholder" ? Theme.C(t.mute) : Color.white);
                if (x.name == "Placeholder") { var tx = x; var was = tx.text; _l.Do(() => tx.text = "Name (optional)", () => { if (tx != null) tx.text = was; }); }
            }
            _l.Rect(f, r => r.sizeDelta = new Vector2(236f, 34f));
            // design "addpin": a card around vanilla's field with the pin's type and Cancel / Add pin
            var (card, _) = Kit.Box(large, "jatify-addpin", Theme.C(t.glass), Theme.C(t.glassLine), 10); _l.Own(card); card.raycastTarget = true;
            _addCard = card.rectTransform; _addCard.anchorMin = _addCard.anchorMax = f.anchorMin; _addCard.pivot = new Vector2(0.5f, 1f); _addCard.sizeDelta = new Vector2(260f, AddH);
            int fi = f.GetSiblingIndex(); _l.Do(() => f.SetAsLastSibling(), () => { if (f != null) f.SetSiblingIndex(fi); });   // above the map's overlays (the scale bar hid Cancel in a corner)
            _addCard.SetSiblingIndex(f.GetSiblingIndex());   // under the field
            for (int i = 0; i < NewTypes.Count; i++)
            {
                var (ty, kind) = NewTypes[i];
                var bg = Kit.Img(_addCard, "Type " + i, Kit.Rounded(7), Color.clear); bg.rectTransform.At(12 + (i % 5) * 48, 52 + (i / 5) * 36, 44, 32); bg.raycastTarget = true;
                bg.gameObject.AddComponent<Button>().onClick.AddListener(() => SetNewType(ty, kind));
                var ic = Kit.Img(bg.rectTransform, "I", kind > 0 ? PinKinds.Icon(kind) : Sprite(ty), Color.white, false); ic.rectTransform.At(12, 6, 20, 20); ic.preserveAspect = true;
                _newPin.Add((bg, ty, kind));
            }
            CardButton(_addCard, "Cancel", 12, Theme.C(t.raised), Color.white, () => { var p = NamePin(_for); if (p != null) _for.RemovePin(p); _for.OnPinTextEntered(""); });   // empty name: vanilla just closes the field
            CardButton(_addCard, "Add pin", 134, Theme.C(t.accent), new Color(0.03f, 0.1f, 0.06f), () => { var fld = _nameField.GetComponent<TMP_InputField>(); _for.OnPinTextEntered(fld != null ? fld.text : ""); });
            _addCard.gameObject.SetActive(false);
        }

        private static void CardButton(RectTransform c, string label, float x, Color fill, Color text, System.Action click)
        {
            var b = Kit.Panel(c, label, fill, 8); b.rectTransform.At(x, AddH - 42, 114, 30); b.raycastTarget = true;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => click());
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Bold, 12.5f, text); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
        }

        // the new pin's kind: the pin already placed changes too, and it is the default for the next one
        private static void SetNewType(Minimap.PinType ty, int kind)
        {
            SelectIcon.Invoke(_for, new object[] { ty }); _newKind = kind;
            var p = NamePin(_for); if (p == null) return;
            p.m_type = ty; p.m_icon = Sprite(ty); if (p.m_iconElement != null) p.m_iconElement.sprite = p.m_icon;
            PinKinds.Set(p, kind);   // 0 clears it
            PinUpdate(_for) = true;
        }

        // your view cone on the map (design), under vanilla's marker and turning with it
        private static void Cone(Minimap m, ThemeData t)
        {
            var mk = m.m_largeMarker; if (mk == null) return;
            foreach (var parent in new[] { mk.parent, m.m_smallMarker != null ? m.m_smallMarker.parent : null })   // cones a crashed or older load left behind
                if (parent != null) foreach (Transform c in parent) if (c.name == "jatify-cone") Object.Destroy(c.gameObject);
            var img = Kit.Img(mk.parent, "jatify-cone", ConeSprite(), Color.white, false); _l.Own(img); img.raycastTarget = false;
            _cone = img.rectTransform; _cone.anchorMin = _cone.anchorMax = mk.anchorMin; _cone.pivot = new Vector2(0.5f, 0f); _cone.sizeDelta = new Vector2(120f, 90f);
            _cone.SetSiblingIndex(mk.GetSiblingIndex());
            // the same cone on the minimap (user), under its arrow; Tick turns it with the arrow
            var sm = m.m_smallMarker; if (sm == null) return;
            var si = Kit.Img(sm.parent, "jatify-cone", ConeSprite(true), Color.white, false); _l.Own(si); si.raycastTarget = false;
            _coneSmall = si.rectTransform; _coneSmall.anchorMin = _coneSmall.anchorMax = sm.anchorMin; _coneSmall.pivot = new Vector2(0.5f, 0f); _coneSmall.sizeDelta = new Vector2(70f, 52f);
            _coneSmall.anchoredPosition = sm.anchoredPosition; _coneSmall.SetSiblingIndex(sm.GetSiblingIndex());
        }

        private static RectTransform _coneSmall;
        private static Sprite _coneSprite, _coneStrong;
        private static Sprite ConeSprite(bool strong = false)
        {
            if (!strong && _coneSprite != null) return _coneSprite;   // made once each, a few KB, kept for the session
            if (strong && _coneStrong != null) return _coneStrong;
            float peak = strong ? 0.42f : 0.16f;   // the minimap is brighter and smaller
            const int W = 128, H = 96; var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H]; float half = 35f * Mathf.Deg2Rad;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float dx = x - W / 2f + 0.5f, dy = y + 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) / H, ang = Mathf.Abs(Mathf.Atan2(dx, dy));
                float a = r > 1f || ang > half ? 0f : peak * (1f - r) * (1f - r) * Mathf.Clamp01((half - ang) / 0.12f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            var sp = UnityEngine.Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f));
            if (strong) _coneStrong = sp; else _coneSprite = sp;
            return sp;
        }

        // ---- every frame while the big map is open ----------------------------------------------------
        internal static void Tick()
        {
            if (_coneSmall != null && _for != null && _for.m_smallMarker != null) { _coneSmall.localRotation = _for.m_smallMarker.localRotation; _coneSmall.anchoredPosition = _for.m_smallMarker.anchoredPosition; }
            var m = _for; if (m == null || m.m_mode != Minimap.MapMode.Large || _window == null) return;
            var t = _t; var me = Player.m_localPlayer;
            var env = EnvMan.instance;
            if (env != null) { float f = env.GetDayFraction(); _sub.text = $"Day {env.GetDay()} · {(int)(f * 24f):00}:{(int)(f * 1440f) % 60:00}"; }
            // under cursor
            var map = (RectTransform)m.m_mapImageLarge.transform; var ptr = ZInput.pointerPosition;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(map, ptr, null);
            if (_cursor.gameObject.activeSelf != over) _cursor.gameObject.SetActive(over);
            if (over)
            {
                var wp = (Vector3)ToWorld.Invoke(m, new object[] { (Vector3)ptr });
                var biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(wp) : Heightmap.Biome.None;
                _cursorBiome.text = Known(m, wp) ? Localization.instance.Localize("$biome_" + biome.ToString().ToLowerInvariant()) : "Unknown";   // under fog: no peeking
                _cursorXY.text = $"{wp.x:0}, {wp.z:0}";
                float bw = Kit.W(_cursorBiome), xw = Kit.W(_cursorXY);
                _cursorBiome.rectTransform.At(98, 0, bw + 4, 30); _cursorXY.rectTransform.At(98 + bw + 10, 0, xw + 4, 30);
                _cursor.sizeDelta = new Vector2(98 + bw + 10 + xw + 14, 30);
            }
            // scale bar: a round length about 40-110 px long
            float metersAcross = m.m_mapImageLarge.uvRect.width * m.m_textureSize * m.m_pixelSize, pxPerM = map.rect.width / Mathf.Max(1f, metersAcross);
            float len = 10f; foreach (var n in new[] { 10f, 25f, 50f, 100f, 250f, 500f, 1000f, 2500f, 5000f }) { len = n; if (n * pxPerM >= 40f) break; }
            float px = Mathf.Clamp(len * pxPerM, 10f, 140f);
            _scaleBar.sizeDelta = new Vector2(px, 8f); _scaleText.text = len >= 1000f ? $"{len / 1000f:0.#} km" : $"{len:0} m";
            _scaleText.rectTransform.At(12 + px + 8, 0, 70, 30); ((RectTransform)_scaleBar.parent).sizeDelta = new Vector2(12 + px + 8 + Kit.W(_scaleText) + 12, 30);
            // card state
            Paint(_swPublic, m.m_publicPosition != null && m.m_publicPosition.isOn);
            Paint(_swShared, SharedOn(m));
            var sel = Selected(m);
            foreach (var (bg, ty, kind) in _newPin) bg.color = ty == sel && kind == _newKind ? Theme.C(t.accentSoft) : Color.clear;
            // the add-pin card follows vanilla's name field
            bool adding = _nameField != null && _nameField.gameObject.activeSelf;
            if (_addCard.gameObject.activeSelf != adding) _addCard.gameObject.SetActive(adding);
            if (adding && NamePin(m) is Minimap.PinData np && np != _kindedPin)
            {   // a pin just placed by double-click: it gets the kind picked last time
                _kindedPin = np;
                if (_newKind > 0 && np.m_type == Minimap.PinType.Icon3) PinKinds.Set(np, _newKind);
            }
            if (adding)
            {   // vanilla puts the field at the cursor: keep it and its card inside the map, whatever the edge
                var lr = ((RectTransform)m.m_largeRoot.transform).rect; var ctr = new Vector2(lr.width, lr.height) * 0.5f;   // field and card are centre-anchored
                var fp = _nameField.anchoredPosition;
                fp.x = Mathf.Clamp(fp.x, MX + 130f - ctr.x, MX + MW - 130f - ctr.x);
                fp.y = Mathf.Clamp(fp.y, ctr.y - (MY + MH) + AddH - 29f, ctr.y - MY - 29f);
                if (fp != _nameField.anchoredPosition) _nameField.anchoredPosition = fp;
                _addCard.anchoredPosition = fp + new Vector2(0f, 17f + 12f);
            }
            if (_undo != null) { _undo.text = Removed.Count > 0 ? $"\u21a9 Undo ({Removed.Count})" : "\u21a9 Undo"; _undo.color = Removed.Count > 0 ? Color.white : Theme.C(t.mute); }
            if (_cone != null && m.m_largeMarker != null) { _cone.anchoredPosition = m.m_largeMarker.anchoredPosition; _cone.localRotation = m.m_largeMarker.localRotation; _cone.gameObject.SetActive(m.m_largeMarker.gameObject.activeSelf); }
            // sizes (design: pins 40-150 %, names 0-150 %, 0 hides them)
            float ps = 48f * Cfg.MapPinSize.Value / 100f;
            if (Mathf.Abs(m.m_pinSizeLarge - ps) > 0.01f)
            {   // Valheim sizes a pin only when it draws it: the ones already on the map follow the slider here
                m.m_pinSizeLarge = ps;
                foreach (var pin in Pins(m))
                    if (pin.m_uiElement != null && !pin.m_animate && pin.m_uiElement.IsChildOf(m.m_pinRootLarge))
                    { float sz = pin.m_doubleSize ? ps * 2f : ps; pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sz); pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sz); }
            }
            float ns = Cfg.MapNameSize.Value / 100f; bool names = ns > 0.01f;
            if (m.m_pinNameRootLarge.gameObject.activeSelf != names) m.m_pinNameRootLarge.gameObject.SetActive(names);
            foreach (Transform n in m.m_pinNameRootLarge) if (Mathf.Abs(n.localScale.x - ns) > 0.001f) n.localScale = new Vector3(ns, ns, 1f);
            if (_sel != null && _sel.m_uiElement != null) _sel.m_uiElement.localScale = Vector3.one * 1.25f;   // the pin picked in the list
            // where a portal leads: the one under the cursor, the row under the cursor, else the one picked in the list
            var hover = RowHover.Over ?? PortalUnder(m, ptr);
            MapMarks.LinkView.Show(m, hover ?? (MapMarks.IsPortal(_sel) ? _sel : null), t);
            PortalTip(m, MapMarks.IsPortal(hover) ? hover : null, ptr, t);
            GroupCard(m, ptr, t);
            // chips and list, twice a second
            if (Time.unscaledTime < _nextList) return;
            _nextList = Time.unscaledTime + 0.5f;
            var all = Pins(m); var counts = new int[Kinds.Count];
            foreach (var p in all)
            {
                int pk = PinKinds.Of(p);
                for (int i = 0; i < Kinds.Count; i++) if (pk > 0 ? Kinds[i].kind == pk : Kinds[i].kind == 0 && System.Array.IndexOf(Kinds[i].types, p.m_type) >= 0) counts[i]++;
            }
            if (ZNet.instance != null) counts[Kinds.Count - 1] = Mathf.Max(0, ZNet.instance.GetPlayerList().Count - 1);   // the others on the map
            var has = new bool[Kinds.Count]; var ck = ""; for (int i = 0; i < Kinds.Count; i++) { has[i] = counts[i] > 0; ck += has[i] ? "1" : "0"; }
            if (ck != _chipKey) { _chipKey = ck; LayoutChips(has); }
            foreach (var (bg, line, name, count, icon, k) in _chips)
            {
                bool on = Shown(k);
                bg.color = on ? Theme.C(t.raised) : new Color(0f, 0f, 0f, 0f); line.color = on ? Theme.C(t.line2) : new Color(1f, 1f, 1f, 0.06f);
                name.color = on ? Color.white : Theme.C(t.mute); icon.color = new Color(1f, 1f, 1f, on ? 1f : 0.35f);
                count.text = counts[k].ToString();
            }
            _allBtn.text = AnyShown() ? "NONE" : "ALL";
            TickList(me);
        }

        private static Minimap.PinData PortalUnder(Minimap m, Vector2 ptr)
        {
            Minimap.PinData best = null; float bd = 22f * 22f;
            foreach (var p in Pins(m))
            {
                if (!MapMarks.IsPortal(p) || p.m_uiElement == null || !p.m_uiElement.gameObject.activeInHierarchy) continue;
                var sp = RectTransformUtility.WorldToScreenPoint(null, p.m_uiElement.position); float d = (sp - ptr).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        internal static bool KnownAt(Minimap m, Vector3 wp) => Known(m, wp);

        // zoom in on a world point (a group, a pin from a group's card)
        internal static void ZoomTo(Vector3 world, float k)
        {
            var m = _for; var me = Player.m_localPlayer; if (m == null || me == null) return;
            var o = world - me.transform.position; o.y = 0f; Offset(m) = o;
            m.LargeZoom = m.LargeZoom * k; PinUpdate(m) = true;
        }

        // open while the cursor is on the badge or in the card; a moment's grace to travel between them
        private static void GroupCard(Minimap m, Vector2 ptr, ThemeData t)
        {
            var g = MapClusters.Over = MapClusters.Under(ptr); float now = Time.unscaledTime;
            if (g != null && ZInput.GetMouseButtonDown(0)) { PinGroup(g); return; }   // click the badge: the card stays open
            if (_cardGroup != null) _cardGroup = Same(_cardGroup);   // groups are rebuilt with every pin refresh: follow ours by its pins
            if (g != null && _cardGroup != null && g.Pins.Contains(_cardGroup.Pins[0])) g = _cardGroup;
            bool inCard = _cardGroup != null && RectTransformUtility.RectangleContainsScreenPoint(_groupCard, ptr, null);
            if (_cardPinned)
            {   // clicked open: until Esc, a click elsewhere, or its group breaks up
                bool away = ZInput.GetMouseButtonDown(0) && !inCard && g != _cardGroup;
                if (ZInput.GetKeyDown(KeyCode.Escape) || away || _cardGroup == null) CloseGroup();
                return;
            }
            if (g != null && g != _cardGroup && !inCard) OpenGroup(m, g, t);
            if (g != null || inCard) _cardKeep = now + 0.35f;
            if (_cardGroup != null && now > _cardKeep) CloseGroup();
            if (_cardGroup == null && _groupCard.gameObject.activeSelf) CloseGroup();
        }

        private static void OpenGroup(Minimap m, MapClusters.Group g, ThemeData t)
        {
            _cardGroup = g; MapClusters.Highlight = null;
            foreach (Transform c in _groupRows) Object.Destroy(c.gameObject);
            var me = Player.m_localPlayer; var pos = me != null ? me.transform.position : g.World;
            var pins = new List<Minimap.PinData>(g.Pins); pins.Sort((a, b) => Utils.DistanceXZ(pos, a.m_pos).CompareTo(Utils.DistanceXZ(pos, b.m_pos)));
            _groupTitle.text = $"{pins.Count} PINS HERE";
            const int Max = 12; float y = 0f;
            for (int i = 0; i < pins.Count && i < Max; i++)
            {
                var p = pins[i];
                var row = Kit.Img(_groupRows, "Row", Kit.Rounded(7), Color.white); row.rectTransform.At(6, y, 268, 30); row.raycastTarget = true;
                var b = row.gameObject.AddComponent<Button>(); var cb = ColorBlock.defaultColorBlock; cb.normalColor = Color.clear; cb.highlightedColor = Theme.C(t.accentSoft); cb.pressedColor = Theme.C(t.accentSoft); cb.selectedColor = Color.clear; b.colors = cb;
                var pin = p; b.onClick.AddListener(() => { CloseGroup(); FlyTo(pin); ZoomTo(pin.m_pos, 0.35f); });   // picking one flies there
                row.gameObject.AddComponent<GroupRow>().Pin = p;
                var ic = Kit.Img(row.rectTransform, "I", p.m_icon, new Color(1f, 1f, 1f, p.m_checked ? 0.45f : 1f), false); ic.rectTransform.At(8, 6, 18, 18); ic.preserveAspect = true;
                bool unnamed = string.IsNullOrEmpty(p.m_name);
                var nm = Kit.Text(row.rectTransform, "N", NameOf(p), Fonts.Medium, 13f, unnamed ? Theme.C(t.mute) : Color.white); if (unnamed) nm.fontStyle = FontStyles.Italic;
                nm.alignment = TextAlignmentOptions.MidlineLeft; nm.overflowMode = TextOverflowModes.Ellipsis; nm.rectTransform.At(34, 0, 160, 30);
                var dt = Kit.Text(row.rectTransform, "D", Km(Utils.DistanceXZ(pos, p.m_pos)), Fonts.Mono, 10.5f, Theme.C(t.mute)); dt.alignment = TextAlignmentOptions.MidlineRight; dt.rectTransform.At(196, 0, 64, 30);
                y += 30f;
            }
            if (pins.Count > Max) { var more = Kit.Text(_groupRows, "More", $"+{pins.Count - Max} more \u00b7 zoom in to see them", Fonts.Medium, 11.5f, Theme.C(t.mute)); more.rectTransform.At(14, y + 4, 250, 18); y += 26f; }
            _groupCard.sizeDelta = new Vector2(280f, 30f + y + 8f);
            // above the badge, kept on screen
            var root = (RectTransform)_groupCard.parent; var sp = RectTransformUtility.WorldToScreenPoint(null, g.Badge.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var lp);
            var r = root.rect; var size = _groupCard.sizeDelta;
            var at = new Vector2(lp.x - r.xMin - size.x / 2f, lp.y - r.yMax + size.y + 26f);
            if (-at.y < 8f) at.y = lp.y - r.yMax - 26f;   // no room above: under it
            at.x = Mathf.Clamp(at.x, 8f, r.width - size.x - 8f);
            _groupCard.anchoredPosition = at; _groupCard.gameObject.SetActive(true);
        }

        // the current group holding the open card's first pin (null: it broke up)
        private static MapClusters.Group Same(MapClusters.Group old)
        {
            if (MapClusters.Groups.Contains(old)) return old;
            var first = old.Pins[0];
            foreach (var g in MapClusters.Groups) if (g.Pins.Contains(first)) return g;
            return null;
        }

        internal static void PinGroup(MapClusters.Group g) {  if (_cardGroup == null || Same(_cardGroup) != g) OpenGroup(_for, g, _t); _cardPinned = true; }

        private static void CloseGroup() { _cardPinned = false; _cardGroup = null; MapClusters.Highlight = null; if (_groupCard != null) _groupCard.gameObject.SetActive(false); }

        private static Minimap.PinData _tipFor; private static float _tipNext;
        private static void PortalTip(Minimap m, Minimap.PinData p, Vector2 ptr, ThemeData t)
        {
            if (p == null) { if (_portalTip.gameObject.activeSelf) _portalTip.gameObject.SetActive(false); _tipFor = null; return; }
            if (p != _tipFor || Time.unscaledTime >= _tipNext)
            {
                _tipFor = p; _tipNext = Time.unscaledTime + 0.5f;
                var to = MapMarks.LinkedTo(p);
                _tipName.text = NameOf(p);
                _tipTo.text = to.Count > 0 ? $"\u2192 {NameOf(to[0])} \u00b7 {Km(Utils.DistanceXZ(p.m_pos, to[0].m_pos))}" + (to.Count > 1 ? $"  +{to.Count - 1}" : "") : "not linked";
                _tipTo.color = to.Count > 0 ? Theme.C(t.accent) : Theme.C(t.mute);
                _portalTip.sizeDelta = new Vector2(Mathf.Max(Kit.W(_tipName), Kit.W(_tipTo)) + 24f, 52f);
            }
            if (!_portalTip.gameObject.activeSelf) _portalTip.gameObject.SetActive(true);
            // centred above the cursor's tip (our cursor hangs down-right and covered it); near the top, below the whole cursor
            var root = (RectTransform)_portalTip.parent; RectTransformUtility.ScreenPointToLocalPointInRectangle(root, ptr, null, out var lp);
            var r = root.rect; var size = _portalTip.sizeDelta;
            var at = new Vector2(lp.x - r.xMin - size.x / 2f, lp.y - r.yMax + size.y + 14f);   // top-left corner, y down from the top
            if (-at.y < 8f) at.y = lp.y - r.yMax - (Cfg.JatifyCursor.Value ? Cfg.CursorSize.Value : 32f) - 6f;
            at.x = Mathf.Clamp(at.x, 8f, r.width - size.x - 8f);
            _portalTip.anchoredPosition = at;
        }
        private static string Km(float d) => d >= 1000f ? $"{d / 1000f:0.0} km" : $"{d:0} m";

        // explored by you, or by others when cartography table data is shown (as the map's fog)
        private static readonly object[] _px = new object[3];
        private static bool Known(Minimap m, Vector3 wp)
        {
            _px[0] = wp; ToPixel.Invoke(m, _px); int x = (int)_px[1], y = (int)_px[2], n = m.m_textureSize;
            if (x < 0 || y < 0 || x >= n || y >= n) return false;
            int i = y * n + x;
            return Explored(m)[i] || (SharedOn(m) && ExploredOthers(m)[i]);
        }

        // the pointer is on our sidebar or card, not the open map: the wheel scrolls the list, it doesn't zoom
        internal static bool PointerOffMap()
        {
            var m = _for; if (m == null || m.m_mode != Minimap.MapMode.Large || _window == null || !ZInput.IsMouseActive()) return false;   // gamepad: the pointer means nothing
            var p = ZInput.pointerPosition;
            return !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)m.m_mapImageLarge.transform, p, null) || (_card != null && RectTransformUtility.RectangleContainsScreenPoint(_card, p, null))
                || (_groupCard != null && _groupCard.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_groupCard, p, null));
        }

        private static void Paint((Image track, RectTransform knob) sw, bool on)
        {
            if (sw.track == null) return;
            sw.track.color = on ? Theme.C(_t.accent) : new Color(1f, 1f, 1f, 0.14f);
            sw.knob.At(on ? 20 : 2, 2, 16, 16);
        }
    }

    // dev builds: Alt + right click on the big map teleports you there, on the ground (right click alone still removes a pin)
#if JATIFY_DEV
    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    internal static class MapTeleport
    {
        private static readonly System.Func<Minimap, Vector3, Vector3> ToWorld = AccessTools.MethodDelegate<System.Func<Minimap, Vector3, Vector3>>(AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint"));
        private static bool Prefix(Minimap __instance)
        {
            var p = Player.m_localPlayer;
            if (!Dev.Build || p == null || !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) return true;
            var at = ToWorld(__instance, ZInput.pointerPosition);
            p.TeleportTo(DevTest.Ground(at.x, at.z), p.transform.rotation, true);
            __instance.SetMapMode(Minimap.MapMode.Small);
            return false;
        }
    }
#endif

    [HarmonyPatch(typeof(Minimap), "UpdateMap")]
    internal static class MapInputOnMap
    {
        private static void Prefix(ref bool takeInput) { if (takeInput && MapStyle.PointerOffMap()) takeInput = false; }
    }

    // remember pins removed by hand (right click / gamepad), for Undo; vanilla's own clean-ups are not recorded
    [HarmonyPatch(typeof(Minimap))]
    internal static class MapUndo
    {
        private static bool _byHand;
        [HarmonyPrefix, HarmonyPatch(nameof(Minimap.RemovePin), typeof(Vector3), typeof(float))] private static void Begin() => _byHand = true;
        [HarmonyFinalizer, HarmonyPatch(nameof(Minimap.RemovePin), typeof(Vector3), typeof(float))] private static void End() => _byHand = false;
        [HarmonyPrefix, HarmonyPatch(nameof(Minimap.RemovePin), typeof(Minimap.PinData))]
        private static void Record(Minimap.PinData pin)
        {
            if (!_byHand || pin == null || !pin.m_save) return;
            MapStyle.Removed.Add((pin.m_pos, pin.m_type, pin.m_name, pin.m_checked, pin.m_ownerID, pin.m_author, PinKinds.Of(pin)));
            if (MapStyle.Removed.Count > 20) MapStyle.Removed.RemoveAt(0);
        }
    }

    // a pin-list row under the cursor (its portal's link shows on the map)
    internal class RowHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        internal static Minimap.PinData Over;
        internal Minimap.PinData Pin;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => Over = Pin;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) { if (Over == Pin) Over = null; }
        private void OnDisable() { if (Over == Pin) Over = null; }
    }

    // a row of a group's card: the cursor on it brings its pin out of the group
    internal class GroupRow : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        internal Minimap.PinData Pin;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => MapClusters.Highlight = Pin;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) { if (MapClusters.Highlight == Pin) MapClusters.Highlight = null; }
    }
}
