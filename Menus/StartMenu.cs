using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Valheim's title menu in Jatify's look (live design, Start screen). The logo stays where the game has it (user); the
    // entries become large Norse text centred under it (green and a little larger on hover) under Valheim's own braid
    // divider (user); "Mods" and "Jatify UI" join them as in the pause menu; the small links and the merch panel stay
    // Valheim's own (user); the modded note and the version sit in a glass card top right. Vanilla's buttons and
    // logic throughout; everything is ledger-tracked, so unloading puts the title screen back.
    internal static class StartMenu
    {
        private static readonly Ledger _l = new Ledger();
        private static FejdStartup _for;
        internal static BepInEx.Configuration.ConfigEntry<bool> On;   // Start Screen / TitleMenu

        internal static void Tick(ThemeData t)
        {
            var fs = FejdStartup.instance;
            if (fs == null || fs.m_mainMenu == null || !On.Value) { if (_for != null) Restore(); return; }   // off: Valheim's title menu (put back live)
            if (_for != fs) Apply(fs, t);
            else if (_card != null && !_fitted && _card.gameObject.activeInHierarchy) Fit();
        }

        internal static void Restore() { _l.Undo(); _for = null; _card = null; }

        private static void Apply(FejdStartup fs, ThemeData t)
        {
            Restore(); _for = fs;
            var menu = fs.m_mainMenu.transform;
            // ---- the entries ----
            var list = menu.Find("MenuList");
            _l.Off(list?.Find("darken")?.GetComponent<Graphic>());
            var entries = list?.Find("MenuEntries");
            if (entries != null)
            {
                var vlg = entries.GetComponent<VerticalLayoutGroup>();   // rows keep vanilla's ~46 px height whatever the font size: pull them together so Quit fits
                if (vlg != null) { var s0 = vlg.spacing; _l.Do(() => vlg.spacing = -8f, () => { if (vlg != null) vlg.spacing = s0; }); }
                MenuEntries.AddTo(entries, _l, t, whatsNew: false);   // Mods, Jatify UI: as in the pause menu (What's new sits bottom left here)
                foreach (Transform e in entries) Entry(e, t);
            }
            // bottom left (Changelog, the licence, Player.log) and the merch panel stay Valheim's own (user), but the links line
            // up on the left: vanilla centres each in a 300 px row, so they stagger
            var links = menu.Find("BottomLeftButtons");
            MenuEntries.AddLinks(links, _l);
            if (links != null) foreach (Transform b in links)
                {
                    var h = b.GetComponent<HorizontalLayoutGroup>(); if (h == null) continue; var a0 = h.childAlignment;
                    _l.Do(() => h.childAlignment = TextAnchor.MiddleLeft, () => { if (h != null) h.childAlignment = a0; });
                }
            // ---- top right: the modded note and the version, in a card ----
            var modded = menu.Find("modded_text")?.GetComponent<TMP_Text>(); var version = menu.Find("version_text")?.GetComponent<TMP_Text>();
            if (modded != null || version != null) Card(menu, modded, version, t);
        }

        // an entry: large Norse, centred, white (Quit quieter), green on hover, no braid knots, no outline
        private static void Entry(Transform e, ThemeData t)
        {
            var tx = e.Find("Text")?.GetComponent<TMP_Text>(); if (tx == null) return;
            _l.Hide(e.Find("LeftKnot")?.gameObject); _l.Hide(e.Find("RightKnot")?.gameObject);
            bool quiet = e.name == "Exit";
            _l.Txt(tx, Fonts.Norse, quiet ? 22f : 28f, quiet ? Theme.C(t.dim) : Color.white);   // user: 44, then 34, too big (34 still pushed Quit off screen)
            _l.Align(tx, TextAlignmentOptions.Center);
            var ol = tx.GetComponent<Outline>(); if (ol != null && ol.enabled) _l.Do(() => ol.enabled = false, () => { if (ol != null) ol.enabled = true; });
            var anim = e.GetComponent<Animator>(); if (anim != null && anim.enabled) _l.Do(() => anim.enabled = false, () => { if (anim != null) anim.enabled = true; });
            Tint(e.GetComponent<Button>(), tx, quiet ? Theme.C(t.dim) : Color.white, Theme.C(t.accent));
            _l.Added(e.gameObject.AddComponent<TitleLift>());
        }

        private static void Tint(Button btn, Graphic target, Color normal, Color over)
        {
            if (btn == null) return;
            var (tg0, tr0, cb0) = (btn.targetGraphic, btn.transition, btn.colors);
            var cb = ColorBlock.defaultColorBlock; cb.normalColor = Color.white; cb.highlightedColor = over == Color.white ? new Color(1.6f, 1.6f, 1.6f) : over; cb.selectedColor = Color.white; cb.pressedColor = over; cb.colorMultiplier = over == Color.white ? 2f : 1f; cb.fadeDuration = 0.12f;
            _l.Do(() => { btn.targetGraphic = target; btn.transition = Selectable.Transition.ColorTint; btn.colors = cb; }, () => { if (btn != null) { btn.targetGraphic = tg0; btn.transition = tr0; btn.colors = cb0; } });
            target.color = normal;
        }

        private static RectTransform _card; private static TMP_Text _body, _ver; private static bool _fitted;

        private static void Card(Transform menu, TMP_Text modded, TMP_Text version, ThemeData t)
        {
            var (card, _) = Kit.Box(menu, "jatify-modcard", Theme.C(t.glass), Theme.C(t.glassLine), 12); _l.Own(card);
            _card = card.rectTransform; _body = null; _ver = null; _fitted = false;
            var rt = card.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f); rt.anchoredPosition = new Vector2(-40f, -34f);   // top right (user)
            var label = Kit.Text(rt, "Label", "MODDED GAME", Fonts.Mono, 10.5f, Theme.C(t.stamina)); label.characterSpacing = 8f; label.rectTransform.At(16, 14, 330, 16);
            if (modded != null)
            {
                _l.Off(modded);
                var body = Kit.Text(rt, "Body", modded.text, Fonts.Medium, 12.5f, Theme.C(t.dim)); body.textWrappingMode = TextWrappingModes.Normal;
                _body = body;
            }
            if (version != null)
            {
                _l.Off(version);
                _ver = Kit.Text(rt, "Version", version.text, Fonts.Mono, 11.5f, Theme.C(t.mute));
            }
            if (_card.gameObject.activeInHierarchy) Fit();   // else once the title menu shows (Tick): text measured while hidden comes out empty (user: the card shrank)
        }

        // the body laid out at its width and measured, the version under it, the card around them
        private static void Fit()
        {
            float y = 34f;
            if (_body != null)
            {
                _body.rectTransform.At(16, y, 328, 400f); _body.ForceMeshUpdate();
                float h = Mathf.Ceil(_body.preferredHeight); _body.rectTransform.At(16, y, 328, h + 2f); y += h + 8f;
            }
            if (_ver != null) { _ver.rectTransform.At(16, y, 328, 18); y += 20f; }
            _card.sizeDelta = new Vector2(360f, y + 14f);
            _fitted = _body == null || _body.preferredHeight > 1f;
        }
    }

    // the title menu's hover: a little larger (centred entries, so no sideways nudge like the pause menu's MenuLift)
    internal class TitleLift : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        private bool _over; private float _k;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) => _over = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) => _over = false;
        private void OnDisable() { _over = false; _k = 0f; transform.localScale = Vector3.one; }
        private void OnDestroy() => transform.localScale = Vector3.one;
        private void Update()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            bool on = _over || (es != null && es.currentSelectedGameObject == gameObject && !ZInput.IsMouseActive());
            float k = Mathf.MoveTowards(_k, on ? 1f : 0f, Time.unscaledDeltaTime / 0.14f);
            if (k == _k) return; _k = k;
            transform.localScale = Vector3.one * (1f + 0.05f * (k * k * (3f - 2f * k)));
        }
    }
}
