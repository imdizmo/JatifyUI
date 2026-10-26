using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The Compendium's knowledge (design "Index as a compendium"): every entry with the recipes that make it and use it.
    // VNEI's catalogue when VNEI is installed (Vnei.Load), otherwise Valheim's own recipes and build pieces.
    internal sealed class Lore
    {
        internal const int Bestiary = 0, Materials = 1, Provisions = 2, Arms = 3, Building = 4, Other = 5, Marked = 6;
        internal static readonly string[] Chapters = { "Bestiary", "Materials", "Provisions", "Arms & armour", "Building", "Other", "Marked" };

        internal sealed class Entry
        {
            internal string Id, Token, Name, Plain, Mod, Kind, Desc; internal int Chapter; internal Sprite Icon; internal GameObject Prefab; internal object Raw;
            internal bool Fav, Known;
            internal readonly List<Recipe> Made = new List<Recipe>(), Used = new List<Recipe>();
        }
        internal struct Part { internal Entry E; internal int Min, Max, Level; internal float Chance; }
        internal sealed class Recipe { internal readonly List<Part> Stations = new List<Part>(), Ins = new List<Part>(), Outs = new List<Part>(); }

        internal readonly List<Entry> All = new List<Entry>();
        internal readonly Dictionary<string, Entry> ById = new Dictionary<string, Entry>();
        private readonly List<Recipe> _recipes = new List<Recipe>();
        internal bool FromVnei;
        internal int KnownCount;
        private int _knownStamp = -1;

        // the name keeps its colour (mini bosses are orange, user); sorting and search use the plain one
        internal void Add(Entry e) { if (ById.ContainsKey(e.Id)) return; e.Plain = System.Text.RegularExpressions.Regex.Replace(e.Name, "<[^>]*>", "").Trim(); if (e.Plain.Length == 0) return; ById[e.Id] = e; All.Add(e); }
        internal void Link(Recipe r)
        {
            if (r.Outs.Count == 0) return;
            _recipes.Add(r);
            foreach (var p in r.Outs) if (!p.E.Made.Contains(r)) p.E.Made.Add(r);
            foreach (var p in r.Ins) if (!p.E.Used.Contains(r)) p.E.Used.Add(r);
        }

        internal static Lore Make()
        {
            var b = new Lore();
            if (Vnei.Load(b)) b.FromVnei = true; else if (!b.FromValheim()) return null;
            b.All.Sort((x, y) => string.Compare(x.Plain, y.Plain, StringComparison.OrdinalIgnoreCase));
            Log.Info($"compendium: {b.All.Count} entries, {b._recipes.Count} recipes from {(b.FromVnei ? "VNEI" : "Valheim")}");
            return b;
        }

        // without VNEI: items, build pieces and crafting recipes (ponytail: no creatures, drops or station conversions; VNEI has those)
        private bool FromValheim()
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null) return false;
            foreach (var go in ObjectDB.instance.m_items)
            {
                var d = go ? go.GetComponent<ItemDrop>() : null; if (d == null || d.m_itemData.m_shared.m_icons == null || d.m_itemData.m_shared.m_icons.Length == 0) continue;
                var s = d.m_itemData.m_shared; var name = Localization.instance.Localize(s.m_name); if (name.StartsWith("$") || name.StartsWith("[")) continue;
                Add(new Entry { Id = go.name, Token = s.m_name, Name = name, Mod = "Valheim", Kind = KindOf(s.m_itemType), Chapter = ChapterOf(s.m_itemType), Icon = d.m_itemData.GetIcon(), Desc = Localization.instance.Localize(s.m_description), Prefab = go });
            }
            var built = new List<(GameObject pgo, Piece pc, Entry tool)>();
            foreach (var go in ObjectDB.instance.m_items)
            {
                var tool = go ? go.GetComponent<ItemDrop>() : null; var table = tool != null ? tool.m_itemData.m_shared.m_buildPieces : null; if (table == null || !ById.TryGetValue(go.name, out var te)) continue;
                foreach (var pgo in table.m_pieces)
                {
                    var pc = pgo ? pgo.GetComponent<Piece>() : null; if (pc == null || pc.m_icon == null || ById.ContainsKey(pgo.name)) continue;
                    var pn = Localization.instance.Localize(pc.m_name); if (pn.StartsWith("$")) continue;
                    Add(new Entry { Id = pgo.name, Token = pc.m_name, Name = pn, Mod = "Valheim", Kind = "Piece", Chapter = Building, Icon = pc.m_icon, Desc = Localization.instance.Localize(pc.m_description), Prefab = pgo });
                    built.Add((pgo, pc, te));
                }
            }
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (r == null || r.m_item == null || !r.m_enabled || !ById.TryGetValue(r.m_item.gameObject.name, out var res)) continue;
                var rec = new Recipe();
                if (r.m_craftingStation != null && ById.TryGetValue(r.m_craftingStation.gameObject.name, out var st)) rec.Stations.Add(new Part { E = st, Min = 1, Max = 1, Chance = 1f, Level = Mathf.Max(1, r.m_minStationLevel) });
                Ins(r.m_resources, rec); rec.Outs.Add(new Part { E = res, Min = r.m_amount, Max = r.m_amount, Chance = 1f, Level = 1 });
                Link(rec);
            }
            foreach (var (pgo, pc, tool) in built)
            {
                var rec = new Recipe(); rec.Stations.Add(new Part { E = tool, Min = 1, Max = 1, Chance = 1f, Level = 1 });
                if (pc.m_craftingStation != null && ById.TryGetValue(pc.m_craftingStation.gameObject.name, out var st)) rec.Stations.Add(new Part { E = st, Min = 1, Max = 1, Chance = 1f, Level = 1 });
                Ins(pc.m_resources, rec); rec.Outs.Add(new Part { E = ById[pgo.name], Min = 1, Max = 1, Chance = 1f, Level = 1 });
                Link(rec);
            }
            return true;
        }

        private void Ins(Piece.Requirement[] res, Recipe rec)
        {
            if (res == null) return;
            foreach (var q in res) if (q.m_resItem != null && q.m_amount > 0 && ById.TryGetValue(q.m_resItem.gameObject.name, out var e)) rec.Ins.Add(new Part { E = e, Min = q.m_amount, Max = q.m_amount, Chance = 1f, Level = 1 });
        }

        internal static string KindOf(ItemDrop.ItemData.ItemType k)
        {
            switch (k)
            {
                case ItemDrop.ItemData.ItemType.Material: return "Material";
                case ItemDrop.ItemData.ItemType.Consumable: return "Food";
                case ItemDrop.ItemData.ItemType.Trophy: return "Trophy";
                case ItemDrop.ItemData.ItemType.Fish: return "Fish";
                case ItemDrop.ItemData.ItemType.Ammo: case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return "Ammunition";
                case ItemDrop.ItemData.ItemType.Shield: return "Shield";
                case ItemDrop.ItemData.ItemType.Tool: case ItemDrop.ItemData.ItemType.Torch: return "Tool";
                case ItemDrop.ItemData.ItemType.Helmet: case ItemDrop.ItemData.ItemType.Chest: case ItemDrop.ItemData.ItemType.Legs: case ItemDrop.ItemData.ItemType.Shoulder: case ItemDrop.ItemData.ItemType.Hands: return "Armour";
                case ItemDrop.ItemData.ItemType.Utility: case ItemDrop.ItemData.ItemType.Trinket: return "Trinket";
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: case ItemDrop.ItemData.ItemType.Bow: return "Weapon";
                default: return "Item";
            }
        }

        private static int ChapterOf(ItemDrop.ItemData.ItemType k)
        {
            switch (KindOf(k))
            {
                case "Material": return Materials;
                case "Food": return Provisions;
                case "Armour": case "Weapon": case "Shield": case "Ammunition": return Arms;
                default: return Other;
            }
        }

        // what you know (VNEI's rule): a material, station or recipe you have met, and everything in a recipe whose
        // ingredients (or results) you all know; worked out again only when the player's known lists grow
        private static readonly HarmonyLib.AccessTools.FieldRef<Player, HashSet<string>> KnownMats = HarmonyLib.AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownMaterial"), KnownRecs = HarmonyLib.AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownRecipes");
        private static readonly HarmonyLib.AccessTools.FieldRef<Player, Dictionary<string, int>> KnownStations = HarmonyLib.AccessTools.FieldRefAccess<Player, Dictionary<string, int>>("m_knownStations");

        internal bool UpdateKnown()
        {
            var p = Player.m_localPlayer; if (p == null) return false;
            var mats = KnownMats(p); var recs = KnownRecs(p); var stations = KnownStations(p);
            int stamp = mats.Count + recs.Count + stations.Count;
            if (stamp == _knownStamp) return false;
            _knownStamp = stamp;
            foreach (var e in All) e.Known = !string.IsNullOrEmpty(e.Token) && (mats.Contains(e.Token) || stations.ContainsKey(e.Token) || recs.Contains(e.Token));
            var self = new HashSet<Entry>(All.Where(e => e.Known));
            foreach (var r in _recipes)
            {
                if (r.Stations.Any(s => !self.Contains(s.E))) continue;
                bool ins = r.Ins.Count > 0 && r.Ins.All(x => self.Contains(x.E)), outs = r.Outs.All(x => self.Contains(x.E));
                if (!ins && !outs) continue;
                foreach (var x in r.Ins) x.E.Known = true; foreach (var x in r.Outs) x.E.Known = true;
            }
            KnownCount = All.Count(e => e.Known);
            return true;
        }

        // the list's one-line hint (only rows on screen ask for it)
        internal static string HintOf(Entry e, bool hide)
        {
            string N(Entry x) => hide && !x.Known ? "something unknown" : x.Name;   // names you haven't found stay hidden in hints too
            var made = e.Made.FirstOrDefault(r => r.Stations.Count > 0); var from = e.Made.FirstOrDefault(r => r.Stations.Count == 0 && r.Ins.Count > 0);
            if (e.Chapter == Bestiary) { var drops = e.Used.Where(r => r.Stations.Count == 0).SelectMany(r => r.Outs).Select(x => N(x.E)).Distinct().Take(2).ToList(); return drops.Count > 0 ? "Drops " + string.Join(", ", drops) : "Creature"; }
            if (made != null) return (e.Chapter == Building ? "Built with the " : "Made at the ") + N(made.Stations[0].E);
            if (from != null) return (from.Ins[0].E.Chapter == Bestiary ? "Dropped by " : "From ") + N(from.Ins[0].E);
            if (e.Used.Count > 0) return $"Used in {e.Used.Count} recipe{(e.Used.Count == 1 ? "" : "s")}";
            return e.Kind;
        }
    }

    // ---- the Compendium page (the character panel's Index tab, renamed; design "Index as a compendium") ----------------
    // Left: chapters with counts and how much you have discovered, the search, an A-Z rail and the list (pooled rows: VNEI has
    // thousands of entries). Right: the entry's page: picture, name, lore, facts, then how it is made, what it is used for and
    // where else it comes from, written out as lines with links. Settings: ShowUndiscovered, DeveloperInfo.
    internal sealed class CompendiumPage
    {
        internal readonly RectTransform Root;
        private readonly ThemeData _t;
        private const float LeftW = 226f, PageX = 250f, PageW = 392f, RowH = 40f, ListY = 344f, ListH = 446f;
        private const int Pool = 14;
        private Lore _book;
        private int _chapter = Lore.Materials;
        private string _q = "", _sel, _listKey, _pageKey;
        private readonly HashSet<string> _open = new HashSet<string>();
        private readonly List<string> _hist = new List<string>();
        private readonly List<(char letter, Lore.Entry e)> _rows = new List<(char, Lore.Entry)>();
        private readonly (Image line, Image fill, TextMeshProUGUI text, TextMeshProUGUI count)[] _chapters = new (Image, Image, TextMeshProUGUI, TextMeshProUGUI)[7];
        private readonly TextMeshProUGUI[] _az = new TextMeshProUGUI[26];
        private readonly Row[] _pool = new Row[Pool];
        private readonly RectTransform _list, _page, _progFill;
        private readonly ScrollRect _scroll;
        private readonly TextMeshProUGUI _prog, _empty;
        private int _first = -1;
        private const string FavKey = "jatify.ixfav";

        private sealed class Row { internal RectTransform Rt; internal Image Bar, Icon; internal TextMeshProUGUI Name, Hint, Letter; internal int Index = -1; }

        internal CompendiumPage(RectTransform parent, ThemeData t)
        {
            _t = t;
            Root = Kit.Node(parent, "Compendium").Fill();
            var title = Kit.Text(Root, "Title", "Compendium", Fonts.Norse, 22f, Theme.C(t.text)); title.rectTransform.At(4, 0, LeftW, 30);
            Pages.Search(Root, "Search the compendium", 0, 38, LeftW, s => { _q = s.Trim(); _listKey = null; });
            for (int i = 0; i < Lore.Chapters.Length; i++)
            {
                int ch = i;
                var (b, f) = Kit.Box(Root, "Chapter", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 8); b.rectTransform.At(0, 82 + i * 30, LeftW, 28);
                var tx = Kit.Text(b.rectTransform, "T", Lore.Chapters[i], Fonts.Medium, 13.5f, Theme.C(t.dim)); tx.rectTransform.At(10, 0, LeftW - 60, 28); tx.alignment = TextAlignmentOptions.MidlineLeft;
                var n = Kit.Text(b.rectTransform, "N", "", Fonts.Mono, 10.5f, Theme.C(t.mute)); n.rectTransform.At(LeftW - 60, 0, 50, 28); n.alignment = TextAlignmentOptions.MidlineRight;
                b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f; btn.onClick.AddListener(() => { _chapter = ch; _listKey = null; _scroll.verticalNormalizedPosition = 1f; });
                _chapters[i] = (b, f, tx, n);
            }
            _prog = Kit.Text(Root, "Progress", "", Fonts.Medium, 11.5f, Theme.C(t.mute)); _prog.rectTransform.At(10, 296, LeftW - 20, 16);
            var bar = Kit.Panel(Root, "Bar", new Color(1f, 1f, 1f, 0.1f), 2f); bar.rectTransform.At(10, 314, LeftW - 20, 4);
            var fill = Kit.Panel(bar.rectTransform, "V", Theme.C(t.accent), 2f); _progFill = fill.rectTransform; _progFill.anchorMin = Vector2.zero; _progFill.anchorMax = new Vector2(0f, 1f); _progFill.offsetMin = _progFill.offsetMax = Vector2.zero;
            for (int i = 0; i < 26; i++)
            {
                char c = (char)('A' + i);
                var l = _az[i] = Kit.Text(Root, "AZ", c.ToString(), Fonts.Mono, 10f, Theme.C(t.mute)); l.alignment = TextAlignmentOptions.Center; l.rectTransform.At(i * (LeftW / 26f), 324, LeftW / 26f, 14);
                l.raycastTarget = true; l.gameObject.AddComponent<Button>().onClick.AddListener(() => Jump(c));
            }
            _list = Pages.Scroll(Root, 0, ListY, LeftW, ListH); _scroll = _list.parent.GetComponent<ScrollRect>();
            for (int i = 0; i < Pool; i++) _pool[i] = MakeRow();
            _empty = Kit.Text(Root, "Empty", "", Fonts.Medium, 12.5f, Theme.C(t.mute)); _empty.rectTransform.At(10, ListY + 10, LeftW - 20, 40); _empty.textWrappingMode = TextWrappingModes.Normal;
            var sep = Kit.Img(Root, "Sep", null, Theme.C(t.glassLine)); sep.rectTransform.At(LeftW + 11, 0, 1, 790);
            _page = Pages.Scroll(Root, PageX, 0, PageW, 790);
        }

        private Row MakeRow()
        {
            var r = new Row(); r.Rt = Kit.Node(_list, "Row").At(0, 0, LeftW, RowH);
            var hit = r.Rt.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            r.Bar = Kit.Img(r.Rt, "Sel", null, Theme.C(_t.accentSoft)); r.Bar.rectTransform.Fill();
            var edge = Kit.Img(r.Bar.rectTransform, "Edge", null, Theme.C(_t.accent)); edge.rectTransform.At(0, 0, 2, RowH);
            r.Icon = Kit.Img(r.Rt, "I", null, Color.white, false); r.Icon.rectTransform.At(10, 6, 28, 28); r.Icon.preserveAspect = true;
            r.Name = Kit.Text(r.Rt, "Name", "", Fonts.Medium, 13.5f, Color.white); r.Name.rectTransform.At(46, 3, LeftW - 52, 18); r.Name.overflowMode = TextOverflowModes.Ellipsis;
            r.Hint = Kit.Text(r.Rt, "Hint", "", Fonts.Medium, 11.5f, Theme.C(_t.mute)); r.Hint.rectTransform.At(46, 20, LeftW - 52, 16); r.Hint.overflowMode = TextOverflowModes.Ellipsis;
            r.Letter = Kit.Text(r.Rt, "Letter", "", Fonts.Norse, 15f, Theme.C(_t.accent)); r.Letter.rectTransform.At(10, 10, 100, 24);
            r.Rt.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (r.Index >= 0 && r.Index < _rows.Count && _rows[r.Index].e != null) Go(_rows[r.Index].e.Id); });
            return r;
        }

        private static bool ShowUnknown => Cfg.CompUndiscovered != null && Cfg.CompUndiscovered.Value;
        private static bool Dev => Cfg.CompDevInfo != null && Cfg.CompDevInfo.Value;

        internal void Tick()
        {
            if (_book == null) { _book = Lore.Make(); if (_book == null) return; LoadFavs(); }
            bool grew = _book.UpdateKnown();
            if (grew) { _listKey = null; _pageKey = null; }
            var lk = $"{_chapter}|{_q}|{ShowUnknown}";
            if (lk != _listKey) { _listKey = lk; Filter(); _first = -1; }
            int first = Mathf.Clamp(Mathf.FloorToInt(_list.anchoredPosition.y / RowH), 0, Mathf.Max(0, _rows.Count - Pool));
            if (first != _first) { _first = first; FillRows(); }
            var pk = $"{_sel}|{ShowUnknown}|{Dev}|{string.Join(",", _open)}|{_hist.Count}";
            if (pk != _pageKey) { _pageKey = pk; BuildPage(); }
        }

        // ---- the list ----
        private void Filter()
        {
            _rows.Clear();
            var toks = _q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            char last = '\0'; var letters = new HashSet<char>();
            foreach (var e in _book.All)
            {
                if (_q.Length == 0 && (_chapter == Lore.Marked ? !e.Fav : e.Chapter != _chapter)) continue;
                if (!Matches(e, toks)) continue;
                char c = char.ToUpperInvariant(e.Plain[0]); if (c < 'A' || c > 'Z') c = '#';
                if (_q.Length == 0 && c != last) { _rows.Add((c, null)); last = c; }
                letters.Add(c); _rows.Add((c, e));
            }
            _list.sizeDelta = new Vector2(0f, _rows.Count * RowH);
            for (int i = 0; i < 26; i++) _az[i].color = Theme.C(letters.Contains((char)('A' + i)) ? _t.dim : _t.mute) * new Color(1, 1, 1, letters.Contains((char)('A' + i)) ? 1f : 0.35f);
            for (int i = 0; i < _chapters.Length; i++)
            {
                bool on = i == _chapter && _q.Length == 0; var c = _chapters[i];
                c.fill.color = on ? Theme.C(_t.accentSoft) : new Color(0, 0, 0, 0); c.text.color = on ? Color.white : Theme.C(_t.dim); c.count.color = Theme.C(on ? _t.accent : _t.mute);
                c.count.text = (i == Lore.Marked ? _book.All.Count(e => e.Fav) : _book.All.Count(e => e.Chapter == i)).ToString();
            }
            _prog.text = $"Discovered {_book.KnownCount:N0} of {_book.All.Count:N0}";
            _progFill.anchorMax = new Vector2(_book.All.Count > 0 ? (float)_book.KnownCount / _book.All.Count : 0f, 1f);
            _empty.text = _rows.Count == 0 ? (_q.Length > 0 ? $"Nothing you know matches \"{_q}\"." : _chapter == Lore.Marked ? "Mark an entry with the star on its page to keep it here." : "Nothing here yet.") : "";
        }

        // name, "@mod" for the mod an entry comes from, "#kind"; names you haven't discovered aren't searched while they're hidden
        private bool Matches(Lore.Entry e, string[] toks)
        {
            if (toks.Length > 0 && !e.Known && !ShowUnknown) return false;
            foreach (var tok in toks)
            {
                bool hit = tok.Length > 1 && tok[0] == '@' ? e.Mod.ToLowerInvariant().Contains(tok.Substring(1))
                         : tok.Length > 1 && tok[0] == '#' ? e.Kind.ToLowerInvariant().Contains(tok.Substring(1)) || Lore.Chapters[e.Chapter].ToLowerInvariant().Contains(tok.Substring(1))
                         : e.Plain.ToLowerInvariant().Contains(tok);
                if (!hit) return false;
            }
            return true;
        }

        private void FillRows()
        {
            for (int i = 0; i < Pool; i++)
            {
                var r = _pool[i]; int idx = _first + i; r.Index = idx;
                bool on = idx < _rows.Count; r.Rt.gameObject.SetActive(on); if (!on) continue;
                r.Rt.At(0, idx * RowH, LeftW, RowH);
                var (letter, e) = _rows[idx];
                bool head = e == null; r.Letter.gameObject.SetActive(head); r.Icon.gameObject.SetActive(!head); r.Name.gameObject.SetActive(!head); r.Hint.gameObject.SetActive(!head);
                if (head) { r.Letter.text = letter.ToString(); r.Bar.enabled = false; r.Bar.transform.GetChild(0).gameObject.SetActive(false); continue; }
                bool sel = e.Id == _sel; r.Bar.enabled = sel; r.Bar.transform.GetChild(0).gameObject.SetActive(sel);
                bool hidden = !e.Known && !ShowUnknown;
                r.Icon.sprite = e.Icon; r.Icon.color = e.Known ? Color.white : hidden ? new Color(0, 0, 0, 0.55f) : new Color(0.45f, 0.45f, 0.45f, 0.8f);
                r.Name.text = hidden ? "Not yet discovered" : e.Known ? e.Name : $"{e.Name} <size=10><color=#{Hex(_t.stamina)}>not found yet</color></size>";
                r.Name.fontStyle = hidden ? FontStyles.Italic : FontStyles.Normal; r.Name.color = e.Known ? Color.white : Theme.C(_t.dim);
                r.Hint.text = hidden ? "Keep exploring" : Lore.HintOf(e, !ShowUnknown);
            }
        }

        private void Jump(char c)
        {
            int i = _rows.FindIndex(r => r.letter == c); if (i < 0) return;
            _list.anchoredPosition = new Vector2(_list.anchoredPosition.x, Mathf.Min(i * RowH, Mathf.Max(0f, _rows.Count * RowH - ListH)));
        }

        private void Go(string id, bool push = true)
        {
            if (id == null || !_book.ById.ContainsKey(id)) return;
            if (push && _sel != null && _sel != id) { _hist.Add(_sel); if (_hist.Count > 50) _hist.RemoveAt(0); }
            _sel = id; _open.Clear(); _first = -1; _page.anchoredPosition = Vector2.zero;
        }

        // ---- marking: VNEI's favourites when VNEI is there (shared with its window), the character's own list otherwise ----
        private void LoadFavs()
        {
            if (_book.FromVnei) return;
            var p = Player.m_localPlayer; if (p == null || !p.m_customData.TryGetValue(FavKey, out var v)) return;
            foreach (var id in v.Split(',')) if (_book.ById.TryGetValue(id, out var e)) e.Fav = true;
        }

        private void ToggleFav(Lore.Entry e)
        {
            e.Fav = !e.Fav;
            if (_book.FromVnei) Vnei.SetFavourite(e.Raw, e.Fav);
            else { var p = Player.m_localPlayer; if (p != null) { var ids = _book.All.Where(x => x.Fav).Select(x => x.Id).ToList(); if (ids.Count == 0) p.m_customData.Remove(FavKey); else p.m_customData[FavKey] = string.Join(",", ids); } }
            _listKey = null; _pageKey = null;
        }

        // ---- the page ----
        private void BuildPage()
        {
            foreach (Transform c in _page) UnityEngine.Object.Destroy(c.gameObject);
            if (_sel == null || !_book.ById.TryGetValue(_sel, out var e))
            {
                var hint = Kit.Text(_page, "Hint", "Pick an entry on the left to read its page.", Fonts.Lore, 15f, Theme.C(_t.mute)); hint.fontStyle = FontStyles.Italic; hint.rectTransform.At(0, 40, PageW, 24);
                _page.sizeDelta = new Vector2(0f, 100f); return;
            }
            bool hidden = !e.Known && !ShowUnknown;
            var (art, _) = Kit.Box(_page, "Art", Theme.C(_t.raised), Theme.C(_t.glassLine), 14); art.rectTransform.At(0, 4, 92, 92);
            var ai = Kit.Img(art.rectTransform, "I", e.Icon, hidden ? new Color(0, 0, 0, 0.6f) : Color.white, false); ai.rectTransform.At(12, 12, 68, 68); ai.preserveAspect = true;
            var kind = Kit.Text(_page, "Kind", hidden ? "UNKNOWN" : (Lore.Chapters[e.Chapter].StartsWith(e.Kind) ? Lore.Chapters[e.Chapter] : $"{Lore.Chapters[e.Chapter]} · {e.Kind}").ToUpperInvariant(), Fonts.Mono, 10.5f, Theme.C(_t.accent)); kind.characterSpacing = 6f; kind.rectTransform.At(108, 14, PageW - 190, 16);
            var nm = Kit.Text(_page, "Name", hidden ? "Not yet discovered" : e.Name, Fonts.Norse, 28f, Color.white); nm.rectTransform.At(108, 32, PageW - 108, 34); nm.overflowMode = TextOverflowModes.Ellipsis; nm.enableAutoSizing = true; nm.fontSizeMin = 18f; nm.fontSizeMax = 28f;
            if (_hist.Count > 0) { var back = Pages.Button(_page, "‹", PageW - 74, 4, 32, 32, () => { var id = _hist[_hist.Count - 1]; _hist.RemoveAt(_hist.Count - 1); Go(id, false); }, 18f); }
            if (!hidden) { var star = Pages.Button(_page, "", PageW - 36, 4, 32, 32, () => ToggleFav(e)); var si = Kit.Img(star.line.rectTransform, "S", Kit.Star(), Theme.C(e.Fav ? _t.stamina : _t.mute), false); si.rectTransform.At(8, 8, 16, 16); }
            float y = 108f;
            if (hidden)
            {
                var t = Kit.Text(_page, "Lore", "You haven't come across this yet. Find it in the world, or what it is made from, to read its page.", Fonts.Lore, 14f, Theme.C(_t.dim)); t.fontStyle = FontStyles.Italic; t.textWrappingMode = TextWrappingModes.Normal;
                t.rectTransform.At(0, y, PageW, 60); _page.sizeDelta = new Vector2(0f, y + 80f); return;
            }
            if (!string.IsNullOrEmpty(e.Desc))
            {
                var d = Kit.Text(_page, "Lore", e.Desc, Fonts.Lore, 14f, new Color32(0xD6, 0xD2, 0xC8, 0xFF)); d.fontStyle = FontStyles.Italic; d.textWrappingMode = TextWrappingModes.Normal;
                float dh = d.GetPreferredValues(e.Desc, PageW, 0f).y; d.rectTransform.At(0, y, PageW, dh); y += dh + 12f;
            }
            y = Facts(e, y) + 14f;
            Knots.DividerAt(_page, 0, y, PageW, false, Theme.C(_t.accent)); y += 18f;
            var made = e.Made.Where(r => r.Stations.Count > 0).ToList(); var from = e.Made.Where(r => r.Stations.Count == 0).ToList();
            if (e.Chapter == Lore.Bestiary)
            {
                var drops = e.Used.Where(r => r.Stations.Count == 0).SelectMany(r => r.Outs).ToList();
                y = Section("What it drops", "drops", drops.Select(p => (p.E, $"{Link(p.E)} {Count(p)}{Chance(p)}")).ToList(), y);
                y = Section("What it is used for", "used", Firsts(e.Used).Select(r => UsedLine(e, r)).ToList(), y);
            }
            else
            {
                y = Section(e.Chapter == Lore.Building ? "How it is built" : "How it is made", "made", made.Select(MadeLine).ToList(), y, "Nothing makes it: " + (from.Count > 0 ? "it is only found." : "it is only found in the world."));
                y = Section("What it is used for", "used", Firsts(e.Used).Select(r => UsedLine(e, r)).ToList(), y, "Nothing you can make uses it.");
                if (from.Count > 0 || _book.FromVnei) y = Section("Where else it comes from", "from", Sources(e, from), y, made.Count > 0 ? "Nothing drops it: the only way is to make it." : "Nowhere else.");
            }
            if (!_book.FromVnei) { var n = Kit.Text(_page, "Note", "Install VNEI for creatures, drops and station conversions.", Fonts.Medium, 11.5f, Theme.C(_t.mute)); n.rectTransform.At(0, y, PageW, 16); y += 26f; }
            if (Dev) y = DevBox(e, y + 4f);
            _page.sizeDelta = new Vector2(0f, y + 20f);
        }

        private float Facts(Lore.Entry e, float y)
        {
            var facts = new List<string>();
            var d = e.Prefab != null ? e.Prefab.GetComponent<ItemDrop>() : null;
            if (d != null) { var s = d.m_itemData.m_shared; facts.Add($"Weight <b>{s.m_weight:0.0#}</b>"); if (s.m_maxStackSize > 1) facts.Add($"Stacks <b>{s.m_maxStackSize}</b>"); if (!s.m_teleportable) facts.Add("Teleports <b>no</b>"); if (s.m_maxQuality > 1) facts.Add($"Levels <b>{s.m_maxQuality}</b>"); if (s.m_food > 0f) facts.Add($"Health <b>{s.m_food:0}</b> · Stamina <b>{s.m_foodStamina:0}</b>"); }
            var ch = e.Prefab != null ? e.Prefab.GetComponent<Character>() : null;
            if (ch != null) facts.Add($"Health <b>{ch.m_health:0}</b>");
            var pc = e.Prefab != null ? e.Prefab.GetComponent<Piece>() : null;
            if (pc != null && pc.m_comfort > 0) facts.Add($"Comfort <b>{pc.m_comfort}</b>");
            facts.Add($"From <b>{e.Mod}</b>");
            float x = 0f;
            foreach (var f in facts)
            {
                var tx = Kit.Text(_page, "F", f, Fonts.Medium, 12f, Theme.C(_t.dim)); tx.richText = true; float w = Kit.W(tx) + 20f;
                if (x + w > PageW) { x = 0f; y += 32f; }
                var (b, _) = Kit.Box(_page, "Fact", Theme.C(_t.raised), Theme.C(_t.glassLine), 13); b.rectTransform.At(x, y, w, 26); b.transform.SetSiblingIndex(tx.transform.GetSiblingIndex());
                tx.rectTransform.At(x + 10, y, w - 10, 26); tx.alignment = TextAlignmentOptions.MidlineLeft; x += w + 6f;
            }
            return y + 26f;
        }

        // a heading and up to four lines (each: the first entry's icon, then the text with links), "and N more" opens the rest
        private float Section(string title, string key, List<(Lore.Entry icon, string text)> lines, float y, string none = null)
        {
            if (lines.Count == 0 && none == null) return y;
            var h = Kit.Text(_page, "H", title, Fonts.Norse, 17f, Color.white); h.characterSpacing = 4f; h.rectTransform.At(0, y, PageW, 22); y += 28f;
            if (lines.Count == 0) { var n = Kit.Text(_page, "None", none, Fonts.Medium, 13f, Theme.C(_t.dim)); n.rectTransform.At(0, y, PageW, 20); return y + 32f; }
            bool all = _open.Contains(key); int shown = all ? Mathf.Min(lines.Count, 60) : Mathf.Min(lines.Count, 4);
            for (int i = 0; i < shown; i++)
            {
                var (ic, text) = lines[i];
                if (ic != null) { var im = Kit.Img(_page, "I", ic.Icon, Color.white, false); im.rectTransform.At(0, y + 1, 20, 20); im.preserveAspect = true; }
                var tx = Kit.Text(_page, "L", text, Fonts.Medium, 13f, Theme.C(_t.dim)); tx.richText = true; tx.textWrappingMode = TextWrappingModes.Normal;
                float th = Mathf.Max(22f, tx.GetPreferredValues(text, PageW - 28f, 0f).y + 2f); tx.rectTransform.At(28, y, PageW - 28, th);
                tx.raycastTarget = true; tx.gameObject.AddComponent<Links>().On = OnLink; y += th + 4f;
            }
            if (lines.Count > 4)
            {
                var more = Kit.Text(_page, "More", all ? "<link=\"less:" + key + "\">show fewer</link>" : $"and {lines.Count - 4} more · <link=\"more:{key}\"><color=#{Hex(_t.accent)}>show all</color></link>", Fonts.Mono, 11.5f, Theme.C(_t.mute));
                more.richText = true; more.rectTransform.At(28, y, PageW - 28, 18); more.raycastTarget = true; more.gameObject.AddComponent<Links>().On = OnLink; y += 22f;
            }
            return y + 12f;
        }

        private (Lore.Entry, string) MadeLine(Lore.Recipe r)
        {
            var st = r.Stations[r.Stations.Count - 1]; var tool = r.Stations.Count > 1 ? r.Stations[0] : default;
            var ins = r.Ins.Count > 0 ? string.Join(", ", r.Ins.Select(p => $"{Link(p.E)} {Count(p)}")) : "nothing";
            var outs = r.Outs.Count == 1 && r.Outs[0].Max > 1 ? $", makes {r.Outs[0].Max}" : "";
            string where = tool.E != null ? $"With the {Link(tool.E)}, near the {Link(st.E)}" : r.Stations.Count == 1 && st.E.Chapter != Lore.Building && st.E.Kind == "Tool" ? $"With the {Link(st.E)}" : $"At the {Link(st.E)}{(st.Level > 1 ? $" (level {st.Level})" : "")}";
            return (st.E, $"{where}: {ins}{outs}");
        }

        private (Lore.Entry, string) UsedLine(Lore.Entry self, Lore.Recipe r)
        {
            var st = r.Stations[r.Stations.Count - 1]; var res = r.Outs[0];
            var others = r.Ins.Where(p => p.E != self).Select(p => Link(p.E)).ToList();
            var mine = r.Ins.FirstOrDefault(p => p.E == self);
            return (res.E, $"{Link(res.E)} at the {Link(st.E)}{(st.Level > 1 ? $" (level {st.Level})" : "")}, {Count(mine)}{(others.Count > 0 ? " with " + string.Join(", ", others) : "")}");
        }

        private static IEnumerable<Lore.Recipe> Firsts(List<Lore.Recipe> used) => used.Where(r => r.Stations.Count > 0 && r.Outs.Count > 0).GroupBy(r => r.Outs[0].E).Select(g => g.First());

        // where else it comes from: the sources you know, one line each; the ones you haven't found yet folded into one line
        private List<(Lore.Entry, string)> Sources(Lore.Entry e, List<Lore.Recipe> from)
        {
            var lines = new List<(Lore.Entry, string)>(); int unknown = 0;
            foreach (var r in from) { if (r.Ins.Count > 0 && !r.Ins[0].E.Known && !ShowUnknown) unknown++; else lines.Add(FromLine(e, r)); }
            lines = lines.GroupBy(l => System.Text.RegularExpressions.Regex.Replace(l.Item2, "<[^>]*>", "")).Select(g => g.First()).ToList();   // the same source in several variants (pots, chests) reads once
            if (unknown > 0) lines.Add((null, $"<i>and {unknown} place{(unknown == 1 ? "" : "s")} you haven't found yet</i>"));
            return lines;
        }

        private (Lore.Entry, string) FromLine(Lore.Entry self, Lore.Recipe r)
        {
            var src = r.Ins.Count > 0 ? r.Ins[0].E : null; var mine = r.Outs.FirstOrDefault(p => p.E == self);
            if (src == null) return (null, $"Found {Count(mine)}{Chance(mine)}");
            return (src, $"{(src.Chapter == Lore.Bestiary ? "Dropped by" : "From")} {Link(src)}: {Count(mine)}{Chance(mine)}");
        }

        private string Link(Lore.Entry e) => !e.Known && !ShowUnknown ? "<i>something unknown</i>" : $"<link=\"go:{e.Id}\"><color=#FFFFFF><u>{e.Name}</u></color></link>";
        private static string Count(Lore.Part p) => p.Min == p.Max ? $"×{p.Max}" : $"×{p.Min}–{p.Max}";
        private string Chance(Lore.Part p) => p.Chance > 0f && p.Chance < 0.999f ? $" <color=#{Hex(_t.stamina)}>{p.Chance * 100f:0.#}%</color>" : "";
        private static string Hex(string token) => ColorUtility.ToHtmlStringRGB(Theme.C(token));

        // DeveloperInfo (setting): the ids a modder looks up, each a link that copies it
        private float DevBox(Lore.Entry e, float y)
        {
            var sp = e.Icon; var drop = e.Prefab != null ? e.Prefab.GetComponent<ItemDrop>() : null;
            var rows = new List<(string, string)>
            {
                ("Prefab", e.Id), ("Name token", string.IsNullOrEmpty(e.Token) ? "—" : e.Token), ("Prefab hash", e.Id.GetStableHashCode().ToString()),
                ("Kind", $"{e.Kind}{(drop != null ? " (" + drop.m_itemData.m_shared.m_itemType + ")" : "")}"),
                ("Icon sprite", sp != null ? $"{sp.name} ({sp.rect.width:0}×{sp.rect.height:0})" : "—"), ("Icon texture", sp != null && sp.texture != null ? sp.texture.name : "—"),
                ("Source", $"{e.Mod} · {(_book.FromVnei ? "VNEI" : "Valheim recipes")}"), ("Recipes", $"{e.Made.Count} make it · {e.Used.Count} use it")
            };
            float h = 30f + rows.Count * 19f + 8f;
            var (box, _) = Kit.Box(_page, "Dev", new Color(0.95f, 0.69f, 0.23f, 0.04f), new Color(0.95f, 0.69f, 0.23f, 0.45f), 10); box.rectTransform.At(0, y, PageW, h);
            var head = Kit.Text(box.rectTransform, "H", "Developer info · click a value to copy it", Fonts.Medium, 11.5f, Theme.C(_t.stamina)); head.rectTransform.At(12, 8, PageW - 24, 16);
            for (int i = 0; i < rows.Count; i++)
            {
                var (k, v) = rows[i];
                var kt = Kit.Text(box.rectTransform, "K", k, Fonts.Mono, 11f, Theme.C(_t.mute)); kt.rectTransform.At(12, 30 + i * 19, 100, 18);
                var vt = Kit.Text(box.rectTransform, "V", $"<link=\"copy:{v}\">{v}</link>", Fonts.Mono, 11f, Color.white); vt.richText = true; vt.overflowMode = TextOverflowModes.Ellipsis; vt.rectTransform.At(116, 30 + i * 19, PageW - 128, 18);
                vt.raycastTarget = true; vt.gameObject.AddComponent<Links>().On = OnLink;
            }
            return y + h + 8f;
        }

        private void OnLink(string id)
        {
            if (id.StartsWith("go:")) Go(id.Substring(3));
            else if (id.StartsWith("more:")) _open.Add(id.Substring(5));
            else if (id.StartsWith("less:")) _open.Remove(id.Substring(5));
            else if (id.StartsWith("copy:")) { GUIUtility.systemCopyBuffer = id.Substring(5); Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Copied " + id.Substring(5)); }
        }

        // clicks on <link> tags in a text
        private sealed class Links : MonoBehaviour, IPointerClickHandler
        {
            internal Action<string> On;
            public void OnPointerClick(PointerEventData ev)
            {
                var tx = GetComponent<TMP_Text>(); int i = TMP_TextUtilities.FindIntersectingLink(tx, ev.position, ev.pressEventCamera);
                if (i >= 0) On?.Invoke(tx.textInfo.linkInfo[i].GetLinkID());
            }
        }

        internal void Test(string q) { _q = q ?? ""; _listKey = null; }
        internal void TestGo(string id) { if (_book == null) return; var e = _book.ById.TryGetValue(id, out var x) ? x : _book.All.FirstOrDefault(y => y.Plain.Equals(id, StringComparison.OrdinalIgnoreCase)); if (e != null) { _chapter = e.Chapter; _listKey = null; Go(e.Id); } }
    }
}
