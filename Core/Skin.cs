using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Generic skin for vanilla windows we have not hand-styled (settings, skills, texts, trophies,
    // achievements, dialogs). Maps by sprite and font name: wood panels -> glass, buttons/tabs ->
    // raised rounded, input/item backgrounds -> dark wells, braids/separators -> hidden, Valheim fonts
    // -> ours. Item icons and anything unrecognised are left alone. Every change is ledger-tracked.
    internal static class Skin
    {
        internal static void Apply(Transform root, Ledger l, ThemeData t)
        {
            if (root == null) return;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var s = img.sprite; if (s == null) continue;
                var n = s.name;
                if (n.StartsWith("woodpanel")) l.Img(img, Kit.Rounded(14), Theme.C(t.glass));
                else if (n == "button" || n == "button_tab" || n == "button_small" || n == "UISprite") l.Img(img, Kit.Rounded(8), Theme.C(t.raised));
                else if (n == "item_background" || n == "InputFieldBackground" || n == "text_field" || n == "sunken" || n == "Background") l.Img(img, Kit.Rounded(8), new Color(0f, 0f, 0f, Mathf.Max(0.25f, img.color.a * 0.6f)));
                else if (n.StartsWith("BraidLine") || n.StartsWith("BraidKnot") || n == "panel_separator") l.Off(img);
                else if (n == "key_base") l.Img(img, Kit.Rounded(5), Theme.C(t.raised));
                else if (n == "selection_frame") l.Img(img, Kit.Rounded(10), Theme.C(t.accentSoft));
                else if (n == "button_tab_selected") l.Img(img, Kit.Rounded(8), Theme.C(t.accentSoft));
                else if (n == "checkbox_marker" || n == "Knob") Tint(img, Theme.C(t.accent), l);    // keep the shape, our colour
                else if (n == "rudder_arrow") Tint(img, Theme.C(t.dim), l);
            }
            foreach (var tx in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var f = tx.font != null ? tx.font.name : "";
                if (!f.StartsWith("Valheim")) continue;
                bool bold = f.Contains("bold") || f.Contains("Norse") && tx.fontSize >= 24f;
                l.Font(tx, f.Contains("Prstartk") ? Fonts.Mono : bold ? Fonts.Bold : Fonts.Medium);
                var c = tx.color;
                if (c.r > 0.85f && c.g > 0.35f && c.g < 0.85f && c.b < 0.5f)         // Valheim's orange labels
                { var c0 = c; var to = bold ? Color.white : Theme.C(t.text); to.a = c.a; l.Do(() => tx.color = to, () => { if (tx != null) tx.color = c0; }); }
            }
        }

        private static void Tint(Image img, Color c, Ledger l)
        {
            var c0 = img.color; c.a = c0.a;
            l.Do(() => img.color = c, () => { if (img != null) img.color = c0; });
        }
    }

    // Applies the skin to vanilla windows as they appear (some are created on open, e.g. Settings),
    // once per window object, and undoes all of it on unload.
    internal static class Windows
    {
        private static readonly Ledger _l = new Ledger();
        private static readonly HashSet<int> _done = new HashSet<int>();
        internal static BepInEx.Configuration.ConfigEntry<bool> MainMenu;
        private static float _next;

        internal static void Tick(ThemeData t)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            var g = InventoryGui.instance;
            if (g != null)
                foreach (var n in new[] { "root/Skills", "root/Achievements", "root/Trophies", "root/SplitDialog", "root/VariantDialog" })
                    Once(g.transform.Find(n), t);
            if (g != null && g.m_textsDialog != null) Once(g.m_textsDialog.transform, t);
            var split = g != null ? g.transform.Find("root/SplitDialog/win_bkg") as RectTransform : null;   // design .dlg .knot: one knot above "Split stack"
            if (split != null && split.gameObject.activeInHierarchy && split.Find("jatify-dlgknot") == null) _l.Own(Knots.DialogTop(split, -14f, Theme.C(t.accent)));
            var hud = Hud.instance;   // the centre indicators: stagger, building-piece health, action progress
            if (hud != null) { if (hud.m_staggerProgress != null) Once(hud.m_staggerProgress.transform, t); if (hud.m_pieceHealthRoot != null) Once(hud.m_pieceHealthRoot, t); if (hud.m_actionProgress != null) Once(hud.m_actionProgress.transform, t); }
            if (hud != null && hud.m_shipHudRoot != null) Once(hud.m_shipHudRoot.transform, t);   // ship controls and wind while steering
            var fs = FejdStartup.instance;   // the title screen: character and world select, credits
            if (fs != null && MainMenu != null && MainMenu.Value) { foreach (var go in new[] { fs.m_characterSelectScreen, fs.m_startGamePanel, fs.m_creditsPanel }) if (go != null) Once(go.transform, t); }
            var sg = StoreGui.instance;   // the trader (Haldor, Hildir, the Bog Witch): the window, and the row template every item is made from
            if (sg != null) { if (sg.m_rootPanel != null) Once(sg.m_rootPanel.transform, t); if (sg.m_listElement != null && _done.Add(sg.m_listElement.GetInstanceID())) Skin.Apply(sg.m_listElement.transform, _l, t); }
            if (Chat.instance != null && Chat.instance.m_chatWindow != null) ChatWindow(Chat.instance.m_chatWindow, t);          // chat: glass, our fonts
            if (Console.instance != null && Console.instance.m_chatWindow != null) ConsoleWindow(Console.instance.m_chatWindow, t);
            var settings = Settings.instance;   // a scene-wide search here cost ~5 ms a frame in a big base
            if (settings != null) Once(settings.transform, t);
        }

        // chat: its "gradient" backdrop becomes a glass panel; the generic skin does the input well and fonts
        private static void ChatWindow(Transform w, ThemeData t)
        {
            if (w == null || !w.gameObject.activeInHierarchy || !_done.Add(w.GetInstanceID())) return;
            Skin.Apply(w, _l, t);
            // World of Warcraft's chat frame (user): a light dark wash (no tab: user), white text with a shadow to read
            // over the world, a slim input bar with "Say:"
            var bkg = w.Find("root/bkg")?.GetComponent<Image>(); if (bkg != null) _l.Img(bkg, Kit.Rounded(4), new Color(0f, 0f, 0f, 0.38f));
            var text = w.Find("root/Text")?.GetComponent<TMP_Text>(); if (text != null) { _l.Txt(text, Fonts.Medium, 15f, Color.white); Shadow(text); }
            WowInput(w.Find("root/ChatInput"), "Say:", t);
            // bottom left like WoW's (user: it sat over the food / meads bars on the right); its frame draws 251 left and 133
            // up of "root", so this puts the frame's corner 24 px in and 120 px up, clear of the tools bar and its key hints
            _l.Rect(w.Find("root") as RectTransform, r => { r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = new Vector2(24f + 251f + 250f, 120f - 133f + 200f); });
        }

        // the input line: a dark bar, its channel label in front
        private static void WowInput(Transform input, string label, ThemeData t)
        {
            if (input == null) return;
            _l.Img(input.GetComponent<Image>(), Kit.Rounded(4), new Color(0f, 0f, 0f, 0.7f));
            var lb = Kit.Text(input, "jatify-say", label, Fonts.Bold, 14f, Color.white); _l.Own(lb); lb.alignment = TextAlignmentOptions.MidlineLeft; Shadow(lb);
            var lr = lb.rectTransform; lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0f, 1f); lr.pivot = new Vector2(0f, 0.5f); lr.anchoredPosition = new Vector2(8f, 0f); lr.sizeDelta = new Vector2(44f, 0f);
            _l.Rect(input.Find("Text Area") as RectTransform, ta => { ta.offsetMin = new Vector2(ta.offsetMin.x + 44f, ta.offsetMin.y); });
            foreach (var x in input.GetComponentsInChildren<TMP_Text>(true)) if (x != lb) _l.Txt(x, Fonts.Medium, 15f, x.name == "Placeholder" ? Theme.C(t.mute) : Color.white);
        }

        // a soft dark outline under light text: readable over snow and fire
        // a dark outline, as one shared material per font: TMP's outlineWidth throws on text whose object hasn't woken yet
        // (the chat input is hidden until you type), which stopped the whole restyle, chat placement included (user)
        private static readonly Dictionary<TMP_FontAsset, Material> _outlined = new Dictionary<TMP_FontAsset, Material>();
        private static void Shadow(TMP_Text x)
        {
            if (x.font == null || x.font.material == null) return;
            if (!_outlined.TryGetValue(x.font, out var m) || m == null)
            {
                m = new Material(x.font.material) { name = x.font.name + " outlined" };
                m.EnableKeyword("OUTLINE_ON"); m.SetFloat("_OutlineWidth", 0.15f); m.SetColor("_OutlineColor", new Color32(0, 0, 0, 170));
                _outlined[x.font] = m;
            }
            x.fontSharedMaterial = m;
        }

        // F5 console: a plain black box (no sprite) -> the glass colour; terminal text in our mono font
        private static void ConsoleWindow(Transform w, ThemeData t)
        {
            if (w == null || !w.gameObject.activeInHierarchy || !_done.Add(w.GetInstanceID())) return;
            Skin.Apply(w, _l, t);
            // WoW-style like the chat: a darker wash (it covers the top half), a "Console" tab, mono text, a ">" input bar
            var bg = w.GetComponent<Image>(); if (bg != null) { var c0 = bg.color; var c = new Color(0f, 0f, 0f, 0.7f); _l.Do(() => bg.color = c, () => { if (bg != null) bg.color = c0; }); }
            foreach (var tx in w.GetComponentsInChildren<TMP_Text>(true)) _l.Font(tx, Fonts.Mono);
            if (bg != null) { var tab = Kit.Img(w, "jatify-tab", Kit.Rounded(4), new Color(0f, 0f, 0f, 0.8f), false); _l.Own(tab); var r = tab.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f); r.anchoredPosition = new Vector2(8f, -24f); r.sizeDelta = new Vector2(90f, 24f);
                var tt = Kit.Text(r, "T", "Console", Fonts.Bold, 12.5f, new Color(1f, 0.82f, 0f)); tt.alignment = TextAlignmentOptions.Center; tt.rectTransform.Fill(); }
            WowInput(w.Find("ConsoleInput"), ">", t);
        }

        private static void Once(Transform w, ThemeData t)
        {
            if (w == null || !w.gameObject.activeInHierarchy || !_done.Add(w.GetInstanceID())) return;
            Skin.Apply(w, _l, t);
        }

        internal static void Restore() { TextEntry.Restore(); _l.Undo(); _done.Clear(); foreach (var m in _outlined.Values) if (m != null) Object.Destroy(m); _outlined.Clear(); }
    }
}
