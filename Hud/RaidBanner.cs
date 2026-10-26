using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The raid warning (Valheim's event banner, which VanillaHud hides), from the live design: under
    // the compass, "RAID" between two swords, the event's name in Valheim's own font (user: the game's Norse lettering),
    // the time left as a thin bar, a slow orange glow. Fades in once the raid has run 3 s; hidden while a boss bar shows.
    internal sealed class RaidBanner
    {
        private const float H = 77f, Top = 84f;
        private readonly RectTransform _root, _bar, _fill;
        private readonly CanvasGroup _group;
        private readonly Image _glow, _swL, _swR;
        private readonly TextMeshProUGUI _head, _name;
        private string _text; private float _a, _barW, _left = -2f;

        internal bool Shown => _a > 0f;
        internal static float FakeUntil;   // dev (DevShot "raid-test"): shown as a raid for a few seconds, no event started

        internal RaidBanner(Transform parent, ThemeData t)
        {
            _root = Kit.Node(parent, "Raid");
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f); _root.anchoredPosition = new Vector2(0f, -Top); _root.sizeDelta = new Vector2(300f, H);
            _group = _root.gameObject.AddComponent<CanvasGroup>(); _group.blocksRaycasts = false; _group.alpha = 0f;
            Color ember = Theme.C(t.ember);
            _glow = Kit.Panel(_root, "Glow", new Color(ember.r, ember.g, ember.b, 0.06f), 17); var g = _glow.rectTransform; g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one; g.offsetMin = new Vector2(-5f, -5f); g.offsetMax = new Vector2(5f, 5f);
            var (line, _) = Kit.Box(_root, "Card", new Color(0.078f, 0.078f, 0.086f, 0.88f), new Color(ember.r, ember.g, ember.b, 0.45f), 12);
            var c = line.rectTransform; c.anchorMin = Vector2.zero; c.anchorMax = Vector2.one; c.offsetMin = c.offsetMax = Vector2.zero;
            _head = Kit.Text(_root, "Head", "RAID", Fonts.Mono, 11f, ember); _head.alignment = TextAlignmentOptions.Center; _head.characterSpacing = 20f;
            _swL = Kit.IconImg(_root, "sword", ember, 13f); _swR = Kit.IconImg(_root, "sword", ember, 13f); _swR.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            _name = Kit.Text(_root, "Name", "", Fonts.Bold, 24f, Color.white);   // Valheim's raid lettering once the HUD has it (Layout)
            _name.alignment = TextAlignmentOptions.Center;
            var (bl, _) = Kit.Box(_root, "Bar", new Color(1f, 1f, 1f, 0.1f), new Color(0f, 0f, 0f, 0f), 2); _bar = bl.rectTransform;
            _fill = Kit.Panel(_bar, "Fill", ember, 2).rectTransform; _fill.anchorMin = Vector2.zero; _fill.anchorMax = new Vector2(0f, 1f); _fill.pivot = new Vector2(0f, 0.5f); _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            var amber = Kit.Panel(_fill, "Amber", Theme.C(t.stamina), 2).rectTransform; amber.anchorMin = new Vector2(0.5f, 0f); amber.anchorMax = Vector2.one; amber.offsetMin = amber.offsetMax = Vector2.zero;   // orange into amber, like the design's gradient
            _root.gameObject.SetActive(false);
        }

        private void Layout(string text)
        {
            var vanilla = Hud.instance != null && Hud.instance.m_eventName != null ? Hud.instance.m_eventName.font : null;
            if (vanilla != null && _name.font != vanilla) { _name.font = vanilla; Shadow(_name); }
            _text = text; _name.text = text; _name.ForceMeshUpdate();
            float nw = Mathf.Ceil(_name.preferredWidth), w = Mathf.Max(300f, nw + 60f);
            _root.sizeDelta = new Vector2(w, H);
            float hw = Mathf.Ceil(_head.GetPreferredValues("RAID").x);
            _head.rectTransform.At((w - hw - 8f) / 2f, 8f, hw + 8f, 16f);
            _swL.rectTransform.At((w - hw) / 2f - 8f - 13f, 9.5f, 13f, 13f); _swR.rectTransform.At((w + hw) / 2f + 8f + 13f, 9.5f, 13f, 13f);   // mirrored around its left edge: +13 puts it back to the right of the word
            _name.rectTransform.At(0f, 24f, w, 30f);
            _barW = w - 60f; _bar.At(30f, 62f, _barW, 3f); _left = -2f;
        }

        // a soft dark edge under the letters, to read over bright skies (a copy of the font's material)
        private static void Shadow(TMP_Text t)
        {
            var m = t.fontMaterial; if (m == null || !m.HasProperty(ShaderUtilities.ID_UnderlayColor)) return;
            m.EnableKeyword("UNDERLAY_ON"); m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.7f));
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f); m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
        }

        internal void Tick()
        {
            var rs = RandEventSystem.instance; var ev = rs != null ? rs.GetActiveEvent() : null;
            bool boss = EnemyHud.instance != null && EnemyHud.instance.ShowingBossHud();
            bool fake = Time.unscaledTime < FakeUntil;
            bool show = (fake || ev != null && !boss && ev.GetTime() > 3f) && !InventoryGui.IsVisible() && !Minimap.IsOpen();
            if (show)
            {
                var text = fake ? "A skeleton surprise!" : Localization.instance.Localize(ev.GetHudText());
                if (text != _text) Layout(text);
            }
            float target = show ? 1f : 0f;
            if (_a == target) { if (show) Pulse(ev, fake); return; }
            _a = Mathf.MoveTowards(_a, target, Time.unscaledDeltaTime * 3f);
            if (_root.gameObject.activeSelf != _a > 0f) _root.gameObject.SetActive(_a > 0f);
            float e = 1f - (1f - _a) * (1f - _a);
            _group.alpha = e; _root.anchoredPosition = new Vector2(0f, -Top + (1f - e) * 10f);
            if (ev != null || fake) Pulse(ev, fake);
        }

        // the glow breathes, the swords beat a little faster, the bar shows what is left
        private void Pulse(RandomEvent ev, bool fake)
        {
            float now = Time.unscaledTime;
            _glow.color = new Color(_glow.color.r, _glow.color.g, _glow.color.b, 0.03f + 0.06f * (0.5f + 0.5f * Mathf.Sin(now * 2.6f)));
            float sa = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(now * 3.9f));
            _swL.color = _swR.color = new Color(_swL.color.r, _swL.color.g, _swL.color.b, sa);
            float left = fake ? Mathf.Clamp01((FakeUntil - Time.unscaledTime) / 8f) : ev.m_duration > 0f ? Mathf.Clamp01(1f - ev.m_time / ev.m_duration) : -1f;
            if (Mathf.Abs(left - _left) < 0.002f) return;
            _left = left; _bar.gameObject.SetActive(left >= 0f);
            if (left >= 0f) _fill.anchorMax = new Vector2(left, 1f);
        }

        internal void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); }
    }
}
