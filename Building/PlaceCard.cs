using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // The piece you are placing (our look; Valheim's own card is hidden in BuildStyle):
    // icon, name and description, then a chip per cost as have/need (have in red when short; nearby chests
    // count as for building) and the station it needs, red when none is in range. Bottom right, left of the
    // food / mead column; rebuilt only when what it shows changes, checked 4 times a second.
    internal static class PlaceCard
    {
        private const float W = 480f, ChipW = 223f, ChipH = 34f;
        private static RectTransform _root; private static string _key; private static float _next;

        internal static void Tick()
        {
            var p = Player.m_localPlayer; var piece = p != null && p.InPlaceMode() ? p.GetSelectedPiece() : null;
            bool show = piece != null && Root.Canvas != null && !Menu.IsVisible() && !InventoryGui.IsVisible() && !Minimap.IsOpen()
                        && Settings.instance == null && ConfigPage.Open == null && !Hud.IsPieceSelectionVisible();
            if (!show) { if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false); _key = null; return; }
            if (_root == null)
            {
                _root = Kit.Node(Root.Canvas.transform, "jatify-placecard");
                _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 0f); _root.anchoredPosition = new Vector2(-96f, 66f);
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            bool free = p.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost));
            var sb = new System.Text.StringBuilder(piece.name).Append(free);
            foreach (var r in piece.m_resources) if (r.m_resItem != null) sb.Append('|').Append(Have(p, r.m_resItem.m_itemData.m_shared.m_name));
            bool st = piece.m_craftingStation == null || CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, p.transform.position) != null
                      || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
            sb.Append(st);
            var key = sb.ToString(); if (key == _key) return;
            _key = key; Build(p, piece, free, st);
        }

        private static int Have(Player p, string name) { bool was = Stock.Building; Stock.Building = true; int n = Stock.Count(p, name); Stock.Building = was; return n; }

        private static void Build(Player p, Piece piece, bool free, bool st)
        {
            var t = Plugin.Instance.ThemeData;
            foreach (Transform c in _root) Object.Destroy(c.gameObject);
            var (box, _) = Card.Box(_root, "Card", t); var card = box.rectTransform;
            var (tile, _) = Kit.Box(card, "IconTile", Theme.C(t.raised), Theme.C(t.glassLine), 9); tile.rectTransform.At(14, 14, 40, 40);
            var ic = Kit.Img(tile.rectTransform, "I", piece.m_icon, Color.white, false); ic.rectTransform.At(5, 5, 30, 30); ic.preserveAspect = true;
            var nm = Kit.Text(card, "Name", Localization.instance.Localize(piece.m_name), Fonts.Bold, Card.Title, Color.white);
            nm.rectTransform.At(66, 12, W - 80, 20); nm.overflowMode = TextOverflowModes.Ellipsis;
            var desc = Localization.instance.Localize(piece.m_description ?? ""); float dh = 0f;
            if (desc.Length > 0)
            {
                var d = Kit.Text(card, "Desc", desc, Fonts.Medium, Card.Body, Theme.C(t.dim)); d.textWrappingMode = TextWrappingModes.Normal;
                dh = Mathf.Ceil(d.GetPreferredValues(desc, W - 80f, 0f).y); d.rectTransform.At(66, 34, W - 80, dh);
            }
            float y = Mathf.Max(54f, 34f + dh) + 12f; int i = 0;
            var short_ = Theme.C(t.ember); var mute = ColorUtility.ToHtmlStringRGB(Theme.C(t.mute));
            foreach (var r in piece.m_resources)
            {
                if (r.m_resItem == null || r.m_amount <= 0) continue;
                var shared = r.m_resItem.m_itemData.m_shared; int have = Have(p, shared.m_name); bool ok = free || have >= r.m_amount;
                var need = ColorUtility.ToHtmlStringRGB(ok ? Theme.C(t.text) : short_);
                Chip(card, i++, y, r.m_resItem.m_itemData.GetIcon(), $"{Localization.instance.Localize(shared.m_name)}  <color=#{need}>{have}</color><color=#{mute}>/{r.m_amount}</color>", ok, t);
            }
            if (piece.m_craftingStation != null)
                Chip(card, i++, y, piece.m_craftingStation.m_icon, Localization.instance.Localize(piece.m_craftingStation.m_name), st, t, station: true);
            if (i == 0) { var f = Kit.Text(card, "Free", "No cost", Fonts.Medium, 12f, Theme.C(t.mute)); f.rectTransform.At(14, y, 200, 20); i = 1; }
            float h = y + ((i + 1) / 2) * (ChipH + 6f) + 8f;
            _root.sizeDelta = new Vector2(W, h); card.Fill();
        }

        private static void Chip(RectTransform card, int i, float y0, Sprite icon, string text, bool ok, ThemeData t, bool station = false)
        {
            float x = 14f + (i % 2) * (ChipW + 6f), y = y0 + (i / 2) * (ChipH + 6f);
            var (b, _) = Kit.Box(card, "Chip", new Color(0f, 0f, 0f, 0.25f), ok ? Theme.C(t.line2) : Theme.C(t.ember), 8); b.rectTransform.At(x, y, ChipW, ChipH);
            var ic = Kit.Img(b.rectTransform, "I", icon, Color.white, false); ic.rectTransform.At(7, 6, 22, 22); ic.preserveAspect = true;
            var tx = Kit.Text(b.rectTransform, "T", text, Fonts.Medium, 12f, station ? (ok ? Theme.C(t.accent) : Theme.C(t.ember)) : Color.white);
            tx.richText = true; tx.alignment = TextAlignmentOptions.MidlineLeft; tx.overflowMode = TextOverflowModes.Ellipsis; tx.rectTransform.At(36, 0, ChipW - 42, ChipH);
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; _key = null; }
    }
}
