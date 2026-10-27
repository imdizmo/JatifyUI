using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The detailed item tooltip (design "New: detailed tooltip"): Valheim keeps its tooltip's life (the hover, its delay, hiding)
    // and its text; while its tooltip is up for an item, ours shows next to the slot and Valheim's draws nothing. Ours reads
    // Valheim's tooltip text and sorts the lines by Valheim's own localized stat names (so other mods' stat lines show too); any
    // line it doesn't break down lands in the "more" block at the end. One card, built once, its rows pooled; refilled only when
    // the hovered item (or its stack, durability, quality, worn state, text) changes.
    internal static class ItemTip
    {
        // our own slots name their item with this (grid slots need none: their InventoryElement says where)
        internal sealed class Source : MonoBehaviour { internal System.Func<ItemDrop.ItemData> Item; internal int Quality; internal bool Crafting, Worn; internal string Note; }
        internal static Source For(UITooltip tip, System.Func<ItemDrop.ItemData> item, int quality = -1, bool crafting = false, bool worn = false, string note = null)
        {
            if (tip == null) return null;
            var s = tip.GetComponent<Source>() ?? tip.gameObject.AddComponent<Source>();
            s.Item = item; s.Quality = quality; s.Crafting = crafting; s.Worn = worn; s.Note = note; return s;
        }

        private const float W = 520f, Pad = 16f, Inner = W - 2f * Pad, ColGap = 18f, ColW = (Inner - ColGap) / 2f, RowH = 19f;
        private static readonly AccessTools.FieldRef<GameObject> Tip = AccessTools.StaticFieldRefAccess<GameObject>(AccessTools.Field(typeof(UITooltip), "m_tooltip"));
        private static readonly AccessTools.FieldRef<UITooltip> Cur = AccessTools.StaticFieldRefAccess<UITooltip>(AccessTools.Field(typeof(UITooltip), "m_current"));
        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Trail = new Regex(@"^(.*?)\s+([+\-−]\d[\d.,]*%?)$", RegexOptions.Compiled);
        private static readonly Regex Num = new Regex(@"[-+−]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        private sealed class Row { internal RectTransform Rt; internal TextMeshProUGUI K, V; }
        private sealed class Col { internal TextMeshProUGUI Sect; internal readonly List<Row> Rows = new List<Row>(); }
        private static RectTransform _root, _card, _head, _cmp, _keys, _knot; private static CanvasGroup _cg;
        private static Image _icon, _durBar, _durFill; private static RectTransform _badge; private static TextMeshProUGUI _badgeT, _name, _type, _wt, _lore, _note, _cmpTitle, _more, _moreT, _magic, _magicT; private static Image _magicCard;
        private static readonly Col _left = new Col(), _right = new Col(), _cmpCol = new Col();
        private static readonly List<(RectTransform box, TextMeshProUGUI t)> _caps = new List<(RectTransform, TextMeshProUGUI)>();
        private static readonly List<TextMeshProUGUI> _capLabels = new List<TextMeshProUGUI>();
        private static ThemeData _t;

        // what shows, and what it was built for
        private static UITooltip _for; private static Source _src; private static InventoryElement _el; private static InventoryGrid _grid;
        private static ItemDrop.ItemData _item; private static int _stack, _dur, _q; private static bool _eq, _knotOn; private static string _text;
        private static GameObject _hidden; private static CanvasGroup[] _inner; private static int _innerAt;

        // ---- every frame (LateUpdate) -------------------------------------------------------------
        internal static void Tick()
        {
            var tip = Tip(); var cur = Cur();
            if (tip == null || !tip.activeSelf || cur == null || Root.Canvas == null) { Hide(); return; }
            if (cur != _for) { _for = cur; _src = cur.GetComponent<Source>(); _el = _src == null ? cur.GetComponent<InventoryElement>() : null; _grid = _el != null ? _el.GetComponentInParent<InventoryGrid>() : null; _item = null; }
            var item = _src != null ? _src.Item?.Invoke() : _grid != null && _grid.GetInventory() != null ? _grid.GetInventory().GetItemAt(_el.Position.x, _el.Position.y) : null;
            if (item == null) { Hide(); return; }
            if (_root == null) Build();
            if (tip != _hidden) { _hidden = tip; var cg = tip.GetComponent<CanvasGroup>() ?? tip.AddComponent<CanvasGroup>(); cg.alpha = 0f; cg.blocksRaycasts = false; _inner = null; }   // Valheim's stays (its hover logic), unseen
            if (_inner == null || _innerAt != Time.frameCount / 30) { _innerAt = Time.frameCount / 30; _inner = tip.GetComponentsInChildren<CanvasGroup>(true); }   // a mod's box inside it with its own group (EpicLoot's TooltipBox ignores ours)
            foreach (var g in _inner) if (g != null && g.gameObject != tip && g.alpha != 0f) g.alpha = 0f;
            int dur = Mathf.RoundToInt(item.m_durability), q = _src != null && _src.Quality > 0 ? _src.Quality : item.m_quality;
            if (item != _item || item.m_stack != _stack || dur != _dur || q != _q || item.m_equipped != _eq || !ReferenceEquals(cur.m_text, _text) || Knots.On != _knotOn)
            {
                _item = item; _stack = item.m_stack; _dur = dur; _q = q; _eq = item.m_equipped; _text = cur.m_text; _knotOn = Knots.On;
                if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
                Fill(item, q, _src != null && _src.Crafting);
                Place((RectTransform)cur.transform);
            }
            if (!_root.gameObject.activeSelf) { _root.gameObject.SetActive(true); Place((RectTransform)cur.transform); }
        }

        private static void Hide()
        {
            _for = null; _item = null; _hidden = null;
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        // next to the slot (right of it, left when there is no room), clamped on screen
        private static readonly Vector3[] _c = new Vector3[4];
        private static void Place(RectTransform slot)
        {
            var canvas = (RectTransform)Root.Canvas.transform; var sc = slot.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = sc != null && sc.renderMode != RenderMode.ScreenSpaceOverlay ? sc.worldCamera : null;
            slot.GetWorldCorners(_c);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, RectTransformUtility.WorldToScreenPoint(cam, _c[1]), null, out var tl);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, RectTransformUtility.WorldToScreenPoint(cam, _c[3]), null, out var br);
            float cw = canvas.rect.width, ch = canvas.rect.height, h = _card.sizeDelta.y;
            float x = br.x + cw / 2f + 10f, y = ch / 2f - tl.y;   // design pixels from the top left
            if (x + W > cw - 8f) x = tl.x + cw / 2f - 10f - W;
            x = Mathf.Clamp(x, 8f, cw - 8f - W); y = Mathf.Clamp(y, 8f, Mathf.Max(8f, ch - 8f - h));
            _root.anchoredPosition = new Vector2(x, -y);
        }

        // ---- build once -------------------------------------------------------------------------------
        private static void Build()
        {
            _t = Plugin.Instance.ThemeData; var t = _t;
            _root = Kit.Node(Root.Canvas.transform, "jatify-itemtip"); _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
            Notices.OnTop(_root);   // over the inventory screen's canvas
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.interactable = false;
            var sh = Kit.Img(_root, "Shadow", Kit.Glow(), new Color(0f, 0f, 0f, 0.45f)); _shadow = sh.rectTransform;   // design: 0 14px 34px black 45 %
            var (line, _) = Kit.Box(_root, "Card", new Color(0.063f, 0.063f, 0.071f, 0.985f), CardLine, 12); _card = line.rectTransform; _card.At(0, 0, W, 100); _cardLine = line;
            var c = _card;
            var clip = Kit.Node(c, "RarityClip"); clip.Fill(); var cm = clip.gameObject.AddComponent<Image>(); cm.sprite = Kit.Rounded(12); cm.type = Image.Type.Sliced; clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;   // the wash keeps the card's round corners
            _glow = Kit.Img(clip, "Rarity", Kit.VGradient(new Color(1f, 1f, 1f, 0.2f), new Color(1f, 1f, 1f, 0.05f), new Color(1f, 1f, 1f, 0f)), Color.white, false); _glow.rectTransform.At(0, 0, W, 96); _glow.gameObject.SetActive(false);
            // header: icon tile with the quality pill, name, type line, weight / value
            _head = Kit.Node(c, "Head").At(Pad, 14, Inner, 52);
            var (tile, _) = Kit.Box(_head, "Icon", new Color(0.173f, 0.173f, 0.196f, 0.85f), TileLine, 10); tile.rectTransform.At(0, 0, 52, 52); _tileLine = tile;
            _icon = Kit.Img(tile.rectTransform, "I", null, Color.white, false); _icon.preserveAspect = true; _icon.rectTransform.At(6, 6, 40, 40);
            var (bd, _) = Kit.Box(_head, "Q", Theme.C(t.stamina), Theme.C(t.stamina), 9); _badge = bd.rectTransform;
            _badgeT = Kit.Text(_badge, "T", "", Fonts.Mono, 11f, new Color(0.067f, 0.067f, 0.067f)); _badgeT.fontStyle = FontStyles.Bold; _badgeT.alignment = TextAlignmentOptions.Center; _badgeT.rectTransform.Fill();
            _name = Kit.Text(_head, "Name", "", Fonts.Norse, 22f, Color.white); _name.characterSpacing = 4f; _name.overflowMode = TextOverflowModes.Ellipsis; _name.alignment = TextAlignmentOptions.BottomLeft;
            _type = Kit.Text(_head, "Type", "", Fonts.Medium, 12.5f, Theme.C(t.dim)); _type.richText = true; _type.overflowMode = TextOverflowModes.Ellipsis; _type.alignment = TextAlignmentOptions.TopLeft;
            _wt = Kit.Text(_head, "Weight", "", Fonts.Mono, 12f, Theme.C(t.dim)); _wt.alignment = TextAlignmentOptions.MidlineRight; _wt.lineSpacing = 10f;
            _knot = Knots.Loose(c, 240f, false, Theme.C(t.accent)); _knot.anchorMin = _knot.anchorMax = _knot.pivot = new Vector2(0.5f, 1f);   // design .kdiv: between the name and the body
            _lore = Kit.Text(c, "Lore", "", Fonts.Lore, 13f, new Color(0.812f, 0.812f, 0.831f)); _lore.fontStyle = FontStyles.Italic; _lore.textWrappingMode = TextWrappingModes.Normal;
            _note = Kit.Text(c, "Note", "", Fonts.Medium, 12.5f, Theme.C(t.ember)); _note.textWrappingMode = TextWrappingModes.Normal;
            // compared with what you hold / wear: a green box
            var (cb, _) = Kit.Box(c, "Compare", new Color(0.133f, 0.839f, 0.494f, 0.07f), new Color(0.133f, 0.839f, 0.494f, 0.25f), 9); _cmp = cb.rectTransform;
            _cmpTitle = Kit.Text(_cmp, "Title", "", Fonts.Mono, 10.5f, Theme.C(t.mute)); _cmpTitle.characterSpacing = 4f; _cmpTitle.overflowMode = TextOverflowModes.Ellipsis;
            Sect(_left, c); Sect(_right, c);
            var tr = Kit.Panel(c, "Durability", new Color(1f, 1f, 1f, 0.1f), 2); _durBar = tr;
            _durFill = Kit.Panel(tr.rectTransform, "V", Theme.C(t.accent), 2); var fr = _durFill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0.5f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            _moreT = Kit.Text(c, "MoreLabel", "MORE", Fonts.Mono, 11f, Theme.C(t.mute)); _moreT.characterSpacing = 8f;
            _magicCard = Kit.Panel(c, "MagicCard", Color.clear, 8); _magicCard.raycastTarget = false;   // EpicLoot's enchantments, sockets and sets: a card in the rarity colour
            _magicT = Kit.Text(_magicCard.rectTransform, "Label", "ENCHANTMENTS", Fonts.Mono, 10.5f, Theme.C(t.mute)); _magicT.characterSpacing = 8f;
            _magic = Kit.Text(_magicCard.rectTransform, "Lines", "", Fonts.Medium, 12.5f, Theme.C(t.text)); _magic.richText = true; _magic.textWrappingMode = TextWrappingModes.Normal; _magic.lineSpacing = 4f;
            _more = Kit.Text(c, "More", "", Fonts.Medium, 12.5f, Theme.C(t.dim)); _more.richText = true; _more.textWrappingMode = TextWrappingModes.Normal; _more.lineSpacing = 4f;
            _keys = Kit.Node(c, "Keys");
            var kl = Kit.Img(_keys, "Line", null, new Color(1f, 1f, 1f, 0.08f)); kl.rectTransform.anchorMin = new Vector2(0f, 1f); kl.rectTransform.anchorMax = Vector2.one; kl.rectTransform.pivot = new Vector2(0.5f, 1f); kl.rectTransform.sizeDelta = new Vector2(0f, 1f); kl.rectTransform.anchoredPosition = Vector2.zero;
            _root.gameObject.SetActive(false);
        }
        private static RectTransform _shadow;
        private static Image _cardLine, _tileLine, _glow;   // rarity (EpicLoot): border, icon tile edge, a wash down from the top
        private static readonly Color CardLine = new Color(1f, 1f, 1f, 0.14f), TileLine = new Color(1f, 1f, 1f, 0.1f);

        private static void Sect(Col col, Transform parent) { col.Sect = Kit.Text(parent, "Sect", "", Fonts.Mono, 11f, Theme.C(_t.mute)); col.Sect.characterSpacing = 8f; col.Sect.overflowMode = TextOverflowModes.Ellipsis; }

        private static Row RowAt(Col col, int i, Transform parent)
        {
            while (col.Rows.Count <= i)
            {
                var rt = Kit.Node(parent, "Row");
                var k = Kit.Text(rt, "K", "", Fonts.Medium, 13f, Theme.C(_t.dim)); k.alignment = TextAlignmentOptions.MidlineLeft; k.overflowMode = TextOverflowModes.Ellipsis; k.rectTransform.Fill();
                var v = Kit.Text(rt, "V", "", Fonts.Mono, 12.5f, Color.white); v.alignment = TextAlignmentOptions.MidlineRight; v.richText = true; v.rectTransform.Fill();
                col.Rows.Add(new Row { Rt = rt, K = k, V = v });
            }
            return col.Rows[i];
        }

        // ---- fill (only when the item changes) ------------------------------------------------------
        private enum Cat { More, Drop, Dur, Dmg, Parry, Block, Armour, Mod, Item, Food, Lasts }
        private static Dictionary<string, Cat> _cats; private static string _catsLang, _setLabel, _oneH, _twoH, _fullAdr;
        private static void Cats()
        {
            var lang = Localization.instance.GetSelectedLanguage(); if (_cats != null && lang == _catsLang) return;
            _catsLang = lang; _cats = new Dictionary<string, Cat>();
            void Add(Cat c, params string[] keys) { foreach (var k in keys) _cats[Localization.instance.Localize(k)] = c; }
            Add(Cat.Drop, "$item_weight", "$item_value", "$item_quality");
            Add(Cat.Dur, "$item_durability");
            Add(Cat.Dmg, "$inventory_damage", "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_chop", "$inventory_pickaxe", "$inventory_fire", "$inventory_frost", "$inventory_lightning", "$inventory_poison", "$inventory_spirit", "NonPlayer",
                "$item_knockback", "$item_backstab", "$item_damagemultipliertotal", "$item_damagemultiplierhp");
            Add(Cat.Parry, "$item_parrybonus");
            Add(Cat.Block, "$item_blockarmor", "$item_blockforce", "$item_parryadrenaline");
            Add(Cat.Armour, "$item_armor");
            Add(Cat.Mod, "$inventory_dmgmod");
            Add(Cat.Item, "$item_repairlevel", "$item_staminause", "$item_eitruse", "$item_healthuse", "$item_staminahold", "$item_healthhitreturn", "$item_eitrhitreturn", "$item_eitrregen_modifier",
                "$item_movement_modifier", "$base_item_modifier", "$item_heat_modifier", "$se_jumpstamina", "$se_attackstamina", "$se_blockstamina", "$se_dodgestamina", "$se_swimstamina", "$se_sneakstamina", "$se_runstamina", "$item_maxadrenaline");
            Add(Cat.Food, "$item_food_health", "$item_food_stamina", "$item_food_eitr");
            Add(Cat.Lasts, "$item_food_duration", "$item_food_regen");
            _setLabel = Localization.instance.Localize("$item_seteffect"); _oneH = Localization.instance.Localize("$item_onehanded"); _fullAdr = Localization.instance.Localize("$item_fulladrenaline"); _twoH = Localization.instance.Localize("$item_twohanded");
            _lowBetter = new HashSet<string>(); foreach (var k in new[] { "$item_staminause", "$item_eitruse", "$item_healthuse", "$item_staminahold", "$se_jumpstamina", "$se_attackstamina", "$se_blockstamina", "$se_dodgestamina", "$se_swimstamina", "$se_sneakstamina", "$se_runstamina" }) _lowBetter.Add(Localization.instance.Localize(k));
        }
        private static HashSet<string> _lowBetter;

        private struct Line { internal Cat C; internal string K, V; }
        private static readonly List<Line> _lines = new List<Line>(), _held = new List<Line>();
        private static readonly List<string> _moreLines = new List<string>(), _setFx = new List<string>(), _magicLines = new List<string>();
        private static readonly List<(string k, string v)> _l = new List<(string, string)>(), _r = new List<(string, string)>(), _cmpRows = new List<(string, string)>();

        // Valheim's tooltip text -> stat lines by kind; lines we don't know go to more (rich text kept), the set paragraph to setFx
        private static void Parse(string text, string desc, List<Line> into, List<string> more, List<string> setFx)
        {
            into.Clear(); more?.Clear(); setFx?.Clear();
            if (string.IsNullOrEmpty(text)) return;
            if (!string.IsNullOrEmpty(desc) && text.StartsWith(desc)) text = text.Substring(desc.Length);
            bool inSet = false;
            foreach (var raw in text.Split('\n'))
            {
                var plain = Tags.Replace(raw, "").Trim();
                if (plain.Length == 0) continue;
                if (plain.StartsWith(_setLabel)) { inSet = true; continue; }   // our set column shows it
                if (inSet && !plain.StartsWith(_fullAdr)) { setFx?.Add(plain); continue; }   // the set's effect runs to the end (its own blank lines too)
                inSet = false;
                if (plain == _oneH || plain == _twoH) continue;   // the type line says it
                int i = plain.IndexOf(':');
                if (i > 0 && _cats.TryGetValue(plain.Substring(0, i).Trim(), out var c)) { into.Add(new Line { C = c, K = plain.Substring(0, i).Trim(), V = plain.Substring(i + 1).Trim() }); continue; }
                more?.Add(raw.Replace("<color=orange>", "<color=#F5B942>").Replace("<color=yellow>", "<color=#B4B4BA>"));
            }
        }

        private static void Fill(ItemDrop.ItemData item, int q, bool crafting)
        {
            var t = _t; Cats(); var p = Player.m_localPlayer; var sh = item.m_shared;
            var epic = Epic.TipData(item, q);   // EpicLoot's magic name, rarity and effects, drawn here rather than parsed from its text
            string vanilla = _src == null && !string.IsNullOrEmpty(_for.m_text) ? Localization.instance.Localize(_for.m_text)
                : Localization.instance.Localize((epic != null ? Epic.TextWithout(item, q, crafting) : null) ?? ItemDrop.ItemData.GetTooltip(item, q, crafting, item.m_worldLevel));
            var desc = Localization.instance.Localize(sh.m_description);
            Parse(vanilla, desc, _lines, _moreLines, _setFx);
            EpicLines(epic);
            // header
            _icon.sprite = item.GetIcon();
            bool qual = sh.m_maxQuality > 1; _badge.gameObject.SetActive(qual);
            if (qual) { _badgeT.text = q.ToString(); float bw = Mathf.Max(18f, Kit.W(_badgeT) + 10f); _badge.At(52 + 6 - bw, -6, bw, 18); }
            bool magic = epic != null && epic.IsMagic && !string.IsNullOrEmpty(epic.DisplayName);
            var rarity = Epic.Color(epic);   // magic items and rarity materials
            Color rc = default; bool tint = rarity != null && ColorUtility.TryParseHtmlString(rarity, out rc);
            _cardLine.color = tint ? new Color(rc.r, rc.g, rc.b, 0.75f) : CardLine; _tileLine.color = tint ? new Color(rc.r, rc.g, rc.b, 0.9f) : TileLine;
            _glow.gameObject.SetActive(tint); if (tint) _glow.color = rc;
            var shown = magic ? epic.DisplayName : Localization.instance.Localize(sh.m_name);
            _name.text = rarity != null ? $"<color={rarity}>{shown}</color>" : shown;
            var station = Station(item, q); var typeName = magic && !string.IsNullOrEmpty(epic.RarityName) ? $"{(rarity != null ? $"<color={rarity}>{epic.RarityName}</color>" : epic.RarityName)} · {TypeName(item)}" : TypeName(item);
            _type.text = station != null ? $"{typeName} · <color=#{Hex(t.accent)}>{station}</color>" : typeName;
            float wt = crafting ? item.GetNonStackedWeight() : item.GetWeight();
            var w = $"{wt:0.0} kg"; if (!crafting && item.m_stack > 1) w += $"\n<size=11><color=#{Hex(t.mute)}>{item.m_stack} × {item.GetNonStackedWeight():0.0}</color></size>";
            if (sh.m_value > 0) w += $"\nvalue {sh.m_value}" + (item.m_stack > 1 && !crafting ? $" <color=#{Hex(t.mute)}>· {item.GetValue()}</color>" : "");
            _wt.text = w; _wt.richText = true; float ww = Mathf.Max(70f, Kit.W(_wt) + 4f);
            _wt.rectTransform.At(Inner - ww, 0, ww, 52); _name.rectTransform.At(64, 0, Inner - 64 - ww - 8, 28); _type.rectTransform.At(64, 29, Inner - 64 - ww - 8, 20);
            float y = 14f + 52f + 10f;
            _knot.gameObject.SetActive(Knots.On);
            if (Knots.On) { _knot.anchoredPosition = new Vector2(0f, -(y - 2f)); y += Knots.Trinity + 8f; }
            // lore, the source's note (crafting: what's missing)
            y = Para(_lore, desc, y);
            y = Para(_note, _src?.Note, y);
            // compared with what you hold / wear in that slot
            y = Compare(item, q, p, y);
            // two columns of stats
            Columns(item, q, crafting, p); _durBar.gameObject.SetActive(_durRowL >= 0 || _durRowR >= 0);
            float lh = Column(_left, _l, Pad, y), rh = Column(_right, _r, Pad + ColW + ColGap, y);
            if (lh > 0f || rh > 0f) y += Mathf.Max(lh, rh) + 10f;
            // EpicLoot's lines on their card
            bool mag = _magicLines.Count > 0; _magicCard.gameObject.SetActive(mag);
            if (mag)
            {
                var a = tint ? rc : Theme.C(_t.accent);
                _magicCard.color = new Color(a.r, a.g, a.b, 0.05f);   // the game blends in linear space: a low alpha already reads strong _magicT.color = new Color(a.r, a.g, a.b, 0.9f);
                _magic.text = string.Join("\n", _magicLines); float gh = _magic.GetPreferredValues(_magic.text, Inner - 20f, 0f).y;
                _magicT.rectTransform.At(10, 9, Inner - 20, 14); _magic.rectTransform.At(10, 28, Inner - 20, gh);
                _magicCard.rectTransform.At(Pad, y, Inner, gh + 38f); y += gh + 38f + 10f;
            }
            // what Valheim (or another mod) says that we didn't break down
            bool more = _moreLines.Count > 0; _more.gameObject.SetActive(more); _moreT.gameObject.SetActive(more);
            if (more)
            {
                _moreT.rectTransform.At(Pad, y, Inner, 14); y += 18f;
                _more.text = string.Join("\n", _moreLines); float mh = _more.GetPreferredValues(_more.text, Inner, 0f).y;
                _more.rectTransform.At(Pad, y, Inner, mh); y += mh + 10f;
            }
            // key hints
            bool keys = TooltipKeys.On != null && TooltipKeys.On.Value && !crafting && (_grid != null || (_src != null && _src.Worn));
            _keys.gameObject.SetActive(keys);
            if (keys) { float kh = Keys(item); _keys.At(Pad, y, Inner, kh + 8f); y += kh + 8f; }
            else y -= 10f - 2f;
            float h = y + 12f;
            _card.sizeDelta = new Vector2(W, h); _shadow.At(-16, -16 + 14, W + 32, h + 32);
        }

        // EpicLoot's enchantments, sockets and sets: their own card (Fill draws it in the rarity colour)
        private static void EpicLines(Epic.Tip e)
        {
            _magicLines.Clear();
            if (e == null) return;
            var add = new List<string>();
            foreach (var fx in e.Effects) if (!string.IsNullOrEmpty(fx.Text)) add.Add(fx.Text);
            if (e.Sockets != null && e.Sockets.Count > 0)
            {
                add.Add($"<color=#{Hex(_t.mute)}>Sockets {e.Sockets.Used} / {e.Sockets.Count}</color>");
                foreach (var so in e.Sockets.Entries) if (!string.IsNullOrEmpty(so.Text)) add.Add("  " + so.Text);
            }
            foreach (var set in e.Sets)
            {
                add.Add($"<b>{set.Name}</b> <color=#{Hex(_t.mute)}>({set.Equipped} / {set.Full})</color>");
                foreach (var b in set.Bonuses) add.Add(b.Active ? $"  ({b.Count}) {b.Text}" : $"  <color=#{Hex(_t.mute)}>({b.Count}) {Tags.Replace(b.Text, "")}</color>");
            }
            foreach (var x in e.Extra) if (!string.IsNullOrEmpty(x.Text)) add.Add(x.Text);
            _magicLines.AddRange(add);
        }

        private static float Para(TextMeshProUGUI x, string s, float y)
        {
            bool on = !string.IsNullOrEmpty(s); x.gameObject.SetActive(on); if (!on) return y;
            x.text = s; float h = x.GetPreferredValues(s, Inner, 0f).y; x.rectTransform.At(Pad, y, Inner, h); return y + h + 10f;
        }

        // ---- the comparison --------------------------------------------------------------------------
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Right = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem"), Left = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem"),
            Helmet = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem"), Chest = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem"), Legs = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem"),
            Cape = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem"), Utility = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem"), Trinket = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_trinketItem");
        private static ItemDrop.ItemData InSlot(Player p, ItemDrop.ItemData it)
        {
            switch (it.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow: case ItemDrop.ItemData.ItemType.Tool: case ItemDrop.ItemData.ItemType.Torch:
                    { var l = Left(p); return Right(p) ?? (l != null && l.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield && l.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Torch ? l : null); }   // what you hold: the right hand, else a bow / two-handed left
                case ItemDrop.ItemData.ItemType.Shield: { var l = Left(p); return l != null && l.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield ? l : null; }
                case ItemDrop.ItemData.ItemType.Helmet: return Helmet(p);
                case ItemDrop.ItemData.ItemType.Chest: return Chest(p);
                case ItemDrop.ItemData.ItemType.Legs: return Legs(p);
                case ItemDrop.ItemData.ItemType.Shoulder: return Cape(p);
                case ItemDrop.ItemData.ItemType.Utility: return Utility(p);
                case ItemDrop.ItemData.ItemType.Trinket: return Trinket(p);
            }
            return null;
        }

        private static float Compare(ItemDrop.ItemData item, int q, Player p, float y)
        {
            _cmpRows.Clear();
            var held = p != null && !(_src != null && _src.Worn) ? InSlot(p, item) : null;
            if (held != null && held != item)
            {
                Parse(Localization.instance.Localize(ItemDrop.ItemData.GetTooltip(held, held.m_quality, false, held.m_worldLevel)), Localization.instance.Localize(held.m_shared.m_description), _held, null, null);
                foreach (var a in _lines) Diff(a.C, a.K, a.V, Find(_held, a.K), item, q, held, false);
                foreach (var b in _held) if (Find(_lines, b.K) == null) Diff(b.C, b.K, null, b.V, item, q, held, true);
            }
            bool on = _cmpRows.Count > 0; _cmp.gameObject.SetActive(on);
            if (!on) return y;
            _cmpTitle.text = "Compared with your " + Localization.instance.Localize(held.m_shared.m_name); _cmpTitle.rectTransform.At(10, 6, Inner - 20, 17);
            for (int i = 0; i < Mathf.Max(_cmpRows.Count, _cmpCol.Rows.Count); i++)
            {
                var r = RowAt(_cmpCol, i, _cmp); bool use = i < _cmpRows.Count; r.Rt.gameObject.SetActive(use); if (!use) continue;
                r.K.text = _cmpRows[i].k; r.V.text = _cmpRows[i].v; r.Rt.At(10, 25 + i * RowH, Inner - 20, RowH);
            }
            float h = 25f + _cmpRows.Count * RowH + 6f; _cmp.At(Pad, y, Inner, h);
            return y + h + 10f;
        }
        private static string Find(List<Line> l, string k) { foreach (var x in l) if (x.K == k) return x.V; return null; }

        private static void Diff(Cat c, string k, string mine, string theirs, ItemDrop.ItemData item, int q, ItemDrop.ItemData held, bool onlyHeld)
        {
            if (c == Cat.Mod || c == Cat.Drop || c == Cat.More || c == Cat.Food || c == Cat.Lasts || k == Localization.instance.Localize("$item_repairlevel")) return;
            float a, b;
            if (c == Cat.Dur) { a = item.GetMaxDurability(q); b = held.GetMaxDurability(held.m_quality); }
            else { a = mine != null ? First(mine) : 0f; b = theirs != null ? First(theirs) : 0f; }
            float d = a - b; if (Mathf.Abs(d) < 0.05f) return;
            bool pct = (mine ?? theirs).Contains("%") && c != Cat.Dur, good = _lowBetter.Contains(k) ? d < 0f : d > 0f;
            var col = Hex(good ? _t.accent : _t.health);
            var n = Mathf.Abs(d - Mathf.Round(d)) < 0.05f ? Mathf.Abs(d).ToString("0") : Mathf.Abs(d).ToString("0.#");
            _cmpRows.Add((k, $"<color=#{col}><size=9>{(d > 0f ? "▲" : "▼")}</size> {(d > 0f ? "+" : "−")}{n}{(pct ? "%" : "")}</color>"));
        }
        private static float First(string v) { var m = Num.Match(v); return m.Success && float.TryParse(m.Value.Replace('−', '-').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f; }

        // ---- the two columns ---------------------------------------------------------------------------
        private static string _lTitle, _rTitle; private static int _durRowL = -1, _durRowR = -1; private static float _durK;
        private static void Columns(ItemDrop.ItemData item, int q, bool crafting, Player p)
        {
            _l.Clear(); _r.Clear(); _durRowL = _durRowR = -1;
            bool dmg = false, block = false, armour = false, food = false;
            foreach (var x in _lines) { dmg |= x.C == Cat.Dmg; block |= x.C == Cat.Block || x.C == Cat.Parry; armour |= x.C == Cat.Armour; food |= x.C == Cat.Food; }
            bool set = !string.IsNullOrEmpty(item.m_shared.m_setName) && item.m_shared.m_setSize > 1;
            // left: Damage (weapons), Block (shields), Armour / Item (armour), Food; right: Item, or the set, or Lasts (food)
            _lTitle = dmg ? "Damage" : block ? "Block" : armour ? (set ? "Item" : "Armour") : food ? "Food" : "Item";
            _rTitle = food ? "Lasts" : set && !dmg && !block ? null : "Item";
            foreach (var x in _lines)
            {
                if (x.C == Cat.Drop || x.C == Cat.More) continue;
                var v = Val(x);
                if (x.C == Cat.Dur) { var row = crafting ? $"{item.GetMaxDurability(q):0}" : $"{item.m_durability:0} / {item.GetMaxDurability(q):0}"; _durK = crafting ? 1f : item.GetDurabilityPercentage();
                    if (_lTitle == "Item") { _l.Add((x.K, row)); _durRowL = _l.Count - 1; } else { _r.Add((x.K, row)); _durRowR = _r.Count - 1; } continue; }
                // weapons: damage and parry left, block with the item stats; shields and armour: theirs left; Item-titled left takes the rest
                bool left = x.C == Cat.Dmg || x.C == Cat.Parry || x.C == Cat.Food || (x.C == Cat.Block || x.C == Cat.Armour || x.C == Cat.Mod) && !dmg || x.C == Cat.Item && _lTitle == "Item";
                if (x.C == Cat.Mod) { int vs = v.IndexOf(" VS ", System.StringComparison.Ordinal); if (vs > 0) (left ? _l : _r).Add((v.Substring(vs + 4), v.Substring(0, vs))); continue; }   // "Resistant VS Fire" -> Fire  Resistant
                (left ? _l : _r).Add((x.K, v));
            }
            if (set) SetRows(item, p);   // weapons with a set (modded): under the item stats
        }

        // a value as the design shows it: "47 (14-28)" -> 47 and its range faint, "3x" -> ×3
        private static string Val(Line x)
        {
            var v = x.V; int sp = v.IndexOf(" (", System.StringComparison.Ordinal);
            string main = sp > 0 ? v.Substring(0, sp) : v, rest = sp > 0 ? v.Substring(sp + 1).Trim() : "";
            if (main.EndsWith("x") && main.Length > 1 && char.IsDigit(main[main.Length - 2])) main = "×" + main.Substring(0, main.Length - 1);
            if (rest.Length > 0) main += $" <size=10.5><color=#{Hex(_t.mute)}>{rest}</color></size>";
            return main;
        }

        private static readonly Dictionary<string, List<ItemDrop.ItemData>> _sets = new Dictionary<string, List<ItemDrop.ItemData>>();   // one entry per set name in the game (a few dozen)
        private static void SetRows(ItemDrop.ItemData item, Player p)
        {
            var name = item.m_shared.m_setName;
            if (!_sets.TryGetValue(name, out var pieces))
            {
                pieces = new List<ItemDrop.ItemData>();
                if (ObjectDB.instance != null) foreach (var go in ObjectDB.instance.m_items) { var d = go ? go.GetComponent<ItemDrop>() : null; if (d != null && d.m_itemData.m_shared.m_setName == name && !pieces.Exists(x => x.m_shared.m_name == d.m_itemData.m_shared.m_name)) pieces.Add(d.m_itemData); }   // variants share a name: once each
                pieces.Sort((a, b) => Order(a).CompareTo(Order(b)));
                _sets[name] = pieces;
            }
            int worn = 0; int start = _r.Count;
            foreach (var pc in pieces)
            {
                bool on = p != null && Worn(p, pc.m_shared.m_name); if (on) worn++;
                _r.Add((Localization.instance.Localize(pc.m_shared.m_name), on ? $"<color=#{Hex(_t.accent)}>worn</color>" : "—"));
            }
            foreach (var fx in _setFx)
            {
                int i = fx.IndexOf(':');
                var m = i > 0 ? null : Trail.Match(fx);   // "Sneak +15": a skill and its bonus
                if (i > 0) { var v = fx.Substring(i + 1).Trim(); _r.Add((fx.Substring(0, i).Trim(), v.StartsWith("+") ? $"<color=#{Hex(_t.accent)}>{v}</color>" : v)); }
                else if (m.Success) _r.Add((m.Groups[1].Value, m.Groups[2].Value.StartsWith("+") ? $"<color=#{Hex(_t.accent)}>{m.Groups[2].Value}</color>" : m.Groups[2].Value));
                else _r.Add((fx, ""));
            }
            var title = $"Set: {Title(name)} ({worn} / {item.m_shared.m_setSize})";
            if (_rTitle == null) _rTitle = title; else _r.Insert(start, ("<b>" + title + "</b>", ""));
        }
        private static int Order(ItemDrop.ItemData d) { switch (d.m_shared.m_itemType) { case ItemDrop.ItemData.ItemType.Helmet: return 0; case ItemDrop.ItemData.ItemType.Chest: return 1; case ItemDrop.ItemData.ItemType.Legs: return 2; case ItemDrop.ItemData.ItemType.Shoulder: return 3; default: return 4; } }
        private static bool Worn(Player p, string name)
        {
            foreach (var f in new[] { Helmet, Chest, Legs, Cape, Utility, Trinket, Right, Left }) { var it = f(p); if (it != null && it.m_shared.m_name == name) return true; }
            return false;
        }
        private static string Title(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1).Replace('_', ' ');

        // a column: its mono label, then its rows (the durability bar under the durability row); returns its height
        private static float Column(Col col, List<(string k, string v)> rows, float x, float y)
        {
            bool left = col == _left; var title = left ? _lTitle : _rTitle; int durRow = left ? _durRowL : _durRowR;
            bool on = rows.Count > 0; col.Sect.gameObject.SetActive(on);
            float y0 = y;
            if (on) { col.Sect.text = (title ?? "").ToUpperInvariant(); col.Sect.rectTransform.At(x, y + 3f, ColW, 17); y += 22f; }
            for (int i = 0; i < Mathf.Max(rows.Count, col.Rows.Count); i++)
            {
                var r = RowAt(col, i, _card); bool use = i < rows.Count; r.Rt.gameObject.SetActive(use); if (!use) continue;
                r.K.text = rows[i].k; r.V.text = rows[i].v; r.K.richText = true; r.Rt.At(x, y, ColW, RowH - 1f); y += RowH;
                if (i == durRow) { _durBar.rectTransform.At(x, y + 1f, ColW, 5); _durFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(_durK), 1f); _durFill.color = Theme.C(_durK < 0.25f ? _t.ember : _t.accent); y += 9f; }
            }
            return on ? y - y0 : 0f;
        }

        // ---- the key hints --------------------------------------------------------------------------------
        private static float Keys(ItemDrop.ItemData item)
        {
            bool bag = _grid != null && InventoryGui.instance != null && _grid == InventoryGui.instance.m_playerGrid, worn = _src != null && _src.Worn;
            int cap = 0, lab = 0; float x = 0f, y = 9f;
            void Hint(string label, params string[] keys)
            {
                float need = label.Length * 7f; foreach (var k in keys) need += k.Length * 7f + 14f;   // rough: wrap before a hint that won't fit
                if (x > 0f && x + need > Inner) { x = 0f; y += 22f; }
                foreach (var k in keys) { var c = Cap(cap++); c.t.text = k; float w = Kit.W(c.t) + 10f; c.box.At(x, y, w, 17); c.box.gameObject.SetActive(true); x += w + 4f; }
                var l = Label(lab++); l.text = label; float lw = Kit.W(l); l.rectTransform.At(x, y - 1f, lw + 2f, 19); l.gameObject.SetActive(true); x += lw + 14f;
            }
            if (worn) { Hint("Pick up", "LMB"); Hint("Unequip", "RMB"); }
            else
            {
                Hint("Move", "LMB");
                if (bag && item.IsEquipable()) Hint(item.m_equipped ? "Unequip" : "Equip", "RMB");
                else if (bag && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) Hint("Use", "RMB");
                if (item.m_stack > 1) Hint("Split", "Shift", "LMB");
                Hint(bag && Azu.Favouriting ? "Favourite" : "Lock", "Alt", "LMB");   // AzuEPI: Alt + click favourites in the bag (chest cells still lock)
                if (bag) Hint("Delete", "Del");
            }
            for (int i = cap; i < _caps.Count; i++) _caps[i].box.gameObject.SetActive(false);
            for (int i = lab; i < _capLabels.Count; i++) _capLabels[i].gameObject.SetActive(false);
            return y + 17f;
        }
        private static (RectTransform box, TextMeshProUGUI t) Cap(int i)
        {
            while (_caps.Count <= i)
            {   // design kbd: mono 10.5 on a dark key with a light edge
                var (b, _) = Kit.Box(_keys, "Key", new Color(0.173f, 0.173f, 0.196f, 0.9f), new Color(1f, 1f, 1f, 0.18f), 5);
                var tx = Kit.Text(b.rectTransform, "K", "", Fonts.Mono, 10.5f, Color.white); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
                _caps.Add((b.rectTransform, tx));
            }
            return _caps[i];
        }
        private static TextMeshProUGUI Label(int i)
        {
            while (_capLabels.Count <= i) { var l = Kit.Text(_keys, "What", "", Fonts.Medium, 12f, Theme.C(_t.dim)); l.alignment = TextAlignmentOptions.MidlineLeft; _capLabels.Add(l); }
            return _capLabels[i];
        }

        // ---- names ---------------------------------------------------------------------------------------
        private static string Hex(string c) => ColorUtility.ToHtmlStringRGB(Theme.C(c));

        // the station and level that make this quality ("Forge 2"); null when nothing crafts it
        private static string Station(ItemDrop.ItemData item, int q)
        {
            var r = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
            if (r == null) return null;
            var st = r.GetRequiredStation(q);
            return st != null ? $"{Localization.instance.Localize(st.m_name)} {r.GetRequiredStationLevel(q)}" : "By hand";
        }

        internal static string TypeName(ItemDrop.ItemData it)
        {
            var s = it.m_shared;
            switch (s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: return "One-handed " + Weapon(s.m_skillType);
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: return "Two-handed " + Weapon(s.m_skillType);
                case ItemDrop.ItemData.ItemType.Bow: return s.m_skillType == Skills.SkillType.Crossbows ? "Crossbow" : "Bow";
                case ItemDrop.ItemData.ItemType.Shield: return "Shield";
                case ItemDrop.ItemData.ItemType.Helmet: return "Helmet";
                case ItemDrop.ItemData.ItemType.Chest: return "Chest armour";
                case ItemDrop.ItemData.ItemType.Legs: return "Leg armour";
                case ItemDrop.ItemData.ItemType.Shoulder: return "Cape";
                case ItemDrop.ItemData.ItemType.Utility: return "Utility";
                case ItemDrop.ItemData.ItemType.Trinket: return "Trinket";
                case ItemDrop.ItemData.ItemType.Tool: return "Tool";
                case ItemDrop.ItemData.ItemType.Torch: return "Torch";
                case ItemDrop.ItemData.ItemType.Ammo: case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return "Ammo";
                case ItemDrop.ItemData.ItemType.Consumable: return s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f ? "Food" : s.m_consumeStatusEffect != null ? "Mead" : "Consumable";
                case ItemDrop.ItemData.ItemType.Material: return "Material";
                case ItemDrop.ItemData.ItemType.Trophy: return "Trophy";
                case ItemDrop.ItemData.ItemType.Fish: return "Fish";
                case ItemDrop.ItemData.ItemType.Customization: return "Customization";
                default: return "Item";
            }
        }
        private static string Weapon(Skills.SkillType k)
        {
            switch (k)
            {
                case Skills.SkillType.Swords: return "sword"; case Skills.SkillType.Knives: return "knife"; case Skills.SkillType.Clubs: return "club";
                case Skills.SkillType.Polearms: return "atgeir"; case Skills.SkillType.Spears: return "spear"; case Skills.SkillType.Axes: return "axe";
                case Skills.SkillType.Pickaxes: return "pickaxe"; case Skills.SkillType.Unarmed: return "fist weapon"; case Skills.SkillType.Bows: return "bow"; case Skills.SkillType.Crossbows: return "crossbow";
                case Skills.SkillType.ElementalMagic: case Skills.SkillType.BloodMagic: return "staff";
                default: return "weapon";
            }
        }

        internal static void Destroy()
        {
            if (_hidden != null) { var cg = _hidden.GetComponent<CanvasGroup>(); if (cg != null) cg.alpha = 1f; }
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = _card = _head = _cmp = _keys = _knot = _shadow = null; _cg = null; _hidden = null; _for = null; _item = null; _src = null; _el = null; _grid = null;
            _left.Rows.Clear(); _right.Rows.Clear(); _cmpCol.Rows.Clear(); _caps.Clear(); _capLabels.Clear(); _sets.Clear(); _cats = null;
        }
    }
}
