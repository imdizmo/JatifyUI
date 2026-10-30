using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // Find servers (#20, user: "the old view"): Valheim's own server browser (ServerListGui on StartGame/Panel/JoinPanel)
    // keeps doing everything (community lists, crossplay, filter, join); this only gives it Campfire's look while it is
    // open from our screen: a glass panel, pill tabs, flat rows like the favourites list, a search field, quiet buttons
    // and a green Connect. The warning box becomes one muted line. Every change is ledger-tracked and undone when the
    // panel closes (Back, joining) or the module unloads. Rows and tabs Valheim makes later are styled as they appear.
    internal static class ServerBrowser
    {
        private static readonly UI.Ledger _l = new UI.Ledger();
        private static readonly HashSet<int> _done = new HashSet<int>();
        private static bool _want;
        private static Transform _join;
        private static ServerListGui _gui;
        private static readonly List<(Button b, TMP_Text t, Image img)> _tabs = new List<(Button, TMP_Text, Image)>();
        private static readonly List<(GameObject sel, TMP_Text name)> _rows = new List<(GameObject, TMP_Text)>();
        private static Button _connect; private static TMP_Text _connectText, _topic;
        private static float _next;
        private static Sprite _pill;
        private static Button _hoverTab;
        // after Valheim's own Update has written its title back
        private sealed class KeepText : MonoBehaviour
        {
            internal string T; private TMP_Text _x;
            private void LateUpdate() { if (_x == null) _x = GetComponent<TMP_Text>(); if (_x != null && _x.text != T) _x.text = T; }
        }
        // Valheim's tab handler draws its gold frame on the picked tab every frame: our pill goes back on after it, each frame
        private sealed class TabHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            internal Button B; internal Image I;
            private void LateUpdate()
            {
                if (B == null || I == null) return;
                if (I.sprite != _pill) I.sprite = _pill;
                if (I.overrideSprite != _pill) I.overrideSprite = null;   // the stone "picked" tab Valheim swaps in
                I.color = !B.interactable ? White : _hoverTab == B ? C("raised") * 1.25f : C("raised");
            }
            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => _hoverTab = B;
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { if (_hoverTab == B) _hoverTab = null; }
        }

        // Fejd.ServerBrowser: the panel is about to open (after Valheim's EULA, if it asks first)
        internal static void Opening() => _want = true;

        internal static void Tick()
        {
            if (_join != null && !_join.gameObject.activeInHierarchy) { Restore(); _want = false; return; }
            if (!_want || !Module.Screen || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            if (_join == null) { var j = Find(); if (j == null || !j.gameObject.activeInHierarchy) return; Style(j); }
            foreach (Transform c in _join) if (c.name.StartsWith("ServerListTab") && c.gameObject.activeSelf && _done.Add(c.GetInstanceID())) Tab(c);
            if (_gui != null && _gui.m_serverListRoot != null)
                foreach (Transform c in _gui.m_serverListRoot) if (c.gameObject.activeSelf && _done.Add(c.GetInstanceID())) Row(c);
            // states Valheim changes: the picked tab is the one that can't be clicked; the picked row has "selected" on
            foreach (var (b, t, img) in _tabs)
            {
                if (b == null || t == null) continue;
                t.color = b.interactable ? C("dim") : SegInk;
            }
            if (_connect != null && _connectText != null) _connectText.color = _connect.interactable ? Ink : C("mute");
        }

        private static Transform Find()
        {
            var f = FejdStartup.instance; if (f == null || f.m_startGamePanel == null) return null;
            return f.m_startGamePanel.transform.Find("Panel/JoinPanel") ?? (f.m_serverListPanel != null ? f.m_serverListPanel.transform : null);
        }

        internal static void Restore()
        {
            _l.Undo(); _done.Clear(); _tabs.Clear(); _rows.Clear();
            _join = null; _gui = null; _connect = null; _connectText = null; _topic = null; _hoverTab = null;
        }

        private static void Style(Transform join)
        {
            _join = join; _gui = join.GetComponent<ServerListGui>();
            var panel = join.parent;   // StartGame/Panel: the Start Game / Join Game tabs and the frame they share
            if (panel != null)
            {
                _l.Hide(panel.Find("Host")?.gameObject); _l.Hide(panel.Find("Join")?.gameObject);   // Start Game leads to Valheim's world list: worlds are on our screen
                foreach (Transform c in panel) if (c.name.StartsWith("bkg")) _l.Off(c.GetComponent<Graphic>());
            }
            foreach (Transform c in join)
            {
                var img = c.GetComponent<Image>();
                if (c.name != "bkg" || img == null) continue;
                var r = (RectTransform)c;
                if (r.sizeDelta.y < 10f) _l.Off(img);   // the braid under the tabs
                else { _l.Img(img, JatifyLook.Rounded(14), C("glass")); Line(c, C("glassLine"), 14); }
            }
            // Valheim's wood panel behind the list is off in this scene: our own glass card behind everything, title to buttons
            // (user: the list stood straight on the snow)
            var card = _l.Own(JatifyLook.Img(join, JatifyLook.Rounded(14), C("glass"), "jatify-card")); card.type = Image.Type.Sliced; card.raycastTarget = true;
            var cr = card.rectTransform; cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = new Vector2(-24f, -40f); cr.offsetMax = new Vector2(24f, 64f);
            card.transform.SetAsFirstSibling(); Line(card.transform, C("glassLine"), 14);
            // Valheim writes "Select Server" back into its title: ours sits on top of it, its own faded out
            var topic = join.Find("topic");
            if (topic?.GetComponent<TMP_Text>() is TMP_Text tt)
            {
                _l.Txt(tt, JatifyLook.Font("title"), 30f, White); var was = tt.text;
                var keep = topic.gameObject.AddComponent<KeepText>(); keep.T = "Find servers";
                _l.Do(() => { }, () => { if (keep != null) Object.Destroy(keep); if (tt != null) tt.text = was; });
            }
            _l.Txt(join.Find("serverCount")?.GetComponent<TMP_Text>(), JatifyLook.Font("mono"), 13f, C("mute"));

            // the list: a dark well; the scroll bar thin and quiet
            _l.Img(join.Find("ServerList")?.GetComponent<Image>(), JatifyLook.Rounded(10), new Color(0f, 0f, 0f, 0.22f));
            var scroll = join.Find("ServerListScroll");
            if (scroll != null)
            {
                _l.Img(scroll.GetComponent<Image>(), JatifyLook.Rounded(4), new Color(1f, 1f, 1f, 0.05f));
                _l.Img(scroll.Find("Sliding Area/Handle")?.GetComponent<Image>(), JatifyLook.Rounded(4), new Color(1f, 1f, 1f, 0.28f));
            }

            Field(join.Find("FilterField"));
            foreach (var n in new[] { "Refresh", "FavoriteButton", "RemoveButton", "MoveUpButton", "MoveDownButton" }) Quiet(join.Find(n), true);
            Quiet(join.Find("Add server"), false);
            Quiet(join.Find("Back"), false);
            var connect = join.Find("Join");
            if (connect != null)
            {
                _connect = connect.GetComponent<Button>();
                Tint(connect, C("accent"), C("raised2"), JatifyLook.Rounded(10));
                _connectText = connect.Find("Text")?.GetComponent<TMP_Text>(); _l.Txt(_connectText, JatifyLook.Font("bold"), 16f, Ink);
                _l.Rect((RectTransform)connect, r => r.sizeDelta = new Vector2(r.sizeDelta.x, 48f));   // 70 tall in vanilla: as tall as Back now
            }

            // Valheim's warning box beside the panel: one muted line under the buttons, as on our screen
            var help = join.Find("Server help");
            if (help != null)
            {
                _l.Hide(help.gameObject);
                var note = _l.Own(JatifyLook.Text(join, "Joining an online server puts your character at risk: a separate character is safer.", "medium", 12.5f, C("mute"), TextAlignmentOptions.Center));
                note.name = "jatify-note"; At(note.rectTransform, 0.5f, 0f, 0f, -6f, 540f, 20f);
            }
        }

        // a pill tab: raised, lit on hover, white while picked (Valheim makes the picked one unclickable: its disabled tint)
        private static void Tab(Transform c)
        {
            var b = c.GetComponent<Button>(); var t = c.GetComponentInChildren<TMP_Text>(); if (b == null) return;
            var img = c.GetComponent<Image>();
            _l.Img(img, _pill = JatifyLook.Rounded(8), White);
            var cb = ColorBlock.defaultColorBlock; cb.normalColor = C("raised"); cb.highlightedColor = C("raised") * 1.25f; cb.pressedColor = C("raised2");
            cb.selectedColor = C("raised"); cb.disabledColor = White; cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            var (tr0, tg0) = (b.transition, b.targetGraphic); _l.Do(() => { b.transition = Selectable.Transition.None; img.CrossFadeColor(Color.white, 0f, true, true); }, () => { if (b != null) { b.transition = tr0; b.targetGraphic = tg0; } });   // the tint a colour transition left on the renderer goes too
            var hv = c.gameObject.AddComponent<TabHover>(); hv.B = b; hv.I = img; _l.Do(() => { }, () => { if (hv != null) Object.Destroy(hv); });
            _l.Txt(t, JatifyLook.Font("medium"), 14f, C("dim"));
            NoTinters(c);
            _tabs.Add((b, t, img));
        }

        // a server row like the favourites list: flat, a raised fill on hover, soft green and a green name when picked
        private static void Row(Transform c)
        {
            var b = c.GetComponent<Button>(); var bkg = c.Find("bkg")?.GetComponent<Image>();
            if (b != null && bkg != null)
            {
                _l.Img(bkg, JatifyLook.Rounded(8), White);
                var cb = ColorBlock.defaultColorBlock; cb.normalColor = NoColor; cb.highlightedColor = C("raised"); cb.pressedColor = C("raised2"); cb.selectedColor = NoColor; cb.fadeDuration = 0.06f;
                Transition(b, bkg, cb);
            }
            var sel = c.Find("selected");
            _l.Img(sel?.GetComponent<Image>(), JatifyLook.Rounded(8), C("accentSoft"));
            var name = c.Find("name")?.GetComponent<TMP_Text>() ?? c.GetComponentInChildren<TMP_Text>();
            _l.Txt(name, JatifyLook.Font("bold"), 14.5f, White);
            _l.Txt(c.Find("modifiers")?.GetComponent<TMP_Text>(), JatifyLook.Font("medium"), 12f, C("mute"));
            _l.Txt(c.Find("version")?.GetComponent<TMP_Text>(), JatifyLook.Font("mono"), 12f, C("mute"));
            _l.Txt(c.Find("players")?.GetComponent<TMP_Text>(), JatifyLook.Font("medium"), 13f, C("dim"));
            foreach (var n in new[] { "crossplay", "Private", "PVP", "UPNP" }) Dim(c.Find(n), 0.35f);
            Dim(c.Find("status"), 0.5f);   // Valheim's state colour, quieter
            if (sel != null) _rows.Add((sel.gameObject, name));
        }

        // the filter: a raised field with a 1 px line, our text
        private static void Field(Transform f)
        {
            if (f == null) return;
            _l.Img(f.GetComponent<Image>(), JatifyLook.Rounded(9), C("raised")); Line(f, LineSoft, 9);
            foreach (var x in f.GetComponentsInChildren<TMP_Text>(true))
            {
                _l.Txt(x, JatifyLook.Font("medium"), 14f, x.name == "Placeholder" ? C("mute") : White);
                _l.Align(x, TextAlignmentOptions.MidlineLeft);
                if (x.name == "Placeholder") { var tx = x; var was = tx.text; _l.Do(() => tx.text = "Search servers", () => { if (tx != null) tx.text = was; }); }
            }
        }

        // the quieter buttons (.sbtn): raised with a line, our label; icon buttons keep their picture, dimmed
        private static void Quiet(Transform b, bool icon)
        {
            if (b == null) return;
            Tint(b, C("raised"), C("raised2"), JatifyLook.Rounded(8)); Line(b, C("line2"), 8); NoTinters(b);
            if (icon) { var i = b.Find("Image")?.GetComponent<Image>(); if (i != null) TintImg(i, C("dim")); }
            else _l.Txt(b.Find("Text")?.GetComponent<TMP_Text>(), JatifyLook.Font("medium"), 14f, White);
        }

        private static void Tint(Transform b, Color fill, Color off, Sprite s)
        {
            var btn = b.GetComponent<Button>(); var img = b.GetComponent<Image>(); if (img == null) return;
            _l.Img(img, s, White);
            if (btn == null) { var c0 = img.color; _l.Do(() => img.color = fill, () => { if (img != null) img.color = c0; }); return; }
            var cb = ColorBlock.defaultColorBlock; cb.normalColor = fill; cb.highlightedColor = fill * 1.15f; cb.pressedColor = fill * 0.85f; cb.selectedColor = fill; cb.disabledColor = off; cb.fadeDuration = 0.08f;
            Transition(btn, img, cb);
        }

        private static void Transition(Button b, Graphic target, ColorBlock cb)
        {
            var (tg0, tr0, cb0, ss0) = (b.targetGraphic, b.transition, b.colors, b.spriteState);
            _l.Do(() => { b.targetGraphic = target; b.transition = Selectable.Transition.ColorTint; b.colors = cb; }, () => { if (b != null) { b.targetGraphic = tg0; b.transition = tr0; b.colors = cb0; b.spriteState = ss0; } });
        }

        // Valheim's ButtonTextColor / ButtonImageColor repaint every frame (gold when picked or hovered): off while we style
        private static void NoTinters(Transform b)
        {
            foreach (var m in b.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (m == null || (!(m is ButtonTextColor) && !(m is ButtonImageColor)) || !m.enabled) continue;
                var mb = m; _l.Do(() => mb.enabled = false, () => { if (mb != null) mb.enabled = true; });
            }
        }

        // dimmed whatever colour Valheim gives it (a row's state icons, set again on each refresh)
        private static void Dim(Transform t, float a)
        {
            if (t == null) return;
            var g = t.GetComponent<CanvasGroup>(); if (g != null) return;
            g = t.gameObject.AddComponent<CanvasGroup>(); g.alpha = a; g.blocksRaycasts = true; var gg = g;
            _l.Do(() => { }, () => { if (gg != null) Object.Destroy(gg); });
        }

        private static void TintImg(Image i, Color c) { var c0 = i.color; c.a = c0.a; _l.Do(() => i.color = c, () => { if (i != null) i.color = c0; }); }

        // a 1 px outline over a vanilla box (ours, removed on undo)
        private static void Line(Transform box, Color c, int radius)
        {
            var ring = JatifyLook.Ring(radius); if (ring == null) return;
            var o = _l.Own(JatifyLook.Img(box, ring, c, "jatify-line")); o.type = Image.Type.Sliced; Fill(o.rectTransform);
        }
    }
}
