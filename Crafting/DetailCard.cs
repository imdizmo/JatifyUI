using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The crafting detail card from the design: 60 px icon tile, name, type line, italic description,
    // "Damage & defence" and "Stats" as label/value tables. It reads the tooltip text
    // Valheim already put in the panel (so other mods' stat lines show too) and sorts the lines by
    // Valheim's own localized stat names. Valheim's icon/name/text are shrunk away (ledger);
    // its requirements and Craft button stay below and keep working.
    internal sealed class DetailCard
    {
        private static readonly Regex Tags = new Regex("</?[A-Za-z#][^<>\\n]*>", RegexOptions.Compiled);
        private static readonly string[] DamageKeys = { "$inventory_damage", "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_chop", "$inventory_pickaxe", "$inventory_fire", "$inventory_frost", "$inventory_lightning", "$inventory_poison", "$inventory_spirit" };
        private static readonly string[] DefenceKeys = { "$item_blockarmor", "$item_blockforce", "$item_parrybonus", "$item_parryadrenaline", "$item_armor", "$item_deflection", "$item_knockback", "$item_backstab" };
        private readonly RectTransform _root, _view;
        private readonly InventoryGui _g;
        private readonly ThemeData _t;
        private string _shown;
        // amount stepper: qty > 1 = Valheim's own multi-craft with m_multiCraftAmount = qty
        private static readonly HarmonyLib.AccessTools.FieldRef<InventoryGui, bool> TouchMulti = HarmonyLib.AccessTools.FieldRefAccess<InventoryGui, bool>("m_touchMultiCrafting");
        private static readonly HarmonyLib.AccessTools.FieldRef<InventoryGui, float> CraftTimer = HarmonyLib.AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");
        private readonly int _baseMulti;
        private int _qty = 1, _qtyShown = -1;
        private TextMeshProUGUI _qtyText, _note; private int _queueShown = -1;
        private readonly RectTransform _reqs; private readonly TextMeshProUGUI _reqLabel;
        private string _reqKey;

        internal DetailCard(RectTransform desc, InventoryGui g, ThemeData t, Ledger l)
        {
            _g = g; _t = t;
            foreach (var r in new[] { g.m_recipeIcon?.rectTransform, g.m_recipeName?.rectTransform, g.m_recipeDecription?.rectTransform })
                l.Rect(r, x => x.localScale = Vector3.zero);
            // a scrolling view above REQUIRES (long stat lists scroll instead of running under the chips)
            _view = Kit.Node(desc, "jatify-detail"); l.Own(_view);
            _view.anchorMin = new Vector2(0f, 0f); _view.anchorMax = new Vector2(1f, 1f); _view.offsetMin = new Vector2(0f, 262f); _view.offsetMax = Vector2.zero;
            _view.gameObject.AddComponent<RectMask2D>();
            var hit = _view.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            _root = Kit.Node(_view, "Content");
            _root.anchorMin = new Vector2(0f, 1f); _root.anchorMax = new Vector2(1f, 1f); _root.pivot = new Vector2(0.5f, 1f); _root.anchoredPosition = Vector2.zero;
            var sr = _view.gameObject.AddComponent<ScrollRect>(); sr.content = _root; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            var req = _reqLabel = Kit.Text(desc, "jatify-requires", "REQUIRES", Fonts.Mono, 10.5f, Theme.C(t.mute)); l.Own(req); req.characterSpacing = 8f;
            req.rectTransform.anchorMin = req.rectTransform.anchorMax = req.rectTransform.pivot = new Vector2(0f, 0f); req.rectTransform.anchoredPosition = new Vector2(14f, 244f); req.rectTransform.sizeDelta = new Vector2(200f, 14f);
            l.Rect(desc.Find("requirements") as RectTransform, r => r.localScale = Vector3.zero);   // replaced by our chips
            // design: two columns of chips (icon, name, "need / have N", tick) between REQUIRES and the stepper row
            _reqs = Kit.Node(desc, "jatify-reqs"); l.Own(_reqs);
            _reqs.anchorMin = _reqs.anchorMax = _reqs.pivot = new Vector2(0f, 0f); _reqs.anchoredPosition = new Vector2(14f, 100f); _reqs.sizeDelta = new Vector2(358f, 136f);
            var note = _note = Kit.Text(desc, "jatify-craftnote", "Crafted items go to your backpack.", Fonts.Medium, 11.5f, Theme.C(t.mute)); l.Own(note); note.richText = true;
            note.rectTransform.anchorMin = note.rectTransform.anchorMax = note.rectTransform.pivot = new Vector2(0f, 0f); note.rectTransform.anchoredPosition = new Vector2(14f, 14f); note.rectTransform.sizeDelta = new Vector2(CraftQueue.On.Value ? 272f : 358f, 28f);
            _baseMulti = g.m_multiCraftAmount;
            int base0 = _baseMulti;
            l.Do(() => { }, () => { if (g != null) { g.m_multiCraftAmount = base0; TouchMulti(g) = false; } });
            // craft button narrower, stepper to its right
            l.Rect(desc.Find("craft_button_panel") as RectTransform, r => { r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(1f, 0f); r.offsetMin = new Vector2(14f, 50f); r.offsetMax = new Vector2(-214f, 90f); });
            if (CraftQueue.On.Value)
            {   // Queue: the selected recipe with the stepper's amount, crafted after what's running
                var qb = Kit.Node(desc, "jatify-queue"); l.Own(qb);
                qb.anchorMin = qb.anchorMax = qb.pivot = new Vector2(1f, 0f); qb.anchoredPosition = new Vector2(-14f, 6f); qb.sizeDelta = new Vector2(76f, 40f);   // beside the note: Craft keeps the row (user: "Missing materials" was squeezed)
                Btn(qb, "Queue", 0, 76, () => { var r = _g.InCraftTab() ? Selected() : null; if (r != null) CraftQueue.Add(r, _qty); });
            }
            var step = Kit.Node(desc, "jatify-stepper"); l.Own(step);
            step.anchorMin = step.anchorMax = step.pivot = new Vector2(1f, 0f); step.anchoredPosition = new Vector2(-14f, 50f); step.sizeDelta = new Vector2(192f, 40f);
            // design: − 1 + Max ↺, 40 tall, the number without a box
            Btn(step, "−", 0, 34, () => SetQty(_qty - 1));
            _qtyText = Kit.Text(step, "N", "1", Fonts.Mono, 15f, Color.white); _qtyText.alignment = TextAlignmentOptions.Center; _qtyText.rectTransform.At(36, 0, 30, 40);
            Btn(step, "+", 68, 34, () => SetQty(_qty + 1));
            Btn(step, "Max", 106, 48, () => SetQty(MaxQty()));
            var (rb, rf) = Kit.Box(step, "Reset", Theme.C(t.raised), Theme.C(t.line2), 8); rb.rectTransform.At(158, 0, 34, 40);
            var ri = Kit.IconImg(rb.rectTransform, "undo", Theme.C(t.dim), 16f); ri.rectTransform.At(9, 12, 16, 16);
            rb.raycastTarget = true; var rbtn = rb.gameObject.AddComponent<Button>(); rbtn.targetGraphic = rf; rbtn.onClick.AddListener(() => SetQty(1));
        }

        // design: ★ button at the top right of the card header
        private void FavBtn(Recipe r)
        {
            bool on = RecipeView.IsFav(r);
            var (b, f) = Kit.Box(_root, "Fav", on ? Theme.C(_t.raised) : new Color(0, 0, 0, 0), on ? Theme.C(_t.stamina) : Theme.C(_t.line2), 8); b.rectTransform.At(330, 20, 34, 34);
            var s = Kit.Img(b.rectTransform, "S", Kit.Star(), on ? Theme.C(_t.stamina) : Theme.C(_t.mute), false); s.rectTransform.At(9, 9, 16, 16);
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f; btn.onClick.AddListener(() => RecipeView.ToggleFav(r));
        }

        private void Btn(RectTransform parent, string label, float x, float w, Action on)
        {
            var (b, f) = Kit.Box(parent, label, Theme.C(_t.raised), Theme.C(_t.line2), 8); b.rectTransform.At(x, 0, w, 40);
            var tx = Kit.Text(b.rectTransform, "T", label, label == "Max" ? Fonts.Medium : Fonts.Bold, label == "Max" ? 13f : 16f, Theme.C(_t.text)); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f; btn.onClick.AddListener(() => on());
        }

        private void SetQty(int q)
        {
            if (CraftTimer(_g) >= 0f) return;                       // not while a craft is running
            _qty = Mathf.Clamp(q, 1, Mathf.Max(1, MaxQty()));
        }

        private int MaxQty()
        {
            var p = Player.m_localPlayer; var r = _g.InCraftTab() ? Selected() : null;
            if (p == null || r == null) return 1;
            int max = 99;
            foreach (var res in r.m_resources)
            {
                if (!Stock.Applies(p, res)) continue;   // upgrader resources only at an upgrader station (Valheim's rule)
                int need = res.GetAmount(1); if (need <= 0) continue;
                max = Mathf.Min(max, Stock.Count(p, res.m_resItem.m_itemData.m_shared.m_name) / need);
            }
            return Mathf.Max(1, max);
        }

        private static readonly System.Reflection.FieldInfo SelField = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private Recipe Selected() { var v = SelField.GetValue(_g); return v == null ? null : HarmonyLib.Traverse.Create(v).Property<Recipe>("Recipe").Value; }

        internal void Tick()
        {
            if (!_g.InCraftTab() && _qty != 1) _qty = 1;
            if (CraftTimer(_g) < 0f)
            {   // apply before Valheim reads it on the Craft click
                if (CraftQueue.Forced == 0)
                {
                    _g.m_multiCraftAmount = _qty > 1 ? _qty : _baseMulti;
                    TouchMulti(_g) = _qty > 1;
                }
            }
            if (_qty != _qtyShown && _qtyText != null) { _qtyShown = _qty; _qtyText.text = _qty.ToString(); }
            int qh = 0; foreach (var q in CraftQueue.Items) qh = qh * 31 + q.qty + q.recipe.GetInstanceID();
            if (qh != _queueShown && _note != null)
            {   // what's waiting, in place of the note
                _queueShown = qh;
                if (CraftQueue.Items.Count == 0) _note.text = "Crafted items go to your backpack.";
                else
                {
                    var parts = new List<string>(); foreach (var q in CraftQueue.Items) parts.Add($"{Localization.instance.Localize(q.recipe.m_item.m_itemData.m_shared.m_name)} \u00d7{q.qty}");
                    _note.text = $"<color=#{ColorUtility.ToHtmlStringRGB(Theme.C(_t.accent))}>Next:</color> " + string.Join(" \u00b7 ", parts);
                }
            }
            Reqs();
            var text = _g.m_recipeDecription != null ? _g.m_recipeDecription.text : "";
            var name = _g.m_recipeName != null ? _g.m_recipeName.text : "";
            var rec = _g.InCraftTab() ? Selected() : null;
            var key = name + "\n" + text + RecipeView.IsFav(rec);
            if (key == _shown) return;
            _shown = key;
            foreach (Transform c in _root) UnityEngine.Object.Destroy(c.gameObject);
            if (string.IsNullOrEmpty(name)) return;
            Build(name, _g.m_recipeIcon != null ? _g.m_recipeIcon.sprite : null, text);
            if (rec != null) FavBtn(rec);
        }

        private void Build(string name, Sprite icon, string tooltip)
        {
            var dmg = new HashSet<string>(); foreach (var k in DamageKeys) dmg.Add(Localization.instance.Localize(k));
            var def = new HashSet<string>(); foreach (var k in DefenceKeys) def.Add(Localization.instance.Localize(k));
            var lines = Tags.Replace(tooltip ?? "", "").Split('\n');
            var damage = new List<(string, string)>(); var stats = new List<(string, string)>(); var notes = new List<string>(); string desc = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim(); if (line.Length == 0) continue;
                int i = line.IndexOf(": ", StringComparison.Ordinal);
                if (i <= 0) { if (desc == null && line.Length > 30) desc = line; else notes.Add(line); continue; }
                var label = line.Substring(0, i).Trim(); var value = line.Substring(i + 2).Trim();
                (dmg.Contains(label) || def.Contains(label) ? damage : stats).Add((label, value));
            }
            // header: icon tile, name, type line
            var (tile, _) = Kit.Box(_root, "Icon", Theme.C(_t.raised), Theme.C(_t.glassLine), 10); tile.rectTransform.At(18, 16, 60, 60);
            if (icon != null) { var ic = Kit.Img(tile.rectTransform, "I", icon, Color.white, false); ic.rectTransform.At(9, 9, 42, 42); ic.preserveAspect = true; }
            var n = Kit.Text(_root, "Name", name, Fonts.Bold, 18f, Color.white); n.rectTransform.At(90, 20, 232, 26); n.overflowMode = TextOverflowModes.Ellipsis;
            // the item's own words under its name (type, short description): Averia italic like the description, two lines (user)
            var type = Kit.Text(_root, "Type", string.Join(" · ", notes), Fonts.Lore, 13.5f, Theme.C(_t.mute)); type.fontStyle = FontStyles.Italic;
            type.textWrappingMode = TextWrappingModes.Normal; type.overflowMode = TextOverflowModes.Ellipsis; type.maxVisibleLines = 2; type.lineSpacing = -6f;
            type.rectTransform.At(90, 45, 232, 40);   // Averia's line is taller than Space Grotesk's: an 18 px box drew nothing
            float y = 90f;
            if (Knots.On) { Knots.DividerAt(_root, 18f, 88f, 350f, false, Theme.C(_t.accent)); y = 98f; }   // design .kdiv: a small knot between the name and the stats
            if (desc != null)
            {
                var d = Kit.Text(_root, "Desc", desc, Fonts.Lore, 14f, Theme.C(_t.dim)); d.fontStyle = FontStyles.Italic;
                d.textWrappingMode = TextWrappingModes.Normal; d.rectTransform.At(18, y, 350, 40);
                y += Mathf.Min(60f, d.GetPreferredValues(desc, 350f, 0f).y) + 12f;
            }
            y = Table("DAMAGE & DEFENCE", damage, y);
            y = Table("STATS", stats, y + 6f);
            _root.sizeDelta = new Vector2(0f, y + 8f); _root.anchoredPosition = Vector2.zero;
        }

        private static readonly System.Text.StringBuilder Sb = new System.Text.StringBuilder();

        private void Reqs()
        {
            var p = Player.m_localPlayer; var r = Selected();
            int q = 1, mult = 1;
            if (r != null && !_g.InCraftTab()) { var it = SelectedItem(); q = it != null ? it.m_quality + 1 : 1; } else mult = _qty;
            Sb.Clear();
            if (p != null && r != null)
            {
                Sb.Append(r.name).Append(q).Append('x').Append(mult);
                foreach (var res in r.m_resources) if (Stock.Applies(p, res)) Sb.Append('|').Append(Stock.Count(p, res.m_resItem.m_itemData.m_shared.m_name));
            }
            var key = Sb.ToString();
            if (key == _reqKey) return;
            _reqKey = key;
            foreach (Transform c in _reqs) UnityEngine.Object.Destroy(c.gameObject);
            if (p == null || r == null) return;
            int n = 0;
            foreach (var res in r.m_resources)
            {
                if (!Stock.Applies(p, res)) continue;   // upgrader resources only at an upgrader station (Valheim's rule)
                int need = res.GetAmount(q) * mult; if (need <= 0) continue;
                var data = res.m_resItem.m_itemData; int have = Stock.Count(p, data.m_shared.m_name); bool ok = have >= need;
                // design .reqchip: 45 tall, 26 px icon, name over "need / have N", a green tick at the right
                var (chip, _) = Kit.Box(_reqs, "Req", Theme.C(_t.raised), Theme.C(_t.glassLine), 9); chip.rectTransform.At((n % 2) * 183, (n / 2) * 51, 175, 45);
                var ic = Kit.Img(chip.rectTransform, "I", data.GetIcon(), Color.white, false); ic.rectTransform.At(8, 9.5f, 26, 26); ic.preserveAspect = true;
                var nm = Kit.Text(chip.rectTransform, "N", Localization.instance.Localize(data.m_shared.m_name), Fonts.Medium, 13f, Theme.C(_t.text)); nm.rectTransform.At(42, 5, 104, 18); nm.overflowMode = TextOverflowModes.Ellipsis;
                var hv = Kit.Text(chip.rectTransform, "H", $"{need} / have {have}", Fonts.Mono, 11f, ok ? Theme.C(_t.mute) : Theme.C(_t.ember)); hv.rectTransform.At(42, 23, 110, 16);
                if (ok) { var tk = Kit.IconImg(chip.rectTransform, "check", Theme.C(_t.accent), 18f); tk.rectTransform.At(149, 13.5f, 18, 18); }
                if (++n == 4) break;                         // vanilla shows at most four too
            }
            // the block grows up from the craft row: one or two chip rows, REQUIRES above them, the card above that
            float h = n > 2 ? 96f : 45f;
            _reqs.sizeDelta = new Vector2(358f, h);
            _reqLabel.rectTransform.anchoredPosition = new Vector2(14f, 100f + h + 6f);
            _view.offsetMin = new Vector2(0f, 100f + h + 26f);
        }

        private ItemDrop.ItemData SelectedItem() { var v = SelField.GetValue(_g); return v == null ? null : HarmonyLib.Traverse.Create(v).Property<ItemDrop.ItemData>("ItemData").Value; }

        private float Table(string title, List<(string, string)> rows, float y)
        {
            if (rows.Count == 0) return y;
            var l = Kit.Text(_root, "Label", title, Fonts.Mono, 10.5f, Theme.C(_t.mute)); l.characterSpacing = 8f; l.rectTransform.At(18, y, 300, 14);
            y += 20f;
            foreach (var (label, value) in rows)
            {
                var a = Kit.Text(_root, "K", label, Fonts.Medium, 13f, Theme.C(_t.dim)); a.rectTransform.At(18, y, 200, 20);
                int p = value.IndexOf(" (", StringComparison.Ordinal);
                string main = p > 0 ? value.Substring(0, p) : value, extra = p > 0 ? value.Substring(p + 1) : "";
                var v = Kit.Text(_root, "V", extra.Length > 0 ? $"{main} <size=11><color=#{ColorUtility.ToHtmlStringRGB(Theme.C(_t.mute))}>{extra}</color></size>" : main, Fonts.Mono, 13f, Color.white);
                v.richText = true; v.alignment = TextAlignmentOptions.MidlineRight; v.rectTransform.At(168, y, 200, 20);
                var sep = Kit.Img(_root, "Line", null, new Color(1f, 1f, 1f, 0.05f)); sep.rectTransform.At(18, y + 21, 350, 1);
                y += 24f;
            }
            return y;
        }
    }
}
