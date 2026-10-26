using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Cheat and debug flags (detail pass, approved): a slim strip left of the compass while any is on (user: not hanging under it), so you can't forget
    // god mode: each active flag in Norse capitals after a small ember rune, thin dividers between them, an ember line along the
    // strip's foot and a soft ember glow under it. Checked four times a second; laid out only when the set changes.
    internal sealed class CheatFlags
    {
        private const float Mid = 43f, Right = 300f, FlagH = 18f, PadX = 4f, PadTop = 4f, PadBot = 5f;   // the compass band's centre is 43 down, its left end 290 left of centre
        private static readonly string[] Names = { "God", "Ghost", "Fly", "No cost", "Devcommands" };
        private readonly RectTransform _root, _clip, _foot;
        private readonly RectTransform[] _flags = new RectTransform[5];
        private readonly Image[] _dividers = new Image[5];
        private readonly float[] _w = new float[5];
        private readonly TextMeshProUGUI[] _texts = new TextMeshProUGUI[5];
        private int _mask = -1; private float _next;

        internal CheatFlags(Transform parent, ThemeData t)
        {
            Color ember = Theme.C(t.ember);
            Box = Kit.Node(parent, "CheatFlags").Fill();   // the HUD hides this with the inventory open; the strip inside shows by the flags
            _root = Kit.Node(Box, "Strip");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f); _root.pivot = new Vector2(1f, 0.5f); _root.anchoredPosition = new Vector2(-Right, -Mid);   // grows leftwards
            var glow = Kit.Img(_root, "Glow", Kit.Glow(), new Color(ember.r, ember.g, ember.b, 0.05f)).rectTransform;   // soft and mostly under the strip (user: the design's .12 all round was a red halo at night)
            glow.anchorMin = Vector2.zero; glow.anchorMax = Vector2.one; glow.offsetMin = new Vector2(-8f, -12f); glow.offsetMax = new Vector2(8f, 0f);
            _clip = Kit.Node(_root, "Clip").Fill();
            var bg = Kit.Panel(_clip, "Strip", new Color(0.067f, 0.067f, 0.075f, 0.86f), 6f).rectTransform;   // design: 14,14,16 .9 into 20,20,22 .78; ponytail: flat, the mean of the two
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.offsetMin = new Vector2(-2f, -2f); bg.offsetMax = new Vector2(2f, 2f);   // our rounded sprites keep a 2 px clear margin
            _foot = Kit.Img(_clip, "Foot", null, new Color(ember.r, ember.g, ember.b, 0.85f)).rectTransform;   // 1.5 px ember along the foot, clear of the round corners
            _foot.anchorMin = new Vector2(0f, 0f); _foot.anchorMax = new Vector2(1f, 0f); _foot.pivot = new Vector2(0.5f, 0f); _foot.offsetMin = new Vector2(5f, 0f); _foot.offsetMax = new Vector2(-5f, 1.5f);
            for (int i = 0; i < Names.Length; i++)
            {
                var f = _flags[i] = Kit.Node(_clip, "Flag-" + Names[i]);
                var rune = Kit.IconImg(f, "rune-cross", ember, 10f); rune.rectTransform.At(10f, (FlagH - 10f) / 2f, 10f, 10f);   // assets/icons/rune-cross.png: the design's rune, strokes a little heavier to read at 10 px
                var tx = _texts[i] = Kit.Text(f, "Text", Names[i].ToUpperInvariant(), Fonts.Norse, 13f, new Color32(0xF4, 0xE2, 0xD8, 0xFF)); tx.characterSpacing = 10f; tx.alignment = TextAlignmentOptions.MidlineLeft;
                _dividers[i] = Kit.Img(f, "Divider", null, new Color(1f, 1f, 1f, 0.12f)); _dividers[i].rectTransform.At(0f, 0f, 1f, FlagH);
                f.gameObject.SetActive(false);
            }
            _root.gameObject.SetActive(false);
        }

        internal void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            var p = Player.m_localPlayer; int mask = 0;
            if (p != null)
            {
                if (p.InGodMode()) mask |= 1;
                if (p.InGhostMode()) mask |= 2;
                if (p.IsDebugFlying()) mask |= 4;
                if (p.NoCostCheat()) mask |= 8;
                if (Terminal.m_cheat) mask |= 16;
            }
            if (mask == _mask) return;
            _mask = mask;
            if (_texts[0].font != Fonts.Norse || _w[0] <= 0f) Fit();
            _root.gameObject.SetActive(mask != 0); if (mask == 0) return;
            float x = PadX; bool first = true;
            for (int i = 0; i < _flags.Length; i++)
            {
                bool on = (mask & (1 << i)) != 0; _flags[i].gameObject.SetActive(on); if (!on) continue;
                _flags[i].At(x, PadTop, _w[i], FlagH); _dividers[i].enabled = !first; first = false; x += _w[i];
            }
            _root.sizeDelta = new Vector2(x + PadX, PadTop + FlagH + PadBot);
        }

        internal readonly RectTransform Box;

        // the flags' widths in Valheim's Norse (the HUD can be built at boot, before the game has loaded its fonts)
        private void Fit()
        {
            for (int i = 0; i < _texts.Length; i++)
            {
                var tx = _texts[i]; tx.font = Fonts.Norse;
                float tw = Mathf.Ceil(Kit.W(tx)); tx.rectTransform.At(26f, 0f, tw + 4f, FlagH);
                _w[i] = 10f + 10f + 6f + tw + 10f;
            }
        }
    }
}
