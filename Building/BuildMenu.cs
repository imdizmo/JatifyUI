using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The build menu as the live design draws it (buildMenu): the hammer's categories as tabs with counts,
    // a search over every piece, "Can build now", Type and Region chips, a grid of named tiles (region dot,
    // comfort, a lock with the station when none is in range, dimmed when you can't place it yet) and a
    // detail card (description, comfort, costs carried / in chests, the keys). Vanilla's BuildUi stays open
    // underneath, invisible (it keeps its keys: Esc / right click close, Q / E change tab); picking a
    // tile is Player.SetSelectedPiece and closes the menu (vanilla), so placing is vanilla's; hovering a tile fills the detail card.
    // Built when it opens, destroyed when it closes.
    internal static class BuildMenu
    {
        // AdminHammer's build menu frame: 1100 x 640, a 220 px column at x24, the rest from x264
        private const float W = 1100, H = 640, X0 = (1920 - W) / 2, Y0 = (1080 - H) / 2 - 30, ColX = 24, ColW = 220, RX = 264, RW = W - RX - 24, Tile = 60, Gap = 6, Foot = 64;
        private static readonly AccessTools.FieldRef<Player, PieceTable> Table = AccessTools.FieldRefAccess<Player, PieceTable>("m_buildPieces");

        // ---- the design's Type and Region (see live.html PIECE_TYPES, MAT_TIER, STATION_TIER, TYPE_OVERRIDE) ----
        private static readonly (string type, Regex re)[] Types =
        {
            ("Stations", new Regex(@"workbench|forge|stonecutter|artisan|galdr|cartography|cooking station|anvil|bellows|cauldron|smoker|oven|press|vice|cutter|grinding|cooler|chopping block|adze|tool shelf|ketill|fermenter|windmill|spinning|foundry|pots and pans|pestle|rolling pin|apron|kiln|smelter|furnace|refinery|spice|incinerator|eitr|wreath|obliterator|beehive|scarecrow|barber|extractor|fountain", RegexOptions.IgnoreCase)),
            ("Portals", new Regex("portal", RegexOptions.IgnoreCase)), ("Stairs", new Regex("stair|ladder", RegexOptions.IgnoreCase)),
            ("Doors & gates", new Regex("door|gate|drawbridge|shutter", RegexOptions.IgnoreCase)),
            ("Defense", new Regex(@"stake|fence|\bward\b|trap|ballista|catapult|ram\b|shield generator", RegexOptions.IgnoreCase)),
            ("Roofs", new Regex("roof", RegexOptions.IgnoreCase)), ("Floors", new Regex("floor", RegexOptions.IgnoreCase)),
            ("Walls", new Regex("wall|window|curtain|divider", RegexOptions.IgnoreCase)), ("Blocks", new Regex(@"\d+x\d+x\d+|block", RegexOptions.IgnoreCase)),
            ("Beams", new Regex("beam|pole|pillar|column|post", RegexOptions.IgnoreCase)), ("Arches", new Regex(@"\barch(?!ery)|cornice|plinth|spire", RegexOptions.IgnoreCase)),
            ("Lights & fires", new Regex("torch|lantern|brazier|candle|sconce|fire|hearth|bonfire|lamp|pyre|fey lights", RegexOptions.IgnoreCase)),
            ("Storage", new Regex("chest|barrel|wardrobe|stack|pile|rack", RegexOptions.IgnoreCase)),
            ("Seating & beds", new Regex("bed|chair|bench|throne|stool|log|seat", RegexOptions.IgnoreCase)), ("Tables", new Regex("table", RegexOptions.IgnoreCase)),
            ("Ships & carts", new Regex(@"raft|karve|longship|drakkar|cart\b", RegexOptions.IgnoreCase)),
            ("Decor", new Regex(@"rug|carpet|banner|adornment|pot\b|stand|sign|trophy|tapestry|statue|bones|skull|skeleton|straw|drapes|garland|nest|rock|tub|t\.w\.i\.g", RegexOptions.IgnoreCase)),
        };
        private static readonly Dictionary<string, string> TypeOverride = new Dictionary<string, string>
        {
            { "wood_fence", "Decor" }, { "wood_fence_gate", "Decor" }, { "stone_fence", "Decor" }, { "darkwood_decowall", "Decor" }, { "crystal_wall_1x1", "Decor" }, { "ashwood_decowall_tree", "Decor" },
            { "piece_preptable", "Stations" }, { "piece_chest_warderobe", "Storage" }, { "piece_cloth_hanging_door", "Decor" }, { "piece_cloth_hanging_door_blue2", "Decor" }, { "piece_FairylightGarland", "Decor" },
            { "piece_TrainingDummy", "Defense" }, { "piece_ArcheryTarget", "Defense" },
        };
        private static readonly string[] TypeOrder = { "Stations", "Station upgrades", "Portals", "Stairs", "Doors & gates", "Defense", "Roofs", "Floors", "Walls", "Blocks", "Beams", "Arches", "Lights & fires", "Storage", "Seating & beds", "Tables", "Ships & carts", "Decor", "Other" };
        internal static readonly (string name, Color color)[] Regions =
        {
            ("Meadows", Hex("7cc35d")), ("Black Forest", Hex("3f9a5a")), ("Swamp", Hex("a08b52")), ("Mountain", Hex("cfd8e3")),
            ("Plains", Hex("d9c25a")), ("Mistlands", Hex("9a86e0")), ("Ashlands", Hex("e0573a")), ("Deep North", Hex("8fd0f2")),
        };
        private static readonly string[] MatTier =
        {
            "",
            "Bronze,Bronze Nails,Copper,Tin,Corewood,Finewood,Greydwarf Eye,Surtling Core,Troll Hide,Blueberries,Thistle,Coal,Coins,Carrot,Ruby,Bone Fragments,Skeleton Trophy",
            "Ancient Bark,Iron,Iron Nails,Chain,Guck,Ectoplasm,Bloodbag,Turnip,Thunder Stone,Iron Pit,Candle Wick,Pot Shard",
            "Silver,Obsidian,Crystal,Wolf Pelt,Wolf Trophy,Fenris Claw,Dragon Tear,Silver Necklace,Red Jute",
            "Black Metal,Tar,Barley,Flax,Lox Pelt,Cloudberries,Barrel Hoops",
            "Yggdrasil Wood,Black Marble,Black Core,Refined Eitr,Sap,Dvergr Extractor,Dvergr Lantern,Majestic Carapace,Mechanical Spring,Luminous Larva,Wisp,Scale Hide,Blue Jute,Sharpening Stone",
            "Ashwood,Grausten,Flametal,Charcoal Resin,Charred Bone,Charred Cogwheel,Charred Skull,Asksvin Hide,Asksvin Neck,Asksvin Pelvis,Asksvin Ribcage,Asksvin Skull,Asksvin Trophy,Molten Core,Proustite Powder,Sulfur,Celestial Feather,Morgen Sinew,Kindled Ribs,Bloodstone,Ceramic Plate,Torn Spirit,Shield Core",
            "Timberwood,Moose Hide,Moose Sinew,Moose Trophy,Seal Pelt,Ice,Snowball,Frostcore,Nornathread,Bloodgold,Writhan Roots",
        };
        private static readonly Dictionary<string, int> StationTier = new Dictionary<string, int> { { "forge", 1 }, { "stonecutter", 2 }, { "artisantable", 3 }, { "blackforge", 5 }, { "galdrtable", 5 } };
        private static Dictionary<string, int> _matTier;

        // ponytail: matched by name without case or spaces against the item's prefab ("Bronze Nails" = BronzeNails), so any language works; unknown = Meadows
        private static string Key(string s) => s.ToLowerInvariant().Replace(" ", "").Replace("_", "");

        private static int RegionOf(Piece p)
        {
            if (_matTier == null)
            {
                _matTier = new Dictionary<string, int>();
                for (int t = 1; t < MatTier.Length; t++) foreach (var m in MatTier[t].Split(',')) _matTier[Key(m)] = t;
            }
            int r = 0;
            if (p.m_craftingStation != null && StationTier.TryGetValue(Key(Localization.instance.Localize(p.m_craftingStation.m_name)), out var st)) r = st;
            foreach (var req in p.m_resources) if (req.m_resItem != null && _matTier.TryGetValue(Key(req.m_resItem.name), out var t)) r = Mathf.Max(r, t);
            return r;
        }

        private static string TypeOf(Piece p, string name)
        {
            if (p.GetComponent<StationExtension>() != null) return "Station upgrades";
            if (TypeOverride.TryGetValue(Utils.GetPrefabName(p.gameObject), out var o)) return o;
            foreach (var (type, re) in Types) if (re.IsMatch(name)) return type;
            return "Other";
        }

        // Valheim's Deep North tab (user: "makes no sense"): its pieces go where their kind lives, the tab goes away
        private static Piece.PieceCategory Fold(Piece p, string type)
        {
            switch (type)
            {
                case "Stations": case "Station upgrades": return Piece.PieceCategory.Crafting;
                case "Walls": case "Floors": case "Roofs": case "Beams": case "Arches": case "Stairs": case "Blocks": case "Doors & gates":
                    return p.m_craftingStation != null && Key(Localization.instance.Localize(p.m_craftingStation.m_name)) == "stonecutter" ? Piece.PieceCategory.BuildingStonecutter : Piece.PieceCategory.BuildingWorkbench;
                case "Storage": case "Seating & beds": case "Tables": case "Decor": case "Lights & fires": return Piece.PieceCategory.Furniture;
                default: return Piece.PieceCategory.Misc;
            }
        }

        private static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        // ---- state ------------------------------------------------------------------------------------
        private sealed class P { internal Piece Piece; internal string Name, Type; internal int Region, Cat; }
        private static RectTransform _root, _grid, _cats, _regionChips, _detail, _picked_tile;
        private static TMP_InputField _search;
        private static ThemeData _t;
        private static readonly List<P> _all = new List<P>();
        private static int _cat = -1, _region = -1; private static string _type, _q = "", _shown; private static bool _avail;
        private static Piece _picked; private static float _nextCheck;

        // favourites (asked for on Nexus): starred pieces, saved on the character, listed in their own tab at the top
        private const int FavCat = -2; internal const string FavKey = "jatify.piecefavs";
        private static readonly HashSet<string> _favs = new HashSet<string>(); private static TextMeshProUGUI _favCount; private static int _lastVc = int.MinValue;
        private static string FavId(Piece p) => Utils.GetPrefabName(p.gameObject);
        internal static bool IsFav(Piece p) => p != null && _favs.Contains(FavId(p));
        private static void LoadFavs(Player me)
        {
            _favs.Clear();
            if (me != null && me.m_customData.TryGetValue(FavKey, out var v)) foreach (var x in v.Split(',')) if (x.Length > 0) _favs.Add(x);
        }
        internal static void ToggleFav(Piece p)
        {
            var me = Player.m_localPlayer; if (me == null || p == null) return;
            var k = FavId(p); if (!_favs.Remove(k)) _favs.Add(k);
            if (_favs.Count == 0) me.m_customData.Remove(FavKey); else me.m_customData[FavKey] = string.Join(",", _favs);
            if (_favCount != null) _favCount.text = _all.Count(x => IsFav(x.Piece)).ToString();
            _shown = null; Refresh();
        }
        private static bool _flat;   // Valheim's hammer order, no type headings (kept for the session)
        private static readonly Dictionary<Piece, (Image bg, Image line, CanvasGroup g, RectTransform lockTag)> _tiles = new Dictionary<Piece, (Image, Image, CanvasGroup, RectTransform)>();
        private static Transform _vanillaWindow, _vanillaInfo; private static float _vanillaWindowA = 1f, _vanillaInfoA = 1f;

        internal static void Tick()
        {
            var h = Hud.instance; var me = Player.m_localPlayer;
            bool open = h != null && me != null && Hud.IsPieceSelectionVisible() && Root.Canvas != null && Table(me) != null;
            if (!open) { if (_root != null) Close(); return; }
            if (_root == null) Open(h, me);
            // ours sits on Jatify's canvas, above the pause menu and the map, where Valheim's own window was covered: it steps
            // aside while they're up (the test sweep found it over the map, the menu and the settings)
            bool covered = Menu.IsVisible() || Minimap.IsOpen() || ConfigPage.Open != null || Settings.instance != null;
            if (_root.gameObject.activeSelf == covered) _root.gameObject.SetActive(!covered);
            if (covered) return;
            // Q / E (vanilla) change the hammer's tab: follow it
            var table = Table(me); int vc = (int)table.GetSelectedCategory();
            if (vc != _lastVc) { bool moved = _lastVc != int.MinValue; _lastVc = vc; if (moved && _q.Length == 0 && vc != _cat && _catTabs.ContainsKey(vc)) { _cat = vc; _type = null; _region = -1; Refresh(); } }   // follows Q / E when pressed, so the Favourites tab stays
            if (Time.unscaledTime >= _nextCheck) { _nextCheck = Time.unscaledTime + 0.5f; Paint(me); }   // what you can build changes as you walk and pick up
        }

        private static readonly Dictionary<int, (Image bg, TextMeshProUGUI label)> _catTabs = new Dictionary<int, (Image, TextMeshProUGUI)>();

        private static void Open(Hud h, Player me)
        {
            _t = Plugin.Instance.ThemeData; var t = _t;
            // vanilla's window and its selected-piece card step aside (still alive: its keys keep working)
            _vanillaWindow = h.m_buildUi != null ? h.m_buildUi.transform.Find("bar/SelectionWindow") : null;
            Fade(_vanillaWindow, 0f, ref _vanillaWindowA);
            _vanillaInfo = h.m_buildHud != null ? h.m_buildHud.transform.Find("SelectedInfo") : null;
            Fade(_vanillaInfo, 0f, ref _vanillaInfoA);

            _root = Kit.Node(Root.Canvas.transform, "jatify-build").Fill(); Notices.OnTop(_root);
            _root.gameObject.AddComponent<GraphicRaycaster>();   // its own canvas (on top): without this no click reached it
            var (win, wfill) = Kit.Box(_root, "Window", Theme.C(t.glass), Theme.C(t.line2), 14); win.raycastTarget = true;
            Knots.Corners(win, wfill, 14, Theme.C(t.accent));
            var w = win.rectTransform.At(X0, Y0, W, H);
            Collect(me);
            // head: title, Can build now
            var title = Kit.Text(w, "Title", "Build", Fonts.Norse, 22f, Color.white); title.rectTransform.At(ColX, 16, 300, 30); title.alignment = TextAlignmentOptions.MidlineLeft;
            var (av, _) = Kit.Box(w, "CanBuild", Theme.C(t.raised), Theme.C(t.line2), 8); av.rectTransform.At(W - 24 - 130, 14, 130, 32); av.raycastTarget = true;
            var avt = Kit.Text(av.rectTransform, "T", "Can build now", Fonts.Medium, 12.5f, Color.white); avt.alignment = TextAlignmentOptions.Center; avt.rectTransform.Fill();
            av.gameObject.AddComponent<Button>().onClick.AddListener(() => { _avail = !_avail; av.color = _avail ? Theme.C(t.accent) : Theme.C(t.line2); avt.color = _avail ? Theme.C(t.accent) : Color.white; Refresh(); });
            // order: grouped by type (ours) or Valheim's own hammer order, flat (user)
            var (ob, _) = Kit.Box(w, "Order", Theme.C(t.raised), Theme.C(t.line2), 8); ob.rectTransform.At(W - 24 - 130 - 8 - 150, 14, 150, 32); ob.raycastTarget = true;
            var obt = Kit.Text(ob.rectTransform, "T", _flat ? "Order: Valheim's" : "Order: by type", Fonts.Medium, 12.5f, Color.white); obt.alignment = TextAlignmentOptions.Center; obt.rectTransform.Fill();
            ob.gameObject.AddComponent<Button>().onClick.AddListener(() => { _flat = !_flat; obt.text = _flat ? "Order: Valheim's" : "Order: by type"; _shown = null; Refresh(); });
            // the hammer's Repair (Valheim lists it in every category): one button up here instead (user)
            var repair = _all.Find(p => p.Piece.m_category == Piece.PieceCategory.All);
            if (repair != null)
            {
                var (rb, _) = Kit.Box(w, "Repair", Theme.C(t.raised), Theme.C(t.line2), 8); rb.rectTransform.At(W - 24 - 130 - 8 - 150 - 8 - 110, 14, 110, 32); rb.raycastTarget = true;
                var ri = Kit.Img(rb.rectTransform, "I", repair.Piece.m_icon, Color.white, false); ri.rectTransform.At(10, 6, 20, 20); ri.preserveAspect = true;
                var rt = Kit.Text(rb.rectTransform, "T", repair.Name, Fonts.Medium, 12.5f, Color.white); rt.alignment = TextAlignmentOptions.MidlineLeft; rt.rectTransform.At(36, 0, 70, 32);
                var rp = repair.Piece; rb.gameObject.AddComponent<Button>().onClick.AddListener(() => Pick(rp));
                rb.gameObject.AddComponent<TileHover>().Piece = rp;
            }
            // left column: the hovered / picked piece, search, the hammer's categories
            _picked_tile = Kit.Node(w, "Picked").At(ColX, 58, ColW, 60);
            Pages.T = Pages.T ?? t;
            var sb = Kit.Node(w, "jatify-search").At(0, 0, W, H);
            _search = Pages.Search(sb, "Search pieces, stations", ColX, 128, ColW, q => { _q = (q ?? "").Trim().ToLowerInvariant(); Refresh(); });
            _cats = Kit.Node(w, "Categories").At(ColX, 174, ColW, H - 174 - Foot);
            var table = Table(me); float y = 0f;
            LoadFavs(me);
            {   // Favourites, first
                var (row, fill) = Kit.Box(_cats, "Cat", Color.clear, Color.clear, 8); row.rectTransform.At(0, y, ColW, 36); row.raycastTarget = true;
                var ic = Kit.Img(row.rectTransform, "I", Kit.Star(), Theme.C(t.stamina), false); ic.rectTransform.At(12, 10, 16, 16);
                var tx = Kit.Text(row.rectTransform, "T", "Favourites", Fonts.Medium, 13.5f, Color.white); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.rectTransform.At(42, 0, ColW - 90, 36);
                _favCount = Kit.Text(row.rectTransform, "N", _all.Count(x => IsFav(x.Piece)).ToString(), Fonts.Mono, 11f, Theme.C(t.mute)); _favCount.alignment = TextAlignmentOptions.MidlineRight; _favCount.rectTransform.At(ColW - 50, 0, 40, 36);
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => { _cat = FavCat; _type = null; _region = -1; if (_search != null) _search.text = ""; Refresh(); });
                _catTabs[FavCat] = (row, tx); y += 40f;
            }
            for (int i = 0; i < table.m_categories.Count; i++)
            {
                int cat = (int)table.m_categories[i]; var inCat = _all.FindAll(p => p.Cat == cat); if (inCat.Count == 0) continue;
                var label = Localization.instance.Localize(i < table.m_categoryLabels.Count ? table.m_categoryLabels[i] : table.m_categories[i].ToString());
                var (row, fill) = Kit.Box(_cats, "Cat", Color.clear, Color.clear, 8); row.rectTransform.At(0, y, ColW, 36); row.raycastTarget = true;
                var ic = Kit.Img(row.rectTransform, "I", inCat[0].Piece.m_icon, Color.white, false); ic.rectTransform.At(8, 6, 24, 24); ic.preserveAspect = true;
                var tx = Kit.Text(row.rectTransform, "T", label, Fonts.Medium, 13.5f, Color.white); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.rectTransform.At(42, 0, ColW - 90, 36); tx.overflowMode = TextOverflowModes.Ellipsis;
                var n = Kit.Text(row.rectTransform, "N", inCat.Count.ToString(), Fonts.Mono, 11f, Theme.C(t.mute)); n.alignment = TextAlignmentOptions.MidlineRight; n.rectTransform.At(ColW - 50, 0, 40, 36);
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => { _cat = cat; table.SetCategory((Piece.PieceCategory)cat); _type = null; _region = -1; if (_search != null) _search.text = ""; Refresh(); });
                _catTabs[cat] = (row, tx); y += 40f;
            }
            // right: region strip, then the grid
            var rl = Kit.Text(w, "RegionL", "REGION", Fonts.Mono, 10f, Theme.C(t.mute)); rl.characterSpacing = 8f; rl.rectTransform.At(RX, 62, 70, 28); rl.alignment = TextAlignmentOptions.MidlineLeft;
            _regionChips = Kit.Node(w, "Regions").At(RX + 70, 62, RW - 70, 28);
            var line = Kit.Img(w, "Line", null, Theme.C(t.line2)); line.rectTransform.At(RX, 102, RW, 1);
            Knots.Divider(line, false, Theme.C(t.accent));   // design .kdiv: between the region strip and the pieces
            var view = Kit.Node(w, "Grid").At(RX, 112, RW + 10, H - 112 - Foot - 8);
            view.gameObject.AddComponent<RectMask2D>(); var hit = view.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            _grid = Kit.Node(view, "Content"); _grid.anchorMin = new Vector2(0f, 1f); _grid.anchorMax = Vector2.one; _grid.pivot = new Vector2(0.5f, 1f); _grid.anchoredPosition = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = _grid; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            // foot: the hovered piece's costs, then the keys
            var fl = Kit.Img(w, "FootLine", null, Theme.C(t.line2)); fl.rectTransform.At(0, H - Foot, W, 1);
            _detail = Kit.Node(w, "Detail").At(ColX, H - Foot, W - 2 * ColX, Foot);
            // start where the hammer is
            _cat = (int)table.GetSelectedCategory(); if (!_catTabs.ContainsKey(_cat) && _catTabs.Count > 0) foreach (var k in _catTabs.Keys) { _cat = k; break; }
            _picked = table.GetSelectedPiece(); _lastVc = (int)table.GetSelectedCategory();
            _avail = false; _type = null; _region = -1; _q = ""; _shown = null;
            Refresh();
        }

        private static void Close()
        {
            Fade(_vanillaWindow, _vanillaWindowA, ref _vanillaWindowA); Fade(_vanillaInfo, _vanillaInfoA, ref _vanillaInfoA);
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null; _hover = null; _tiles.Clear(); _catTabs.Clear(); _all.Clear(); _favCount = null; _lastVc = int.MinValue;
        }

        internal static void Destroy() => Close();

        private static void Fade(Transform tr, float to, ref float was)
        {
            if (tr == null) return;
            var g = tr.GetComponent<CanvasGroup>() ?? tr.gameObject.AddComponent<CanvasGroup>();
            if (to < 0.5f) was = g.alpha;
            g.alpha = to; g.blocksRaycasts = to > 0.5f; g.interactable = to > 0.5f;
        }

        private static void Collect(Player me)
        {
            _all.Clear();
            foreach (var piece in Table(me).m_availablePieces)
            {
                if (piece == null) continue;
                var name = Localization.instance.Localize(piece.m_name);
                var type = TypeOf(piece, name);
                _all.Add(new P { Piece = piece, Name = name, Type = type, Region = RegionOf(piece), Cat = (int)(piece.m_category == Piece.PieceCategory.DeepNorth ? Fold(piece, type) : piece.m_category) });
            }
            _all.Sort((a, b) => Table(me).m_pieces.FindIndex(g => g == a.Piece.gameObject).CompareTo(Table(me).m_pieces.FindIndex(g => g == b.Piece.gameObject)));   // the hammer's own order
        }

        // ---- filters and grid -------------------------------------------------------------------------
        private static List<P> InTab()
        {
            if (_q.Length == 0) return _cat == FavCat ? _all.FindAll(p => IsFav(p.Piece)) : _all.FindAll(p => p.Cat == _cat);   // Repair (category All) has its own button
            return _all.FindAll(p => (p.Name + " " + (p.Piece.m_craftingStation != null ? Localization.instance.Localize(p.Piece.m_craftingStation.m_name) : "") + " " + Regions[p.Region].name).ToLowerInvariant().Contains(_q));
        }

        private static bool CanPlace(Player me, Piece p)
        {
            bool was = Stock.Building; Stock.Building = true;
            try { return me.HaveRequirements(p, Player.RequirementMode.CanBuild); } finally { Stock.Building = was; }
        }

        private static bool StationOk(Piece p) => p.m_craftingStation == null || CraftingStation.HaveBuildStationInRange(p.m_craftingStation.m_name, Player.m_localPlayer.transform.position) != null;

        private static void Refresh()
        {
            if (_root == null) return;
            var t = _t; var me = Player.m_localPlayer;
            foreach (var kv in _catTabs) { bool on = _q.Length == 0 && kv.Key == _cat; kv.Value.bg.color = on ? Theme.C(t.accent) : Color.clear; kv.Value.bg.transform.GetChild(0).GetComponent<Image>().color = on ? Theme.C(t.accentSoft) : Color.clear; }
            var inTab = InTab();
            var types = new List<string>(); var regions = new List<int>();
            foreach (var p in inTab) { if (!types.Contains(p.Type)) types.Add(p.Type); if (!regions.Contains(p.Region)) regions.Add(p.Region); }
            types.Sort((a, b) => System.Array.IndexOf(TypeOrder, a).CompareTo(System.Array.IndexOf(TypeOrder, b))); regions.Sort();
            if (_type != null && !types.Contains(_type)) _type = null;
            if (_region >= 0 && !regions.Contains(_region)) _region = -1;
            Chips(_regionChips, regions.ConvertAll(r => (Regions[r].name, inTab.FindAll(p => p.Region == r).Count, r == _region, (Color?)Regions[r].color)), i => { _region = _region == regions[i] ? -1 : regions[i]; Refresh(); });
            var list = inTab.FindAll(p => (!_avail || CanPlace(me, p.Piece)) && (_region < 0 || p.Region == _region));
            if (!_flat) list = list.OrderBy(p => System.Array.IndexOf(TypeOrder, p.Type)).ToList();   // grouped by type, the hammer's order inside (OrderBy is stable)
            Grid(list, t);
            if (_picked == null || list.Find(p => p.Piece == _picked) == null) { if (list.Count > 0 && _picked == null) _picked = list[0].Piece; }
            ShowDetail();
        }

        private static void Chips(RectTransform host, List<(string label, int n, bool on, Color? dot)> chips, System.Action<int> click)
        {
            var t = _t;
            foreach (Transform c in host) Object.Destroy(c.gameObject);
            float x = 0f;
            for (int i = 0; i < chips.Count; i++)
            {
                var (label, n, on, dot) = chips[i]; int k = i;
                var (b, f) = Kit.Box(host, "Chip", on ? Theme.C(t.accentSoft) : Color.clear, on ? Theme.C(t.accent) : Theme.C(t.line2), 15); b.raycastTarget = true;
                float lead = dot.HasValue ? 26f : 12f;
                var tx = Kit.Text(b.rectTransform, "T", $"{label} <size=85%><color=#{ColorUtility.ToHtmlStringRGB(Theme.C(t.mute))}>{n}</color></size>", Fonts.Medium, 12.5f, on ? Color.white : Theme.C(t.text)); tx.alignment = TextAlignmentOptions.MidlineLeft;
                float cw = lead + Kit.W(tx) + 12f; b.rectTransform.At(x, 0, cw, 28); tx.rectTransform.At(lead, 0, cw - lead, 28);
                if (dot.HasValue) { var d = Kit.Img(b.rectTransform, "Dot", Kit.Circle(), dot.Value, false); d.rectTransform.At(11, 10, 8, 8); }
                b.gameObject.AddComponent<Button>().onClick.AddListener(() => click(k));
                x += cw + 6f;
            }
        }

        // icon tiles under a small heading per type (Walls 24, Floors 12...): region dot, comfort, a lock while its station is missing
        private static void Grid(List<P> list, ThemeData t)
        {
            var key = _cat + "|" + _q + "|" + _region + "|" + _avail + "|" + _flat + "|" + list.Count + "|" + _favs.Count;
            if (key == _shown) return; _shown = key;
            foreach (Transform c in _grid) Object.Destroy(c.gameObject);
            _tiles.Clear();
            int cols = Mathf.Max(1, (int)((RW + Gap) / (Tile + Gap)));
            float y = 0f; int col = 0; string type = null;
            foreach (var p in list)
            {
                if (!_flat && p.Type != type)
                {   // a new group: finish the row, then its heading
                    if (col > 0) { y += Tile + Gap; col = 0; }
                    type = p.Type; int n = list.Count(q => q.Type == type);
                    var hd = Kit.Text(_grid, "Group", $"{type.ToUpperInvariant()}  <color=#{ColorUtility.ToHtmlStringRGB(Theme.C(t.mute))}>{n}</color>", Fonts.Mono, 10f, Theme.C(t.dim)); hd.characterSpacing = 6f;
                    hd.rectTransform.At(0, y + (y > 0f ? 6f : 0f), RW, 18); y += y > 0f ? 30f : 24f;
                }
                float x = col * (Tile + Gap);
                var (line, bg) = Kit.Box(_grid, "Tile", Theme.C(t.raised), Theme.C(t.line2), 9); line.rectTransform.At(x, y, Tile, Tile); line.raycastTarget = true;
                var g = line.gameObject.AddComponent<CanvasGroup>();
                var ic = Kit.Img(line.rectTransform, "I", p.Piece.m_icon, Color.white, false); ic.rectTransform.At(9, 9, Tile - 18, Tile - 18); ic.preserveAspect = true;
                var dot = Kit.Img(line.rectTransform, "Region", Kit.Circle(), Regions[p.Region].color, false); dot.rectTransform.At(Tile - 12, Tile - 12, 6, 6);
                if (p.Piece.m_comfort > 0) { var cf = Kit.Text(line.rectTransform, "Comfort", p.Piece.m_comfort.ToString(), Fonts.Mono, 9.5f, Theme.C(t.dim)); cf.rectTransform.At(5, 3, 20, 13); }
                RectTransform lockTag = null;
                if (p.Piece.m_craftingStation != null)
                {   // its station is missing: a lock, top right (Paint shows it)
                    var lk = Kit.IconImg(line.rectTransform, "lock", Theme.C(t.ember), 12f); lockTag = lk.rectTransform; lockTag.At(Tile - 16, 4, 12, 12);
                }
                var piece = p.Piece;
                // the star, bottom left: shown on favourites and while hovered; a click on it (not the tile) stars or unstars
                bool fav = IsFav(piece);
                var fs = Kit.Img(line.rectTransform, "Fav", Kit.Star(), fav ? Theme.C(t.stamina) : new Color(1f, 1f, 1f, 0.55f), false); fs.rectTransform.At(4, Tile - 18, 14, 14); fs.raycastTarget = true;
                fs.gameObject.AddComponent<Button>().onClick.AddListener(() => ToggleFav(piece)); fs.gameObject.SetActive(fav);
                var th = line.gameObject.AddComponent<TileHover>(); th.Piece = piece; th.Star = fav ? null : fs.gameObject;
                line.gameObject.AddComponent<RightPick>().Piece = piece;   // right click: Valheim's quick select (pick and close)
                line.gameObject.AddComponent<Button>().onClick.AddListener(() => Pick(piece));
                _tiles[p.Piece] = (bg, line, g, lockTag);
                if (++col == cols) { col = 0; y += Tile + Gap; }
            }
            if (col > 0) y += Tile + Gap;
            _grid.sizeDelta = new Vector2(0f, y + 10f);
            if (list.Count == 0) { var none = Kit.Text(_grid, "None", _cat == FavCat && _q.Length == 0 && !_avail ? "No favourites yet: click the star on a piece (it shows when you point at it) to add it here." : _avail ? "No piece matches that you can build right now." : "No piece matches.", Fonts.Medium, 14f, Theme.C(t.mute)); none.rectTransform.At(4, 10, 700, 24); }
            Paint(Player.m_localPlayer);
        }

        // states that change while it's open: what you can place, which station is in range, which is picked
        private static void Paint(Player me)
        {
            if (me == null) return; var t = _t;
            foreach (var kv in _tiles)
            {
                var (bg, line, g, lockTag) = kv.Value; if (bg == null) continue;
                bool ok = CanPlace(me, kv.Key), on = kv.Key == _picked, st = StationOk(kv.Key);
                g.alpha = ok ? 1f : 0.45f;
                line.color = on ? Theme.C(t.accent) : Theme.C(t.line2); bg.color = on ? Theme.C(t.accentSoft) : Theme.C(t.raised);
                if (lockTag != null) lockTag.gameObject.SetActive(!st);
            }
        }

        internal static void PickPiece(Piece p) => Pick(p);
        private static void Pick(Piece p)
        {
            _picked = p; var me = Player.m_localPlayer;
            if (me != null) me.SetSelectedPiece(p);   // vanilla: selects it and makes the ghost
            Hud.HidePieceSelection();                // and, as in vanilla, straight to placing it in the world
        }

        // the detail card follows the hovered tile, then goes back to the selected piece
        internal static void Hover(Piece p, bool on) { if (on) _hover = p; else if (_hover == p) _hover = null; if (_root != null) ShowDetail(); }
        private static Piece _hover;

        // ---- the hovered (else picked) piece: its tile in the column, its costs in the foot --------------------
        private static void ShowDetail()
        {
            foreach (Transform c in _detail) Object.Destroy(c.gameObject);
            foreach (Transform c in _picked_tile) Object.Destroy(c.gameObject);
            var show = _hover ?? _picked; var p = _all.Find(x => x.Piece == show); if (p == null) return;
            var t = _t; var me = Player.m_localPlayer; var piece = p.Piece;
            var (tile, _) = Kit.Box(_picked_tile, "Tile", Theme.C(t.raised), Theme.C(t.line2), 9); tile.rectTransform.At(0, 0, 56, 56);
            var ic = Kit.Img(tile.rectTransform, "I", piece.m_icon, Color.white, false); ic.rectTransform.At(8, 8, 40, 40); ic.preserveAspect = true;
            var nm = Kit.Text(_picked_tile, "N", p.Name, Fonts.Bold, 14f, Color.white); nm.rectTransform.At(68, 4, ColW - 68 - 26, 20); nm.overflowMode = TextOverflowModes.Ellipsis;
            var star = Kit.Img(_picked_tile, "Fav", Kit.Star(), IsFav(piece) ? Theme.C(t.stamina) : new Color(1f, 1f, 1f, 0.3f), false); star.rectTransform.At(ColW - 20, 6, 16, 16); star.raycastTarget = true;
            star.gameObject.AddComponent<Button>().onClick.AddListener(() => { ToggleFav(piece); ShowDetail(); });
            var dot = Kit.Img(_picked_tile, "Dot", Kit.Circle(), Regions[p.Region].color, false); dot.rectTransform.At(68, 32, 7, 7);
            var sub = Kit.Text(_picked_tile, "S", $"{Regions[p.Region].name} · {p.Type}" + (piece.m_comfort > 0 ? $" · comfort {piece.m_comfort}" : ""), Fonts.Medium, 11f, Theme.C(t.mute)); sub.rectTransform.At(80, 26, ColW - 80, 18); sub.overflowMode = TextOverflowModes.Ellipsis;
            // foot: costs as have/need chips (red when short), the station, then the keys
            float x = 0f; var mute = ColorUtility.ToHtmlStringRGB(Theme.C(t.mute));
            foreach (var req in piece.m_resources)
            {
                if (req.m_resItem == null || req.m_amount <= 0) continue;
                var item = req.m_resItem.m_itemData.m_shared.m_name; bool was = Stock.Building; Stock.Building = true; int have = Stock.Count(me, item); Stock.Building = was;
                bool ok = have >= req.m_amount; var hc = ColorUtility.ToHtmlStringRGB(ok ? Theme.C(t.text) : Theme.C(t.ember));
                x += FootChip(req.m_resItem.m_itemData.GetIcon(), $"{Localization.instance.Localize(item)}  <color=#{hc}>{have}</color><color=#{mute}>/{req.m_amount}</color>", ok, Color.white, x) + 6f;
            }
            if (piece.m_craftingStation != null) { bool st = StationOk(piece); x += FootChip(piece.m_craftingStation.m_icon, Localization.instance.Localize(piece.m_craftingStation.m_name), st, st ? Theme.C(t.accent) : Theme.C(t.ember), x) + 6f; }
            if (x == 0f) { var free = Kit.Text(_detail, "Free", "No cost", Fonts.Medium, 12.5f, Theme.C(t.mute)); free.rectTransform.At(0, 0, 120, Foot); }
            var keys = Kit.Node(_detail, "Keys").At(W - 2 * ColX - 340, 0, 340, Foot); float kx = 0f;
            float ky = (Foot - 20f) / 2f;
            kx += HoverCard.Row(keys, "Click", "place", kx, ky, t) + 16f;
            {   // the star you click, as it looks on the tiles (a text star drew as nothing in the key font)
                var st = Kit.Img(keys, "Star", Kit.Star(), Theme.C(t.stamina), false); st.rectTransform.At(kx + 2f, ky + 3f, 15, 15);
                var wt = Kit.Text(keys, "What", "favourite", Fonts.Medium, 12.5f, Theme.C(t.dim)); wt.alignment = TextAlignmentOptions.MidlineLeft;
                float ww = Kit.W(wt); wt.rectTransform.At(kx + 25f, ky, ww + 4f, 21); kx += 25f + ww + 16f;
            }
            kx += HoverCard.Row(keys, "Esc", "close", kx, ky, t) + 16f;
        }

        private static float FootChip(Sprite icon, string text, bool ok, Color color, float x)
        {
            var t = _t;
            var tx = Kit.Text(_detail, "T", text, Fonts.Medium, 12f, color); tx.richText = true; tx.alignment = TextAlignmentOptions.MidlineLeft;
            float w = Mathf.Min(220f, Kit.W(tx) + 44f);
            var (b, _) = Kit.Box(_detail, "Chip", new Color(0f, 0f, 0f, 0.25f), ok ? Theme.C(t.line2) : Theme.C(t.ember), 8); b.rectTransform.At(x, (Foot - 34f) / 2f, w, 34);
            tx.transform.SetParent(b.transform, false); tx.rectTransform.At(34, 0, w - 40, 34); tx.overflowMode = TextOverflowModes.Ellipsis;
            var ic = Kit.Img(b.rectTransform, "I", icon, Color.white, false); ic.rectTransform.At(7, 6, 22, 22); ic.preserveAspect = true;
            return w;
        }
    }

    // right click on a piece: Valheim's quick select, pick it and close. On the press, as Valheim does: right mouse also toggles
    // the build menu, so on release the menu was already gone. (Button only answers the left button.)
    internal class RightPick : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
    {
        internal Piece Piece;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) BuildMenu.PickPiece(Piece); }
    }

    internal class TileHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        internal Piece Piece; internal GameObject Star;   // Star: shown only while hovered (a favourite's always shows)
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData _) { if (Star != null) Star.SetActive(true); BuildMenu.Hover(Piece, true); }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData _) { if (Star != null) Star.SetActive(false); BuildMenu.Hover(Piece, false); }
    }
}
