using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The character panel's own pages (design: New Recipes, Index). Picking one of those tabs lays a
    // page over the crafting panel; Crafting takes it away. Built once with the crafting skin (ledger-owned).
    internal static class Pages
    {
        internal static string Open;                 // null = crafting, "recipes", "index" (the Compendium)
        private static RectTransform _root;
        private static UnlocksPage _unlocks;
        private static CompendiumPage _index;
        private static AchievementsPage _ach;
        private static TrophiesPage _trophies;
        private static TextsPage _texts;
        private static AutomationPage _auto;
        internal static ThemeData T;

        internal static void Build(RectTransform crafting, ThemeData t, Ledger l)
        {
            T = t;
            // no fill of its own: the crafting panel's glass shows through (live design), its contents hide while a page is open
            var bg = Kit.Img(crafting, "jatify-page", null, new Color(0, 0, 0, 0)); l.Own(bg);
            bg.rectTransform.Fill(-14f); bg.raycastTarget = true;
            _root = Kit.Node(bg.rectTransform, "Content").Fill(14f);
            _unlocks = new UnlocksPage(_root, t); _index = new CompendiumPage(_root, t); _ach = new AchievementsPage(_root, t); _trophies = new TrophiesPage(_root, t); _texts = new TextsPage(_root, t); _auto = new AutomationPage(_root, t);
            Show(Open);
        }

        private static readonly Dictionary<CanvasGroup, (float, bool, bool)> _hid = new Dictionary<CanvasGroup, (float, bool, bool)>();
        private static readonly List<CanvasGroup> _added = new List<CanvasGroup>();

        // the crafting panel's own contents step aside while a page is open (its glass, "jatify-panel", stays)
        private static void HideCrafting(bool hide)
        {
            var crafting = _root != null ? _root.parent.parent : null; if (crafting == null) return;
            if (hide)
            {
                var g = InventoryGui.instance; var extra = new List<Transform>();   // Craft / Upgrade tabs sit deeper (CraftingStyle moves them)
                if (g != null) { if (g.m_tabCraft) extra.Add(g.m_tabCraft.transform); if (g.m_tabUpgrade) extra.Add(g.m_tabUpgrade.transform); }
                foreach (var lk in crafting.GetComponentsInChildren<Transform>(true)) if (lk.name == "jatify-upgrade-locked") extra.Add(lk);
                foreach (Transform c in crafting.Cast<Transform>().Concat(extra).ToList())
                {
                    if (c.name == "jatify-panel" || c.name == "jatify-page" || c.name == "AzuEPI_CharacterPage") continue;   // AzuEPI keeps its slot cells there (they show in our inventory)
                    var cg = c.GetComponent<CanvasGroup>(); if (cg == null) { cg = c.gameObject.AddComponent<CanvasGroup>(); _added.Add(cg); }
                    if (!_hid.ContainsKey(cg)) _hid[cg] = (cg.alpha, cg.blocksRaycasts, cg.interactable);
                    cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
                }
            }
            else
            {
                foreach (var kv in _hid) if (kv.Key != null) { kv.Key.alpha = kv.Value.Item1; kv.Key.blocksRaycasts = kv.Value.Item2; kv.Key.interactable = kv.Value.Item3; }
                _hid.Clear();
            }
        }

        internal static void CloseEmbedded() => _texts?.Unembed(true);

        internal static void Show(string page)
        {
            if (page != "texts") _texts?.Unembed(true);   // a mod's page laid over the Texts panel goes with it
            Open = page;
            if (_root == null) return;
            HideCrafting(page != null);
            _root.parent.gameObject.SetActive(page != null);
            _root.parent.SetAsLastSibling();
            _unlocks.Root.gameObject.SetActive(page == "recipes");
            _index.Root.gameObject.SetActive(page == "index");
            _ach.Root.gameObject.SetActive(page == "ach");
            _trophies.Root.gameObject.SetActive(page == "trophies");
            _texts.Root.gameObject.SetActive(page == "texts");
            _auto.Root.gameObject.SetActive(page == "auto");
        }

        // Craft / Upgrade tabs: CraftingStyle and vanilla keep showing them, so while a page is open they're held at scale 0
        private static readonly List<Transform> _craftTabs = new List<Transform>();
        private static void CraftTabs(bool hide)
        {
            if (hide && _craftTabs.Count == 0)
            {
                var g = InventoryGui.instance; if (g == null) return;
                if (g.m_tabCraft) _craftTabs.Add(g.m_tabCraft.transform); if (g.m_tabUpgrade) _craftTabs.Add(g.m_tabUpgrade.transform);
                foreach (var lk in g.GetComponentsInChildren<Transform>(true)) if (lk.name == "jatify-upgrade-locked") _craftTabs.Add(lk);
            }
            foreach (var tr in _craftTabs) if (tr != null) tr.localScale = hide ? Vector3.zero : Vector3.one;
            if (!hide) _craftTabs.Clear();
        }

        internal static void Tick()
        {
            Vnei.HideTab();   // VNEI's own tab beside Craft / Upgrade: the Compendium replaces it (user)
            if (_root == null || Open == null) { if (_craftTabs.Count > 0) CraftTabs(false); return; }
            CraftTabs(true);
            var host = _root.parent; if (host.GetSiblingIndex() != host.parent.childCount - 1) host.SetAsLastSibling();   // above widgets added after it
            if (Open == "recipes") _unlocks.Tick(); else if (Open == "ach") _ach.Tick(); else if (Open == "trophies") _trophies.Tick(); else if (Open == "texts") _texts.Tick(); else if (Open == "auto") _auto.Tick(); else _index.Tick();
        }

        internal static void TestAch(string cat) { _ach?.ShowCategory(cat); }
        internal static void TestIndex(string q, string go) { if (_index == null) return; if (go != null) _index.TestGo(go); else _index.Test(q); }

        internal static void Destroy() { CraftTabs(false); HideCrafting(false); foreach (var cg in _added) if (cg != null) UnityEngine.Object.Destroy(cg); _added.Clear(); _root = null; _unlocks = null; _index = null; _ach = null; _trophies = null; _texts = null; _auto = null; }

        // ---- small shared widgets ----
        internal static (Image line, Image fill, TextMeshProUGUI text) Button(Transform parent, string label, float x, float y, float w, float h, Action on, float size = 12.5f)
        {
            var (b, f) = Kit.Box(parent, "Btn", Theme.C(T.raised), Theme.C(T.glassLine), Mathf.Min(14f, h / 2f));
            b.rectTransform.At(x, y, w, h);
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Medium, size, Theme.C(T.dim)); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill(); tx.richText = true;
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<UnityEngine.UI.Button>(); btn.targetGraphic = f;
            var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.35f); btn.colors = cb;
            btn.onClick.AddListener(() => on());
            return (b, f, tx);
        }

        internal static void Paint((Image line, Image fill, TextMeshProUGUI text) b, bool on)
        {
            b.fill.color = on ? Color.white : Theme.C(T.raised);
            b.text.color = on ? new Color(0.07f, 0.07f, 0.08f) : Theme.C(T.dim);
        }

        internal static TMP_InputField Search(Transform parent, string placeholder, float x, float y, float w, Action<string> changed)
        {
            var (box, _) = Kit.Box(parent, "Search", Theme.C(T.raised), Theme.C(T.line2), 8); box.rectTransform.At(x, y, w, 34);
            var mag = Kit.IconImg(box.rectTransform, "search", Theme.C(T.mute), 17f); mag.rectTransform.At(10, 8.5f, 17, 17);
            var area = Kit.Node(box.rectTransform, "Area").Fill(); area.offsetMin = new Vector2(34, 4); area.offsetMax = new Vector2(-12, -4);
            area.gameObject.AddComponent<RectMask2D>();
            var tx = Kit.Text(area, "Text", "", Fonts.Medium, 14f, Color.white); tx.rectTransform.Fill(); tx.alignment = TextAlignmentOptions.MidlineLeft;
            var ph = Kit.Text(area, "Placeholder", placeholder, Fonts.Medium, 14f, Theme.C(T.mute)); ph.rectTransform.Fill(); ph.alignment = TextAlignmentOptions.MidlineLeft;
            box.raycastTarget = true;
            var f = box.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area; f.textComponent = tx; f.placeholder = ph; f.fontAsset = Fonts.Medium; f.pointSize = 14f; Kit.Caret(f);
            f.onValueChanged.AddListener(s => changed(s ?? ""));
            return f;
        }

        // icon + text in a small dark pill (design .ulchip)
        internal static float Pill(Transform parent, Sprite icon, string text, float x, float y, float maxW, Color color, bool dashed = false)
        {
            var tx = Kit.Text(parent, "PillT", text, Fonts.Medium, 12f, color); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.overflowMode = TextOverflowModes.Ellipsis;
            float lead = icon != null ? 26f : 9f;
            float w = Mathf.Min(maxW, Kit.W(tx) + lead + 9f);
            var (b, _) = Kit.Box(parent, "Pill", new Color(0, 0, 0, dashed ? 0f : 0.2f), Theme.C(dashed ? T.line2 : T.glassLine), 7); b.rectTransform.At(x, y, w, 26);
            b.transform.SetSiblingIndex(tx.transform.GetSiblingIndex());
            if (icon != null) { var ic = Kit.Img(b.rectTransform, "I", icon, Color.white, false); ic.rectTransform.At(4, 4, 18, 18); ic.preserveAspect = true; }
            tx.rectTransform.At(x + lead, y, w - lead - 6f, 26);
            return w;
        }

        internal static RectTransform Scroll(Transform parent, float x, float y, float w, float h)
        {
            var view = Kit.Node(parent, "Scroll").At(x, y, w, h);
            var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Kit.Node(view, "Content");
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        internal static string Clock(int minute) => $"{minute / 60:00}:{minute % 60:00}";
    }

    // ---- New Recipes ---------------------------------------------------------------------------
    internal sealed class UnlocksPage
    {
        internal readonly RectTransform Root;
        private readonly ThemeData _t;
        private readonly RectTransform _list;
        private readonly TextMeshProUGUI _count, _foot;
        private readonly List<(Image, Image, TextMeshProUGUI)> _chips = new List<(Image, Image, TextMeshProUGUI)>();
        private readonly (Image line, Image fill, TextMeshProUGUI text) _seen, _clear;
        private string _q = "", _key;
        private int _filter = -1, _open = -1;
        private bool _sure;
        private readonly HashSet<int> _groups = new HashSet<int>();
        private static readonly string[] Filters = { "All", "Recipes", "Buildings", "Materials" };
        private const int MaxRows = 300;   // ponytail: newest 300 shown; paging if anyone keeps more in view

        internal UnlocksPage(RectTransform parent, ThemeData t)
        {
            _t = t;
            Root = Kit.Node(parent, "Unlocks").Fill();
            var title = Kit.Text(Root, "Title", "New Recipes", Fonts.Norse, 24f, Theme.C(t.text)); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 0, 642, 38);
            Kit.Heading(title, -120f, Theme.C(t.accent));   // design .htitle; the lines stop short of the count on the right
            _count = Kit.Text(Root, "Count", "", Fonts.Mono, 12f, Theme.C(t.mute)); _count.alignment = TextAlignmentOptions.MidlineRight; _count.rectTransform.At(440, 0, 196, 38);
            Pages.Search(Root, "Search by name or station", 0, 52, 642, s => { _q = s; _key = null; });
            for (int i = 0; i < Filters.Length; i++)
            {
                int k = i - 1;
                _chips.Add(Pages.Button(Root, Filters[i], 0, 96, 110, 28, () => { _filter = k; _key = null; }));
            }
            float cx = 12;
            foreach (var (h, w) in new[] { ("UNLOCK", 186f), ("PICKED UP", 133f), ("STATION", 146f), ("TIME", 92f) })
            {
                var l = Kit.Text(Root, "Head", h, Fonts.Mono, 10.5f, Theme.C(t.mute)); l.characterSpacing = 8f; l.rectTransform.At(cx, 134, w, 16);
                if (h == "TIME") l.alignment = TextAlignmentOptions.TopRight;
                cx += w + 10f;
            }
            var line = Kit.Img(Root, "Line", null, Theme.C(t.glassLine)); line.rectTransform.At(0, 158, 642, 1);
            Knots.Divider(line, false, Theme.C(t.accent));   // design .kdiv: a small knot in the section line
            _list = Pages.Scroll(Root, 0, 166, 642, 572);
            var fl = Kit.Img(Root, "Line", null, Theme.C(t.glassLine)); fl.rectTransform.At(0, 744, 642, 1);
            _foot = Kit.Text(Root, "Foot", "Kept for the whole character · similar unlocks within 10 minutes are grouped", Fonts.Medium, 12f, Theme.C(t.mute));
            _foot.fontSize = 11f; _foot.alignment = TextAlignmentOptions.MidlineLeft; _foot.overflowMode = TextOverflowModes.Ellipsis; _foot.rectTransform.At(0, 750, 440, 40);
            _seen = Link(Root, () => { Unlocks.MarkSeen(); _key = null; });
            _clear = Link(Root, () => { if (_sure) { Unlocks.Clear(); _sure = false; } else _sure = true; _key = null; });
        }

        internal void Tick()
        {
            Unlocks.Ensure();
            var key = $"{Unlocks.Version}|{Unlocks.Seen}|{_q}|{_filter}|{_open}|{_sure}|{string.Join(",", _groups)}";
            if (key == _key) return;
            _key = key;
            var all = Unlocks.All;
            _count.text = $"{all.Count} entries";
            for (int i = 0; i < _chips.Count; i++)
            {
                int k = i - 1, n = k < 0 ? all.Count : all.Count(e => e.Kind == k);
                Pages.Paint(_chips[i], k == _filter);
                _chips[i].Item3.text = $"{Filters[i]} <color=#{ColorUtility.ToHtmlStringRGB(k == _filter ? new Color(0.3f, 0.3f, 0.32f) : Theme.C(_t.mute))}>{n}</color>";
            }
            float cx = 0f;   // pills as wide as their text (design .chips)
            foreach (var c in _chips) { float w = Kit.W(c.Item3) + 24f; c.Item1.rectTransform.At(cx, 96, w, 28); cx += w + 6f; }
            // design .linkbtn: green mono caps, right-aligned in the footer
            _clear.text.text = _sure ? "CLEAR ALL?" : "CLEAR"; _clear.text.color = Theme.C(_sure ? _t.ember : _t.accent);
            _seen.text.text = $"MARK {Unlocks.Unseen} SEEN";
            float rx = 642f;
            foreach (var (lk, show) in new[] { (_clear, all.Count > 0), (_seen, Unlocks.Unseen > 0) })
            {
                lk.line.gameObject.SetActive(show); if (!show) continue;
                float w = Kit.W(lk.text) + 4f; rx -= w; lk.line.rectTransform.At(rx, 756, w, 28); rx -= 14f;
            }
            Rebuild();
        }

        private bool Hit(Unlocks.Entry e)
        {
            if (_filter >= 0 && e.Kind != _filter) return false;
            if (_q.Length == 0) return true;
            foreach (var k in new[] { e.Key, e.Cause, e.Station })
                if (!string.IsNullOrEmpty(k) && Unlocks.Resolve(k).name.IndexOf(_q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void Rebuild()
        {
            foreach (Transform c in _list) UnityEngine.Object.Destroy(c.gameObject);
            var all = Unlocks.All; var shown = new List<int>();
            for (int i = all.Count - 1; i >= 0 && shown.Count < MaxRows; i--) if (Hit(all[i])) shown.Add(i);
            float y = 0f; int day = -1;
            if (shown.Count == 0)
            {
                var b = Kit.Text(_list, "Empty", all.Count > 0 ? "Nothing found" : "Nothing unlocked yet", Fonts.Bold, 15f, Theme.C(_t.text)); b.alignment = TextAlignmentOptions.Center; b.rectTransform.At(0, 40, 642, 22);
                if (all.Count == 0) { var s = Kit.Text(_list, "EmptySub", "Pick things up: new recipes, buildings and materials appear here with their icon, station and time.", Fonts.Medium, 12.5f, Theme.C(_t.mute)); s.alignment = TextAlignmentOptions.Center; s.textWrappingMode = TextWrappingModes.Normal; s.rectTransform.At(100, 66, 442, 40); }
            }
            for (int n = 0; n < shown.Count;)
            {
                var e = all[shown[n]];
                if (e.Day != day) { day = e.Day; var dl = Kit.Text(_list, "Day", $"DAY {day}", Fonts.Mono, 10.5f, Theme.C(_t.dim)); dl.characterSpacing = 8f; dl.rectTransform.At(0, y + 6, 300, 16); y += 26f; }
                int j = n;   // grouping: same kind, same day, <= 10 real minutes apart, 3+ in a row; off while searching
                if (_q.Length == 0) while (j + 1 < shown.Count && all[shown[j + 1]].Kind == e.Kind && all[shown[j + 1]].Day == e.Day && all[shown[j]].Unix - all[shown[j + 1]].Unix <= 600) j++;
                if (j - n + 1 >= 3)
                {
                    y = Group(shown, n, j, y);
                    if (_groups.Contains(shown[n])) for (int k = n; k <= j; k++) y = Row(shown[k], y, true);
                    n = j + 1;
                }
                else { y = Row(shown[n], y, false); n++; }
            }
            if (shown.Count == MaxRows) { var m = Kit.Text(_list, "More", $"Showing the newest {MaxRows}. Search to find older ones.", Fonts.Medium, 12f, Theme.C(_t.mute)); m.rectTransform.At(12, y + 6, 500, 18); y += 30f; }
            _list.sizeDelta = new Vector2(0f, y + 8f);
        }

        private float Group(List<int> shown, int a, int b, float y)
        {
            var all = Unlocks.All; var first = all[shown[a]]; int id = shown[a]; bool open = _groups.Contains(id);
            var (bg, fill) = Kit.Box(_list, "Group", new Color(1f, 1f, 1f, 0.02f), Theme.C(_t.line2), 10); bg.rectTransform.At(0, y, 642, 46);   // design .ulgroup: near-clear, stronger outline
            var t = Kit.Text(bg.rectTransform, "T", $"{new[] { "Recipes", "Buildings", "Materials" }[first.Kind]} in a row: {b - a + 1}", Fonts.Bold, 13.5f, Theme.C(_t.text)); t.rectTransform.At(12, 0, 190, 46); t.alignment = TextAlignmentOptions.MidlineLeft;
            for (int k = 0; k < Mathf.Min(12, b - a + 1); k++) { var ic = Kit.Img(bg.rectTransform, "I", Unlocks.Resolve(all[shown[a + k]].Key).icon, Color.white, false); ic.rectTransform.At(206 + k * 26, 12, 22, 22); ic.preserveAspect = true; }
            if (b - a + 1 > 12) { var more = Kit.Text(bg.rectTransform, "More", $"+{b - a + 1 - 12}", Fonts.Mono, 11f, Theme.C(_t.mute)); more.rectTransform.At(206 + 12 * 26, 0, 40, 46); more.alignment = TextAlignmentOptions.MidlineLeft; }
            var tm = Kit.Text(bg.rectTransform, "Time", $"{Pages.Clock(all[shown[b]].Minute)}–{Pages.Clock(first.Minute)}", Fonts.Mono, 12f, Theme.C(_t.dim)); tm.alignment = TextAlignmentOptions.MidlineRight; tm.rectTransform.At(480, 0, 130, 46);
            var ch = Kit.Text(bg.rectTransform, "Chev", open ? "▾" : "▸", Fonts.Medium, 14f, Theme.C(_t.dim)); ch.rectTransform.At(616, 0, 20, 46); ch.alignment = TextAlignmentOptions.Center;
            bg.raycastTarget = true; bg.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { if (!_groups.Remove(id)) _groups.Add(id); _key = null; });
            return y + 52f;
        }

        private TextMeshProUGUI _measure;
        private float NameHeight(string name)
        {
            if (_measure == null) { _measure = Kit.Text(Root, "Measure", "", Fonts.Bold, 14f, Color.clear); _measure.textWrappingMode = TextWrappingModes.Normal; _measure.rectTransform.At(0, 0, 136, 10); }
            return _measure.GetPreferredValues(name, 136f, 0f).y;
        }

        private (Image line, Image fill, TextMeshProUGUI text) Link(Transform parent, System.Action on)
        {
            var hit = Kit.Img(parent, "Link", null, new Color(0, 0, 0, 0)); hit.raycastTarget = true;
            var tx = Kit.Text(hit.rectTransform, "T", "", Fonts.Mono, 11f, Theme.C(_t.accent)); tx.characterSpacing = 6f; tx.alignment = TextAlignmentOptions.MidlineRight; tx.rectTransform.Fill();
            hit.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => on());
            return (hit, hit, tx);
        }

        private float Row(int i, float y, bool small)
        {
            var e = Unlocks.All[i]; bool open = _open == i;
            var what = Unlocks.Resolve(e.Key);
            var needs = open ? Needs(e) : null;
            float nameH = Mathf.Min(40f, Mathf.Max(18f, NameHeight(what.name)));   // long names wrap to two lines (design)
            float h = Mathf.Max(54f, 16f + nameH + 20f) + (open ? 44f : 0f);
            float top = h - (open ? 44f : 0f);
            var (bg, fill) = Kit.Box(_list, "Row", open ? Theme.C(_t.accentSoft) : Theme.C(_t.raised), open ? Theme.C(_t.accent) : Theme.C(_t.glassLine), 10);
            bg.rectTransform.At(small ? 16 : 0, y, small ? 626 : 642, h);
            var tile = Kit.Panel(bg.rectTransform, "IcBg", new Color(0, 0, 0, 0.25f), 8); tile.rectTransform.At(10, 8, 38, 38);
            if (what.icon != null) { var ic = Kit.Img(tile.rectTransform, "I", what.icon, Color.white, false); ic.rectTransform.At(3, 3, 32, 32); ic.preserveAspect = true; }
            var nm = Kit.Text(bg.rectTransform, "Name", what.name, Fonts.Bold, 14f, Theme.C(_t.text)); nm.textWrappingMode = TextWrappingModes.Normal; nm.rectTransform.At(56, 8, 136, nameH + 2f); nm.overflowMode = TextOverflowModes.Ellipsis;
            var kind = e.Kind == Unlocks.Material && e.Key.StartsWith("Trophy") ? "Trophy" : new[] { "Recipe", "Building", "Material" }[e.Kind];
            var kt = Kit.Text(bg.rectTransform, "Kind", kind, Fonts.Mono, 11f, Theme.C(_t.mute)); kt.rectTransform.At(56, 10 + nameH, 80, 16);
            if (i >= Unlocks.Seen)
            {
                var (nb, _) = Kit.Box(bg.rectTransform, "New", Theme.C(_t.accent), Theme.C(_t.accent), 5); nb.rectTransform.At(56 + Kit.W(kt) + 6, 10 + nameH, 32, 16);
                var nt = Kit.Text(nb.rectTransform, "T", "New", Fonts.Mono, 10f, new Color(0.02f, 0.08f, 0.05f)); nt.alignment = TextAlignmentOptions.Center; nt.rectTransform.Fill();
            }
            float off = small ? -16f : 0f;
            if (!string.IsNullOrEmpty(e.Cause) && e.Cause != e.Key && Unlocks.StationByName(e.Cause) == null) { var c = Unlocks.Resolve(e.Cause); Pages.Pill(bg.rectTransform, c.icon, c.name, 208 + off, 14, 133, Theme.C(_t.dim)); }
            if (e.Kind != Unlocks.Material)
            {
                var st = Unlocks.StationByName(e.Station ?? (Unlocks.StationByName(e.Cause) != null ? e.Cause : null));
                if (st != null) Pages.Pill(bg.rectTransform, st.m_icon, Localization.instance.Localize(st.m_name) + (e.Level > 1 ? $" · lvl {e.Level}" : ""), 351 + off, 14, 146, Color.white);
                else Pages.Pill(bg.rectTransform, null, "No station", 351 + off, 14, 146, Theme.C(_t.mute), true);
            }
            var tb = Kit.Text(bg.rectTransform, "Clock", Pages.Clock(e.Minute), Fonts.Mono, 13f, Theme.C(_t.text)); tb.alignment = TextAlignmentOptions.TopRight; tb.rectTransform.At(507 + off, 10, 92 + (small ? 14 : 0), 18);
            var real = DateTimeOffset.FromUnixTimeSeconds(e.Unix).ToLocalTime();
            var tr = Kit.Text(bg.rectTransform, "Real", real.ToString("dd.MM · HH:mm"), Fonts.Mono, 10.5f, Theme.C(_t.mute)); tr.alignment = TextAlignmentOptions.TopRight; tr.rectTransform.At(507 + off, 29, 92 + (small ? 14 : 0), 16);
            if (open)
            {
                float x = 12f;
                var nl = Kit.Text(bg.rectTransform, "Needs", needs.Count > 0 ? "NEEDS" : "FOUND IT · NOTHING TO CRAFT", Fonts.Mono, 10.5f, Theme.C(_t.mute)); nl.characterSpacing = 8f; nl.rectTransform.At(x, top + 10f, 240, 16);
                x += needs.Count > 0 ? 64f : 0f;
                float room = (e.Kind == Unlocks.Recipe ? 470f : 630f) - (small ? 16f : 0f);   // the pills stop before the button
                for (int k = 0; k < needs.Count; k++)
                {
                    if (room - x < 80f) { var more = Kit.Text(bg.rectTransform, "More", $"+{needs.Count - k} more", Fonts.Mono, 11f, Theme.C(_t.mute)); more.alignment = TextAlignmentOptions.MidlineLeft; more.rectTransform.At(x, top + 4f, 70, 26); break; }
                    var r = Unlocks.Resolve(needs[k].Item1);
                    x += Pages.Pill(bg.rectTransform, r.icon, $"{r.name} <b>{needs[k].Item2}</b>", x, top + 4f, Mathf.Min(160f, room - x - 60f), Theme.C(_t.dim)) + 6f;
                }
                if (e.Kind == Unlocks.Recipe)
                {   // design .linkbtn: green text, right end of the needs line
                    var lk = Kit.Text(bg.rectTransform, "Show", "SHOW IN CRAFTING →", Fonts.Mono, 11f, Theme.C(_t.accent)); lk.characterSpacing = 4f; lk.alignment = TextAlignmentOptions.MidlineRight;
                    lk.rectTransform.At(room, top + 4f, (small ? 610f : 626f) - room, 26); lk.raycastTarget = true;
                    var nameNow = what.name; lk.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { Pages.Show(null); CraftFilter.SetQuery(nameNow); });
                }
            }
            bg.raycastTarget = true; var btn = bg.gameObject.AddComponent<UnityEngine.UI.Button>(); btn.targetGraphic = fill;
            btn.onClick.AddListener(() => { _open = _open == i ? -1 : i; _key = null; });
            return y + h + 6f;
        }

        private static List<(string, int)> Needs(Unlocks.Entry e)
        {
            var list = new List<(string, int)>();
            Piece.Requirement[] res = null;
            if (e.Kind == Unlocks.Recipe) { var item = ObjectDB.instance.GetItemPrefab(e.Key)?.GetComponent<ItemDrop>(); var r = item != null ? ObjectDB.instance.GetRecipe(item.m_itemData) : null; res = r?.m_resources; }
            else if (e.Kind == Unlocks.Piece) res = ZNetScene.instance.GetPrefab(e.Key)?.GetComponent<Piece>()?.m_resources;
            if (res != null) foreach (var r in res) if (r.m_resItem != null && r.m_amount > 0) list.Add((r.m_resItem.gameObject.name, r.m_amount));
            return list;
        }
    }


    // ---- Achievements (live design achView) ------------------------------------------------------
    // Categories on the left (Summary first), the list on the right: icon, name and description, points,
    // progress or the date earned, track on the HUD. A click opens the criteria. Data: Deeds.Model.
    internal sealed class AchievementsPage
    {
        internal readonly RectTransform Root;
        private readonly ThemeData _t;
        private readonly RectTransform _nav, _list;
        private readonly TextMeshProUGUI _points;
        private string _cat = "Summary", _open, _key;
        private const float ListW = 462f;
        internal void ShowCategory(string cat) { _cat = cat; _open = null; _key = null; }   // test kit: t:ach:<category>

        internal AchievementsPage(RectTransform parent, ThemeData t)
        {
            _t = t;
            Root = Kit.Node(parent, "Achievements").Fill();
            var title = Kit.Text(Root, "Title", "Achievements", Fonts.Norse, 24f, Theme.C(t.text)); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 0, 642, 38);
            Kit.Heading(title, -120f, Theme.C(t.accent));   // design .htitle; the lines stop short of the count on the right
            _points = Kit.Text(Root, "Points", "", Fonts.Mono, 12f, Theme.C(t.mute)); _points.alignment = TextAlignmentOptions.MidlineRight; _points.rectTransform.At(440, 0, 196, 38);
            var hl = Kit.Img(Root, "Line", null, Theme.C(t.glassLine)); hl.rectTransform.At(-14, 44, 670, 1);
            Knots.Divider(hl, false, Theme.C(t.accent));   // design .kdiv: a small knot in the section line
            _nav = Kit.Node(Root, "Nav").At(0, 56, 160, 730);
            var vl = Kit.Img(Root, "VLine", null, Theme.C(t.glassLine)); vl.rectTransform.At(168, 45, 1, 745);
            _list = Pages.Scroll(Root, 180, 56, ListW, 730);
        }

        private static float Pct(Deeds.Deed d)
        {
            if (d.IsEarned) return 1f;
            if (d.Criteria.Count == 1) { var c = d.Criteria[0]; return c.Need > 0f ? Mathf.Clamp01(c.Have / c.Need) : 0f; }
            return d.Criteria.Count == 0 ? 0f : (float)d.Done / d.Criteria.Count;
        }
        private static string Prog(Deeds.Deed d) => d.Criteria.Count == 1 ? $"{Num(Mathf.Min(d.Criteria[0].Have, d.Criteria[0].Need))}/{Num(d.Criteria[0].Need)}" : $"{d.Done}/{d.Criteria.Count}";
        private static string Num(float v) => v >= 10000f ? (v / 1000f).ToString("0.#") + "k" : Mathf.FloorToInt(v).ToString();

        internal void Tick()
        {
            if (!Deeds.Model.Ready) return;
            var all = Deeds.Model.All;
            int earned = all.Count(d => d.IsEarned);
            var key = $"{_cat}|{_open}|{earned}|{all.Sum(d => d.Done)}|{string.Join(",", Deeds.Model.Tracked)}|{(_cat == "Statistics" ? StatsKey() : 0)}";
            if (key == _key) return;
            _key = key;
            _points.text = $"{earned * 10} points";
            Nav(all);
            foreach (Transform c in _list) UnityEngine.Object.Destroy(c.gameObject);
            float y = 0f;
            if (_cat == "Summary") y = Summary(all, earned);
            else if (_cat == "Statistics") y = Stats();
            else foreach (var d in all.Where(x => x.Category == _cat).OrderBy(x => x.IsEarned).ThenByDescending(Pct)) y = Row(d, y);
            _list.sizeDelta = new Vector2(0f, y + 8f);
        }

        private void Nav(List<Deeds.Deed> all)
        {
            foreach (Transform c in _nav) UnityEngine.Object.Destroy(c.gameObject);
            float y = 0f;
            foreach (var cat in new[] { "Summary", "Statistics" }.Concat(Deeds.Model.Categories))
            {
                var c = cat; bool on = _cat == cat;
                var (b, f) = Kit.Box(_nav, "Cat", on ? Theme.C(_t.accentSoft) : new Color(0, 0, 0, 0), on ? Theme.C(_t.accent) : new Color(0, 0, 0, 0), 8); b.rectTransform.At(0, y, 160, 38);
                var tx = Kit.Text(b.rectTransform, "T", cat, Fonts.Medium, 13f, on ? Color.white : Theme.C(_t.dim)); tx.rectTransform.At(10, 0, 110, 38); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.overflowMode = TextOverflowModes.Ellipsis;
                if (cat != "Summary" && cat != "Statistics") { var l = all.Where(d => d.Category == cat).ToList(); var n = Kit.Text(b.rectTransform, "N", $"{l.Count(d => d.IsEarned)}/{l.Count}", Fonts.Mono, 11f, Theme.C(_t.mute)); n.alignment = TextAlignmentOptions.MidlineRight; n.rectTransform.At(100, 0, 52, 38); }
                b.raycastTarget = true; b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { _cat = c; _open = null; _key = null; });
                y += cat == "Statistics" ? 48f : 40f;   // a gap after the two overview entries
            }
        }

        private RectTransform Bar(Transform parent, float x, float y, float w, float h, float v)
        {
            var track = Kit.Panel(parent, "Bar", new Color(1f, 1f, 1f, 0.08f), h / 2f); track.rectTransform.At(x, y, w, h);
            if (v > 0f) { var fill = Kit.Panel(track.rectTransform, "Fill", Theme.C(_t.accent), h / 2f); var fr = fill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(Mathf.Clamp01(v), 1f); fr.offsetMin = fr.offsetMax = Vector2.zero; }
            return track.rectTransform;
        }

        private float Label(string text, string note, float y)
        {
            var l = Kit.Text(_list, "Label", text, Fonts.Mono, 11f, Theme.C(_t.mute)); l.characterSpacing = 8f; l.rectTransform.At(0, y + 10, ListW, 14);
            if (note != null) { var n = Kit.Text(_list, "Note", note, Fonts.Mono, 11f, Theme.C(_t.mute)); n.alignment = TextAlignmentOptions.TopRight; n.rectTransform.At(0, y + 10, ListW, 14); }
            return y + 32f;
        }

        private float Summary(List<Deeds.Deed> all, int earned)
        {
            // points total
            var (tot, _) = Kit.Box(_list, "Total", Theme.C(_t.raised), Theme.C(_t.glassLine), 12); tot.rectTransform.At(0, 0, ListW, 70);
            var tl = Kit.Text(tot.rectTransform, "L", "ACHIEVEMENT POINTS", Fonts.Mono, 11f, Theme.C(_t.mute)); tl.characterSpacing = 8f; tl.rectTransform.At(16, 14, 200, 14);
            var tv = Kit.Text(tot.rectTransform, "V", $"{earned * 10} <size=13><color=#{ColorUtility.ToHtmlStringRGB(Theme.C(_t.mute))}>/ {all.Count * 10}</color></size>", Fonts.Bold, 24f, Color.white); tv.richText = true; tv.rectTransform.At(16, 30, 200, 30);
            Bar(tot.rectTransform, 210, 24, 236, 10, all.Count > 0 ? (float)earned / all.Count : 0f);
            var te = Kit.Text(tot.rectTransform, "E", $"{earned} of {all.Count} earned", Fonts.Mono, 11.5f, Theme.C(_t.dim)); te.alignment = TextAlignmentOptions.TopRight; te.rectTransform.At(210, 40, 236, 16);
            float y = 82f;
            // categories, two per row
            var cats = Deeds.Model.Categories;
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i]; var l = all.Where(d => d.Category == c).ToList(); int e = l.Count(d => d.IsEarned);
                float x = (i % 2) * (ListW / 2f + 3f), cy = y + (i / 2) * 64f, w = ListW / 2f - 3f;
                var (cb, _) = Kit.Box(_list, "Cat", Theme.C(_t.raised), Theme.C(_t.glassLine), 10); cb.rectTransform.At(x, cy, w, 58);
                var cn = Kit.Text(cb.rectTransform, "N", c, Fonts.Medium, 13f, Color.white); cn.rectTransform.At(12, 10, w - 70, 18); cn.overflowMode = TextOverflowModes.Ellipsis;
                var cc = Kit.Text(cb.rectTransform, "C", $"{e} / {l.Count}", Fonts.Mono, 11f, Theme.C(_t.mute)); cc.alignment = TextAlignmentOptions.TopRight; cc.rectTransform.At(w - 70, 12, 58, 16);
                Bar(cb.rectTransform, 12, 38, w - 24, 6, l.Count > 0 ? (float)e / l.Count : 0f);
                cb.raycastTarget = true; cb.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { _cat = c; _open = null; _key = null; });
            }
            y += ((cats.Count + 1) / 2) * 64f + 4f;
            var tracked = all.Where(Deeds.Model.IsTracked).ToList();
            if (tracked.Count > 0) { y = Label("TRACKED ON THE HUD", $"{tracked.Count} / 5", y); foreach (var d in tracked) y = Row(d, y); }
            y = Label("CLOSEST TO DONE", null, y);
            foreach (var d in all.Where(d => !d.IsEarned && !d.A.m_isSecret).OrderByDescending(Pct).Take(4)) y = Row(d, y);
            var recent = all.Where(d => d.IsEarned).OrderByDescending(d => string.IsNullOrEmpty(d.Earned) || d.Earned == "earlier" ? "" : d.Earned).Take(3).ToList();
            if (recent.Count > 0) { y = Label("RECENTLY EARNED", null, y); foreach (var d in recent) y = Row(d, y); }
            return y;
        }

        // ---- Statistics: Valheim's all-time player stats ----------------------
        private static readonly PlayerStatType[] Pinned = { PlayerStatType.Deaths, PlayerStatType.EnemyKills, PlayerStatType.DistanceTraveled, PlayerStatType.Crafts, PlayerStatType.Builds, PlayerStatType.PortalsUsed };
        private static System.Collections.Generic.Dictionary<PlayerStatType, float> AllStats() { var pr = Game.instance != null ? Game.instance.GetPlayerProfile() : null; return pr != null && pr.m_playerStats != null && pr.m_playerStats.Length > 0 && pr.m_playerStats[0] != null ? pr.m_playerStats[0].m_stats : null; }
        private static int StatsKey() { var st = AllStats(); if (st == null) return 0; float sum = 0f; foreach (var v in st.Values) sum += v; return Mathf.RoundToInt(sum); }
        private static string StatName(PlayerStatType k) => System.Text.RegularExpressions.Regex.Replace(k.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
        private static string StatValue(PlayerStatType k, float v)
        {
            var n = k.ToString();
            if (n.StartsWith("Distance")) return v >= 1000f ? $"{v / 1000f:0.0} km" : $"{v:0} m";
            if (n.StartsWith("Time")) return v >= 3600f ? $"{v / 3600f:0.0} h" : $"{v / 60f:0} min";
            return Num(v);
        }

        private float Stats()
        {
            var st = AllStats(); if (st == null) return Label("NO STATISTICS YET", null, 0f);
            float y = 0f, w = (ListW - 12f) / 3f;
            for (int i = 0; i < Pinned.Length; i++)
            {
                var k = Pinned[i]; st.TryGetValue(k, out var v);
                float x = (i % 3) * (w + 6f), cy = (i / 3) * 70f;
                var (cb, _) = Kit.Box(_list, "Card", Theme.C(_t.raised), Theme.C(_t.glassLine), 10); cb.rectTransform.At(x, cy, w, 64);
                var vl = Kit.Text(cb.rectTransform, "V", StatValue(k, v), Fonts.Bold, 20f, Color.white); vl.rectTransform.At(12, 10, w - 24, 26);
                var nl = Kit.Text(cb.rectTransform, "N", StatName(k), Fonts.Medium, 11.5f, Theme.C(_t.mute)); nl.rectTransform.At(12, 38, w - 24, 16); nl.overflowMode = TextOverflowModes.Ellipsis;
            }
            y = ((Pinned.Length + 2) / 3) * 70f + 6f;
            y = Label("ALL STATISTICS", "all worlds", y);
            int col = 0; float half = ListW / 2f - 3f;
            foreach (PlayerStatType k in System.Enum.GetValues(typeof(PlayerStatType)))
            {
                if (!st.TryGetValue(k, out var v) || v <= 0f || System.Array.IndexOf(Pinned, k) >= 0) continue;
                float x = col * (half + 6f);
                var nl = Kit.Text(_list, "N", StatName(k), Fonts.Medium, 12.5f, Theme.C(_t.dim)); nl.rectTransform.At(x, y, half - 90, 22); nl.overflowMode = TextOverflowModes.Ellipsis;
                var vl = Kit.Text(_list, "V", StatValue(k, v), Fonts.Mono, 12f, Color.white); vl.alignment = TextAlignmentOptions.MidlineRight; vl.rectTransform.At(x + half - 90, y, 86, 22);
                col = 1 - col; if (col == 0) y += 24f;
            }
            return y + (col == 1 ? 24f : 0f);
        }

        private float Row(Deeds.Deed d, float y)
        {
            bool ok = d.IsEarned, secret = d.A.m_isSecret && !ok, open = _open == d.A.m_id, on = Deeds.Model.IsTracked(d);
            var crit = open && !secret ? d.Criteria : null;
            int lines = crit != null ? Mathf.Min((crit.Count + 1) / 2, 14) : 0;
            float h = 60f + (lines > 0 ? 12f + lines * 20f : 0f);
            var (bg, fill) = Kit.Box(_list, "Row", Theme.C(_t.raised), Theme.C(open ? _t.line2 : _t.glassLine), 10); bg.rectTransform.At(0, y, ListW, h);
            if (ok) { var glow = Kit.Img(bg.rectTransform, "Glow", SkillBanner.Fade(), new Color(0.13f, 0.84f, 0.49f, 0.08f), false); glow.rectTransform.At(-ListW * 0.5f, 1, ListW * 1.2f, 58); glow.transform.SetSiblingIndex(1); }
            var ic = Kit.Panel(bg.rectTransform, "IcBg", new Color(0, 0, 0, 0.3f), 10); ic.rectTransform.At(10, 8, 44, 44);
            if (d.A.m_icon != null) { var im = Kit.Img(ic.rectTransform, "I", d.A.m_icon, ok ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.8f), false); im.rectTransform.At(2, 2, 40, 40); im.preserveAspect = true; }
            var nm = Kit.Text(bg.rectTransform, "Name", secret ? "Hidden achievement" : d.Name, Fonts.Bold, 14f, Color.white); nm.rectTransform.At(64, 11, 180, 20); nm.overflowMode = TextOverflowModes.Ellipsis;
            var ds = Kit.Text(bg.rectTransform, "Desc", secret ? "Keep playing to find out." : d.Desc, Fonts.Medium, 12f, Theme.C(_t.mute)); ds.rectTransform.At(64, 31, 180, 18); ds.overflowMode = TextOverflowModes.Ellipsis;
            var (pc, _) = Kit.Box(bg.rectTransform, "Pts", new Color(0, 0, 0, 0), Theme.C(ok ? _t.accent : _t.line2), 15); pc.rectTransform.At(254, 15, 30, 30);
            var pt = Kit.Text(pc.rectTransform, "T", d.Points.ToString(), Fonts.Mono, 12f, Theme.C(ok ? _t.accent : _t.mute)); pt.alignment = TextAlignmentOptions.Center; pt.rectTransform.Fill();
            if (ok)
            {
                var when = string.IsNullOrEmpty(d.Earned) || d.Earned == "earlier" ? "Earned" : string.Join(".", d.Earned.Split('-').Reverse());
                var em = Kit.Text(bg.rectTransform, "When", "✓ " + when, Fonts.Mono, 11.5f, Theme.C(_t.accent)); em.alignment = TextAlignmentOptions.MidlineRight; em.rectTransform.At(294, 0, 118, 60);
            }
            else
            {
                Bar(bg.rectTransform, 294, 22, 118, 6, Pct(d));
                var pg = Kit.Text(bg.rectTransform, "Prog", Prog(d), Fonts.Mono, 11.5f, Theme.C(_t.dim)); pg.alignment = TextAlignmentOptions.TopRight; pg.rectTransform.At(294, 32, 118, 16);
                // track on the HUD
                var (tb, tf) = Kit.Box(bg.rectTransform, "Track", on ? Theme.C(_t.accent) : new Color(0, 0, 0, 0), Theme.C(on ? _t.accent : _t.glassLine), 8); tb.rectTransform.At(422, 15, 30, 30);
                var ti = Kit.IconImg(tb.rectTransform, "track", on ? new Color(0.02f, 0.08f, 0.05f) : Theme.C(_t.mute), 18f); ti.rectTransform.At(6, 6, 18, 18);
                tb.raycastTarget = true; var tbtn = tb.gameObject.AddComponent<UnityEngine.UI.Button>(); tbtn.targetGraphic = tf; var dd = d;
                tbtn.onClick.AddListener(() => { Deeds.Model.ToggleTrack(dd); _key = null; });
            }
            if (lines > 0)
            {   // criteria, two columns, under a line
                var ln = Kit.Img(bg.rectTransform, "CritLine", null, Theme.C(_t.glassLine)); ln.rectTransform.At(10, 60, ListW - 20, 1);
                for (int i = 0; i < Mathf.Min(crit.Count, lines * 2); i++)
                {
                    var c = crit[i]; float cx = 64 + (i % 2) * 196, cy = 70 + (i / 2) * 20;
                    var (box, _) = Kit.Box(bg.rectTransform, "Chk", c.Met ? Theme.C(_t.accentSoft) : new Color(0, 0, 0, 0), Theme.C(c.Met ? _t.accent : _t.line2), 4); box.rectTransform.At(cx, cy + 2, 14, 14);
                    if (c.Met) { var ck = Kit.IconImg(box.rectTransform, "check", Theme.C(_t.accent), 10f); ck.rectTransform.At(2, 2, 10, 10); }
                    var cl = Kit.Text(bg.rectTransform, "Crit", c.Label, Fonts.Medium, 12f, c.Met ? Color.white : Theme.C(_t.dim)); cl.rectTransform.At(cx + 22, cy, 120, 18); cl.overflowMode = TextOverflowModes.Ellipsis;
                    if (c.Need > 1f) { var cn = Kit.Text(bg.rectTransform, "N", $"{Num(Mathf.Min(c.Have, c.Need))}/{Num(c.Need)}", Fonts.Mono, 10.5f, Theme.C(_t.mute)); cn.alignment = TextAlignmentOptions.TopRight; cn.rectTransform.At(cx + 120, cy + 2, 66, 16); }
                }
                if (crit.Count > lines * 2) { var more = Kit.Text(bg.rectTransform, "More", $"+{crit.Count - lines * 2} more", Fonts.Mono, 11f, Theme.C(_t.mute)); more.rectTransform.At(64, h - 18, 200, 14); }
            }
            bg.raycastTarget = true; var btn = bg.gameObject.AddComponent<UnityEngine.UI.Button>(); btn.targetGraphic = fill; var id = d.A.m_id;
            var cb2 = btn.colors; cb2.highlightedColor = new Color(1.15f, 1.15f, 1.2f); btn.colors = cb2;
            btn.onClick.AddListener(() => { _open = _open == id ? null : id; _key = null; });
            return y + h + 6f;
        }
    }

    // ---- Trophies (live design) -------------------------------------------------------------------
    // "N of M collected", a 9-wide grid in Valheim's own trophy order (missing ones dark), the picked one below.
    internal sealed class TrophiesPage
    {
        internal readonly RectTransform Root;
        private readonly ThemeData _t;
        private readonly RectTransform _grid, _detail;
        private readonly TextMeshProUGUI _count;
        private string _sel, _key;

        internal TrophiesPage(RectTransform parent, ThemeData t)
        {
            _t = t;
            Root = Kit.Node(parent, "Trophies").Fill();
            var title = Kit.Text(Root, "Title", "Trophies", Fonts.Norse, 24f, Theme.C(t.text)); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 0, 642, 38);
            Kit.Heading(title, 6f, Theme.C(t.accent));   // design .htitle: the knots, lines fading out to the panel's edges
            var hl = Kit.Img(Root, "Line", null, Theme.C(t.glassLine)); hl.rectTransform.At(-14, 44, 670, 1);
            Knots.Divider(hl, false, Theme.C(t.accent));   // design .kdiv: a small knot in the section line
            _count = Kit.Text(Root, "Count", "", Fonts.Mono, 11f, Theme.C(t.mute)); _count.characterSpacing = 8f; _count.richText = true; _count.rectTransform.At(18, 60, 606, 14);
            _grid = Pages.Scroll(Root, 18, 84, 606, 618);
            var bl = Kit.Img(Root, "Line2", null, Theme.C(t.glassLine)); bl.rectTransform.At(-14, 710, 670, 1);
            _detail = Kit.Node(Root, "Detail").At(18, 720, 606, 66);
        }

        private static List<ItemDrop> All()
        {
            var list = new List<ItemDrop>();
            foreach (var go in ObjectDB.instance.m_items) { var d = go ? go.GetComponent<ItemDrop>() : null; if (d != null && d.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && d.m_itemData.m_shared.m_icons.Length > 0) list.Add(d); }
            return list.OrderBy(d => d.m_itemData.m_shared.m_trophyPos.y).ThenBy(d => d.m_itemData.m_shared.m_trophyPos.x).ThenBy(d => d.name).ToList();
        }

        internal void Tick()
        {
            var p = Player.m_localPlayer; if (p == null || ObjectDB.instance == null) return;
            var have = new HashSet<string>(p.GetTrophies());
            var key = $"{have.Count}|{_sel}";
            if (key == _key) return;
            _key = key;
            var all = All();
            if (_sel == null) _sel = all.FirstOrDefault(d => have.Contains(d.name))?.name ?? all.FirstOrDefault()?.name;
            int got = all.Count(d => have.Contains(d.name));
            _count.text = $"{got} OF {all.Count} COLLECTED   <color=#{ColorUtility.ToHtmlStringRGB(Theme.C(_t.mute))}>· MISSING ONES ARE DARK</color>";
            foreach (Transform c in _grid) UnityEngine.Object.Destroy(c.gameObject);
            const float tile = 62f, gap = 6f;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i]; bool ok = have.Contains(d.name), on = d.name == _sel;
                var (b, f) = Kit.Box(_grid, d.name, Theme.C(_t.raised), Theme.C(on ? _t.accent : _t.glassLine), 9); b.rectTransform.At((i % 9) * (tile + gap), (i / 9) * (tile + gap), tile, tile);
                var ic = Kit.Img(b.rectTransform, "I", d.m_itemData.GetIcon(), ok ? Color.white : new Color(0f, 0f, 0f, 0.55f), false); ic.rectTransform.At(6, 6, 50, 50); ic.preserveAspect = true;
                b.raycastTarget = true; var name = d.name; b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { _sel = name; _key = null; });
            }
            _grid.sizeDelta = new Vector2(0f, ((all.Count + 8) / 9) * (tile + gap));
            foreach (Transform c in _detail) UnityEngine.Object.Destroy(c.gameObject);
            var sel = all.FirstOrDefault(d => d.name == _sel); if (sel == null) return;
            bool selOk = have.Contains(sel.name);
            var si = Kit.Img(_detail, "I", sel.m_itemData.GetIcon(), selOk ? Color.white : new Color(0f, 0f, 0f, 0.55f), false); si.rectTransform.At(0, 5, 56, 56); si.preserveAspect = true;
            var nm = Localization.instance.Localize(sel.m_itemData.m_shared.m_name);
            var tn = Kit.Text(_detail, "N", selOk ? nm : "Not found yet", Fonts.Bold, 15f, Color.white); tn.rectTransform.At(68, 12, 520, 22);
            var ts = Kit.Text(_detail, "S", selOk ? "Hang it on an item stand to show it off." : "Kill this creature and carry its trophy to add it.", Fonts.Medium, 12.5f, Theme.C(_t.mute)); ts.rectTransform.At(68, 36, 520, 18);
        }
    }

    // ---- Texts (live design) ----------------------------------------------------------------------
    // The lore you found: list on the left, the picked text on the right.
    internal sealed class TextsPage
    {
        internal readonly RectTransform Root;
        private readonly ThemeData _t;
        private readonly RectTransform _list, _body;
        private string _sel, _key;

        internal TextsPage(RectTransform parent, ThemeData t)
        {
            _t = t;
            Root = Kit.Node(parent, "Texts").Fill();
            var title = Kit.Text(Root, "Title", "Texts", Fonts.Norse, 24f, Theme.C(t.text)); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 0, 642, 38);
            Kit.Heading(title, 6f, Theme.C(t.accent));   // design .htitle: the knots, lines fading out to the panel's edges
            var hl = Kit.Img(Root, "Line", null, Theme.C(t.glassLine)); hl.rectTransform.At(-14, 44, 670, 1);
            Knots.Divider(hl, false, Theme.C(t.accent));   // design .kdiv: a small knot in the section line
            _list = Pages.Scroll(Root, 0, 54, 220, 732);
            var vl = Kit.Img(Root, "VLine", null, Theme.C(t.glassLine)); vl.rectTransform.At(228, 45, 1, 745);
            _body = Pages.Scroll(Root, 246, 62, 392, 724);
            _vline = vl.rectTransform;
            var (bb, _) = Kit.Box(Root, "Back", Theme.C(t.raised), Theme.C(t.line2), 8); bb.rectTransform.At(0, 6, 86, 28); bb.raycastTarget = true;
            var bt = Kit.Text(bb.rectTransform, "T", "‹ Texts", Fonts.Medium, 13f, Theme.C(t.text)); bt.alignment = TextAlignmentOptions.Center; bt.rectTransform.Fill();
            bb.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => Unembed(true));
            _back = bb.rectTransform; Notices.OnTop(_back); _back.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>(); _back.gameObject.SetActive(false);   // over Valheim's dialog (a higher canvas), so it can be clicked
        }

        // ---- a mod's page shown inside this panel: Valheim's texts dialog (which draws it) laid over the panel, scaled to
        // fit, without its own frame, list, title and backdrop; our list hides behind a "‹ Texts" button
        private RectTransform _back, _vline; private TextsDialog _embedIn; private readonly List<GameObject> _embedHidden = new List<GameObject>();
        private static readonly Vector3[] _wc = new Vector3[4];

        private void Embed(TextsDialog d)
        {
            _embedIn = d; _list.gameObject.SetActive(false); _body.gameObject.SetActive(false); _vline.gameObject.SetActive(false); _back.gameObject.SetActive(true);
            _embedHidden.Clear();
            var frame = d.transform.Find("Texts_frame");
            foreach (var n in new[] { "blur", "darken", "Closebutton" }) Hide(d.transform.Find(n));
            if (frame != null) foreach (var n in new[] { "bkg", "topic", "TextList", "Closebutton" }) Hide(frame.Find(n));
        }
        private void Hide(Transform t) { if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); _embedHidden.Add(t.gameObject); } }

        internal void Unembed(bool close)
        {
            var d = _embedIn; _embedIn = null;
            foreach (var go in _embedHidden) if (go != null) go.SetActive(true);
            _embedHidden.Clear();
            var frame = d != null ? d.transform.Find("Texts_frame") as RectTransform : null;
            if (frame != null) { frame.localScale = Vector3.one; frame.anchoredPosition = Vector2.zero; }
            if (close && d != null) d.gameObject.SetActive(false);
            _list.gameObject.SetActive(true); _body.gameObject.SetActive(true); _vline.gameObject.SetActive(true); _back.gameObject.SetActive(false); _key = null;
        }

        // every frame while embedded: keep the page over the panel (Valheim re-lays the dialog when it changes pages)
        private void FollowEmbed()
        {
            var d = _embedIn; if (d == null) return;
            if (!d.gameObject.activeInHierarchy || !Root.gameObject.activeInHierarchy) { Unembed(false); return; }
            var frame = d.transform.Find("Texts_frame") as RectTransform; if (frame == null) return;
            // the part to show, in the frame's own units: EpicLoot's text area and the buttons under it
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            foreach (var n in new[] { "EpicLootMagicTextArea", "TextArea", "collapseAll", "searchField" })
            {
                var r = frame.Find(n) as RectTransform; if (r == null || !r.gameObject.activeSelf) continue;
                r.GetWorldCorners(_wc);
                for (int i = 0; i < 4; i += 2) { var lp = frame.InverseTransformPoint(_wc[i]); xMin = Mathf.Min(xMin, lp.x); xMax = Mathf.Max(xMax, lp.x); yMin = Mathf.Min(yMin, lp.y); yMax = Mathf.Max(yMax, lp.y); }
            }
            if (xMax <= xMin) return;
            // where: the panel under its title line
            Root.GetWorldCorners(_wc); float rs = Root.lossyScale.x;
            float tlx = _wc[1].x, tly = _wc[1].y - 54f * rs, tw = _wc[2].x - _wc[1].x, th = (_wc[1].y - _wc[0].y) - 54f * rs;
            float world = Mathf.Min(tw / (xMax - xMin), th / (yMax - yMin));   // world units per frame unit
            var ps = frame.parent.lossyScale.x; float k = world / ps;
            if (Mathf.Abs(frame.localScale.x - k) > 0.0001f) frame.localScale = new Vector3(k, k, 1f);
            var want = new Vector3(tlx - xMin * world, tly - yMax * world, frame.position.z);
            if ((frame.position - want).sqrMagnitude > 0.25f) frame.position = want;
        }

        // pages other mods add to Valheim's texts dialog (EpicLoot: magic effects, sets, treasure and bounties, shard stones):
        // listed under the mod's name, opened in that dialog, where their own search and sections work
        private static readonly System.Reflection.MethodInfo UpdateList = AccessTools.Method(typeof(TextsDialog), "UpdateTextsList"), ShowText = AccessTools.Method(typeof(TextsDialog), "ShowText", new[] { typeof(TextsDialog.TextInfo) });
        private static readonly AccessTools.FieldRef<TextsDialog, System.Collections.Generic.List<TextsDialog.TextInfo>> Texts = AccessTools.FieldRefAccess<TextsDialog, System.Collections.Generic.List<TextsDialog.TextInfo>>("m_texts");
        private static bool _woke;
        private float ModPages(float y)
        {
            var g = InventoryGui.instance; var d = g != null ? g.m_textsDialog : null; var p = Player.m_localPlayer;
            if (!Epic.On || d == null || p == null) return y;
            System.Func<TextsDialog.TextInfo, bool> mods = i => i != null && i.GetType() != typeof(TextsDialog.TextInfo) && (i.GetType().Namespace ?? "").StartsWith("EpicLoot");
            if (!d.gameObject.activeSelf && !_woke) { _woke = true; d.gameObject.SetActive(true); d.gameObject.SetActive(false); }   // mods hook its Awake (EpicLoot adds its pages there); our Texts page means it never opened: woken once, same frame, unseen
            if (!Texts(d).Any(mods)) try { UpdateList?.Invoke(d, null); } catch (System.Exception e) { Log.Warn("texts: mod pages: " + e.Message); }   // its pages go in when the list is built
            bool head = false;
            foreach (var info in Texts(d))
            {
                if (!mods(info)) continue;
                if (!head) { head = true; var h = Kit.Text(_list, "ModHead", "EPIC LOOT", Fonts.Mono, 10.5f, Theme.C(_t.mute)); h.characterSpacing = 8f; h.rectTransform.At(8, y + 2, 200, 14); y += 22f; }
                var (b, _) = Kit.Box(_list, "ModText", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 8); b.rectTransform.At(0, y, 212, 38);
                var ic = Kit.Img(b.rectTransform, "I", Kit.Icon("texts_icon"), Theme.C(_t.accent), false); ic.rectTransform.At(8, 9, 20, 20); ic.preserveAspect = true;
                var tx = Kit.Text(b.rectTransform, "T", Localization.instance.Localize(info.m_topic), Fonts.Medium, 13f, Theme.C(_t.dim)); tx.rectTransform.At(36, 0, 170, 38); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.overflowMode = TextOverflowModes.Ellipsis;
                var page = info; b.raycastTarget = true;
                b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { d.Setup(p); ShowText?.Invoke(d, new object[] { page }); Embed(d); });
                y += 40f;
            }
            return y;
        }

        internal void Tick()
        {
            var p = Player.m_localPlayer; if (p == null) return;
            FollowEmbed(); if (_embedIn != null) return;
            var texts = p.GetKnownTexts();
            var key = $"{texts.Count}|{_sel}";
            if (key == _key) return;
            _key = key;
            if (_sel == null && texts.Count > 0) _sel = texts[0].Key;
            foreach (Transform c in _list) UnityEngine.Object.Destroy(c.gameObject);
            float y = ModPages(0f);   // a mod's pages first (the runestone texts can run long)
            if (y > 0f) { var rh = Kit.Text(_list, "TextsHead", "RUNESTONES", Fonts.Mono, 10.5f, Theme.C(_t.mute)); rh.characterSpacing = 8f; rh.rectTransform.At(8, y + 10, 200, 14); y += 30f; }
            foreach (var kv in texts)
            {
                var label = kv.Key; bool on = label == _sel;
                var (b, f) = Kit.Box(_list, "Text", on ? Theme.C(_t.accentSoft) : new Color(0, 0, 0, 0), on ? Theme.C(_t.accent) : new Color(0, 0, 0, 0), 8); b.rectTransform.At(0, y, 212, 38);
                var ic = Kit.Img(b.rectTransform, "I", Kit.Icon("texts_icon"), Color.white, false); ic.rectTransform.At(8, 9, 20, 20); ic.preserveAspect = true;
                var tx = Kit.Text(b.rectTransform, "T", Localization.instance.Localize(label), Fonts.Medium, 13f, on ? Color.white : Theme.C(_t.dim)); tx.rectTransform.At(36, 0, 170, 38); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.overflowMode = TextOverflowModes.Ellipsis;
                b.raycastTarget = true; b.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { _sel = label; _key = null; });
                y += 40f;
            }
            if (texts.Count == 0) { var e = Kit.Text(_list, "Empty", "No texts found yet. Read runestones to collect them.", Fonts.Medium, 12.5f, Theme.C(_t.mute)); e.textWrappingMode = TextWrappingModes.Normal; e.rectTransform.At(8, y + 8, 200, 60); y += 70f; }
            _list.sizeDelta = new Vector2(0f, y);
            foreach (Transform c in _body) UnityEngine.Object.Destroy(c.gameObject);
            var sel = texts.FirstOrDefault(kv => kv.Key == _sel);
            if (sel.Key == null) return;
            var h = Kit.Text(_body, "Title", Localization.instance.Localize(sel.Key), Fonts.Norse, 24f, Color.white); h.textWrappingMode = TextWrappingModes.Normal; h.rectTransform.At(0, 0, 392, 26);
            var body = Localization.instance.Localize(sel.Value).Replace("\\n", "\n");
            var bt = Kit.Text(_body, "Body", body, Fonts.Lore, 15f, Theme.C(_t.dim)); bt.fontStyle = FontStyles.Italic; bt.textWrappingMode = TextWrappingModes.Normal; bt.lineSpacing = 12f; bt.paragraphSpacing = 12f;
            float bh = Kit.W(bt) > 0f ? bt.GetPreferredValues(body, 392f, 0f).y : 400f;
            bt.rectTransform.At(0, 38, 392, bh + 10f);
            _body.sizeDelta = new Vector2(0f, 38f + bh + 20f);
        }
    }
}

namespace Jatify.UI
{
    // closing the inventory: a mod's page laid over the Texts panel closes too
    [HarmonyLib.HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class TextsEmbedClose
    {
        private static void Postfix() => Pages.CloseEmbedded();
    }

    // opening the inventory (or a station) starts on Crafting, not on the page you had open last
    [HarmonyLib.HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class StationOpensCrafting
    {
        private static void Postfix() { if (Pages.Open != null) Pages.Show(null); }   // the inventory always opens on Crafting (user), not the last page
    }
}
