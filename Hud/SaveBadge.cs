using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Saving, as a banner under the compass (user): Valheim's own save icon, small, pulsing, with
    // "Saving world..." while it saves, then "World saved" for a moment. Same band as our other banners
    // (fading to the sides, green hairlines). Valheim's big corner icon stays hidden (VanillaHud) and its
    // "World saved" message is folded into this (Notices). A bad connection shows the same way, in ember.
    internal static class SaveBadge
    {
        private const float Y = -124f, Done = 2.2f;   // under the compass and the dev badge
        private static RectTransform _root; private static CanvasGroup _cg;
        private static Image _icon; private static TextMeshProUGUI _text; private static Image[] _lines;
        private static bool _wasSaving; private static float _savedAt = -99f, _alpha;
        internal static bool Saved() { _savedAt = Time.unscaledTime; return true; }   // Valheim's "World saved" message

        internal static void Tick()
        {
            var h = Hud.instance; if (h == null || Root.Canvas == null) { Destroy(); return; }
            if (_root == null) Build(h);
            bool saving = h.m_saveIcon != null && h.m_saveIcon.activeSelf;
            bool bad = !saving && h.m_badConnectionIcon != null && h.m_badConnectionIcon.activeSelf;
            if (_wasSaving && !saving) _savedAt = Time.unscaledTime;
            _wasSaving = saving;
            bool done = !saving && !bad && Time.unscaledTime - _savedAt < Done;
            var t = Plugin.Instance.ThemeData;
            string msg = saving ? "Saving world…" : bad ? "Connection problem" : done ? "World saved" : null;
            if (msg != null && _text.text != msg)
            {
                _text.text = msg;
                _icon.sprite = bad && h.m_badConnectionIcon.GetComponent<Image>() != null ? h.m_badConnectionIcon.GetComponent<Image>().sprite : h.m_saveIconImage.sprite;
                var lc = Theme.C(bad ? t.ember : t.accent);
                _lines[0].color = new Color(lc.r, lc.g, lc.b, 0.7f); _lines[1].color = new Color(lc.r, lc.g, lc.b, 0.35f);
                float tw = Kit.W(_text); float gx = 260f - (tw + 34f) / 2f;
                _icon.rectTransform.At(gx, 11, 24, 24); _text.rectTransform.At(gx + 34f, 0, tw + 4f, 46);
            }
            _alpha = Mathf.MoveTowards(_alpha, msg != null ? 1f : 0f, Time.unscaledDeltaTime * (msg != null ? 5f : 2f));
            _cg.alpha = _alpha;
            if (msg != null) { var c = _icon.color; c.a = saving ? 0.4f + 0.6f * Mathf.PingPong(Time.unscaledTime * 2f, 1f) : 1f; _icon.color = c; }   // pulses while saving, like Valheim's
        }

        private static void Build(Hud h)
        {
            _root = Kit.Node(Root.Canvas.transform, "jatify-save");
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f); _root.anchoredPosition = new Vector2(0f, Y); _root.sizeDelta = new Vector2(520f, 46f);
            Notices.OnTop(_root);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
            var band = Kit.Img(_root, "Band", SkillBanner.Fade(), new Color(0.04f, 0.045f, 0.05f, 0.85f), false); band.rectTransform.At(0, 0, 520, 46);
            _lines = new Image[2];
            for (int i = 0; i < 2; i++) { _lines[i] = Kit.Img(_root, "Line", SkillBanner.Fade(), Color.white, false); _lines[i].rectTransform.At(40, i == 0 ? 0 : 45, 440, 1); }
            _icon = Kit.Img(_root, "Icon", h.m_saveIconImage != null ? h.m_saveIconImage.sprite : null, Color.white, false); _icon.preserveAspect = true;
            _text = Kit.Text(_root, "T", "", Fonts.Medium, 15f, Color.white); _text.alignment = TextAlignmentOptions.MidlineLeft;
            _alpha = 0f; _wasSaving = false;
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; }
    }
}
