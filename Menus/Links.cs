using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Jatify's Discord (community and help) and Buy me a coffee (support), as on jatify.com: two side by side buttons with an icon
    // and a second line, at the foot of the settings page and of What's new; Discord on the title menu too (user)
    internal static class Links
    {
        internal const string Discord = "https://discord.gg/Ru9t2KdfVK", Coffee = "https://buymeacoffee.com/imdizmo";

        // the pair at x, y (top left) in parent; 236 x 44 each, 12 apart
        internal static void Row(RectTransform parent, float x, float y, ThemeData t)
        {
            One(parent, x, y, "discord-logo", "Discord", "Community and help", Discord, t);
            One(parent, x + 248f, y, "coffee", "Buy me a coffee", "Support Jatify", Coffee, t);
        }

        private static void One(RectTransform parent, float x, float y, string icon, string title, string sub, string url, ThemeData t)
        {
            var (b, f) = Kit.Box(parent, "jatify-link-" + icon, Theme.C(t.raised), Theme.C(t.line2), 9); b.rectTransform.At(x, y, 236, 44); b.raycastTarget = true;
            var ic = Kit.IconImg(b.rectTransform, icon, Theme.C(t.accent), 22f); ic.rectTransform.At(12, 11, 22, 22);
            var tt = Kit.Text(b.rectTransform, "T", title, Fonts.Bold, 14f, Color.white); tt.rectTransform.At(44, 5, 186, 18); tt.alignment = TextAlignmentOptions.MidlineLeft;
            var st = Kit.Text(b.rectTransform, "S", sub, Fonts.Medium, 11.5f, Theme.C(t.dim)); st.rectTransform.At(44, 23, 186, 16); st.alignment = TextAlignmentOptions.MidlineLeft;
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f; var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.35f); btn.colors = cb;
            btn.onClick.AddListener(() => Application.OpenURL(url));
        }
    }
}
