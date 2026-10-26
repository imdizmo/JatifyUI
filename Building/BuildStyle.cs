using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Build menu restyle (vanilla's new BuildUi: tabs, search, tag list, piece grid, favourites; our
    // look): glass window, rounded tabs and keycaps, dark wells, our piece and tag tiles, and the
    // selected-piece card lifted above the HUD dock.
    // Static parts go through the ledger. Piece tiles are rebuilt by Valheim per category and are
    // restyled as they appear; each keeps its original sprites, put back on unload because our
    // generated sprites are destroyed then (a tile left with a destroyed sprite draws white).
    internal static class BuildStyle
    {
        private static readonly Ledger _l = new Ledger();
        private static readonly List<(TMP_Text text, TMP_FontAsset font, float size)> _texts = new List<(TMP_Text, TMP_FontAsset, float)>();
        private static readonly List<(Image img, Sprite sprite, Color color, Image.Type type)> _tiles = new List<(Image, Sprite, Color, Image.Type)>();
        private static Hud _for;
        private static ThemeData _t;

        internal static void Apply(ThemeData t)
        {
            var h = Hud.instance; if (h == null || h.m_pieceSelectionWindow == null) return;
            Restore();
            _for = h; _t = t;
            var ui = h.m_buildUi != null ? h.m_buildUi.transform : null;
            var w = ui?.Find("bar/SelectionWindow");
            if (w != null)
            {
                _l.Off(w.Find("Background")?.GetComponent<Graphic>());
                _l.Off(w.Find("TabContainer/TabBorder")?.GetComponent<Graphic>());
                var (line, _) = Kit.Box(w, "jatify-panel", Theme.C(t.glass), Theme.C(t.glassLine), 14);
                _l.Own(line); line.rectTransform.SetAsFirstSibling(); line.rectTransform.Fill(-6f); line.raycastTarget = true;
                var tabs = w.Find("TabContainer/Tabs");
                if (tabs != null)
                    foreach (Transform tab in tabs)
                    {
                        _l.Img(tab.GetComponent<Image>(), Kit.Rounded(8), Theme.C(t.raised));
                        _l.Txt(tab.Find("Text")?.GetComponent<TMP_Text>(), Fonts.Medium, 15f, Theme.C(t.text));
                    }
                foreach (var k in w.GetComponentsInChildren<Image>(true))
                    if (k.sprite != null && k.sprite.name == "key_base") _l.Img(k, Kit.Rounded(5), Theme.C(t.raised));
                var list = w.Find("Main View/Root/Piece List Container/Piece List View");
                _l.Img(list?.GetComponent<Image>(), Kit.Rounded(10), new Color(0f, 0f, 0f, 0.28f));
                _l.Img(list?.Find("Scrollbar Vertical")?.GetComponent<Image>(), Kit.Rounded(4), new Color(0f, 0f, 0f, 0.25f));
                _l.Img(list?.Find("Scrollbar Vertical/Sliding Area/Handle")?.GetComponent<Image>(), Kit.Rounded(4), Theme.C(t.line2));
                foreach (var sb in w.GetComponentsInChildren<Scrollbar>(true))
                {
                    _l.Img(sb.GetComponent<Image>(), Kit.Rounded(4), new Color(0f, 0f, 0f, 0.25f));
                    _l.Img(sb.handleRect != null ? sb.handleRect.GetComponent<Image>() : null, Kit.Rounded(4), Theme.C(t.line2));
                }
                var tags = w.Find("Main View/Root/TagList");
                _l.Img(tags?.Find("Scroll View")?.GetComponent<Image>(), Kit.Rounded(10), new Color(0f, 0f, 0f, 0.28f));
                var search = tags?.Find("LayoutGroup/SearchBar");
                _l.Img(search?.GetComponent<Image>(), Kit.Rounded(8), Theme.C(t.raised));
                foreach (var x in search != null ? search.GetComponentsInChildren<TMP_Text>(true) : new TMP_Text[0]) _l.Txt(x, Fonts.Medium, 14f, x.name == "Placeholder" ? Theme.C(t.mute) : Theme.C(t.text));
            }
            var fav = ui?.Find("FavoritesDropdown");
            _l.Img(fav?.GetComponent<Image>(), Kit.Rounded(10), Theme.C(t.glass));
            // Valheim's selected-piece card: ours replaces it (PlaceCard)
            _l.Rect(h.m_buildHud.transform.Find("SelectedInfo") as RectTransform, r => r.localScale = Vector3.zero);
        }

        private static float _next;
        internal static void Tick()
        {
            var ui = _for != null ? _for.m_buildUi : null;
            if (ui == null || _t == null || !ui.gameObject.activeInHierarchy || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.2f; // tiles appear when a tab or tag changes; 5 checks a second is plenty
            foreach (var pb in ui.GetComponentsInChildren<BuildUiPieceButton>(false))
            {
                if (pb.transform.Find("jatify") != null) continue;
                Kit.Node(pb.transform, "jatify").gameObject.SetActive(false);
                _tiles.RemoveAll(x => x.img == null);
                Keep(pb.GetComponent<Image>(), Kit.Rounded(10), Theme.C(_t.raised));
                var icon = pb.transform.Find("Piece Icon") as RectTransform; if (icon != null && ((RectTransform)pb.transform).rect.width > 50f) icon.Fill(8f);
            }
            foreach (var tb in ui.GetComponentsInChildren<BuildUiTagButton>(false))
            {
                if (tb.transform.Find("jatify") != null) continue;
                Kit.Node(tb.transform, "jatify").gameObject.SetActive(false);
                _tiles.RemoveAll(x => x.img == null);
                Keep(tb.GetComponent<Image>(), Kit.Rounded(7), new Color(1f, 1f, 1f, 0.04f));
                var sel = tb.transform.Find("Selected")?.GetComponent<Image>(); if (sel != null) Keep(sel, Kit.Rounded(7), Theme.C(_t.accentSoft));
                _texts.RemoveAll(x => x.text == null);
                foreach (var tx in tb.GetComponentsInChildren<TMP_Text>(true)) { _texts.Add((tx, tx.font, tx.fontSize)); tx.font = Fonts.Medium; tx.fontSize = 14f; } // vanilla keeps the colour
            }
        }

        private static void Keep(Image i, Sprite s, Color c)
        {
            if (i == null) return;
            _tiles.Add((i, i.sprite, i.color, i.type));
            i.sprite = s; i.type = Image.Type.Sliced; i.color = c;
        }

        internal static void Restore()
        {
            foreach (var (img, sprite, color, type) in _tiles)
                if (img != null) { img.sprite = sprite; img.color = color; img.type = type; var m = img.transform.Find("jatify"); if (m != null) Object.Destroy(m.gameObject); }
            _tiles.Clear();
            foreach (var (text, font, size) in _texts) if (text != null) { text.font = font; text.fontSize = size; }
            _texts.Clear();
            _l.Undo(); _for = null;
        }
    }
}
