using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Pause menu (vanilla buttons and logic; our look): the list moves to the left as large text in our
    // font, the braid ornaments go, and the dark blob becomes a full-screen dim. Ledger-tracked.
    internal static class MenuStyle
    {
        private static readonly Ledger _l = new Ledger();
        private static Menu _for;

        internal static void Apply(ThemeData t)
        {
            var m = Menu.instance; if (m == null || m.m_root == null) return;
            Restore(); _for = m;
            var menu = m.m_root.Find("Menu") as RectTransform; if (menu == null) return;
            _l.Off(menu.Find("darken")?.GetComponent<Graphic>());
            _l.Off(menu.Find("ornament")?.GetComponent<Graphic>());
            var dim = Kit.Img(m.m_root, "jatify-dim", null, new Color(0.024f, 0.027f, 0.031f, 0.84f)); // the world stays visible behind the menu, dark enough to read over (user: 0.72 was too light)
            _l.Own(dim); dim.rectTransform.Fill(); dim.transform.SetAsFirstSibling();
            _l.Rect(menu, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(140f, -300f); });
            var entries = menu.Find("MenuEntries") as RectTransform;
            if (entries == null) return;
            _l.Rect(entries, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = Vector2.zero; });
            var vlg = entries.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) { var (a0, s0) = (vlg.childAlignment, vlg.spacing); _l.Do(() => { vlg.childAlignment = TextAnchor.UpperLeft; vlg.spacing = 6f; }, () => { if (vlg != null) { vlg.childAlignment = a0; vlg.spacing = s0; } }); }
            MenuEntries.Add(m, _l, t);
            // live design: "WORLD · DAY N" above the list, short notes beside items, a world card bottom right
            _head = Kit.Text(menu, "jatify-menuhead", "", Fonts.Mono, 12f, Theme.C(t.mute)); _l.Own(_head); _head.characterSpacing = 8f;
            _head.rectTransform.anchorMin = _head.rectTransform.anchorMax = _head.rectTransform.pivot = new Vector2(0f, 1f); _head.rectTransform.anchoredPosition = new Vector2(2f, 26f); _head.rectTransform.sizeDelta = new Vector2(500f, 18f);
            _l.Off(entries.Find("LastTimeSaved")?.GetComponent<Graphic>());   // in the world card instead (Off: vanilla re-activates it)
            BuildWorldCard(m.m_root, t);
            // the start screen's logo (Deep North, kept from the start screen), top left (user)
            var logo = Kit.Img(m.m_root, "jatify-logo", StartLogo.Sprite ?? Kit.Icon("Logo2_menu_highres"), Color.white, false); _l.Own(logo); logo.preserveAspect = true;
            var lr = logo.rectTransform; lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 1f); lr.anchoredPosition = new Vector2(106f, -70f); lr.sizeDelta = new Vector2(380f, 150f);
            StyleConfirm(m.m_root.Find("ExitConfirm"), t); StyleConfirm(m.m_root.Find("LogoutConfirm"), t);
            foreach (Transform e in entries)
            {
                _l.Hide(e.Find("LeftKnot")?.gameObject);   // hidden, not just invisible: they take layout space
                _l.Hide(e.Find("RightKnot")?.gameObject);
                var tx = e.Find("Text")?.GetComponent<TMP_Text>();
                if (tx == null) { var lone = e.GetComponent<TMP_Text>(); if (lone != null) _l.Txt(lone, Fonts.Mono, 13f, Theme.C(t.mute)); continue; }
                _l.Rect((RectTransform)e, r => r.pivot = new Vector2(0f, r.pivot.y));   // the list lays out before each entry fits its label: a left pivot keeps them in line whatever the width
                var al0 = tx.alignment; _l.Do(() => tx.alignment = TextAlignmentOptions.MidlineLeft, () => { if (tx != null) tx.alignment = al0; });   // vanilla centres each label
                bool quiet = e.name == "Logout" || e.name == "Exit";
                _l.Txt(tx, Fonts.Bold, quiet ? 26f : 40f, quiet ? Theme.C(t.dim) : Color.white);
                // vanilla's hover animation turns the text orange: ours goes green
                var anim = e.GetComponent<Animator>(); if (anim != null && anim.enabled) _l.Do(() => anim.enabled = false, () => { if (anim != null) anim.enabled = true; });
                var btn = e.GetComponent<Button>();
                if (btn != null)
                {
                    var (tg0, tr0, cb0) = (btn.targetGraphic, btn.transition, btn.colors);
                    var cb = ColorBlock.defaultColorBlock; cb.normalColor = Color.white; cb.highlightedColor = Theme.C(t.accent); cb.selectedColor = Color.white; cb.pressedColor = Theme.C(t.accent); cb.fadeDuration = 0.12f;
                    _l.Do(() => { btn.targetGraphic = tx; btn.transition = Selectable.Transition.ColorTint; btn.colors = cb; }, () => { if (btn != null) { btn.targetGraphic = tg0; btn.transition = tr0; btn.colors = cb0; } });
                }
                var hl = e.GetComponent<HorizontalLayoutGroup>(); if (hl != null) _l.Added(e.gameObject.AddComponent<MenuLift>()).Init(hl);
                var ol = tx.GetComponent<Outline>(); if (ol != null && ol.enabled) _l.Do(() => ol.enabled = false, () => { if (ol != null) ol.enabled = true; });
            }
        }

        private static TextMeshProUGUI _head, _cardTitle, _cardBody;
        private static float _nextTick;

        private static void BuildWorldCard(Transform root, ThemeData t)
        {
            var (card, _) = Kit.Box(root, "jatify-worldcard", Theme.C(t.glass), Theme.C(t.glassLine), 12); _l.Own(card);
            var rt = card.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f); rt.anchoredPosition = new Vector2(-60f, 150f); rt.sizeDelta = new Vector2(320f, 186f);
            var l = Kit.Text(rt, "Label", "WORLD", Fonts.Mono, 10.5f, Theme.C(t.mute)); l.characterSpacing = 8f; l.rectTransform.At(18, 16, 280, 14);
            _cardTitle = Kit.Text(rt, "Name", "", Fonts.Bold, 20f, Color.white); _cardTitle.rectTransform.At(18, 32, 284, 28); _cardTitle.overflowMode = TextOverflowModes.Ellipsis;
            _cardBody = Kit.Text(rt, "Rows", "", Fonts.Medium, 12.5f, Theme.C(t.mute)); _cardBody.richText = true; _cardBody.lineSpacing = 22f; _cardBody.rectTransform.At(18, 72, 284, 100);
            MenuEntries.Splash(rt, new Vector2(1f, 1f), new Vector2(-70f, 20f), true);   // on the card's top edge, its right end (user); it hides with the card
        }

        // Valheim's quit / log out dialogs: a glass box, the question, when you last saved, Cancel | Quit (red)
        // quit / log out confirm: floating text cascading right of the menu, like the Settings and Jatify UI pages (user:
        // no panel). A clear full-screen blocker keeps it modal; the question, a line on what happens, then the choices.
        private static void StyleConfirm(Transform conf, ThemeData t)
        {
            var dlg = conf != null ? conf.Find("dialog") as RectTransform : null; if (dlg == null) return;
            _l.Off(dlg.Find("border (1)")?.GetComponent<Graphic>()); _l.Off(dlg.Find("Image")?.GetComponent<Graphic>());
            _l.Rect((RectTransform)conf, r => { r.anchoredPosition = Vector2.zero; r.sizeDelta = Vector2.zero; });   // vanilla sits it 139 px up
            var block = Kit.Img(conf, "jatify-dim", null, new Color(0f, 0f, 0f, 0f)); _l.Own(block); block.rectTransform.Fill(); block.transform.SetAsFirstSibling(); block.raycastTarget = true;
            _l.Rect(dlg, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(420f, -300f); r.sizeDelta = new Vector2(520f, 260f); });
            bool quit = conf.name == "ExitConfirm";
            var head = Kit.Text(dlg, "jatify-head", quit ? "QUIT" : "LOG OUT", Fonts.Mono, 12f, Theme.C(t.mute)); _l.Own(head); head.characterSpacing = 8f;
            Place(head.rectTransform, 0f, 0f, 260f, 18f);
            // no knot over this prompt (user): it floats on the scene without a panel, the QUIT / LOG OUT line heads it
            TMP_Text q = dlg.Find("Exit")?.GetComponent<TMP_Text>() ?? dlg.Find("Quit")?.GetComponent<TMP_Text>();
            if (q != null) { _l.Txt(q, Fonts.Bold, 26f, Color.white); _l.Align(q, TextAlignmentOptions.TopLeft); _l.Rect(q.rectTransform, r => Place(r, 0f, 30f, 520f, 34f)); }
            var sub = Kit.Text(dlg, "jatify-sub", "", Fonts.Medium, 13f, Theme.C(t.dim)); _l.Own(sub); sub.textWrappingMode = TextWrappingModes.Normal;
            Place(sub.rectTransform, 0f, 70f, 440f, 40f);
            _confirmSubs[conf.name] = sub;
            Btn(dlg.Find("Button_yes"), quit ? "Quit" : "Log out", 120f, Theme.C(t.health), t);
            Btn(dlg.Find("Button_no"), "Cancel", 164f, new Color(1f, 1f, 1f, 0.78f), t);
        }

        private static void Place(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }

        private static readonly Dictionary<string, TextMeshProUGUI> _confirmSubs = new Dictionary<string, TextMeshProUGUI>();

        // a choice as menu text: no fill, our label, slides forward on hover like the menu entries
        private static void Btn(Transform b, string label, float y, Color color, ThemeData t)
        {
            if (b == null) return; var rt = (RectTransform)b;
            _l.Rect(rt, r => Place(r, 0f, y, 260f, 40f));
            foreach (var img in b.GetComponentsInChildren<Image>(true)) { var i = img; var c0 = i.color; _l.Do(() => i.color = Color.clear, () => { if (i != null) i.color = c0; }); }   // no fill; keeps its raycast
            var sel = b.GetComponent<Selectable>(); if (sel != null) { var tr0 = sel.transition; _l.Do(() => sel.transition = Selectable.Transition.None, () => { if (sel != null) sel.transition = tr0; }); }
            var tx = b.GetComponentInChildren<TMP_Text>(); _l.Off(tx);   // vanilla writes "No"/"Yes" back each time; our own label
            var lb = Kit.Text(b, "jatify-label", label, Fonts.Bold, 26f, color); _l.Own(lb); lb.alignment = TextAlignmentOptions.MidlineLeft; lb.rectTransform.Fill();
            _l.Added(b.gameObject.AddComponent<TextLift>()).Label = lb.rectTransform;
        }

        private static string Ago(Menu m)
        {
            var saved = m != null && m.m_root != null ? m.m_root.GetComponentsInChildren<TMP_Text>(true) : null;
            foreach (var x in saved ?? new TMP_Text[0]) if (x.name == "LastTimeSaved") return (x.text ?? "").Replace("Last save:", "Last save").Trim();
            return "";
        }

        private static bool Confirming() => _for != null && (_for.m_quitDialog != null && _for.m_quitDialog.gameObject.activeInHierarchy || _for.m_logoutDialog != null && _for.m_logoutDialog.gameObject.activeInHierarchy);

        // dynamic bits, twice a second while the menu shows
        internal static void Tick()
        {
            if (_cardTitle != null)
            {   // the world card belongs to the bare pause menu: a submenu (Settings, Mods, Jatify UI) takes the screen
                var card = _cardTitle.transform.parent.gameObject; bool bare = ConfigPage.Open == null && Settings.instance == null && !PlayersPage.Open && !Confirming();
                if (card.activeSelf != bare) card.SetActive(bare);
            }
            var m = _for; if (m == null || !Menu.IsVisible() || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            var zn = ZNet.instance; var env = EnvMan.instance;
            // the world name is null until a joined server has sent its world (it was the "menu failed" at every join)
            string world = (zn != null ? zn.GetWorldName() : null) ?? ""; int day = env != null ? env.GetDay() : 0;
            float frac = env != null ? env.GetDayFraction() : 0f; string clock = $"{(int)(frac * 24f):00}:{(int)(frac * 1440f) % 60:00}";
            int players = zn != null ? zn.GetNrOfPlayers() : 1;
            if (_head != null) _head.text = $"{world.ToUpperInvariant()} · DAY {day}";
            var ago = Ago(m);
            if (m.m_root.Find("Menu/MenuEntries") is RectTransform list) LayoutRebuilder.ForceRebuildLayoutImmediate(list);   // vanilla lays the list out before the labels change width
            if (_cardTitle != null) _cardTitle.text = world;
            if (_cardBody != null)
            {
                string Row(string k, string v) => $"{k}<pos=110><color=#FFFFFF>{v}</color>";
                var code = ZPlayFabMatchmaking.JoinCode;
                _cardBody.text = Row("Day", $"{day} · {clock}") + "\n" + Row("Players", $"{players} online") + "\n" + Row("Saved", ago.Replace("Last save", "").Trim()) + "\n" + Row("Mods", $"{BepInEx.Bootstrap.Chainloader.PluginInfos.Count} installed") + (string.IsNullOrEmpty(code) ? "" : "\n" + Row("Join code", code));
                int rows = string.IsNullOrEmpty(code) ? 4 : 5;
                ((RectTransform)_cardBody.transform.parent).sizeDelta = new Vector2(320f, 72f + rows * 22f + 14f);
            }
            foreach (var kv in _confirmSubs)
                if (kv.Value != null) kv.Value.text = (kv.Key == "ExitConfirm" ? "Your character and world are saved first." : "You go back to character select. The world keeps running for the others.") + (ago.Length > 0 ? " " + ago + "." : "");
        }

        internal static void Restore() { _l.Undo(); _for = null; _confirmSubs.Clear(); _head = _cardTitle = _cardBody = null; }
        internal static bool AppliedTo(Menu m) => _for == m && m != null;
    }

    // live design: a hovered (or keyboard-selected) entry slides forward 6 px and grows a touch.
    // Through the entry's own layout padding, so the list's layout doesn't snap it back.
    // hover: the label slides 6 px forward and grows 4 %, like the menu entries (MenuLift needs a layout group)
    internal class TextLift : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        internal RectTransform Label; private bool _over; private float _k;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => _over = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) => _over = false;
        private void OnDisable() { _over = false; _k = 0f; Apply(); }
        private void Update() { float k = Mathf.MoveTowards(_k, _over ? 1f : 0f, Time.unscaledDeltaTime / 0.14f); if (k != _k) { _k = k; Apply(); } }
        private void Apply() { if (Label == null) return; float e = _k * _k * (3f - 2f * _k); Label.anchoredPosition = new Vector2(6f * e, 0f); Label.localScale = Vector3.one * (1f + 0.04f * e); }
    }

    internal class MenuLift : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        private HorizontalLayoutGroup _hl; private int _pad0; private bool _over; private float _k;
        internal void Init(HorizontalLayoutGroup hl) { _hl = hl; _pad0 = hl.padding.left; }
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => _over = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) => _over = false;
        private void OnDisable() { _over = false; _k = 0f; Apply(); }
        private void OnDestroy() { if (_hl != null) { _hl.padding.left = _pad0; transform.localScale = Vector3.one; LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform); } }
        private void Update()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            bool on = _over || (es != null && es.currentSelectedGameObject == gameObject && !ZInput.IsMouseActive());
            float k = Mathf.MoveTowards(_k, on ? 1f : 0f, Time.unscaledDeltaTime / 0.14f);
            if (k != _k) { _k = k; Apply(); }
        }
        private void Apply()
        {
            if (_hl == null) return;
            float e = _k * _k * (3f - 2f * _k);   // smoothstep
            _hl.padding.left = _pad0 + Mathf.RoundToInt(6f * e);
            transform.localScale = Vector3.one * (1f + 0.04f * e);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
