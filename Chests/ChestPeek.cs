using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Looking at a chest (live design chestPeek): a small card right of the crosshair with the chest's
    // name, how full it is, a mini grid of what is inside, "E Open" and whether crafting uses it.
    // Valheim keeps every loaded chest's inventory in sync, so this only reads it; a chest you may not
    // open shows its name and "Private" only.
    internal static class ChestPeek
    {
        private const float Cell = 42f, Gap = 4f;   // user: bigger than the design's 30
        private static readonly System.Func<Container, long, bool> Access = AccessTools.MethodDelegate<System.Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        private static RectTransform _root; private static CanvasGroup _cg;
        private static Container _for; private static string _key;
        internal static bool Showing;   // the hover card steps aside while the peek shows (it takes over the action lines)

        internal static void Tick()
        {
            var p = Player.m_localPlayer;
            Container c = null;
            if (p != null && Root.Canvas != null && !InventoryGui.IsVisible() && !Menu.IsVisible() && !Minimap.IsOpen() && !Hud.IsPieceSelectionVisible())
            {
                var go = p.GetHoverObject();
                c = go != null ? go.GetComponentInParent<Container>() : null;
                if (c != null && (c.GetInventory() == null || c.GetComponentInParent<Player>() != null)) c = null;
            }
            Showing = c != null;
            if (_root == null && c != null) Build();
            if (_root == null) return;
            _cg.alpha = Mathf.MoveTowards(_cg.alpha, c != null ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (c == null) { _for = null; return; }
            var inv = c.GetInventory();
            bool open = Access(c, p.GetPlayerID()) && Stock.Ward(c);   // warded or private: no contents
            var hover = Hud.instance != null && Hud.instance.m_hoverName != null ? Hud.instance.m_hoverName.text : "";
            var key = $"{hover}|{c.GetInstanceID()}|{open}|{ChestPanel.NoCraft(c)}|{string.Join(",", inv.GetAllItems().ConvertAll(i => i.m_shared.m_name + i.m_stack + "@" + i.m_gridPos.x + "." + i.m_gridPos.y))}";
            if (c == _for && key == _key) return;
            _for = c; _key = key;
            Fill(c, inv, open);
        }

        private static void Build()
        {
            _root = Kit.Node(Root.Canvas.transform, "jatify-peek");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f); _root.pivot = new Vector2(0f, 1f); _root.anchoredPosition = new Vector2(28f, -600f);
            Notices.OnTop(_root);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
        }

        private static void Fill(Container c, Inventory inv, bool open)
        {
            var t = Plugin.Instance.ThemeData;
            foreach (Transform ch in _root) Object.Destroy(ch.gameObject);
            int slots = inv.GetWidth() * inv.GetHeight(), cols = ChestPanel.Wide(inv.GetWidth(), inv.GetHeight()), rows = (slots + cols - 1) / cols;   // the panel's shape
            float gridW = cols * (Cell + Gap) - Gap, w = Mathf.Max(240f, gridW + 24f);
            HoverCard.Parse(Hud.instance != null && Hud.instance.m_hoverName != null ? Hud.instance.m_hoverName.text : "");
            int lines = Mathf.Max(1, HoverCard.Actions.Count) + (open ? 1 : 0);   // Valheim's actions for this chest, and the crafting state
            float h = (open ? 12 + 24 + 8 + rows * (Cell + Gap) - Gap + 10 : 12 + 24 + 8) + lines * 24 + 8;
            _root.anchoredPosition = new Vector2(28f, -Mathf.Min(600f, 1080f - 16f - h));   // never past the bottom of the screen
            var (bg, _) = Card.Box(_root, "Card", t); bg.rectTransform.At(0, 0, w, h);
            // title: icon, name, used / total
            var piece = c.GetComponentInParent<Piece>();
            if (piece != null && piece.m_icon != null) { var ic = Kit.Img(bg.rectTransform, "Icon", piece.m_icon, Color.white, false); ic.rectTransform.At(12, 12, 24, 24); ic.preserveAspect = true; }
            var nm = Kit.Text(bg.rectTransform, "Name", Localization.instance.Localize(c.m_name), Fonts.Bold, Card.Title, Color.white); nm.rectTransform.At(44, 12, w - 44 - 60, 24); nm.alignment = TextAlignmentOptions.MidlineLeft; nm.overflowMode = TextOverflowModes.Ellipsis;
            var used = Kit.Text(bg.rectTransform, "Used", open ? $"{inv.GetAllItems().Count} / {slots}" : "Private", Fonts.Mono, Card.Meta, Theme.C(open ? t.mute : t.ember)); used.alignment = TextAlignmentOptions.MidlineRight; used.rectTransform.At(w - 64, 12, 52, 24);
            float y = 44f;
            if (open)
            {   // mini grid, Valheim's own cell order laid out in the panel's columns
                float x0 = (w - gridW) / 2f; int vw = inv.GetWidth();
                for (int i = 0; i < slots; i++)
                    {
                        var cell = Kit.Panel(bg.rectTransform, "Cell", new Color(1f, 1f, 1f, 0.05f), 5); cell.rectTransform.At(x0 + i % cols * (Cell + Gap), y + i / cols * (Cell + Gap), Cell, Cell);
                        var it = inv.GetItemAt(i % vw, i / vw); if (it == null) continue;
                        var ii = Kit.Img(cell.rectTransform, "I", it.GetIcon(), Color.white, false); ii.rectTransform.At(5, 5, 32, 32); ii.preserveAspect = true;
                        if (it.m_stack > 1) { var n = Kit.Text(cell.rectTransform, "N", it.m_stack.ToString(), Fonts.Mono, 10f, Color.white); n.alignment = TextAlignmentOptions.BottomRight; n.rectTransform.Fill(2.5f); }
                    }
                y += rows * (Cell + Gap) - Gap + 10f;
            }
            // Valheim's own actions for this chest ("[E] Open (Hold to stack)"), then whether crafting uses it
            if (HoverCard.Actions.Count == 0) HoverCard.Actions.Add(("E", open ? "Open" : "Open (needs access)"));
            foreach (var (k, what) in HoverCard.Actions) { HoverCard.Row(bg.rectTransform, k, what, 12f, y, t); y += 24f; }
            if (open)
            {
                bool craft = !ChestPanel.NoCraft(c);
                var dot = Kit.Img(bg.rectTransform, "Dot", Kit.Circle(), Theme.C(craft ? t.accent : t.mute), false); dot.rectTransform.At(18, y + 7, 7, 7);
                var ft = Kit.Text(bg.rectTransform, "Craft", craft ? "Used for crafting" : "Excluded from crafting", Fonts.Medium, 12f, Theme.C(t.mute)); ft.alignment = TextAlignmentOptions.MidlineLeft; ft.rectTransform.At(32, y, w - 44, 21);
            }
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; _for = null; }
    }
}
