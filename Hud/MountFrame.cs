using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Riding (detail pass, approved): a WoW-style unit frame left of our health and stamina bars (user), instead of Valheim's mount bars (VanillaHud
    // hides those; they sat on top of ours). A round portrait (the creature's trophy, else its first letter) in a green ring with a
    // level badge holding Valheim's stars, the name in Norse, Hungry (amber) or Fed (green), health and stamina with cur / max.
    // Slides in on mounting, out on dismounting. Texts are rewritten only when what they show changes.
    internal sealed class MountFrame
    {
        private const float W = 270f, H = 80f, X = 47f - 12f - W, Top = 66f - H;   // in the dock: 12 px left of the HP bar (47 in), its foot level with the ST bar's (66 down), above the tools bar (110 down)
        private readonly RectTransform _root, _badge, _hpFill, _stFill;
        private readonly CanvasGroup _group;
        private readonly Image _pic; private readonly Image[] _stars = new Image[2];
        private readonly TextMeshProUGUI _name, _state, _letter, _hp, _st;
        private readonly ThemeData _t;
        private Character _for; private int _hpC = -1, _hpM = -1, _stC = -1, _stM = -1, _hungry = -1; private float _a;

        internal MountFrame(Transform parent, ThemeData t)
        {
            _t = t;
            _root = Kit.Node(parent, "MountFrame").At(X, Top, W, H);
            _group = _root.gameObject.AddComponent<CanvasGroup>(); _group.blocksRaycasts = false; _group.alpha = 0f;
            var (box, _) = Kit.Box(_root, "Frame", new Color(0.078f, 0.078f, 0.086f, 0.82f), new Color(1f, 1f, 1f, 0.16f), 12); box.rectTransform.Fill();
            // portrait: 56 px, a 2 px green ring
            var po = Kit.Node(_root, "Portrait").At(12f, 12f, 56f, 56f);
            Kit.Img(po, "Ring", Kit.Circle(), Theme.C(t.accent), false).rectTransform.Fill(-1f);
            Kit.Img(po, "Fill", Kit.Circle(), new Color32(0x3A, 0x28, 0x18, 0xFF), false).rectTransform.Fill(1.5f);   // design: radial #6b4a2c into #2a1c10; ponytail: flat, between the two
            _pic = Kit.Img(po, "Pic", null, Color.white, false); _pic.preserveAspect = true; _pic.rectTransform.Fill(8f);
            _letter = Kit.Text(po, "Letter", "", Fonts.Norse, 22f, Color.white); _letter.alignment = TextAlignmentOptions.Center; _letter.rectTransform.Fill();
            // level badge on the portrait's lower right: Valheim's star sprites, one per star
            var (bl, _) = Kit.Box(_root, "Level", Theme.C(t.raised), Theme.C(t.line2), 9); _badge = bl.rectTransform;
            for (int i = 0; i < 2; i++)
            {
                var o = Kit.Img(_badge, "Star", Kit.Icon("craft_icon_32"), Theme.C(t.stamina), false); o.preserveAspect = true; o.rectTransform.At(4f + i * 12f, 3f, 12f, 12f);
                var inner = Kit.Img(o.rectTransform, "Inner", Kit.Icon("craft_icon"), Theme.C(t.stamina), false); inner.preserveAspect = true; inner.rectTransform.Fill(1.2f);
                _stars[i] = o;
            }
            const float cx = 12f + 56f + 10f, cw = W - cx - 12f;
            _name = Kit.Text(_root, "Name", "", Fonts.Norse, 17f, Color.white); _name.characterSpacing = 3f; _name.alignment = TextAlignmentOptions.MidlineLeft; _name.rectTransform.At(cx, 10f, cw, 20f);
            _state = Kit.Text(_root, "State", "", Fonts.Medium, 11f, Theme.C(t.stamina)); _state.alignment = TextAlignmentOptions.MidlineLeft; _state.rectTransform.At(cx, 11f, 80f, 20f);
            _hpFill = Bar(_root, cx, 36f, cw, Theme.C(t.health));
            _stFill = Bar(_root, cx, 49f, cw, Theme.C(t.stamina));
            _hp = Kit.Text(_root, "HP", "", Fonts.Mono, 10.5f, new Color(1f, 1f, 1f, 0.9f)); _hp.alignment = TextAlignmentOptions.MidlineLeft; _hp.rectTransform.At(cx, 58f, cw / 2f, 14f);
            _st = Kit.Text(_root, "ST", "", Fonts.Mono, 10.5f, new Color(1f, 1f, 1f, 0.9f)); _st.alignment = TextAlignmentOptions.MidlineRight; _st.rectTransform.At(cx + cw / 2f, 58f, cw / 2f, 14f);
            _root.gameObject.SetActive(false);
        }

        // an 8 px bar on a dark track (design .unit .bar); returns the fill, sized by its right anchor
        private static RectTransform Bar(RectTransform parent, float x, float y, float w, Color c)
        {
            var track = Kit.Panel(parent, "Track", new Color(0f, 0f, 0f, 0.55f), 4f).rectTransform.At(x, y, w, 8f);
            var f = Kit.Panel(track, "Fill", c, 4f).rectTransform; f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one; f.offsetMin = f.offsetMax = Vector2.zero;
            return f;
        }

        internal void Tick(Player p)
        {
            var sadle = p != null ? p.GetDoodadController() as Sadle : null;
            var c = sadle != null ? sadle.GetCharacter() : null;
            if (c != null && c != _for) Show(c);
            float target = c != null ? 1f : 0f;
            if (_a != target)
            {
                _a = Mathf.MoveTowards(_a, target, Time.unscaledDeltaTime * 4f);
                if (_root.gameObject.activeSelf != _a > 0f) _root.gameObject.SetActive(_a > 0f);
                float e = 1f - (1f - _a) * (1f - _a);
                _group.alpha = e; _root.anchoredPosition = new Vector2(X - (1f - e) * 60f, -Top);   // slides in from the left
                if (_a <= 0f) _for = null;
            }
            if (c == null) return;
            Num(_hp, c.GetHealth(), c.GetMaxHealth(), ref _hpC, ref _hpM, _hpFill);
            Num(_st, sadle.GetStamina(), sadle.GetMaxStamina(), ref _stC, ref _stM, _stFill);
            var tame = sadle.GetTameable(); int hungry = tame == null ? -1 : tame.IsHungry() ? 1 : 0;
            if (hungry != _hungry)
            {
                _hungry = hungry; _state.text = hungry < 0 ? "" : hungry == 1 ? "Hungry" : "Fed"; _state.color = Theme.C(hungry == 1 ? _t.stamina : _t.accent);
            }
        }

        private static void Num(TMP_Text t, float cur, float max, ref int shownC, ref int shownM, RectTransform fill)
        {
            fill.anchorMax = new Vector2(max > 0f ? Mathf.Clamp01(cur / max) : 0f, 1f);
            int c = Mathf.CeilToInt(cur), m = Mathf.CeilToInt(max);
            if (c != shownC || m != shownM) { shownC = c; shownM = m; t.text = c + " / " + m; }
        }

        // a new mount: its portrait, name and stars
        private void Show(Character c)
        {
            _for = c; _hpC = _hpM = _stC = _stM = _hungry = -1;
            _name.font = _letter.font = Fonts.Norse; var sh = Fonts.Shadowed(Fonts.Norse); if (sh != null) _name.fontSharedMaterial = sh;   // here, not when built: the HUD can be built at boot, before the game's fonts are loaded
            var name = Kit.Plain(Localization.instance.Localize(c.GetHoverName()));   // a tamed name is the player's own text
            _name.text = name;
            float nw = Mathf.Min(Mathf.Ceil(Kit.W(_name)), 120f);
            _state.rectTransform.At(_name.rectTransform.anchoredPosition.x + nw + 8f, 11f, 80f, 20f);
            var trophy = EnemyBars.Trophy(c); _pic.sprite = trophy; _pic.enabled = trophy != null;
            _letter.text = trophy == null && name.Length > 0 ? name.Substring(0, 1) : "";
            int stars = Mathf.Clamp(c.GetLevel() - 1, 0, 2);
            _badge.gameObject.SetActive(stars > 0);
            for (int i = 0; i < 2; i++) _stars[i].gameObject.SetActive(i < stars);
            float bw = 8f + stars * 12f; _badge.At(12f + 56f + 4f - bw, 12f + 56f + 4f - 18f, bw, 18f);   // 4 px past the portrait's corner (design: bottom -4, right -4)
        }

        internal void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); }
    }
}
