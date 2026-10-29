using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Valheim's Settings in the live design's style, cascading right of the pause menu like our Mods page
    // (user): the tabs become a text nav beside the menu, each tab's controls become rows (label left,
    // control right) in a scroll column. Vanilla's own controls are moved, not copied, so its apply / save /
    // key binding logic is untouched; Save is vanilla OK, Back and Esc are vanilla Back (discard).
    // Settings is created on open and destroyed on close, so nothing here needs undoing.
    internal static class SettingsStyle
    {
        private const float RowH = 48f, DescRowH = 64f;
        private static Settings _for;
        private static string _q = "";

        // one setting: vanilla's control in our row, its section and line of help, and its value when Settings opened
        private sealed class R
        {
            internal GameObject Ctl; internal RectTransform Row; internal TMP_Text Label; internal Image Dot;
            internal string Section, Desc, Start; internal int Order; internal System.Func<string> Value;
            internal bool Shown(string q) => Ctl != null && Ctl.activeSelf && (q.Length == 0 || ((Label != null ? Label.text : "") + " " + Desc + " " + Section).ToLowerInvariant().Contains(q));
        }
        private sealed class P { internal RectTransform Col; internal readonly List<R> Rows = new List<R>(); internal readonly Dictionary<string, (TMP_Text h, Image line)> Heads = new Dictionary<string, (TMP_Text, Image)>(); internal readonly List<string> Sections = new List<string>(); internal string Shown; }
        private static readonly List<R> _rows = new List<R>();
        private static readonly List<P> _pages = new List<P>();
        private static readonly List<(GameObject sel, TMP_Text label, Image mark, string name, Transform page)> _tabs = new List<(GameObject, TMP_Text, Image, string, Transform)>();
        private static readonly HashSet<Transform> _used = new HashSet<Transform>();
        private static readonly HashSet<RectTransform> _nudged = new HashSet<RectTransform>();
        private static readonly List<(RectTransform bg, TMP_Text topic, TMP_Text text)> _tips = new List<(RectTransform, TMP_Text, TMP_Text)>();

        internal static void Tick()
        {
            var s = Settings.instance;
            if (s != _for) { _for = s; _q = ""; _pages.Clear(); _tabs.Clear(); _tips.Clear(); _nudged.Clear(); if (s != null) Build(s, Plugin.Instance.ThemeData); }
            if (s == null) return;
            var m = Menu.instance;   // the menu beside us closed or moved on: so do we (vanilla Back: unsaved changes dropped)
            if (m != null && s.transform.IsChildOf(m.transform) && (!m.m_root.gameObject.activeSelf || m.m_quitDialog.gameObject.activeSelf || m.m_logoutDialog.gameObject.activeSelf)) { s.OnBack(); return; }
            var t = Plugin.Instance.ThemeData;
            foreach (var (sel, label, mark, name, page) in _tabs)
            {
                bool on = sel != null && sel.activeSelf;
                label.color = on ? Theme.C(t.accent) : new Color(1, 1, 1, 0.78f); mark.enabled = on;
                // while searching: how many matches each tab holds (search works on the open tab)
                int n = 0; if (_q.Length > 0) foreach (var pg in _pages) if (page != null && pg.Col.IsChildOf(page)) foreach (var r in pg.Rows) if (r.Shown(_q)) n++;
                var want = _q.Length > 0 ? $"{name} <size=55%><color=#{ColorUtility.ToHtmlStringRGB(Theme.C(n > 0 ? t.accent : t.mute))}>{n}</color></size>" : name;
                if (label.text != want) label.text = want;
            }
            foreach (var (bg, topic, text) in _tips)
            {   // vanilla sizes its tooltip box before the text has wrapped: long ones (Draw distance) spilled out
                if (bg == null) continue;
                if (!bg.gameObject.activeInHierarchy) { _nudged.Remove(bg); continue; }
                if (_nudged.Add(bg)) { var tip = (RectTransform)bg.parent; tip.anchoredPosition += new Vector2(30f, 0f); }   // vanilla places it on the warning icon: step right of it
                float w = 300f, th = text.GetPreferredValues(text.text, w - 20f, 0f).y, h = 12f + 18f + 6f + th + 12f;
                if (Mathf.Abs(bg.sizeDelta.y - h) < 0.5f && Mathf.Abs(bg.sizeDelta.x - w) < 0.5f) continue;
                bg.sizeDelta = new Vector2(w, h);
                topic.rectTransform.anchorMin = new Vector2(0f, 1f); topic.rectTransform.anchorMax = Vector2.one; topic.rectTransform.pivot = new Vector2(0.5f, 1f); topic.rectTransform.offsetMin = new Vector2(10f, -30f); topic.rectTransform.offsetMax = new Vector2(-10f, -12f);
                text.rectTransform.anchorMin = new Vector2(0f, 1f); text.rectTransform.anchorMax = Vector2.one; text.rectTransform.pivot = new Vector2(0.5f, 1f); text.rectTransform.offsetMin = new Vector2(10f, -36f - th); text.rectTransform.offsetMax = new Vector2(-10f, -36f);
            }
            foreach (var pg in _pages)
            {
                if (!pg.Col.gameObject.activeInHierarchy) continue;
                // rows follow their control's visibility (vanilla shows / hides some, e.g. the resolution Test button) and the search
                var key = _q; foreach (var r in pg.Rows) key += r.Shown(_q) ? "1" : "0";
                if (key != pg.Shown) { pg.Shown = key; Stack(pg); }
                foreach (var r in pg.Rows) if (r.Dot != null && r.Value != null) r.Dot.enabled = r.Value() != r.Start;   // changed, not saved yet
            }
        }

        // unload while Settings is open: close it the vanilla way (its objects were moved, not ledgered)
        internal static void Restore() { if (Settings.instance != null) Settings.instance.OnBack(); _for = null; _pages.Clear(); _tabs.Clear(); }

        private static void Build(Settings s, ThemeData t)
        {
            ConfigPage.Open?.Close(); PlayersPage.Close();
            var root = (RectTransform)s.transform;
            Off(root.GetComponent<Image>());   // the pause menu's dim is enough; the menu stays clickable
            root.localScale = Vector3.one;   // vanilla draws Settings 1.3x; ours lines up with the pause menu
            var panel = root.Find("Panel") as RectTransform; if (panel == null) return;
            Off(panel.GetComponent<Image>()); panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.Find("Title")?.gameObject.SetActive(false);
            Off(panel.Find("HeaderLine")?.GetComponent<Image>());
            var head = Kit.Text(panel, "jatify-head", "SETTINGS", Fonts.Mono, 12f, Theme.C(t.mute)); head.characterSpacing = 8f; head.rectTransform.At(420, 300, 260, 18);

            // tabs -> nav
            var tabs = panel.Find("TabButtons") as RectTransform; float y = 0f;
            if (tabs != null)
            {
                var hl = tabs.GetComponent<HorizontalLayoutGroup>(); if (hl != null) hl.enabled = false;
                tabs.At(420, 330, 260, 400);
                foreach (Transform b in tabs)
                {
                    if (!b.gameObject.activeSelf) continue;
                    Off(b.GetComponent<Image>()); var sel = b.Find("Selected"); Off(sel?.GetComponent<Image>());
                    var tx = b.GetComponentInChildren<TMP_Text>(); if (tx == null) continue;
                    foreach (var other in b.GetComponentsInChildren<TMP_Text>(true)) if (other != tx) Off(other);   // the selected state's own copy of the name
                    ((RectTransform)b).At(0, y, 260, 44);
                    Style(tx, Fonts.Bold, 26f, Color.white, TextAlignmentOptions.MidlineLeft); tx.rectTransform.Fill(); tx.textWrappingMode = TextWrappingModes.NoWrap;
                    var mark = Kit.Img(b, "jatify-mark", null, Theme.C(t.accent)); mark.rectTransform.At(-16, 19, 7, 7); mark.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
                    Transform pageOf = null; var th = tabs.GetComponent<TabHandler>();
                    if (th != null) foreach (var tab in th.m_tabs) if (tab.m_button != null && tab.m_button.transform == b) pageOf = tab.m_page;
                    var lc = tx.GetComponent<Localize>(); if (lc != null) lc.enabled = false;   // we write the name (plus a match count) from now on
                    _tabs.Add((sel != null ? sel.gameObject : null, tx, mark, Localization.instance.Localize(tx.text), pageOf));   // read before Valheim localized it: "$settings_gameplay"
                    y += 44f;
                }
            }
            // Save (vanilla OK) and Back under the tabs
            y += 330f + 18f;
            NavButton(panel.Find("Ok"), "Save", Theme.C(t.accent), y, t); NavButton(panel.Find("Back"), "Back", Theme.C(t.dim), y + 34f, t);
            var esc = Kit.Text(panel, "jatify-esc", "Esc", Fonts.Mono, 12f, Theme.C(t.mute)); esc.rectTransform.At(420 + 62, y + 34f, 60, 30); esc.alignment = TextAlignmentOptions.MidlineLeft;

            // search over every tab's rows (design: "Search settings")
            var sb = Kit.Node(panel, "jatify-search").At(0, 0, 1920, 1080); sb.SetAsLastSibling();
            Pages.T = Pages.T ?? t; Pages.Search(sb, "Search settings", 720, 262, 360, q => _q = (q ?? "").Trim().ToLowerInvariant());   // T is set once the inventory has opened

            // each tab page: its controls as rows in our column
            var content = panel.Find("TabContent") as RectTransform; if (content == null) return;
            Off(content.GetComponent<Image>()); content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one; content.offsetMin = content.offsetMax = Vector2.zero;
            foreach (Transform page in content)
            {
                var pr = (RectTransform)page; pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.offsetMin = pr.offsetMax = Vector2.zero;
                Off(page.GetComponent<Image>());
                Page(pr, t);
            }
        }

        private static void Page(RectTransform page, ThemeData t)
        {
            if (page.GetComponent<Valheim.SettingsGui.GamepadSettings>() != null)
            {   // the controller map is a picture, not rows: just moved into our column
                foreach (Transform c in page) if (!Skip(c)) ((RectTransform)c).At(720, 262, 800, 640);
                return;
            }
            foreach (Transform c in page)
                if (c.name == "SettingsTooltip" && c.Find("Background") is RectTransform bg && bg.Find("Topic")?.GetComponent<TMP_Text>() is TMP_Text tp && bg.Find("Text")?.GetComponent<TMP_Text>() is TMP_Text tt)
                {
                    Style(tp, Fonts.Bold, 14f, Color.white, TextAlignmentOptions.TopLeft); Style(tt, Fonts.Medium, 12.5f, Theme.C(t.dim), TextAlignmentOptions.TopLeft); tt.textWrappingMode = TextWrappingModes.Normal;
                    _tips.Add((bg, tp, tt));
                    c.SetAsLastSibling();   // above our rows (the warning icon showed through it)
                }
            var old = new List<Transform>(); foreach (Transform c in page) old.Add(c);
            var view = Kit.Node(page, "jatify-view").At(700, 312, 820, 710);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var col = Kit.Node(view, "Content"); col.anchorMin = new Vector2(0f, 1f); col.anchorMax = Vector2.one; col.pivot = new Vector2(0.5f, 1f); col.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = col; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            _rows.Clear(); _used.Clear();
            foreach (var c in old) if (!Skip(c)) Walk(c, col, t);
            if (_rows.Count == 0) { Object.Destroy(view.gameObject); return; }   // nothing we know: leave the page as it is
            foreach (var c in old) if (!Skip(c)) c.gameObject.SetActive(false);   // emptied: their controls live in our rows now
            var pg = new P { Col = col };
            foreach (var r in _rows) if (!pg.Sections.Contains(r.Section)) pg.Sections.Add(r.Section);
            pg.Sections.Sort((a, b) => Book.Order(a).CompareTo(Book.Order(b)));
            for (int i = 0; i < pg.Sections.Count; i++)
            {   // design: the first heading big and white, the next ones quieter
                var h = Kit.Text(col, "Section", pg.Sections[i], Fonts.Bold, i == 0 ? 22f : 17f, i == 0 ? Color.white : Theme.C(t.dim)); h.alignment = TextAlignmentOptions.BottomLeft;
                var line = Kit.Img(col, "Line", null, new Color(0.13f, 0.84f, 0.49f, i == 0 ? 0.45f : 0.22f));
                pg.Heads[pg.Sections[i]] = (h, line);
            }
            pg.Rows.AddRange(_rows); pg.Rows.Sort((a, b) => a.Order.CompareTo(b.Order));
            _pages.Add(pg);
        }

        // dialogs, tooltips and dev helpers keep their place (they cover the page when shown)
        private static bool Skip(Transform c) { var n = c.name; return n.Contains("Dialog") || n.Contains("Tooltip") || n.StartsWith("Dev") || n == "KeyHint" || n.StartsWith("jatify-"); }

        private static void Walk(Transform t, RectTransform col, ThemeData th)
        {
            if (Skip(t) || _used.Contains(t)) return;
            if (t.GetComponent<Slider>() is Slider sl) { Row(t, Label(t), col, th, Kind.Slider); return; }
            if (t.GetComponent<Toggle>() != null) { Row(t, Label(t), col, th, Kind.Toggle); return; }
            if (t.GetComponent("GuiStepper") != null) { Row(t, Label(t), col, th, Kind.Stepper); return; }
            if (t.GetComponent<TMP_Dropdown>() != null) { Row(t, Label(t), col, th, Kind.Dropdown); return; }
            if (t.GetComponent<Button>() != null) { var l = Label(t); Row(t, l, col, th, l != null ? Kind.Key : Kind.Action); return; }
            var kids = new List<Transform>(); foreach (Transform c in t) kids.Add(c);
            foreach (var c in kids) Walk(c, col, th);
        }

        // a control's label: its own "Label" child (toggles, sliders), else a "Label" beside it (steppers, key binds)
        private static TMP_Text Label(Transform ctl)
        {
            bool caption = ctl.GetComponent<Button>() != null || ctl.GetComponent<TMP_Dropdown>() != null;   // their "Label" is the value shown inside
            var own = ctl.Find("Label"); if (own != null && !caption) return own.GetComponent<TMP_Text>();
            var sib = ctl.parent != null ? ctl.parent.Find("Label") : null;
            return sib != null && sib != ctl && !_used.Contains(sib) ? sib.GetComponent<TMP_Text>() : null;
        }

        private enum Kind { Toggle, Slider, Stepper, Dropdown, Key, Action }

        private static void Row(Transform ctl, TMP_Text label, RectTransform col, ThemeData t, Kind kind)
        {
            var row = Kit.Node(col, "Row " + ctl.name);
            row.gameObject.SetActive(ctl.gameObject.activeSelf);
            if (label != null) { _used.Add(label.transform); label.transform.SetParent(row, false); Style(label, Fonts.Medium, 16f, Color.white, TextAlignmentOptions.MidlineLeft); label.rectTransform.At(0, 0, kind == Kind.Slider ? 380 : 520, RowH); label.textWrappingMode = TextWrappingModes.Normal; label.enabled = true; }
            if (label == null && kind == Kind.Dropdown)
            {   // vanilla names the resolution pickers only by their place: "Resolution", "Upscaled 3D resolutions", "Scaling mode"
                var up = ctl.parent; while (up != null && (up.name == "Resolutions" || up.name == "Anchor")) up = up.parent;
                var l = Kit.Text(row, "Label", up != null ? up.name : ctl.name, Fonts.Medium, 16f, Color.white); l.alignment = TextAlignmentOptions.MidlineLeft; l.rectTransform.At(0, 0, 520, RowH);
            }
            if (kind == Kind.Action)
            {   // "Reset raven hints": the button's own words are the row's label, a small button on the right
                var own = ctl.GetComponentInChildren<TMP_Text>(true);
                if (own != null) { var l = Kit.Text(row, "Label", own.text, Fonts.Medium, 16f, Color.white); l.alignment = TextAlignmentOptions.MidlineLeft; l.rectTransform.At(0, 0, 520, RowH); }
            }
            var key = Key(ctl, kind);   // before the move: it may need the row vanilla had it in
            _used.Add(ctl); ctl.SetParent(row, false);
            var rt = (RectTransform)ctl; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            var le = ctl.GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true;
            switch (kind)
            {
                case Kind.Toggle: Switch((Toggle)ctl.GetComponent<Toggle>(), t); break;
                case Kind.Slider: SliderLook(ctl.GetComponent<Slider>(), t); break;
                case Kind.Stepper: rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(260f, 34f); StepperLook(rt, t); break;
                case Kind.Dropdown: rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(260f, 34f); Raised(ctl.GetComponent<Image>(), t); ForTexts(ctl, 14f); ListRows(ctl.GetComponent<TMP_Dropdown>(), t); break;
                case Kind.Key: rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(160f, 34f); Raised(ctl.GetComponent<Image>(), t); ForTexts(ctl, 13f, Fonts.Mono); break;
                case Kind.Action:
                {
                    var tx = ctl.GetComponentInChildren<TMP_Text>(true);
                    if (tx != null) Off(tx);   // the page's Localize writes vanilla's long name back: our own short word
                    var word = Kit.Text(ctl, "jatify-word", tx != null && tx.text.Length <= 10 ? tx.text : ctl.name.StartsWith("Reset") ? "Reset" : ctl.name.StartsWith("Delete") ? "Delete" : "Open", Fonts.Medium, 13f, Color.white);
                    word.alignment = TextAlignmentOptions.Center; word.rectTransform.Fill();
                    rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(96f, 32f); Raised(ctl.GetComponent<Image>(), t); break;
                }
            }
            var (section, desc, order) = Book.Find(key, kind, _rows.Count);
            var r = new R { Ctl = ctl.gameObject, Row = row, Label = row.Find("Label")?.GetComponent<TMP_Text>(), Section = section, Desc = desc, Order = order, Value = ValueOf(ctl, kind) };
            r.Start = r.Value?.Invoke();
            if (r.Label != null)
            {
                if (desc.Length > 0)
                {
                    r.Label.rectTransform.At(0, 9, kind == Kind.Slider ? 380 : 520, 24);
                    var d = Kit.Text(row, "Desc", desc, Fonts.Medium, 12.5f, Theme.C(t.mute)); d.rectTransform.At(0, 33, kind == Kind.Slider ? 380 : 470, 22); d.overflowMode = TextOverflowModes.Ellipsis;
                }
                if (r.Value != null)
                {   // design: an ember dot left of a changed setting's name
                    r.Dot = Kit.Img(row, "Changed", Kit.Circle(), Theme.C(t.ember), false); r.Dot.rectTransform.At(-14, desc.Length > 0 ? 18 : RowH / 2f - 3f, 6, 6); r.Dot.enabled = false;
                }
            }
            _rows.Add(r);
        }

        // the name a control is known by in Book: its own, or its row's when vanilla calls it "Stepper" / "Button"
        private static string Key(Transform ctl, Kind kind)
        {
            if (kind == Kind.Dropdown) { var up = ctl.parent; while (up != null && (up.name == "Resolutions" || up.name == "Anchor")) up = up.parent; return up != null ? up.name : ctl.name; }
            return ctl.name == "Stepper" || ctl.name == "Button" ? ctl.parent.name : ctl.name;
        }

        private static System.Func<string> ValueOf(Transform ctl, Kind kind)
        {
            switch (kind)
            {
                case Kind.Toggle: { var c = ctl.GetComponent<Toggle>(); return () => c.isOn ? "1" : "0"; }
                case Kind.Slider: { var c = ctl.GetComponent<Slider>(); return () => c.value.ToString("0.###"); }
                case Kind.Dropdown: { var c = ctl.GetComponent<TMP_Dropdown>(); return () => c.value.ToString(); }
                case Kind.Stepper: case Kind.Key: { var c = ctl.GetComponentInChildren<TMP_Text>(true); return c == null ? (System.Func<string>)null : () => c.text; }
                default: return null;
            }
        }

        private static void Stack(P pg)
        {
            float y = 0f; bool first = true; const float X = 20f;   // room left of the rows for the changed dot (the view masks)
            foreach (var r in pg.Rows) r.Row.gameObject.SetActive(false);
            foreach (var sec in pg.Sections)
            {
                var (h, line) = pg.Heads[sec]; bool any = false;
                foreach (var r in pg.Rows) if (r.Section == sec && r.Shown(_q)) { any = true; break; }
                h.gameObject.SetActive(any); line.gameObject.SetActive(any);
                if (!any) continue;
                if (!first) y += 14f;
                h.rectTransform.At(X, y, 800, first ? 34 : 28); y += first ? 38 : 32; line.rectTransform.At(X, y, 240, 1); y += 8; first = false;
                foreach (var r in pg.Rows)
                {
                    if (r.Section != sec) continue;
                    if (!r.Shown(_q)) continue;
                    r.Row.gameObject.SetActive(true);
                    float rh = r.Desc.Length > 0 ? DescRowH : RowH; r.Row.At(X, y, 800, rh); y += rh;
                }
            }
            if (first && _q.Length > 0)
            {   // nothing matched on this tab
                if (pg.Heads.Count > 0) { var (h, _) = pg.Heads[pg.Sections[0]]; h.gameObject.SetActive(true); h.text = $"Nothing here matches \u201c{_q}\u201d"; h.rectTransform.At(X, 0, 800, 34); }
            }
            else if (pg.Sections.Count > 0) pg.Heads[pg.Sections[0]].h.text = pg.Sections[0];
            pg.Col.sizeDelta = new Vector2(0f, y + 40f);
        }

        // vanilla's checkbox -> our switch (same Toggle; a look that follows isOn)
        private static void Switch(Toggle tg, ThemeData t)
        {
            var rt = (RectTransform)tg.transform; rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(46f, 24f);
            foreach (var g in tg.GetComponentsInChildren<Image>(true)) Off(g);
            var track = Kit.Panel(rt, "jatify-track", Theme.C(t.raised), 12); track.rectTransform.Fill(); track.raycastTarget = true;
            var knob = Kit.Img(track.rectTransform, "Knob", Kit.Circle(), Color.white, false);
            tg.targetGraphic = track; tg.graphic = null; tg.transition = Selectable.Transition.None;
            tg.gameObject.AddComponent<SwitchLook>().Init(tg, track, knob.rectTransform, Theme.C(t.accent), new Color(1f, 1f, 1f, 0.14f));
        }

        private static void SliderLook(Slider s, ThemeData t)
        {
            var rt = (RectTransform)s.transform; rt.anchoredPosition = new Vector2(-170f, 0f); rt.sizeDelta = new Vector2(220f, 24f);
            var bg = s.transform.Find("Background") as RectTransform;
            if (bg != null) { bg.anchorMin = new Vector2(0f, 0.4f); bg.anchorMax = new Vector2(1f, 0.6f); bg.offsetMin = bg.offsetMax = Vector2.zero; var i = bg.GetComponent<Image>(); i.sprite = Kit.Rounded(3); i.type = Image.Type.Sliced; i.color = new Color(1f, 1f, 1f, 0.12f); }
            var fa = s.transform.Find("Fill Area") as RectTransform;
            if (fa != null) { fa.anchorMin = new Vector2(0f, 0.4f); fa.anchorMax = new Vector2(1f, 0.6f); fa.offsetMin = fa.offsetMax = Vector2.zero; }
            if (s.fillRect != null) { s.fillRect.offsetMin = s.fillRect.offsetMax = Vector2.zero; var i = s.fillRect.GetComponent<Image>(); if (i != null) { i.sprite = Kit.Rounded(3); i.type = Image.Type.Sliced; i.color = Theme.C(t.accent); } }
            if (s.handleRect != null) { s.handleRect.sizeDelta = new Vector2(14f, 0f); var i = s.handleRect.GetComponent<Image>(); if (i != null) { i.sprite = Kit.Circle(); i.type = Image.Type.Simple; i.color = Color.white; i.preserveAspect = true; } }
            // the value (or quality sliders' value + warning) right of the track
            var val = (s.transform.Find("Info") ?? s.transform.Find("Value")) as RectTransform;
            if (val != null) { val.anchorMin = val.anchorMax = new Vector2(1f, 0.5f); val.pivot = new Vector2(0f, 0.5f); val.anchoredPosition = new Vector2(14f, 0f); val.sizeDelta = new Vector2(156f, 40f); }
            foreach (var tx in s.GetComponentsInChildren<TMP_Text>(true)) if (tx.name == "Value") { Style(tx, Fonts.Mono, 12.5f, Color.white, TextAlignmentOptions.MidlineLeft); tx.textWrappingMode = TextWrappingModes.Normal; tx.overflowMode = TextOverflowModes.Overflow; }   // "[Extreme] (circular 544 m)" takes two lines
        }

        private static void StepperLook(RectTransform st, ThemeData t)
        {
            foreach (var side in new[] { "Left", "Right" })
            {
                var b = st.Find(side) as RectTransform; if (b == null) continue;
                b.sizeDelta = new Vector2(34f, 34f); Raised(b.GetComponent<Image>(), t); ForTexts(b, 16f);
            }
            var v = st.Find("Value") as RectTransform;
            if (v != null) { v.offsetMin = new Vector2(38f, 0f); v.offsetMax = new Vector2(-38f, 0f); var i = v.GetComponent<Image>(); if (i != null) { i.sprite = Kit.Rounded(8); i.type = Image.Type.Sliced; i.color = new Color(0f, 0f, 0f, 0.3f); } ForTexts(v, 14f); }
        }

        private static void NavButton(Transform b, string label, Color c, float y, ThemeData t)
        {
            if (b == null) return;
            var rt = (RectTransform)b; rt.At(420, y, 80, 30);
            var hint = b.Find("KeyHint"); if (hint != null) hint.gameObject.SetActive(false);
            foreach (var tx in b.GetComponentsInChildren<TMP_Text>(true)) if (tx.transform.parent == b) Off(tx);
            var l = Kit.Text(b, "jatify-label", label, Fonts.Bold, 20f, c); l.alignment = TextAlignmentOptions.MidlineLeft; l.rectTransform.Fill(); l.raycastTarget = true;   // the click target now
            Off(b.GetComponent<Image>());
        }

        // the open list's rows: vanilla's row background is plain white (its text was dark); ours is light, so the
        // background goes clear, with a soft accent under the hovered row and the current choice
        private static void ListRows(TMP_Dropdown dd, ThemeData t)
        {
            var item = dd != null && dd.template != null ? dd.template.Find("Viewport/Content/Item") : null; if (item == null) return;
            var bg = item.Find("Item Background")?.GetComponent<Image>(); var check = item.Find("Item Checkmark")?.GetComponent<Image>();
            if (bg != null)
            {
                bg.sprite = Kit.Rounded(6); bg.type = Image.Type.Sliced; bg.color = Color.white;
                var tg = item.GetComponent<Toggle>();
                if (tg != null) { var cb = ColorBlock.defaultColorBlock; cb.normalColor = Color.clear; cb.highlightedColor = Theme.C(t.raised2); cb.selectedColor = Theme.C(t.raised2); cb.pressedColor = Theme.C(t.raised); cb.colorMultiplier = 1f; cb.fadeDuration = 0.06f; tg.targetGraphic = bg; tg.colors = cb; }
            }
            if (check != null) { check.sprite = Kit.Rounded(6); check.type = Image.Type.Sliced; check.color = Theme.C(t.accentSoft); }
            var label = item.Find("Item Label")?.GetComponent<TMP_Text>(); if (label != null) { label.alignment = TextAlignmentOptions.MidlineLeft; label.color = Color.white; label.overflowMode = TextOverflowModes.Overflow; }
            ((RectTransform)item).sizeDelta = new Vector2(((RectTransform)item).sizeDelta.x, 28f);   // vanilla's 20 px row is shorter than our font's line: TMP drew nothing
        }

        private static void Raised(Image i, ThemeData t)
        {
            if (i == null) return; i.sprite = Kit.Rounded(8); i.type = Image.Type.Sliced; i.color = Theme.C(t.raised);
            var sel = i.GetComponent<Selectable>(); if (sel == null) return;   // vanilla's tint went near black when selected (the resolution picker)
            var cb = ColorBlock.defaultColorBlock; cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); cb.selectedColor = new Color(1.12f, 1.12f, 1.12f); cb.disabledColor = new Color(1f, 1f, 1f, 0.4f); cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            sel.targetGraphic = i; sel.transition = Selectable.Transition.ColorTint; sel.colors = cb;
        }
        private static void ForTexts(Transform root, float size, TMP_FontAsset font = null)
        {
            foreach (var tx in root.GetComponentsInChildren<TMP_Text>(true))
            {
                Style(tx, font ?? Fonts.Medium, size, Color.white, TextAlignmentOptions.Center);
                if (tx.transform.parent == root) tx.rectTransform.Fill(4f);   // vanilla's label boxes are sized for its bigger buttons
                tx.overflowMode = TextOverflowModes.Ellipsis; tx.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
        private static void Style(TMP_Text tx, TMP_FontAsset f, float size, Color c, TextAlignmentOptions a) { tx.font = f; tx.fontSize = size; tx.enableAutoSizing = false; tx.color = c; tx.alignment = a; tx.fontStyle = FontStyles.Normal; }
        private static void Off(Behaviour b) { if (b != null) b.enabled = false; }
    }

    // Sections and help lines for Valheim's settings, from the live design's settings book (SETTINGS),
    // keyed by vanilla's object names. Anything not listed keeps vanilla's order under "More".
    internal static class Book
    {
        private static readonly string[] Sections = { "Hints", "Controls", "Saves & system", "Mouse", "Movement", "Actions", "Display", "Quality", "Effects", "Volume", "Music", "Screen", "Camera", "More" };
        private static readonly string[] Rows =
        {
            "Tutorial|Hints|Oden sends his ravens to advise you on your adventures.",
            "Reset|Hints|Show every raven hint again, including the ones you have read.",
            "KeyHints|Hints|Key prompts next to things you can use.",
            "EnableShowBuildPieceAuthor|Hints|Show who placed a piece when you look at it.",
            "ToggleSprint|Controls|Press once to run instead of holding.",
            "ToggleAttackTowardsLookDir|Controls|Swing where the camera points instead of where you face.",
            "ToggleBlock|Controls|Press once to raise your shield instead of holding.",
            "Language|Saves & system|The game needs a restart when you change language.",
            "AutoBackups|Saves & system|How many older saves are kept for you.",
            "ReduceBackgroundUsage|Saves & system|Lower the frame rate while the game is not in focus.",
            "EnableConsole|Saves & system|Opens the developer console with F5.",
            "Skip intro cinematic|Saves & system|",
            "Radial Settings|Saves & system|The radial menu for controllers.",
            "DeletePlayfabAccount|Saves & system|",
            "MouseSensitivity|Mouse|",
            "InvertMouse|Mouse|",
            "QuickPieceSelect|Mouse|Pick build pieces with the right mouse button.",
            "Forward|Movement|", "Backward|Movement|", "Left|Movement|", "Right|Movement|", "Jump|Movement|", "Crouch|Movement|", "Run|Movement|", "Walk|Movement|", "AutoRun|Movement|", "Dodge|Movement|", "AltDodge|Movement|", "Sit|Movement|",
            "ResetControls|Actions|Every key back to its default.",
            "Resolution|Display|", "Fullscreen|Display|", "Test|Display|", "Upscaled 3D resolutions|Display|", "Scaling mode|Display|", "FPSLimit|Display|", "VSync|Display|",
            "GraphicsMode|Display|Change any setting below and this becomes Custom.",
            "MasterVolume|Volume|", "SfxVolume|Volume|", "MusicVolume|Volume|",
            "ContinuosMusic|Music|Keep music playing instead of only at moments.",
            "GuiScaling|Screen|Makes every panel and text larger. Best on 2560 \u00d7 1440 or bigger.",
            "ReduceFlashingLights|Screen|Softens lightning and other bright flashes.",
            "ClosedCaptions|Screen|", "DirectionalIndicators|Screen|",
            "ImmersiveCamera|Camera|When sailing, the camera moves with the waves.",
            "CameraShake|Camera|Shake the camera when you take damage.",
            "MotionBlur|Camera|", "DepthOfField|Camera|",
        };

        internal static int Order(string section) { int i = System.Array.IndexOf(Sections, section); return i < 0 ? Sections.Length : i; }

        // (section, help line, sort order); walk = the control's place in vanilla's page
        internal static (string, string, int) Find(string key, object kind, int walk)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                var f = Rows[i].Split('|');
                if (f[0] != key) continue;
                if (kind.ToString() == "Key" && f[1] != "Movement") break;   // a key bind named like a setting (the console key): it's a key
                return (f[1], f[2], key == "ResetControls" ? 100000 : i);   // design: Reset controls closes the key list
            }
            if (key.StartsWith("QualitySlider")) return ("Quality", "", 1000 + walk);
            if (key.StartsWith("QualityToggle")) return ("Effects", "", 1000 + walk);
            if (kind.ToString() == "Key") return ("Actions", "", 1000 + walk);
            return ("More", "", 1000 + walk);
        }
    }

    internal class SwitchLook : MonoBehaviour
    {
        private Toggle _tg; private Image _track; private RectTransform _knob; private Color _on, _off; private int _last = -1;
        internal void Init(Toggle tg, Image track, RectTransform knob, Color on, Color off) { _tg = tg; _track = track; _knob = knob; _on = on; _off = off; }
        private void LateUpdate()
        {
            int v = _tg.isOn ? 1 : 0; if (v == _last) return; _last = v;
            _track.color = v == 1 ? _on : _off; _knob.At(v == 1 ? 25 : 3, 3, 18, 18);
        }
    }

    [HarmonyPatch]
    internal static class MenuStaysLive
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(Types.Find("UIGroupHandler"), "Update");   // the type lives in Valheim's GUI assembly
        // vanilla's UI groups lock (and grey out) every group under the top one, every frame: the pause menu
        // sits beside our Settings, so it stays usable (patched right after the group's own update, else a click
        // could land on a locked frame)
        private static readonly AccessTools.FieldRef<object, CanvasGroup> Group = AccessTools.FieldRefAccess<CanvasGroup>(Types.Find("UIGroupHandler"), "m_canvasGroup");
        private static void Postfix(MonoBehaviour __instance)
        {
            if ((Settings.instance == null && !PlayersPage.Open) || Menu.instance == null || __instance.transform != Menu.instance.m_root) return;   // beside Settings, and beside the Players page (Valheim's hidden list takes the focus)
            var g = Group(__instance); if (g != null) { g.interactable = true; g.blocksRaycasts = true; }
        }
    }

    [HarmonyPatch(typeof(Menu), nameof(Menu.OnSettings))]
    internal static class OneSettings
    {
        // the menu stays clickable beside Settings: Settings again must not open a second one
        private static bool Prefix() => Settings.instance == null;
    }
}

namespace Jatify.UI
{
    // a dropdown's open list sits on its own canvas at 30000, under our pages (32000): the rows behind showed through and the
    // options looked empty (user). Lifted above them each time it opens.
    [HarmonyLib.HarmonyPatch(typeof(TMPro.TMP_Dropdown), nameof(TMPro.TMP_Dropdown.Show))]
    internal static class DropdownOnTop
    {
        internal static bool Arm;   // dev: dump the next list that opens
        private static void Postfix(TMPro.TMP_Dropdown __instance)
        {
            var list = __instance.transform.Find("Dropdown List"); var cv = list != null ? list.GetComponent<Canvas>() : null;
            if (cv != null) { cv.overrideSorting = true; cv.sortingOrder = 32600; }
#if JATIFY_DEV
            if ((Arm || Dev.Build) && list != null) { Arm = false; DevDump.Tree("dropdownlist", list, BepInEx.Paths.CachePath); Log.Info("dev dump -> dump-dropdownlist.txt"); }
#endif
        }
    }
}
