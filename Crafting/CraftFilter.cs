using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Crafting panel extras from the design: search, type chips, "Can craft", list / grid view.
    // Filtering happens on the recipe list Valheim is about to show (UpdateRecipeList prefix), so
    // selection, crafting and sorting stay vanilla. Grid view re-lays the rows Valheim just built as
    // 44 px icon tiles (they are rebuilt on every refresh, so nothing needs undoing).
    internal static class CraftFilter
    {
        internal static string Query = "";
        internal static int Cat;            // index into Cats
        internal static bool ReadyOnly, Grid, ByAvail = true;
        internal static readonly string[] Cats = { "All", "Weapons", "Armour", "Tools", "Food", "Other" };
        private static readonly MethodInfo Refresh = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");
        private static readonly List<GameObject> _ui = new List<GameObject>();
        private static readonly List<(Image bg, TextMeshProUGUI tx, int i)> _chips = new List<(Image, TextMeshProUGUI, int)>();
        private static Image _readyBg, _listBg, _gridBg, _availBg; private static TextMeshProUGUI _readyTx, _availTx;
        private static ThemeData _t;

        internal static void Build(RectTransform c, ThemeData t, Ledger l)
        {
            _t = t; Destroy();
            // search (left) + list/grid buttons (right), at y 96
            var (box, _) = Kit.Box(c, "jatify-search", Theme.C(t.raised), Theme.C(t.line2), 8); box.rectTransform.At(5, 101, 540, 36); _ui.Add(box.gameObject);
            var mag = Kit.IconImg(box.rectTransform, "search", Theme.C(t.mute), 17f); mag.rectTransform.At(10, 9, 18, 18);
            var area = Kit.Node(box.rectTransform, "Area").Fill(); area.offsetMin = new Vector2(34, 4); area.offsetMax = new Vector2(-70, -4);
            area.gameObject.AddComponent<RectMask2D>();
            var tx = Kit.Text(area, "Text", "", Fonts.Medium, 14f, Color.white); tx.rectTransform.Fill(); tx.alignment = TextAlignmentOptions.MidlineLeft;
            var ph = Kit.Text(area, "Placeholder", "Search recipes", Fonts.Medium, 14f, Theme.C(t.mute)); ph.rectTransform.Fill(); ph.alignment = TextAlignmentOptions.MidlineLeft;
            box.raycastTarget = true;   // the field only gets clicks through a raycast graphic
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area; field.textComponent = tx; field.placeholder = ph; field.fontAsset = Fonts.Medium; field.pointSize = 14f; field.text = Query;
            Kit.Caret(field);
            field.onValueChanged.AddListener(s => { Query = s ?? ""; Apply(); });
            _field = field;
            // design: the shortcut as a key cap inside the field
            var (cap, _) = Kit.Box(box.rectTransform, "Key", new Color(1f, 1f, 1f, 0.08f), Theme.C(t.line2), 5);
            var ct = Kit.Text(cap.rectTransform, "K", "Ctrl F", Fonts.Mono, 11f, Theme.C(t.dim)); ct.alignment = TextAlignmentOptions.Center; ct.rectTransform.Fill();
            float cw = Kit.W(ct) + 12f; cap.rectTransform.At(540 - 8 - cw, 8.5f, cw, 19);
            // list / grid: one segmented control, the open one white (design .iconbtns)
            var (seg, _) = Kit.Box(c, "jatify-viewseg", Theme.C(t.raised), Theme.C(t.glassLine), 8); seg.rectTransform.At(569, 103, 68, 32); _ui.Add(seg.gameObject);
            _listBg = IconBtn(seg.rectTransform, "List", 3, t, () => { Grid = false; Apply(); });
            _gridBg = IconBtn(seg.rectTransform, "Grid", 35, t, () => { Grid = true; Apply(); });
            // chips at y 138
            float x = 4f;
            for (int i = 0; i < Cats.Length; i++)
            {
                int idx = i;
                var (bg, txt) = Chip(c, Cats[i], ref x, t, () => { Cat = idx; Apply(); });
                _chips.Add((bg, txt, i));
            }
            x += 8f;
            (_readyBg, _readyTx) = Chip(c, "Can craft", ref x, t, () => { ReadyOnly = !ReadyOnly; Apply(); });
            (_availBg, _availTx) = Chip(c, "By availability", ref x, t, () => { ByAvail = !ByAvail; Apply(); });
            Paint();
        }

        private static Image IconBtn(RectTransform c, string label, float x, ThemeData t, System.Action on)
        {
            var b = Kit.Panel(c, "jatify-" + label, Theme.C(t.raised), 6); var f = b; b.rectTransform.At(x, 3, 30, 26);
            var ic = Kit.IconImg(b.rectTransform, label == "List" ? "view-list" : "view-grid", Theme.C(t.dim), 15f); ic.name = "Icon";
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = ic.rectTransform.pivot = new Vector2(0.5f, 0.5f); ic.rectTransform.anchoredPosition = Vector2.zero;
            b.raycastTarget = true; b.gameObject.AddComponent<Button>().onClick.AddListener(() => on());
            return f;
        }

        private static (Image, TextMeshProUGUI) Chip(RectTransform c, string label, ref float x, ThemeData t, System.Action on)
        {
            var tx = Kit.Text(c, "jatify-chip", label, Fonts.Medium, 12.5f, Theme.C(t.dim)); tx.alignment = TextAlignmentOptions.Center;
            float w = Kit.W(tx) + 22f;
            var bg = Kit.Panel(c, "jatify-chipbg", Theme.C(t.raised), 14); bg.rectTransform.At(x, 155, w, 28);
            tx.rectTransform.At(x, 155, w, 28); tx.transform.SetAsLastSibling();
            bg.raycastTarget = true; bg.gameObject.AddComponent<Button>().onClick.AddListener(() => on());
            _ui.Add(bg.gameObject); _ui.Add(tx.gameObject);
            x += w + 6f;
            return (bg, tx);
        }

        private static void Paint()
        {
            if (_t == null) return;
            foreach (var (bg, tx, i) in _chips) { bool on = i == Cat; if (bg) bg.color = on ? Color.white : Theme.C(_t.raised); if (tx) tx.color = on ? new Color(0.07f, 0.07f, 0.08f) : Theme.C(_t.dim); }
            if (_readyBg) { _readyBg.color = ReadyOnly ? Theme.C(_t.accent) : Theme.C(_t.raised); _readyTx.color = ReadyOnly ? new Color(0.02f, 0.08f, 0.05f) : Theme.C(_t.dim); }
            if (_availBg) { _availBg.color = ByAvail ? Color.white : Theme.C(_t.raised); _availTx.color = ByAvail ? new Color(0.07f, 0.07f, 0.08f) : Theme.C(_t.dim); }
            foreach (var (bg, on) in new[] { (_listBg, !Grid), (_gridBg, Grid) })
            {
                if (!bg) continue;
                bg.color = on ? Color.white : new Color(0, 0, 0, 0);
                var ic = bg.transform.Find("Icon")?.GetComponent<Image>(); if (ic) ic.color = on ? new Color(0.07f, 0.07f, 0.08f) : Theme.C(_t.dim);
            }
        }

        internal static void Poke() => Apply();
        private static TMP_InputField _field;
        internal static void SetQuery(string q) { if (_field != null) _field.text = q; else { Query = q; Apply(); } }
        internal static string FieldInfo() => _field == null ? "null" : $"{_field.name} active {_field.isActiveAndEnabled} interactable {_field.IsInteractable()}";
        internal static void FocusSearch() { if (_field != null) { _field.ActivateInputField(); _field.Select(); } }

        private static void Apply()
        {
            Paint();
            var g = InventoryGui.instance;
            if (g != null && InventoryGui.IsVisible() && Player.m_localPlayer != null) Refresh.Invoke(g, new object[] { false });
        }

        internal static bool Matches(Recipe r, Player p)
        {
            var it = r.m_item != null ? r.m_item.m_itemData : null; if (it == null) return true;
            if (Query.Length > 0 && Localization.instance.Localize(it.m_shared.m_name).IndexOf(Query, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (ReadyOnly && !p.HaveRequirements(r, false, 1)) return false;
            var k = it.m_shared.m_itemType;
            switch (Cats[Cat])
            {
                case "Weapons": return k == ItemDrop.ItemData.ItemType.OneHandedWeapon || k == ItemDrop.ItemData.ItemType.TwoHandedWeapon || k == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || k == ItemDrop.ItemData.ItemType.Bow || k == ItemDrop.ItemData.ItemType.Ammo || k == ItemDrop.ItemData.ItemType.Shield;
                case "Armour": return k == ItemDrop.ItemData.ItemType.Helmet || k == ItemDrop.ItemData.ItemType.Chest || k == ItemDrop.ItemData.ItemType.Legs || k == ItemDrop.ItemData.ItemType.Shoulder || k == ItemDrop.ItemData.ItemType.Utility || k == ItemDrop.ItemData.ItemType.Trinket;
                case "Tools": return k == ItemDrop.ItemData.ItemType.Tool || k == ItemDrop.ItemData.ItemType.Torch;
                case "Food": return k == ItemDrop.ItemData.ItemType.Consumable;
                case "Other": return k == ItemDrop.ItemData.ItemType.Material || k == ItemDrop.ItemData.ItemType.Misc || k == ItemDrop.ItemData.ItemType.AmmoNonEquipable || k == ItemDrop.ItemData.ItemType.Trophy;
                default: return true;
            }
        }

        // Grid view: rows -> 44 px tiles, 5 per row inside the 236 px list.
        internal static void LayGrid(InventoryGui g)
        {
            if (!Grid || g == null || g.m_recipeListRoot == null) return;
            var root = g.m_recipeListRoot;
            var rows = new List<RectTransform>();
            for (int i = 0; i < root.childCount; i++) rows.Add((RectTransform)root.GetChild(i));
            rows.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y)); // vanilla's sorted order (top first)
            int n = 0;
            foreach (var row in rows)
            {
                row.sizeDelta = new Vector2(44f, 44f);
                row.anchoredPosition = new Vector2((n % 5) * 46f + 2f, -(n / 5) * 46f - 2f);
                var name = row.Find("name"); if (name) name.gameObject.SetActive(false);
                if (row.Find("jatify-tile") == null && _t != null) { var tile = Kit.Panel(row, "jatify-tile", Theme.C(_t.raised), 8); tile.rectTransform.Fill(); tile.transform.SetAsFirstSibling(); }
                var icon = row.Find("icon") as RectTransform;
                if (icon) { icon.anchorMin = Vector2.zero; icon.anchorMax = Vector2.one; icon.offsetMin = new Vector2(6, 6); icon.offsetMax = new Vector2(-6, -6); }
                n++;
            }
            // ponytail: list height stays vanilla's (rows x 30, taller than the grid needs); resizing it moved the content off the top
        }

        internal static void Destroy()
        {
            foreach (var go in _ui) if (go) Object.Destroy(go);
            _ui.Clear(); _chips.Clear(); _readyBg = _listBg = _gridBg = _availBg = null; _readyTx = _availTx = null;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class RecipeListPatch
    {
        private static void Prefix(List<Recipe> recipes)
        {
            var p = Player.m_localPlayer; if (p == null) return;
            recipes.RemoveAll(r => !CraftFilter.Matches(r, p));
        }

    }
}
