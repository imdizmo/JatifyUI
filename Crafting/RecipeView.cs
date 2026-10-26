using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Our recipe list (design: "Ready to craft" / "Missing something" sections; list rows with icon,
    // name and what is missing, or a 4-wide grid of 42 px cells with an ember dot when something is
    // missing). Data is Valheim's own m_availableRecipes; clicking calls Valheim's SetRecipe, so the
    // detail card, crafting and upgrading stay vanilla. Vanilla's list stays alive but invisible.
    internal sealed class RecipeView
    {
        private static readonly FieldInfo Avail = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
        private static readonly MethodInfo SetRecipe = AccessTools.Method(typeof(InventoryGui), "SetRecipe");
        private static readonly MethodInfo Selected = AccessTools.Method(typeof(InventoryGui), "GetSelectedRecipeIndex");
        private readonly RectTransform _root, _content;
        private readonly ThemeData _t;
        private string _key;

        // Favourite recipes (design: star on the detail card, ★ in the list), saved with the character.
        internal const string FavKey = "jatify.favs";
        internal static string Favs => Player.m_localPlayer != null && Player.m_localPlayer.m_customData.TryGetValue(FavKey, out var v) ? v : "";
        internal static bool IsFav(Recipe r) => r != null && ("," + Favs + ",").Contains("," + r.name + ",");
        internal static void ToggleFav(Recipe r)
        {
            var p = Player.m_localPlayer; if (p == null || r == null) return;
            var set = new List<string>(Favs.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (!set.Remove(r.name)) set.Add(r.name);
            if (set.Count == 0) p.m_customData.Remove(FavKey); else p.m_customData[FavKey] = string.Join(",", set);
        }

        private struct Entry { public Recipe Recipe; public ItemDrop.ItemData Item; public bool Can; public int Index; }

        internal RecipeView(RectTransform crafting, ThemeData t, Ledger l)
        {
            _t = t;
            var vanilla = crafting.Find("RecipeList");
            if (vanilla != null)
            {
                var cg = vanilla.GetComponent<CanvasGroup>() ?? l.Added(vanilla.gameObject.AddComponent<CanvasGroup>());
                var (a0, b0) = (cg.alpha, cg.blocksRaycasts);
                l.Do(() => { cg.alpha = 0f; cg.blocksRaycasts = false; }, () => { if (cg != null) { cg.alpha = a0; cg.blocksRaycasts = b0; } });
            }
            // design: list column 210 wide + 18/10 padding, a line on its right; here 236 wide from x 4, y 178
            _root = Kit.Node(crafting, "jatify-recipes"); l.Own(_root);
            _root.At(4, 196, 236, 590);
            var hit = _root.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            _root.gameObject.AddComponent<RectMask2D>();
            var sep = Kit.Img(crafting, "jatify-listline", null, Theme.C(t.glassLine)); l.Own(sep); sep.rectTransform.At(244, 191, 1, 599);
            _content = Kit.Node(_root, "Content");
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f); _content.anchoredPosition = Vector2.zero;
            var sr = _root.gameObject.AddComponent<ScrollRect>(); sr.content = _content; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
        }

        private static List<Entry> Read(InventoryGui g)
        {
            var list = new List<Entry>();
            if (!(Avail.GetValue(g) is IList raw)) return list;
            for (int i = 0; i < raw.Count; i++)
            {
                var tr = Traverse.Create(raw[i]);
                list.Add(new Entry { Recipe = tr.Property<Recipe>("Recipe").Value, Item = tr.Property<ItemDrop.ItemData>("ItemData").Value, Can = tr.Property<bool>("CanCraft").Value, Index = i });
            }
            return list;
        }

        internal void Tick(InventoryGui g)
        {
            if (g == null || !InventoryGui.IsVisible()) return;
            var entries = Read(g);
            int sel = (int)Selected.Invoke(g, new object[] { false });
            var key = CraftFilter.Grid + "|" + CraftFilter.ByAvail + "|" + Favs + "|" + sel + "|" + string.Join(",", entries.Select(e => e.Recipe.name + (e.Can ? "1" : "0") + (e.Item?.m_quality ?? 0)));
            if (key == _key) return;                       // rebuilt only when the list or selection changes
            _key = key;
            foreach (Transform c in _content) Object.Destroy(c.gameObject);
            var ready = entries.Where(e => e.Can).ToList(); var missing = entries.Where(e => !e.Can).ToList();
            float y = 6f;
            if (CraftFilter.ByAvail)
            {
                y = Section($"READY TO CRAFT · {ready.Count}", Theme.C(_t.accent), ready, sel, g, y);
                y = Section($"MISSING SOMETHING · {missing.Count}", Theme.C(_t.ember), missing, sel, g, y + 8f);
            }
            else y = Section($"ALL RECIPES · {entries.Count}", Theme.C(_t.mute), entries, sel, g, y);
            _content.sizeDelta = new Vector2(0f, y + 10f);
        }

        private float Section(string title, Color c, List<Entry> list, int sel, InventoryGui g, float y)
        {
            if (list.Count == 0) return y;
            var l = Kit.Text(_content, "Label", title, Fonts.Mono, 10.5f, c); l.characterSpacing = 8f; l.rectTransform.At(10, y, 220, 16);
            y += 22f;
            if (CraftFilter.Grid)
            {
                for (int i = 0; i < list.Count; i++) Cell(list[i], 10 + (i % 4) * 47, y + (i / 4) * 47, list[i].Index == sel, g);
                return y + ((list.Count + 3) / 4) * 47f;
            }
            foreach (var e in list) y = Row(e, y, e.Index == sel, g);
            return y;
        }

        private float Row(Entry e, float y, bool on, InventoryGui g)
        {
            var miss = e.Can ? "" : Missing(e);
            float h = miss.Length > 0 ? 46f : 40f;
            var (bg, fill) = Kit.Box(_content, "Row", on ? Theme.C(_t.accentSoft) : new Color(0, 0, 0, 0), on ? Theme.C(_t.accent) : new Color(0, 0, 0, 0), 8);
            bg.rectTransform.At(4, y, 226, h);
            var icon = Kit.Img(bg.rectTransform, "Icon", e.Recipe.m_item.m_itemData.GetIcon(), e.Can ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.9f), false);
            icon.rectTransform.At(8, (h - 28) / 2f, 28, 28); icon.preserveAspect = true;
            var name = Kit.Text(bg.rectTransform, "Name", Name(e), Fonts.Medium, 13.5f, e.Can ? Theme.C(_t.text) : Theme.C(_t.mute));
            bool fav = IsFav(e.Recipe);
            name.rectTransform.At(46, miss.Length > 0 ? 6 : 0, fav ? 160 : 176, miss.Length > 0 ? 20 : h); name.alignment = TextAlignmentOptions.MidlineLeft; name.overflowMode = TextOverflowModes.Ellipsis;
            if (fav) { var fs = Kit.Img(bg.rectTransform, "Fav", Kit.Star(), Theme.C(_t.stamina), false); fs.rectTransform.At(208, miss.Length > 0 ? 11 : (h - 10) / 2f, 10, 10); }
            if (miss.Length > 0) { var m = Kit.Text(bg.rectTransform, "Missing", "Missing: " + miss, Fonts.Medium, 11f, Theme.C(_t.ember)); m.rectTransform.At(46, 25, 176, 16); m.overflowMode = TextOverflowModes.Ellipsis; }
            Click(bg, fill, e, g, on);
            return y + h + 4f;
        }

        private void Cell(Entry e, float x, float y, bool on, InventoryGui g)
        {
            if (on) { var glow = Kit.Panel(_content, "Glow", Theme.C(_t.accentSoft), 11); glow.rectTransform.At(x - 3, y - 3, 48, 48); }
            var (bg, fill) = Kit.Box(_content, "Cell", Theme.C(_t.raised), on ? Theme.C(_t.accent) : Theme.C(_t.glassLine), 9);
            bg.rectTransform.At(x, y, 42, 42);
            var icon = Kit.Img(bg.rectTransform, "Icon", e.Recipe.m_item.m_itemData.GetIcon(), e.Can ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f), false);
            icon.rectTransform.At(6, 6, 30, 30); icon.preserveAspect = true;
            if (e.Recipe.m_amount > 1) { var c = Kit.Text(bg.rectTransform, "Count", e.Recipe.m_amount.ToString(), Fonts.Mono, 10f, Color.white); c.alignment = TextAlignmentOptions.TopRight; c.rectTransform.At(2, 1, 37, 14); }
            if (IsFav(e.Recipe)) { var fs = Kit.Img(bg.rectTransform, "Fav", Kit.Star(), Theme.C(_t.stamina), false); fs.rectTransform.At(4, 4, 9, 9); }
            if (!e.Can) { var dot = Kit.Img(bg.rectTransform, "Dot", Kit.Circle(), Theme.C(_t.ember), false); dot.rectTransform.At(32, 32, 6, 6); }
            Click(bg, fill, e, g, on);
            var tip = bg.gameObject.AddComponent<UITooltip>(); var vt = g.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>();
            if (vt != null) { tip.m_tooltipPrefab = vt.m_tooltipPrefab; tip.Set(Name(e), e.Can ? "" : "Missing: " + Missing(e)); ItemTip.For(tip, () => e.Recipe.m_item.m_itemData, e.Item != null ? e.Item.m_quality + 1 : 1, crafting: true, note: e.Can ? null : "Missing: " + Missing(e)); }
        }

        private static void Click(Image bg, Image fill, Entry e, InventoryGui g, bool on)
        {
            bg.raycastTarget = true;
            var b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = fill;
            var cb = b.colors; cb.highlightedColor = new Color(1.35f, 1.35f, 1.4f); b.colors = cb;
            int idx = e.Index; b.onClick.AddListener(() => SetRecipe.Invoke(g, new object[] { idx, false }));
        }

        private static string Name(Entry e)
        {
            var n = Localization.instance.Localize(e.Recipe.m_item.m_itemData.m_shared.m_name);
            if (e.Item != null) n += $" · level {e.Item.m_quality + 1}";
            else if (e.Recipe.m_amount > 1) n += " ×" + e.Recipe.m_amount;
            return n;
        }

        private static string Missing(Entry e)
        {
            var p = Player.m_localPlayer; if (p == null) return "";
            int q = e.Item != null ? e.Item.m_quality + 1 : 1;
            var parts = new List<string>();
            foreach (var r in e.Recipe.m_resources)
            {
                if (!Stock.Applies(p, r)) continue;   // upgrader resources only at an upgrader station
                int need = r.GetAmount(q), have = Stock.Count(p, r.m_resItem.m_itemData.m_shared.m_name);
                if (need > have) parts.Add($"{Localization.instance.Localize(r.m_resItem.m_itemData.m_shared.m_name)} ×{need - have}");
            }
            return string.Join(", ", parts);
        }
    }
}
