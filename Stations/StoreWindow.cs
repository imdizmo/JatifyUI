using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The trader in our own window (user: "we can do better"): the trader's name and your coins on top,
    // a grid of tiles (icon, name, price), the picked item's card with a quantity and Buy, and your valuables to sell. Valheim's
    // store stays open underneath, invisible: buying is its own OnBuyItem (repeated for the quantity), selling does exactly what
    // its SellItem does for the stack you click, and it still closes when you walk away.
    internal static class StoreWindow
    {
        private const float X0 = 150, Y0 = 150, W = 760, H = 760, Tile = 132, Gap = 13, Foot = 210;
        internal static RectTransform Win;   // the window (EpicLoot's trader panel docks beside it)
        private static readonly AccessTools.FieldRef<StoreGui, Trader> TraderOf = AccessTools.FieldRefAccess<StoreGui, Trader>("m_trader");
        private static readonly AccessTools.FieldRef<StoreGui, Trader.TradeItem> Selected = AccessTools.FieldRefAccess<StoreGui, Trader.TradeItem>("m_selectedItem");
        private static readonly System.Reflection.MethodInfo Select = AccessTools.Method(typeof(StoreGui), "SelectItem");
        private static readonly System.Reflection.MethodInfo Fill = AccessTools.Method(typeof(StoreGui), "FillList");
        private static RectTransform _root, _grid, _detail, _sell; private static TextMeshProUGUI _title, _coins;
        private static string _sig; private static float _next; private static int _qty = 1;

        internal static void Tick()
        {
            var g = StoreGui.instance; bool open = g != null && StoreGui.IsVisible() && Root.Canvas != null && Player.m_localPlayer != null && TraderOf(g) != null;
            if (!open) { if (_root != null) Close(); Plates(true); return; }
            Hide(g, true); Plates(false);
            if (_root == null) Build();
            float x0 = EpicSkin.TraderPanel() != null ? 16f : X0;   // EpicLoot's trader panel open: we move left, it docks on our right
            if (Win != null && !Mathf.Approximately(Win.anchoredPosition.x, x0)) Win.anchoredPosition = new Vector2(x0, Win.anchoredPosition.y);
            if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 0.25f;
            var items = TraderOf(g).GetAvailableItems(); int coins = Coins(g); var sel = Selected(g);
            var vals = Valuables(g);
            var sig = $"{items.Count}|{coins}|{items.IndexOf(sel)}|{_qty}|" + string.Join(",", vals.Select(v => v.m_shared.m_name + v.m_stack));
            if (sig == _sig) return; _sig = sig;
            Paint(g, items, sel, coins, vals);
        }

        // creature nameplates drew over the shop (user): hidden while it is open, back when it closes
        private static bool _platesOff;
        private static void Plates(bool show)
        {
            if (_platesOff != show || EnemyHud.instance == null || EnemyHud.instance.m_hudRoot == null) return;
            EnemyHud.instance.m_hudRoot.SetActive(show); _platesOff = !show; Log.Info($"store: nameplates {(show ? "back" : "hidden")} ({EnemyHud.instance.m_hudRoot.name}, {EnemyHud.instance.m_hudRoot.transform.childCount} children)");
        }

        // the trader's own name; the Bog Witch has none set (her window had no title), so then the prefab's, spaced ("BogWitch" -> "Bog Witch")
        private static string NameOf(Trader tr)
        {
            var n = Localization.instance.Localize(tr.m_name ?? "");
            if (!string.IsNullOrWhiteSpace(n) && !n.StartsWith("$") && !n.StartsWith("[")) return n;
            return System.Text.RegularExpressions.Regex.Replace(tr.gameObject.name.Replace("(Clone)", "").Trim(), "(?<=[a-z])(?=[A-Z])", " ");
        }

        private static int Coins(StoreGui g) => Player.m_localPlayer.GetInventory().CountItems(g.m_coinPrefab.m_itemData.m_shared.m_name);
        private static List<ItemDrop.ItemData> Valuables(StoreGui g)
        {
            var list = new List<ItemDrop.ItemData>(); Player.m_localPlayer.GetInventory().GetValuableItems(list);
            list.RemoveAll(v => v.m_shared.m_name == g.m_coinPrefab.m_itemData.m_shared.m_name || Epic.Keep(v)); return list;
        }

        // Valheim's store: kept open (its walk-away close, its buying) but invisible and out of the pointer's way
        private static void Hide(StoreGui g, bool hide)
        {
            if (g == null || g.m_rootPanel == null) return;
            var cg = g.m_rootPanel.GetComponent<CanvasGroup>() ?? g.m_rootPanel.AddComponent<CanvasGroup>();
            float a = hide ? 0f : 1f; if (cg.alpha != a) { cg.alpha = a; cg.blocksRaycasts = !hide; cg.interactable = !hide; }
        }

        private static void Build()
        {
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-store").Fill(); Notices.OnTop(_root); _root.gameObject.AddComponent<GraphicRaycaster>();
            var (win, wfill) = Card.Box(_root, "Window", t); win.raycastTarget = true; Knots.Corners(win, wfill, Card.R, Theme.C(t.accent)); var w = win.rectTransform.At(X0, Y0, W, H); Win = w;
            var eb = Kit.Text(w, "Eyebrow", "TRADER", Fonts.Mono, 10.5f, Theme.C(t.mute)); eb.characterSpacing = 8f; eb.rectTransform.At(24, 14, 200, 14);
            _title = Kit.Text(w, "Title", "", Fonts.Norse, 26f, Color.white); _title.rectTransform.At(24, 28, 400, 28);
            var (chip, _) = Kit.Box(w, "Coins", Theme.C(t.raised), Theme.C(t.glassLine), 8); chip.rectTransform.At(W - 24 - 40 - 10 - 150, 18, 150, 36);
            var ci = Kit.Img(chip.rectTransform, "I", Kit.Icon("coins") ?? StoreGui.instance.m_coinPrefab.m_itemData.GetIcon(), Color.white, false); ci.rectTransform.At(8, 6, 24, 24); ci.preserveAspect = true;
            _coins = Kit.Text(chip.rectTransform, "N", "", Fonts.Mono, 15f, Color.white); _coins.alignment = TextAlignmentOptions.MidlineRight; _coins.rectTransform.At(36, 0, 104, 36);
            var (cl, cf) = Kit.Box(w, "Close", Theme.C(t.raised), Theme.C(t.glassLine), 8); cl.rectTransform.At(W - 24 - 40, 16, 40, 40); cl.raycastTarget = true;
            var cx = Kit.IconImg(cl.rectTransform, "x", Theme.C(t.dim), 16f); cx.rectTransform.At(12, 12, 16, 16);
            cl.gameObject.AddComponent<Button>().onClick.AddListener(() => StoreGui.instance?.Hide());
            var hl = Kit.Img(w, "HeadLine", null, Theme.C(t.glassLine)); hl.rectTransform.At(0, 70, W, 1);
            var view = Kit.Node(w, "Grid").At(24, 82, W - 40, H - 82 - Foot - 8);
            view.gameObject.AddComponent<RectMask2D>(); var hit = view.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            _grid = Kit.Node(view, "Content"); _grid.anchorMin = new Vector2(0f, 1f); _grid.anchorMax = Vector2.one; _grid.pivot = new Vector2(0.5f, 1f); _grid.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = _grid; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            var fl = Kit.Img(w, "FootLine", null, Theme.C(t.glassLine)); fl.rectTransform.At(0, H - Foot, W, 1);
            _detail = Kit.Node(w, "Detail").At(24, H - Foot + 14, W - 48, 110);
            var sl = Kit.Img(w, "SellLine", null, Theme.C(t.glassLine)); sl.rectTransform.At(24, H - 76, W - 48, 1);
            Knots.Divider(sl, false, Theme.C(t.accent));   // design .kdiv: between buying and selling
            _sell = Kit.Node(w, "Sell").At(24, H - 66, W - 48, 52);
            _sig = null; _next = 0f; _qty = 1;
        }

        private static void Paint(StoreGui g, List<Trader.TradeItem> items, Trader.TradeItem sel, int coins, List<ItemDrop.ItemData> vals)
        {
            var t = Plugin.Instance.ThemeData; var trader = TraderOf(g);
            _title.text = NameOf(trader); _coins.text = coins.ToString("N0");
            // the grid
            foreach (Transform c in _grid) Object.Destroy(c.gameObject);
            int cols = 5;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i]; bool afford = it.m_price <= coins, on = it == sel; int idx = i;
                var (tile, fill) = Kit.Box(_grid, "Tile", on ? Theme.C(t.accentSoft) : Theme.C(t.raised), on ? Theme.C(t.accent) : Theme.C(t.glassLine), 10);
                tile.rectTransform.At((i % cols) * (Tile + Gap), (i / cols) * (Tile + Gap), Tile, Tile); tile.raycastTarget = true;
                var icon = it.m_icon != null ? it.m_icon : it.m_prefab != null ? it.m_prefab.m_itemData.GetIcon() : null;
                var ic = Kit.Img(tile.rectTransform, "I", icon, afford ? Color.white : new Color(1f, 1f, 1f, 0.35f), false); ic.rectTransform.At((Tile - 56) / 2f, 12, 56, 56); ic.preserveAspect = true;
                var nm = Kit.Text(tile.rectTransform, "N", Name(it), Fonts.Medium, 12.5f, afford ? Color.white : Theme.C(t.mute)); nm.alignment = TextAlignmentOptions.Top; nm.textWrappingMode = TextWrappingModes.Normal; nm.overflowMode = TextOverflowModes.Ellipsis; nm.rectTransform.At(8, 74, Tile - 16, 32);
                var pr = Kit.Text(tile.rectTransform, "P", $"{it.m_price:N0}", Fonts.Mono, 12f, afford ? Theme.C(t.stamina) : Theme.C(t.ember)); pr.alignment = TextAlignmentOptions.Bottom; pr.rectTransform.At(8, Tile - 26, Tile - 16, 18);
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() => { Select.Invoke(g, new object[] { idx, false }); _qty = 1; _sig = null; _next = 0f; });
            }
            int rows = (items.Count + cols - 1) / cols; _grid.sizeDelta = new Vector2(0f, rows * (Tile + Gap));
            if (items.Count == 0) { var none = Kit.Text(_grid, "None", "Nothing for sale right now.", Fonts.Medium, 14f, Theme.C(t.mute)); none.rectTransform.At(4, 8, 400, 22); }
            Detail(g, sel, coins, t);
            SellRow(g, vals, t);
        }

        private static string Name(Trader.TradeItem it)
        {
            var n = Localization.instance.Localize(it.m_prefab != null ? it.m_prefab.m_itemData.m_shared.m_name : it.m_name);
            return it.m_stack > 1 ? $"{n} ×{it.m_stack}" : n;
        }

        // the picked item: what it is, what it costs, how many, Buy
        private static void Detail(StoreGui g, Trader.TradeItem sel, int coins, ThemeData t)
        {
            foreach (Transform c in _detail) Object.Destroy(c.gameObject);
            if (sel == null) { var pick = Kit.Text(_detail, "Pick", "Pick something to buy.", Fonts.Medium, 13f, Theme.C(t.mute)); pick.rectTransform.At(0, 40, 400, 22); return; }
            var icon = sel.m_icon != null ? sel.m_icon : sel.m_prefab != null ? sel.m_prefab.m_itemData.GetIcon() : null;
            var (tile, _) = Kit.Box(_detail, "Tile", Theme.C(t.raised), Theme.C(t.glassLine), 10); tile.rectTransform.At(0, 0, 72, 72);
            var ic = Kit.Img(tile.rectTransform, "I", icon, Color.white, false); ic.rectTransform.At(10, 10, 52, 52); ic.preserveAspect = true;
            var nm = Kit.Text(_detail, "N", Name(sel), Fonts.Bold, 17f, Color.white); nm.rectTransform.At(88, 0, 380, 24);
            string desc = sel.m_tooltip.Length > 0 ? Localization.instance.Localize(sel.m_tooltip) : sel.m_prefab != null ? Localization.instance.Localize(sel.m_prefab.m_itemData.m_shared.m_description) : "";
            var d = Kit.Text(_detail, "D", Kit.Plain(desc), Fonts.Medium, 12.5f, Theme.C(t.dim)); d.textWrappingMode = TextWrappingModes.Normal; d.overflowMode = TextOverflowModes.Ellipsis; d.rectTransform.At(88, 28, 380, 70);
            // quantity, total, Buy (at most what you can afford and what fits)
            int max = sel.m_price > 0 ? Mathf.Max(1, coins / sel.m_price) : 1; _qty = Mathf.Clamp(_qty, 1, Mathf.Max(1, Mathf.Min(max, 99)));
            float rx = W - 48 - 230;
            Step(_detail, "−", rx, 0, () => { _qty = Mathf.Max(1, _qty - 1); _sig = null; _next = 0f; }, t);
            var q = Kit.Text(_detail, "Q", _qty.ToString(), Fonts.Mono, 15f, Color.white); q.alignment = TextAlignmentOptions.Center; q.rectTransform.At(rx + 40, 0, 50, 36);
            Step(_detail, "+", rx + 90, 0, () => { _qty = _qty + 1; _sig = null; _next = 0f; }, t);
            Step(_detail, "Max", rx + 132, 0, () => { _qty = max; _sig = null; _next = 0f; }, t, 56);
            int total = sel.m_price * _qty; bool afford = total <= coins;
            var tot = Kit.Text(_detail, "Total", $"{total:N0} coins", Fonts.Mono, 13f, afford ? Theme.C(t.stamina) : Theme.C(t.ember)); tot.alignment = TextAlignmentOptions.MidlineRight; tot.rectTransform.At(rx, 44, 188, 22);
            var (buy, _) = Kit.Box(_detail, "Buy", afford ? Theme.C(t.accent) : Theme.C(t.raised2), afford ? Theme.C(t.accent) : Theme.C(t.line2), 9); buy.rectTransform.At(rx, 72, 230, 38); buy.raycastTarget = afford;
            var bt = Kit.Text(buy.rectTransform, "T", afford ? (_qty > 1 ? $"Buy {_qty}" : "Buy") : "Not enough coins", Fonts.Bold, 14f, afford ? new Color(0.024f, 0.082f, 0.051f) : Theme.C(t.mute)); bt.alignment = TextAlignmentOptions.Center; bt.rectTransform.Fill();
            if (afford) buy.gameObject.AddComponent<Button>().onClick.AddListener(() => Buy(g));
        }

        private static void Step(RectTransform p, string label, float x, float y, System.Action click, ThemeData t, float w = 36f)
        {
            var (b, _) = Kit.Box(p, label, Theme.C(t.raised), Theme.C(t.line2), 8); b.rectTransform.At(x, y, w, 36); b.raycastTarget = true;
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Medium, 13f, Color.white); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => click());
        }

        // Valheim's own buy, as many times as asked, stopping when coins run short or nothing changed (bag full)
        private static void Buy(StoreGui g)
        {
            for (int i = 0; i < _qty; i++)
            {
                var sel = Selected(g); int before = Coins(g); if (sel == null || sel.m_price > before) break;
                g.OnBuyItem(); if (Coins(g) == before) break;
            }
            _qty = 1; _sig = null; _next = 0f;
        }

        // your valuables (amber, rubies, pearls...): click one to sell that stack, or sell them all
        private static void SellRow(StoreGui g, List<ItemDrop.ItemData> vals, ThemeData t)
        {
            foreach (Transform c in _sell) Object.Destroy(c.gameObject);
            var lb = Kit.Text(_sell, "L", "SELL", Fonts.Mono, 10.5f, Theme.C(t.mute)); lb.characterSpacing = 8f; lb.rectTransform.At(0, 0, 60, 52); lb.alignment = TextAlignmentOptions.MidlineLeft;
            if (vals.Count == 0) { var none = Kit.Text(_sell, "None", "Nothing to sell: valuables you carry show here.", Fonts.Medium, 12.5f, Theme.C(t.mute)); none.rectTransform.At(64, 0, 500, 52); none.alignment = TextAlignmentOptions.MidlineLeft; return; }
            float x = 64f; int sum = 0;
            foreach (var v in vals.Take(9))
            {
                var it = v; int worth = v.m_shared.m_value * v.m_stack; sum += worth;
                var (tile, _) = Kit.Box(_sell, "V", Theme.C(t.raised), Theme.C(t.glassLine), 8); tile.rectTransform.At(x, 2, 48, 48); tile.raycastTarget = true;
                var ic = Kit.Img(tile.rectTransform, "I", v.GetIcon(), Color.white, false); ic.rectTransform.At(7, 6, 34, 34); ic.preserveAspect = true;
                if (v.m_stack > 1) { var n = Kit.Text(tile.rectTransform, "N", v.m_stack.ToString(), Fonts.Mono, 10f, Color.white); n.alignment = TextAlignmentOptions.BottomRight; n.rectTransform.Fill(3f); }
                var tip = tile.gameObject.AddComponent<UITooltip>(); var vt = g.m_listElement != null ? g.m_listElement.GetComponent<UITooltip>() : null; if (vt != null) tip.m_tooltipPrefab = vt.m_tooltipPrefab;
                tip.Set(v.m_shared.m_name, $"Sell for {worth:N0} coins");
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (Sell(g, it)) g.m_sellEffects.Create(g.transform.position, Quaternion.identity); });
                x += 54f;
            }
            foreach (var v in vals.Skip(9)) sum += v.m_shared.m_value * v.m_stack;
            var (all, _) = Kit.Box(_sell, "All", Theme.C(t.raised), Theme.C(t.line2), 9); all.rectTransform.At(W - 48 - 190, 7, 190, 38); all.raycastTarget = true;
            var at = Kit.Text(all.rectTransform, "T", $"Sell all · {sum:N0} coins", Fonts.Medium, 13f, Color.white); at.alignment = TextAlignmentOptions.Center; at.rectTransform.Fill();
            all.gameObject.AddComponent<Button>().onClick.AddListener(() => { bool any = false; foreach (var v in Valuables(g)) any |= Sell(g, v); if (any) g.m_sellEffects.Create(g.transform.position, Quaternion.identity); });
        }

        // exactly Valheim's SellItem, for the stack you picked
        private static bool Sell(StoreGui g, ItemDrop.ItemData item)
        {
            var p = Player.m_localPlayer; var trader = TraderOf(g); if (p == null || trader == null || !p.GetInventory().ContainsItem(item)) return false;
            int value = item.m_shared.m_value * item.m_stack; bool cheated = p.GetInventory().ItemCheated(item.m_shared.m_name);
            p.GetInventory().RemoveItem(item);
            p.GetInventory().AddItem(g.m_coinPrefab.gameObject.name, value, g.m_coinPrefab.m_itemData.m_quality, g.m_coinPrefab.m_itemData.m_variant, 0L, "", cheated);
            string text = item.m_stack <= 1 ? item.m_shared.m_name : item.m_stack + "x" + item.m_shared.m_name;
            p.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_sold", text, value.ToString()), 0, item.m_shared.m_icons[0]);
            trader.OnSold(); Fill.Invoke(g, null);
            _sig = null; _next = 0f; return true;
        }

        private static void Close() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; }
        internal static void Destroy() { Close(); Hide(StoreGui.instance, false); Plates(true); }
    }
}
