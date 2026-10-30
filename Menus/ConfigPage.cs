using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The Mods and Jatify UI pages from the pause menu (design: panel-less; the left column is the
    // navigation - crumb, title, sections - and the rows float over the dimmed game). Mods lists every
    // plugin's BepInEx settings; Jatify UI lists ours. Editors follow the setting's type: switch,
    // option buttons, slider, key capture, text. A change is written to the entry and BepInEx saves it.
    internal static class ConfigFileExt
    {
        internal static IEnumerable<ConfigEntryBase> Entries(this ConfigFile c) => c.Select(kv => kv.Value);
    }

    internal sealed class ConfigPage
    {
        private readonly RectTransform _root, _nav, _content;
        private readonly ThemeData _t;
        private readonly bool _modsMode;
        private string _sel, _q = "";   // _q: the search, lower case
        private TMP_InputField _search;
        private ConfigEntryBase _listening; private TextMeshProUGUI _listenLabel;
        internal static ConfigPage Open; private readonly bool _fromMenu;

        private ConfigPage(Transform canvas, ThemeData t, bool mods)
        {
            _t = t; _modsMode = mods; _fromMenu = Menu.IsVisible();
            _root = Kit.Node(canvas, mods ? "ModsPage" : "JatifyPage").Fill();
            var own = _root.gameObject.AddComponent<Canvas>(); own.overrideSorting = true; own.sortingOrder = 32000; // above Valheim's menus
            _root.gameObject.AddComponent<GraphicRaycaster>();
            // on the title screen there's no pause menu to cascade from: Valheim's menu (and its logo) step aside while
            // we're open (user: the page sat over them) and the scene dims behind the text
            var fs = FejdStartup.instance;
            if (!_fromMenu && fs != null && fs.m_mainMenu != null && fs.m_mainMenu.activeSelf)
            {
                _title = fs.m_mainMenu; _title.SetActive(false);
                var dim = Kit.Img(_root, "Dim", null, new Color(0.02f, 0.02f, 0.024f, 0.6f)); dim.rectTransform.Fill(); dim.raycastTarget = true;
            }
            else if (Player.m_localPlayer != null && !Menu.IsVisible())
            {   // opened from the inventory's gear in the world (no pause menu darkening it): a dim, or the rows sat unreadable on bright snow
                var dim = Kit.Img(_root, "Dim", null, new Color(0.02f, 0.02f, 0.024f, 0.86f)); dim.rectTransform.Fill(); dim.raycastTarget = true; dim.transform.SetAsFirstSibling();
            }
            // a submenu cascading right of the pause menu, in its style (user): no panel, the menu stays visible and clickable
            var head = Kit.Text(_root, "Head", (mods ? "Mods" : "Jatify UI").ToUpperInvariant(), Fonts.Mono, 12f, Theme.C(t.mute)); head.characterSpacing = 8f; head.rectTransform.At(420, 300, 260, 18);
            // the navigation scrolls: with many mods it ran off the screen (#21). Its view starts left of it, so the selection mark isn't clipped.
            var navView = Kit.Node(_root, "NavView").At(400, 330, 280, 690);
            navView.gameObject.AddComponent<RectMask2D>();
            var navHit = navView.gameObject.AddComponent<Image>(); navHit.color = new Color(0, 0, 0, 0); navHit.raycastTarget = true;
            _nav = Kit.Node(navView, "Nav").At(20, 0, 260, 640);
            var nsr = navView.gameObject.AddComponent<ScrollRect>(); nsr.content = _nav; nsr.horizontal = false; nsr.scrollSensitivity = Kit.ScrollSpeed; nsr.movementType = ScrollRect.MovementType.Clamped;
            // search over every section (on Mods: every mod), then the rows: a scroll view right of the navigation
            Pages.T = Pages.T ?? t;
            _search = Pages.Search(_root, mods ? "Search mod settings" : "Search settings", 720, 262, 360, q => { _q = q.Trim().ToLowerInvariant(); BuildRows(); });
            var view = Kit.Node(_root, "View").At(720, 312, 800, 710);
            view.gameObject.AddComponent<RectMask2D>();
            var img = view.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
            _content = Kit.Node(view, "Content");
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f); _content.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = _content; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            BuildNav();
        }


        internal static void Show(Transform canvas, ThemeData t, bool mods) { Open?.Close(); PlayersPage.Close(); if (Settings.instance != null) Settings.instance.OnBack(); Open = new ConfigPage(canvas, t, mods); }

        internal void DevSection(string name) { _sel = name; BuildNav(); }   // dev: open a section (What's new screenshots)
        private GameObject _title;
        internal void Close()
        {
            if (_title != null) _title.SetActive(true);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            if (Open == this) Open = null;
        }

        // ---- data -------------------------------------------------------------------------------------
        private IEnumerable<(string name, ConfigFile cfg)> Sources()
        {
            if (!_modsMode) { yield return ("Jatify UI", Plugin.Instance.Config); yield break; }
            foreach (var pi in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.Name))
                if (pi.Instance != null && pi.Instance.Config != null && pi.Instance.Config.Count > 0) yield return (pi.Metadata.Name, pi.Instance.Config);
        }

        private List<(string group, List<ConfigEntryBase> entries)> Groups()
        {
            var src = Sources().ToList();
            if (_q.Length > 0)   // searching: every match, grouped by mod (Mods) and section, whatever is selected
                return src.SelectMany(x => x.cfg.Entries().Where(Matches).Select(e => (g: _modsMode ? x.name + " · " + e.Definition.Section : e.Definition.Section, e)))
                          .GroupBy(x => x.g).Select(g => (g.Key, g.Select(x => x.e).ToList())).ToList();
            if (_modsMode) { var s = src.FirstOrDefault(x => x.name == _sel); return s.cfg == null ? new List<(string, List<ConfigEntryBase>)>() : s.cfg.Entries().GroupBy(e => e.Definition.Section).Select(g => (g.Key, g.ToList())).ToList(); }
            var cfg = src[0].cfg;
            return cfg.Entries().Where(e => _sel == null || e.Definition.Section == _sel).GroupBy(e => e.Definition.Section).Select(g => (g.Key, g.ToList())).ToList();
        }

        // key (as shown and as written), section and description
        private bool Matches(ConfigEntryBase e) =>
            Nice(e.Definition.Key).ToLowerInvariant().Contains(_q) || e.Definition.Key.ToLowerInvariant().Contains(_q) ||
            e.Definition.Section.ToLowerInvariant().Contains(_q) || (e.Description?.Description ?? "").ToLowerInvariant().Contains(_q);

        private void BuildNav()
        {
            foreach (Transform c in _nav) UnityEngine.Object.Destroy(c.gameObject);
            var items = _modsMode ? Sources().Select(s => s.name).ToList() : Plugin.Instance.Config.Entries().Select(e => e.Definition.Section).Distinct().ToList();
            if (_sel == null || !items.Contains(_sel)) _sel = items.FirstOrDefault();
            float y = 0f, size = _modsMode ? 20f : 26f, step = _modsMode ? 34f : 44f;
            foreach (var n in items)
            {
                bool on = n == _sel; var name = n;
                var tx = Kit.Text(_nav, n, n, Fonts.Bold, size, on ? Theme.C(_t.accent) : new Color(1, 1, 1, 0.78f));
                tx.rectTransform.At(0, y, 260, step); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.overflowMode = TextOverflowModes.Ellipsis; tx.raycastTarget = true;
                tx.gameObject.AddComponent<Button>().onClick.AddListener(() => { _sel = name; BuildNav(); });
                if (on) { var dia = Kit.Img(_nav, "Mark", null, Theme.C(_t.accent)); dia.rectTransform.At(-16, y + step / 2f - 3f, 7, 7); dia.rectTransform.localEulerAngles = new Vector3(0, 0, 45); }
                y += step;
            }
            var back = Kit.Text(_nav, "Back", "Back", Fonts.Bold, 20f, Theme.C(_t.dim)); back.rectTransform.At(0, y + 18, 80, 30); back.alignment = TextAlignmentOptions.MidlineLeft;
            back.raycastTarget = true; back.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            var esc = Kit.Text(_nav, "Esc", "Esc", Fonts.Mono, 12f, Theme.C(_t.mute)); esc.rectTransform.At(Kit.W(back) + 12f, y + 18, 60, 30); esc.alignment = TextAlignmentOptions.MidlineLeft;
            _nav.sizeDelta = new Vector2(260f, y + 60f);
            BuildRows();
        }

        // ---- rows -------------------------------------------------------------------------------------
        private void BuildRows()
        {
            foreach (Transform c in _content) UnityEngine.Object.Destroy(c.gameObject);
            float y = 0f;
            foreach (var (group, entries) in Groups())
            {
                var h = Kit.Text(_content, "Group", group, Fonts.Norse, 28f, Color.white); h.rectTransform.At(0, y + 12, 800, 30);
                var line = Kit.Img(_content, "Line", null, new Color(0.13f, 0.84f, 0.49f, 0.45f)); line.rectTransform.At(0, y + 46, 240, 1);
                y += 58f;
                foreach (var e in entries) y = Row(e, y);
            }
            if (y == 0f) { var none = Kit.Text(_content, "None", _q.Length > 0 ? "No setting matches that." : "This mod has no settings to change.", Fonts.Medium, 15f, Theme.C(_t.mute)); none.rectTransform.At(0, 16, 600, 24); y = 60f; }
            _content.sizeDelta = new Vector2(0f, y + 40f);
        }

        private float Row(ConfigEntryBase e, float y)
        {
            var desc = e.Description?.Description ?? "";
            bool server = Sync.ReadOnly(e);
            var note = server ? "Set by the server while you're on it. Your own value comes back when you leave." : Inert(e); if (note != null) desc = $"<color=#{ColorUtility.ToHtmlStringRGB(Theme.C(_t.stamina))}>{note}</color>" + (desc.Length > 0 ? "\n" + desc : "");
            float h = 44f;
            var label = Kit.Text(_content, "Label", Nice(e.Definition.Key), Fonts.Medium, 16f, Color.white); label.rectTransform.At(0, y + 6, 470, 22);
            if (desc.Length > 0)
            {   // wraps beside the editor (sliders start at 470), the row grows with it
                var d = Kit.Text(_content, "Desc", desc, Fonts.Medium, 12.5f, Theme.C(_t.dim)); d.textWrappingMode = TextWrappingModes.Normal;
                var em = Fonts.Edged(Fonts.Medium); if (em != null) d.fontSharedMaterial = em;   // a dark edge: the page has no panel, and mute grey vanished over snow (user)
                float dh = Mathf.Ceil(d.GetPreferredValues(desc, 440f, 0f).y);
                d.rectTransform.At(0, y + 28, 440, dh); h = 28f + dh + 12f;
            }
            if (server)
            {   // the server's value, not an editor (a change here would only be local, and wrong)
                var shown = e.BoxedValue is bool b ? (b ? "On" : "Off") : e.BoxedValue?.ToString() ?? "";
                var v = Kit.Text(_content, "Server", shown, Fonts.Mono, 14f, Theme.C(_t.dim)); v.alignment = TextAlignmentOptions.MidlineRight; v.rectTransform.At(800 - 220, y + (h - 32f) / 2f, 220, 32);   // right-aligned with the switches
            }
            else Editor(e, y + (h - 32f) / 2f);
            return y + h + 4f;
        }

        // a setting another installed mod has taken over: said on its row, so nobody wonders why it does nothing
        private static string Inert(ConfigEntryBase e)
        {
            if (!Azu.Drives) return null;
            if (e == ExtraRows.Extra) return "AzuEPI is installed and manages your inventory rows (its Extra Inventory Rows setting): this one does nothing.";
            var k = e.Definition.Key; if (e.Definition.Section == "Keys" && (k.StartsWith("FoodSlot") || k.StartsWith("ToolSlot") || k.StartsWith("MeadSlot")))
                return "AzuEPI is installed: there are no Food / Tools / Meads rows. Its quick slots use its own keys (its Quick Slot Hotkeys).";
            return null;
        }

        private static string Nice(string key) => System.Text.RegularExpressions.Regex.Replace(key, "(?<=[a-z])([A-Z])", " $1");

        private void Editor(ConfigEntryBase e, float y)
        {
            var type = e.SettingType; var range = e.Description?.AcceptableValues;
            if (type == typeof(bool)) { Switch(e, y); return; }
            if (type.IsEnum) { Options(e, Enum.GetNames(type), y); return; }
            if (range is AcceptableValueList<string> list) { Options(e, list.AcceptableValues, y); return; }
            var vals = range?.GetType().GetProperty("AcceptableValues")?.GetValue(range) as Array;   // a list of ints etc.
            if (vals != null) { Options(e, vals.Cast<object>().Select(o => o.ToString()).ToList(), y); return; }
            if ((type == typeof(int) || type == typeof(float)) && range != null) { Slider(e, range, y); return; }
            if (type == typeof(KeyboardShortcut) || type == typeof(KeyCode)) { KeyCapture(e, y); return; }
            Text(e, y);
        }

        private void Switch(ConfigEntryBase e, float y)
        {
            bool on = (bool)e.BoxedValue;
            var (track, fill) = Kit.Box(_content, "Switch", Theme.C(on ? _t.accent : _t.raised), Theme.C(on ? _t.accent : _t.line2), 11);
            track.rectTransform.At(800 - 46, y + 5, 46, 24);
            var knob = Kit.Img(track.rectTransform, "Knob", Kit.Circle(), Color.white, false); knob.rectTransform.At(on ? 24 : 3, 3, 18, 18);
            track.raycastTarget = true; track.gameObject.AddComponent<Button>().onClick.AddListener(() => { e.BoxedValue = !(bool)e.BoxedValue; BuildRows(); });
        }

        private void Options(ConfigEntryBase e, IList<string> opts, float y)
        {
            if (opts.Count > 5) { Cycle(e, opts, y); return; }
            float x = 800f; var cur = e.BoxedValue.ToString();
            for (int i = opts.Count - 1; i >= 0; i--)
            {
                var o = opts[i]; bool on = o == cur;
                var t = Kit.Text(_content, "Opt", Nice(o), Fonts.Medium, 12.5f, on ? new Color(0.06f, 0.06f, 0.07f) : Theme.C(_t.dim)); t.alignment = TextAlignmentOptions.Center;
                float w = Kit.W(t) + 20f; x -= w + 2f;
                var bg = Kit.Panel(_content, "OptBg", on ? Color.white : Theme.C(_t.raised), 8); bg.rectTransform.At(x, y + 2, w, 30); bg.transform.SetSiblingIndex(t.transform.GetSiblingIndex());
                t.rectTransform.At(x, y + 2, w, 30);
                bg.raycastTarget = true; var val = o; bg.gameObject.AddComponent<Button>().onClick.AddListener(() => { Set(e, val); BuildRows(); });
            }
        }

        private void Cycle(ConfigEntryBase e, IList<string> opts, float y)
        {
            var cur = e.BoxedValue.ToString(); int i = Mathf.Max(0, opts.IndexOf(cur));
            var (b, _) = Kit.Box(_content, "Cycle", Theme.C(_t.raised), Theme.C(_t.line2), 8); b.rectTransform.At(800 - 220, y, 220, 32);
            var t = Kit.Text(b.rectTransform, "V", "‹  " + Nice(cur) + "  ›", Fonts.Medium, 13f, Color.white); t.alignment = TextAlignmentOptions.Center; t.rectTransform.Fill();
            b.raycastTarget = true; b.gameObject.AddComponent<Button>().onClick.AddListener(() => { Set(e, opts[(i + 1) % opts.Count]); BuildRows(); });
        }

        private void Slider(ConfigEntryBase e, AcceptableValueBase range, float y)
        {
            bool isInt = e.SettingType == typeof(int);
            float min = Convert.ToSingle(range.GetType().GetProperty("MinValue").GetValue(range)), max = Convert.ToSingle(range.GetType().GetProperty("MaxValue").GetValue(range));
            float v = Convert.ToSingle(e.BoxedValue);
            var track = Kit.Panel(_content, "Track", new Color(1, 1, 1, 0.12f), 3); track.rectTransform.At(800 - 330, y + 13, 260, 6);
            var fill = Kit.Panel(track.rectTransform, "Fill", Theme.C(_t.accent), 3); var fr = fill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(Mathf.InverseLerp(min, max, v), 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            var val = Kit.Text(_content, "Value", isInt ? ((int)v).ToString() : v.ToString("0.##"), Fonts.Mono, 13f, Color.white); val.alignment = TextAlignmentOptions.MidlineRight; val.rectTransform.At(800 - 60, y, 60, 32);
            var s = track.gameObject.AddComponent<UnityEngine.UI.Slider>(); s.minValue = min; s.maxValue = max; s.wholeNumbers = isInt; s.value = v; s.fillRect = fr; s.transition = Selectable.Transition.None;
            track.raycastTarget = true;
            s.onValueChanged.AddListener(x => { val.text = isInt ? ((int)x).ToString() : x.ToString("0.##"); e.BoxedValue = isInt ? (object)(int)x : x; });
        }

        private void KeyCapture(ConfigEntryBase e, float y)
        {
            var (b, _) = Kit.Box(_content, "Key", Theme.C(_t.raised), Theme.C(_t.line2), 8); b.rectTransform.At(800 - 180, y, 180, 32);
            var t = Kit.Text(b.rectTransform, "V", KeyName(e), Fonts.Mono, 12.5f, Color.white); t.alignment = TextAlignmentOptions.Center; t.rectTransform.Fill();
            b.raycastTarget = true; b.gameObject.AddComponent<Button>().onClick.AddListener(() => { _listening = e; _listenLabel = t; t.text = "Press a key…"; t.color = Theme.C(_t.accent); });
        }

        private static string KeyName(ConfigEntryBase e) => e.BoxedValue is KeyboardShortcut k ? (k.MainKey == KeyCode.None ? "Not set" : k.ToString()) : e.BoxedValue.ToString();

        private void Text(ConfigEntryBase e, float y)
        {
            var (b, _) = Kit.Box(_content, "Field", Theme.C(_t.raised), Theme.C(_t.line2), 8); b.rectTransform.At(800 - 280, y, 280, 32);
            var area = Kit.Node(b.rectTransform, "Area").Fill(); area.offsetMin = new Vector2(10, 4); area.offsetMax = new Vector2(-10, -4);
            area.gameObject.AddComponent<RectMask2D>();
            var tx = Kit.Text(area, "Text", "", Fonts.Mono, 12.5f, Color.white); tx.rectTransform.Fill(); tx.alignment = TextAlignmentOptions.MidlineLeft;
            var field = b.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area; field.textComponent = tx; field.fontAsset = Fonts.Mono; field.pointSize = 12.5f;
            field.text = e.GetSerializedValue(); Kit.Caret(field);
            field.onEndEdit.AddListener(s => { try { e.SetSerializedValue(s); } catch (Exception ex) { Log.Warn($"{e.Definition.Key}: {ex.Message}"); } field.text = e.GetSerializedValue(); });
        }

        private static void Set(ConfigEntryBase e, string value)
        {
            try { e.BoxedValue = TomlTypeConverter.ConvertToValue(value, e.SettingType); }
            catch (Exception ex) { Log.Warn($"{e.Definition.Key}: {ex.Message}"); }
        }

        // Esc closes; a pending key capture takes the next key (Esc cancels it).
        internal void Tick()
        {
            if (_fromMenu && !Menu.IsVisible()) { Close(); return; }   // Continue clicked beside us
            if (_listening != null)
            {
                if (Menu.instance == null && ZInput.GetKeyDown(KeyCode.Escape)) { Escape(); return; }   // in game Menu.Update hands Esc over (ConfigPageEsc)
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6 || !Input.GetKeyDown(k)) continue;
                    if (k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift || k == KeyCode.LeftAlt || k == KeyCode.RightAlt) continue;
                    var mods = new List<KeyCode>();
                    foreach (var m in new[] { KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.LeftAlt }) if (Input.GetKey(m)) mods.Add(m);
                    if (_listening.SettingType == typeof(KeyCode)) _listening.BoxedValue = k; else _listening.BoxedValue = new KeyboardShortcut(k, mods.ToArray());
                    _listening = null; BuildRows(); return;
                }
                return;
            }
            if (Menu.instance == null && ZInput.GetKeyDown(KeyCode.Escape)) Escape();
        }

        // Esc (or B): cancels a key capture, else clears the search, else closes the page; the pause menu stays
        internal void Escape()
        {
            if (_listening != null) { _listening = null; BuildRows(); }
            else if (_q.Length > 0) { _search.text = ""; _search.DeactivateInputField(); }
            else Close();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Menu), "Update")]
    internal static class ConfigPageEsc
    {
        // Valheim reads Esc here and would close the whole pause menu; with our page open Esc is ours
        private static bool Prefix()
        {
            if (SmelterCard.Open && ZInput.GetKeyDown(KeyCode.Escape)) { SmelterCard.Close(); return false; }
            if (WhatsNew.Open) { if (!WhatsNew.MustRead && (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))) WhatsNew.Close(); return false; }   // read to the end first; then (or from the menu) Esc closes it, without opening the menu over it   // Esc closes the card, not opens the menu
            var page = ConfigPage.Open; if (page == null && !PlayersPage.Open) return true;
            if (!ZInput.GetKeyDown(KeyCode.Escape) && !ZInput.GetButtonDown("JoyButtonB")) return true;
            if (page != null) page.Escape(); else PlayersPage.Close();
            return false;
        }
    }
}
