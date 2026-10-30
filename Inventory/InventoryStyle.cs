using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Inventory, first slice: Valheim keeps all item handling (drag, split, use, tooltips); Jatify UI
    // restyles what it draws. Slots become the design's (rounded raised box, 60 px, 6 px gaps, 36 px
    // icon, labels 7 px from the corners), the wooden panel becomes a glass panel with a header.
    // Undo: slots are rebuilt by Valheim from its untouched prefab; panel changes are recorded and put back.
    internal static class InventoryStyle
    {
        private const float Slot = 60f, Gap = 7f, Header = 52f;   // design: 60 px slots, 7 px apart
        private static readonly AccessTools.FieldRef<InventoryGrid, int> GridW = AccessTools.FieldRefAccess<InventoryGrid, int>("m_width");
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static float _chestSpaceWas;
        private static CanvasGroup _craftFade;
        private static readonly AccessTools.FieldRef<GuiBar, Color> BarColor = AccessTools.FieldRefAccess<GuiBar, Color>("m_originalColor");
        private static ThemeData _t;
        private static InventoryGui _for;
        private static float _spaceWas;
        private static Vector2 _posWas, _craftWas, _infoWas;
        private static readonly Ledger _l = new Ledger();
        private static readonly List<Graphic> _off = new List<Graphic>();
        private static readonly List<GameObject> _ours = new List<GameObject>();
        private static TextMeshProUGUI _armor, _weight;
        private static Equipment _eq;
        private static Trash _trash;
        internal static Trash TrashSlot => _trash;
        private static int _rowsShown = -1;
        private static Image _weightFill;
        private static int _armorShown = -1, _wShown = -1, _wMaxShown = -1;

        internal static void Apply(ThemeData t)
        {
            _t = t;
            var g = InventoryGui.instance;
            if (g == null) return;
            Log.Info($"inventory restyle: player {(g.m_player != null)}, grid {(g.m_playerGrid != null)}, crafting {(g.m_crafting != null)}, info {(g.m_info != null)}, container {(g.m_container != null)}");
            if (_for == g) { Restore(); }                  // theme change: rebuild with the new colours
            _for = g;
            var grid = g.m_playerGrid;
            _spaceWas = grid.m_elementSpace;
            grid.m_elementSpace = Slot + Gap;
            GridW(grid) = 0;                                // Valheim recreates the slots on its next update

            var panel = g.m_player;
            _posWas = panel.anchoredPosition;
            if (g.m_crafting != null) _craftWas = g.m_crafting.anchoredPosition;
            if (g.m_info != null) _infoWas = g.m_info.anchoredPosition;
            foreach (var name in new[] { "Bkg", "Darken", "Armor/bkg", "Armor/armor_icon", "Armor/ac_text", "Weight/bkg", "Weight/weight_icon", "Weight/weight_text" })
            {
                var tr = panel.Find(name);
                var gr = tr != null ? tr.GetComponent<Graphic>() : null;
                if (gr != null && gr.enabled) { gr.enabled = false; _off.Add(gr); }
            }

            // Everything of ours sits in one frame laid out in design pixels (the live design's 850 x 930 panel
            // at 160, 32); Layout() keeps it there. The vanilla grid is moved so its first cell is at 445, 123.
            _frame = Kit.Node(panel, "jatify-frame"); _frame.SetAsFirstSibling(); _ours.Add(_frame.gameObject);
            var (line, pfill) = Kit.Box(_frame, "jatify-panel", Theme.C(t.glass), Theme.C(t.line2), 14);
            line.rectTransform.Fill(); line.raycastTarget = true;           // the panel swallows clicks like the wood did
            Knots.Corners(line, pfill, 14, Theme.C(t.accent));
            var head = Kit.Node(_frame, "jatify-header").At(0, 0, 850, 52);
            var hl = Kit.Img(_frame, "HeadLine", null, Theme.C(t.glassLine)); hl.rectTransform.At(0, 52, 850, 1);
            var title = Kit.Text(head, "Title", "Inventory", Fonts.Norse, 22f, Theme.C(t.text)); title.alignment = TextAlignmentOptions.MidlineLeft; title.rectTransform.At(18, 0, 90, 52);
            // weight chip (with its mini bar) and armour chip, right-aligned (design: 668 / 791, 30 tall)
            // Weight stays in the header (user: clearer than Carry in the card); Armour lives in the stats card
            var wChip = Chip(head, 13f, 202f, "Weight", t, out _weight);
            var bar = Kit.Panel(wChip, "Bar", new Color(1f, 1f, 1f, 0.1f), 2); bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            bar.rectTransform.pivot = new Vector2(0f, 0.5f); bar.rectTransform.anchoredPosition = new Vector2(82f, 0f); bar.rectTransform.sizeDelta = new Vector2(46f, 4f);
            _weightFill = Kit.Panel(bar.rectTransform, "Value", Theme.C(t.stamina), 2); var fr = _weightFill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0.5f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            _wShown = _wMaxShown = -1;
            // actions (design: To chests / Restock / Merge / Sort from x 105, 4 apart)
            float bx = 105f;
            bx = Action(head, bx, "To chests", "to-chest", t, () => { var p = Player.m_localPlayer; if (p) InvActions.Say($"Put {InvActions.ToChests(p)} stack(s) into nearby chests"); });
            bx = Action(head, bx, "Restock", "from-chest", t, () => { var p = Player.m_localPlayer; if (p) InvActions.Say($"Took {InvActions.Restock(p)} stack(s) from nearby chests"); });
            bx = Action(head, bx, "Merge", "merge", t, () => { var p = Player.m_localPlayer; if (p) InvActions.Say(InvActions.Merge(p) ? "Merged stacks" : "Nothing to merge"); });
            _sortText = null;
            Action(head, bx, "Sort · " + InvActions.SortModes[InvActions.SortMode], "sort", t, () =>
            {   // click sorts; click again within 2 s switches the order and sorts again
                var p = Player.m_localPlayer; if (!p) return;
                if (Time.unscaledTime - _lastSort < 2f) InvActions.SortMode = (InvActions.SortMode + 1) % InvActions.SortModes.Length;
                _lastSort = Time.unscaledTime;
                InvActions.Sort(p);
                if (_sortText != null) _sortText.text = "Sort · " + InvActions.SortModes[InvActions.SortMode];
            });
            if (Azu.Drives)
            {   // AzuEPI's own screens, opened by its (hidden) toggle buttons; icon buttons after Sort
                var tipPrefab = grid.m_elementPrefab.GetComponent<UITooltip>()?.m_tooltipPrefab; float ax = 505f;
                ax = IconAction(head, ax, "tab-eye", "Vanity", "Change how your gear looks without changing its stats (AzuEPI)", "AzuEPI_VanityToggleButton", tipPrefab, t);
                ax = IconAction(head, ax, "shield-knot", "Loadouts", "Save sets of gear and put one on at once (AzuEPI)", "AzuEPI_LoadoutsToggleButton", tipPrefab, t);
                IconAction(head, ax, "tab-book", "Stats", "Your full character sheet (AzuEPI)", "AzuEPI_StatsToggleButton", tipPrefab, t);
            }
            _armorShown = _wShown = _wMaxShown = -1;
            _eq = new Equipment(_frame, t, grid.m_elementPrefab.GetComponent<UITooltip>()?.m_tooltipPrefab);
            var mask = grid.GetComponent<RectMask2D>();                    // our rows sit outside vanilla's grid rect: do not clip them
            if (mask != null && mask.enabled) _l.Do(() => mask.enabled = false, () => { if (mask != null) mask.enabled = true; });
            SectionLabel(_frame, "ACTION BAR", "KEYS 1–8", 285, 67, 529, t);
            _invLabel = SectionLabel(_frame, "INVENTORY", "", 285, 184, 529, t); _countsShown = null;
            // Food / Tools / Meads: three columns of six under the equipment (design .belts)
            var beltLine = Kit.Img(_frame, "BeltLine", null, Theme.C(t.glassLine)); _beltLine = beltLine.rectTransform;
            Knots.Divider(beltLine, true, Theme.C(t.accent));   // design .kdiv.shield: the shield knot above Food / Tools / Meads
            _beltLabels.Clear();
            for (int i = 0; i < SpecialRows.Count; i++)
            {
                var bl = Kit.Text(_frame, "jatify-beltlabel", SpecialRows.Names[i], Fonts.Mono, 11f, Theme.C(t.mute)); bl.characterSpacing = 8f;
                _beltLabels.Add(bl.rectTransform);
            }
            _beltsLaid = -1;
            var (fb, _) = Kit.Box(_frame, "jatify-fold", Theme.C(t.raised), Theme.C(t.line2), 6); fb.raycastTarget = true; _fold = fb.rectTransform;
            // its own canvas one up, as the trash: Valheim's drop-item area and the bag grid lie over this spot and took the click (user)
            var fc = fb.gameObject.AddComponent<Canvas>(); fc.overrideSorting = true; fc.sortingOrder = (_frame.GetComponentInParent<Canvas>()?.rootCanvas.sortingOrder ?? 600) + 1; fb.gameObject.AddComponent<GraphicRaycaster>();
            _foldText = Kit.Text(_fold, "T", "", Fonts.Medium, 11.5f, Theme.C(t.dim)); _foldText.alignment = TMPro.TextAlignmentOptions.Center; _foldText.rectTransform.Fill();
            fb.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (Cfg.BarsCollapsed != null) Cfg.BarsCollapsed.Value = !Cfg.BarsCollapsed.Value; });
            // the trash sits under the grid (design position); the item detail box beside it was removed (user)
            StyleInfo(g.m_info, t);
            _trash = new Trash(_frame, t);
            _stats = new StatsCard(_frame, t);
            LayBelts();
            var cg = ContainerGrid(g);
            if (cg != null) { _chestSpaceWas = cg.m_elementSpace; cg.m_elementSpace = Slot + Gap; GridW(cg) = 0; }
            ChestPanel.Build(g, t, _l);
            if (g.m_crafting != null && g.m_crafting.GetComponent<CanvasGroup>() == null) _craftFade = _l.Added(g.m_crafting.gameObject.AddComponent<CanvasGroup>());
        }

        // Vanilla's character panel: the name and the Texts / Skills / Trophies / Achievements buttons and
        // the PvP toggle keep their logic and tooltips; they get a glass panel and rounded tiles.
        private static void StyleInfo(RectTransform info, ThemeData t)
        {
            if (info == null) return;
            foreach (var n in new[] { "Bkg", "Darken", "TitlePanel/BraidLineHorisontalMedium (1)", "TitlePanel/BraidLineHorisontalMedium (2)" }) _l.Off(info.Find(n)?.GetComponent<Graphic>());
            var (line, _) = Kit.Box(info, "jatify-panel", Theme.C(t.glass), Theme.C(t.glassLine), 12);
            _l.Own(line); line.rectTransform.SetAsFirstSibling(); line.raycastTarget = true;
            line.rectTransform.anchorMin = line.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(670f, 140f); line.rectTransform.anchoredPosition = new Vector2(0f, 2f);
            var name = info.Find("TitlePanel/charactername")?.GetComponent<TMP_Text>();
            _l.Txt(name, Fonts.Norse, 24f, Theme.C(t.text));
            if (name != null) _l.Rect(name.rectTransform, r => r.sizeDelta = new Vector2(480f, r.sizeDelta.y)); // the name is wider than vanilla's box
            if (name != null) _l.Rect(name.rectTransform, r => r.anchoredPosition = new Vector2(0f, -12f));   // lower in the header, centred (user)
            _l.Align(name, TextAlignmentOptions.Center);
            // design tab set: Crafting (our panel), Achievements, Trophies, Texts; icon over label, 62 tall.
            // Skills lives in the equipment area's Skills tab and PvP beside it, so vanilla's are hidden.
            _l.Hide(info.Find("Skills")?.gameObject); _l.Hide(info.Find("PVP")?.gameObject);
            // our three tabs: greyscale painted icons (user's, in Valheim's tab style), lit gold like the vanilla tabs.
            // Ours are copies of the vanilla icon (lithud material) with the vanilla button's gold tint.
            var vIcon = info.Find("Trophies/Image") as RectTransform;
            var vBtn = info.Find("Trophies")?.GetComponent<Button>();
            var gold = vBtn != null ? vBtn.colors.normalColor : new Color(1f, 0.718f, 0.36f);
            Image LitIcon(Transform parent, string sprite)
            {
                if (vIcon == null) { var f = Kit.IconImg(parent, sprite, Color.white, 26f); f.name = "Icon"; return f; }
                var go = Object.Instantiate(vIcon.gameObject, parent, false); go.name = "Icon";
                var im = go.GetComponent<Image>(); im.sprite = Kit.Icon(sprite); im.preserveAspect = true; im.raycastTarget = false;
                im.color = gold;   // the vanilla tab button tints its icon with this (ColorTint); lithud adds the painted light
                return im;
            }
            var crafting = Kit.Node(info, "Crafting-tab"); _l.Own(crafting);
            var cic = LitIcon(crafting, "tab-hammer"); cic.rectTransform.sizeDelta = new Vector2(26f, 26f);
            var cbt = crafting.gameObject.AddComponent<Button>();
            cbt.onClick.AddListener(() => { CloseWindows(); Pages.Show(null); });
            foreach (var (n, icon, page) in new[] { ("Recipes-tab", "tab-book", "recipes"), ("Index-tab", "tab-eye", "index"), ("Auto-tab", "tab-hammer", "auto") })
            {
                var node = Kit.Node(info, n); _l.Own(node);
                var ic = LitIcon(node, icon); ic.rectTransform.sizeDelta = new Vector2(26f, 26f);
                if (page == "auto") { var sm = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("smelter")?.GetComponent<Piece>()?.m_icon : null; if (sm != null) ic.sprite = sm; }   // the smelter's own picture
                node.gameObject.AddComponent<Button>().onClick.AddListener(() => { CloseWindows(); Pages.Show(page); });
            }
            foreach (var (n, page) in new[] { ("Achievements", "ach"), ("Trophies", "trophies"), ("Texts", "texts") })
            {   // our pages (live design) instead of Valheim's windows; the vanilla clicks come back on unload
                var vb = info.Find(n)?.GetComponent<Button>(); if (vb == null) continue;
                var was = vb.onClick; var pg = page;
                _l.Do(() => { vb.onClick = new Button.ButtonClickedEvent(); vb.onClick.AddListener(() => { CloseWindows(); Pages.Show(pg); }); }, () => { if (vb != null) vb.onClick = was; });
            }
            var tabs = new[] { ("Crafting-tab", "Crafting"), ("Achievements", "Achievements"), ("Trophies", "Trophies"), ("Texts", "Texts"), ("Recipes-tab", "New Recipes"), ("Index-tab", "Compendium"), ("Auto-tab", "Automation") };
            _tabs.Clear();
            // each tile as wide as its label needs, the room left shared out evenly (user: Achievements and New Recipes were
            // squashed into 81 px while Texts and Index had room to spare); one label size for all
            var laid = new List<(RectTransform b, TextMeshProUGUI lt, float w)>();
            for (int i = 0; i < tabs.Length; i++)
            {
                var (n, label) = tabs[i];
                var b = info.Find(n) as RectTransform; if (b == null) continue;
                var (tl, tf) = Kit.Box(b, "jatify-tile", Theme.C(t.raised), Theme.C(t.glassLine), 10);
                _l.Own(tl); tl.rectTransform.SetAsFirstSibling(); tl.rectTransform.Fill(); tl.raycastTarget = true;
                // ours get Valheim's own hover (its tabs grey a little when hovered; user: keep that look for all seven)
                var btn = b.GetComponent<Button>();
                if (btn != null && btn.targetGraphic == null)
                {   // like Valheim's: the icon carries the tint (gold, lighter on hover), the tile stays as it is
                    var icon = b.Find("Icon")?.GetComponent<Graphic>();
                    if (icon != null && vBtn != null) { btn.targetGraphic = icon; btn.transition = vBtn.transition; btn.colors = vBtn.colors; icon.color = Color.white; } else btn.targetGraphic = tf;   // white: the block's gold tints it once
                }
                var vtip = b.GetComponent<UITooltip>();   // Valheim's own hover tooltip on its tabs repeats the label we show (a ghost name over the icon)
                if (vtip != null && vtip.enabled) _l.Do(() => vtip.enabled = false, () => { if (vtip != null) vtip.enabled = true; });
                _tabs.Add((n, tl, tf));
                _l.Off(b.Find("Background")?.GetComponent<Graphic>());
                foreach (var icn in new[] { "Icon", "Checked", "Image" })
                {
                    var ir = b.Find(icn) as RectTransform;
                    _l.Rect(ir, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0f, -7f); r.sizeDelta = new Vector2(30f, 30f); });
                }
                var lt = Kit.Text(b, "jatify-label", label, Fonts.Medium, 12f, Theme.C(t.dim)); _l.Own(lt); lt.alignment = TextAlignmentOptions.Center;
                laid.Add((b, lt, Mathf.Max(64f, Kit.W(lt) + 20f)));
                if (n == "Recipes-tab")
                {   // unseen unlocks (design .badge), top right of the tile
                    var (bb, _) = Kit.Box(b, "jatify-badge", Theme.C(t.accent), Theme.C(t.accent), 5); _l.Own(bb);
                    var br = bb.rectTransform; br.anchorMin = br.anchorMax = br.pivot = Vector2.one; br.anchoredPosition = new Vector2(-5f, -5f); br.sizeDelta = new Vector2(24f, 16f);
                    _badge = Kit.Text(br, "T", "", Fonts.Mono, 10f, new Color(0.02f, 0.08f, 0.05f)); _badge.alignment = TextAlignmentOptions.Center; _badge.rectTransform.Fill();
                }
            }
            const float Row = 626f, TabGap = 8f;   // the header's inner width
            float need = TabGap * (laid.Count - 1); foreach (var e in laid) need += e.w;
            float extra = laid.Count > 0 ? Mathf.Max(0f, Row - need) / laid.Count : 0f, x0 = -Mathf.Min(Row, need + extra * laid.Count) / 2f;
            foreach (var (b, lt, w0) in laid)
            {
                float w = w0 + extra, cx = x0 + w / 2f; x0 += w + TabGap;
                _l.Rect(b, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = new Vector2(cx, -22f); r.sizeDelta = new Vector2(w, 62f); });
                lt.rectTransform.At(0, 40, w, 16);
            }
            // settings gear, top right (design): opens the Jatify UI settings page
            var (gb, gf) = Kit.Box(info, "jatify-gear", Theme.C(t.raised), Theme.C(t.glassLine), 8); _l.Own(gb);
            gb.rectTransform.anchorMin = gb.rectTransform.anchorMax = gb.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            gb.rectTransform.anchoredPosition = new Vector2(303f, 37f); gb.rectTransform.sizeDelta = new Vector2(32f, 32f);   // level with the name (user)
            var gi = Kit.IconImg(gb.rectTransform, "repair32", Theme.C(t.dim), 18f); gi.rectTransform.At(7, 7, 18, 18);
            gb.raycastTarget = true; var gbtn = gb.gameObject.AddComponent<Button>(); gbtn.targetGraphic = gf;
            gbtn.onClick.AddListener(() => { InventoryGui.instance?.Hide(); ConfigPage.Show(Root.Canvas.transform, t, false); });
        }

        private static TextMeshProUGUI _badge; private static int _badgeShown = -1;
        private static void CloseWindows() { var g = InventoryGui.instance; if (g == null) return; g.OnCloseTrophies(); g.OnCloseAchievements(); if (g.m_textsDialog) g.m_textsDialog.gameObject.SetActive(false); }
        private static readonly List<(string name, Image line, Image fill)> _tabs = new List<(string, Image, Image)>();
        private static string _tabOn;
        // the open window's tab is green (design); Crafting when none is open
        private static void PaintTabs(InventoryGui g, ThemeData t)
        {
            string on = (g.m_achievementsPanel && g.m_achievementsPanel.gameObject.activeSelf) || Pages.Open == "ach" ? "Achievements"
                : (g.m_trophiesPanel && g.m_trophiesPanel.activeSelf) || Pages.Open == "trophies" ? "Trophies"
                : (g.m_textsDialog && g.m_textsDialog.gameObject.activeInHierarchy) || Pages.Open == "texts" ? "Texts"
                : Pages.Open == "recipes" ? "Recipes-tab" : Pages.Open == "index" ? "Index-tab" : Pages.Open == "auto" ? "Auto-tab" : "Crafting-tab";
            Unlocks.Ensure();
            if (_badge != null && Unlocks.Unseen != _badgeShown) { _badgeShown = Unlocks.Unseen; _badge.text = _badgeShown > 99 ? "99+" : _badgeShown.ToString(); _badge.transform.parent.gameObject.SetActive(_badgeShown > 0); }
            if (on == _tabOn) return;
            _tabOn = on;
            foreach (var (n, line, fill) in _tabs)
            {
                if (!line) continue;
                bool a = n == on;
                line.color = Theme.C(a ? t.accent : t.glassLine); fill.color = Theme.C(a ? t.accentSoft : t.raised);
                var lt = line.transform.parent.Find("jatify-label")?.GetComponent<TMP_Text>(); if (lt) lt.color = a ? Color.white : Theme.C(t.dim);
            }
        }

        // Extra space above row y for section labels: Action bar, Inventory, then one per special row.
        // Where a player-grid cell goes, in design pixels from the first action-bar cell (445, 123 on screen):
        // action bar on row 0, inventory rows from y 240, Food / Tools / Meads as columns under the equipment.
        internal static Vector2 CellDesign(int x, int y)
        {
            if (y == 0) return new Vector2(x * Pitch, 0f);
            if (y < SpecialRows.Start) return new Vector2(x * Pitch, 117f + (y - 1) * Pitch);
            int band = y - SpecialRows.Start;
            if (!SpecialRows.On) return new Vector2(x * Pitch, BeltTop - 123f + band * Pitch);   // AzuEPI's equipment / quick cells: rows under the grid, lined up with it
            if (BeltRows) return band == 1 ? new Vector2(x * Pitch, BeltTop - 123f + BeltLabel)             // Tools first, a row under the grid, its label over its first slot (user)
                : new Vector2((band == 2 ? MeadsX : 0f) + x * Pitch, BeltTop - 123f + 2f * BeltLabel + Pitch);   // then Food and Meads side by side
            if (BeltBlocks) return new Vector2(band * BlockStep + (x % 2) * Pitch, BlockTop - 123f + (x / 2) * Pitch);   // 2 x 3 blocks under the grid
            return new Vector2(179f - 445f + band * 90f, 548f - 123f + x * Pitch);                   // a column under the equipment
        }

        // Food / Tools / Meads (live design): three rows under the inventory grid while they fit above the trash,
        // three columns under the equipment once more rows would make them overlap it.
        private static int InvRows => SpecialRows.Start - 1;                                             // rows under the action bar
        private static float BeltTop => 240f + InvRows * Pitch - 7f + 30f;                              // screen y of the first belt row
        internal static bool BeltRows => BeltTop + BeltH - 7f <= 852f - 10f;         // short inventories: Tools, then Food and Meads
        private const float BeltLabel = 30f;                                          // each row's label sits over it, above the key caps
        private static float BeltH => 2f * (BeltLabel + Pitch);
        private const float MeadsX = SpecialRows.ShortWidth * Pitch;                  // Meads' cells right after Food's 3: one bar (user)
        // more rows (user): Food / Tools / Meads as 2 x 3 blocks side by side under the grid, labels above; the
        // panel grows. Columns under the equipment only when even that would leave the screen.
        private const float BlockStep = 157f;                                                           // 2 slots (127) + 30 between blocks
        private static float BlockTop => GridBottom + 30f + 22f;
        private static float BlocksBottom => BlockTop + 3f * Pitch - 7f;
        internal static bool BeltBlocks => !BeltRows && BlocksBottom + 18f <= 1062f;
        private static RectTransform _beltLine; private static readonly List<RectTransform> _beltLabels = new List<RectTransform>(); private static int _beltsLaid = -1;
        // the Tools / Food / Meads rows fold away with a button on their line (user); the items stay and work from the HUD
        private static bool Folded => SpecialRows.On && Cfg.BarsCollapsed != null && Cfg.BarsCollapsed.Value;
        private static RectTransform _fold; private static TMPro.TextMeshProUGUI _foldText;

        // the panel ends under its lowest part; the trash sits in the 8th column under the last inventory row
        private static float GridBottom => 240f + InvRows * Pitch - 7f;
        private static float TrashTop => GridBottom + 30f;                                              // just under the separator line
        // the stats card sits under the hands row (y 505) whenever Food / Tools / Meads are under the grid
        private const float StatsTop = 505f;
        private static bool BeltColumns => !BeltRows && !BeltBlocks;
        private static float StatsH => _stats != null ? _stats.Height : StatsCard.Needed;
        private static float PanelBottom => Folded ? Mathf.Max(TrashTop + 60f, StatsTop + StatsH)
            : BeltRows ? Mathf.Max(BeltTop + BeltH - 7f, StatsTop + StatsH)
            : BeltBlocks ? Mathf.Max(BlocksBottom, StatsTop + StatsH) : Mathf.Max(943f, TrashTop + 60f);
        private static StatsCard _stats;
        internal static float FrameH => PanelBottom + 18f - 32f;

        // one of AzuEPI's cells: its armour slots hide while they hold what the equipment panel already shows (or nothing);
        // every other one shows, with the slot's name while empty. A cell holding an item it doesn't wear always shows.
        private static void AzuCell(Transform c, InventoryElement el, Inventory inv, Vector2 origin)
        {
            var d = CellDesign(el.Position.x, el.Position.y); var rt = (RectTransform)c; var at = origin + new Vector2(d.x, -d.y);
            if (rt.anchoredPosition != at) rt.anchoredPosition = at;   // AzuEPI places them in its own panel each update: ours, every frame
            var slot = Azu.SlotAt(inv, el.Position.x, el.Position.y); var it = inv.GetItemAt(el.Position.x, el.Position.y);
            bool show = !slot.Known && it != null;   // its slots are drawn by its own cells (AzuSlotCell); a grid cell only for an item no slot of its holds
            if (c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            var nm = c.Find("jatify-azuname") as RectTransform;
            if (nm == null)
            {
                var tx = Kit.Text(c, "jatify-azuname", "", Fonts.Mono, 8.5f, new Color(1f, 1f, 1f, 0.35f)); tx.alignment = TMPro.TextAlignmentOptions.Center; tx.textWrappingMode = TMPro.TextWrappingModes.Normal;
                tx.rectTransform.Fill(4f); tx.raycastTarget = false; nm = tx.rectTransform;
            }
            var t = nm.GetComponent<TMPro.TextMeshProUGUI>(); var want = it == null && slot.Known ? slot.Name : "";
            if (t.text != want) t.text = want;
            AzuBind(c);
        }

        // its slot name / key label (it sets it big, wrapped and auto-sized over the whole cell): our small top line
        private static void AzuBind(Transform cell)
        {
            var b = cell.Find("binding")?.GetComponent<TMP_Text>(); if (b == null || !b.enableAutoSizing) return;
            Label(b, Fonts.Mono, 8.5f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Top, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(Slot - 8f, 12f));
            b.textWrappingMode = TextWrappingModes.NoWrap; b.overflowMode = TextOverflowModes.Ellipsis;
        }

        // its HUD quick-slot bar: unseen (our tools bar shows its quick slots; put back on unload)
        private static CanvasGroup _azuBar;
        private static void AzuQuickBar()
        {
            if (_azuBar == null)
            {
                var bar = Hud.instance != null ? Hud.instance.m_rootObject.transform.Find("AzuEPI_QuickAccessBar") : null; if (bar == null) return;
                _azuBar = bar.GetComponent<CanvasGroup>(); if (_azuBar == null) { _azuBar = bar.gameObject.AddComponent<CanvasGroup>(); _azuHidden.Add(_azuBar); }
            }
            if (_azuBar.alpha != 0f) { _azuBar.alpha = 0f; _azuBar.blocksRaycasts = false; }
        }

        private static void AzuSlotCell(InventoryElement e, Inventory inv, RectTransform cell00, RectTransform root)
        {
            var rt = (RectTransform)e.transform; var slot = Azu.SlotAt(inv, e.Position.x, e.Position.y); var it = inv.GetItemAt(e.Position.x, e.Position.y);
            var doll = Azu.DollSlot(slot);
            bool show = slot.Known ? doll != null || !(slot.Armour && (it == null || it.m_equipped)) : it != null;   // the equipment panel shows worn armour; its doll cells always show; a cell left from a removed slot only if it holds something
            if (e.gameObject.activeSelf != show) e.gameObject.SetActive(show);
            if (!show) return;
            int extraAt = doll == null && !slot.Quick ? _azuExtras.IndexOf(e) : -1;   // other mods' slots (Jewelcrafting's rings, its config's custom slots, ...)
            RectTransform over = doll != null ? _eq?.DollCell(doll) : extraAt >= 0 ? _eq?.ExtraCell(extraAt) : null;   // its equipment on the doll, where ours is; other mods' slots beside the ammo and key pouch
            if ((doll != null || extraAt >= 0) && over == null) { e.gameObject.SetActive(false); return; }   // the Skills tab is showing
            int quick = slot.Quick ? _azuQuick.IndexOf(e) : -1;   // quick slots: the first row under the grid, by order (it packs its cells one after another)
            if (slot.Quick && quick < 0) _azuScan = 0f;   // a quick slot just added: look again next frame
            var d = quick >= 0 ? CellDesign(quick % 8, Azu.NormalRows + quick / 8) : CellDesign(e.Position.x, e.Position.y);
            var size = over != null ? over.rect.size : cell00.sizeDelta; var scaleFrom = over != null ? over : root;
            float k = scaleFrom.lossyScale.x / Mathf.Max(0.0001f, rt.parent.lossyScale.x);
            if (Mathf.Abs(rt.localScale.x - k) > 0.0001f) rt.localScale = new Vector3(k, k, 1f);
            var pivot = over != null ? new Vector2(0.5f, 0.5f) : cell00.pivot;
            if (rt.pivot != pivot) rt.pivot = pivot;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            var at = over != null ? over.TransformPoint(over.rect.center) : cell00.position + root.TransformVector(new Vector3(d.x, -d.y, 0f));
            if ((rt.position - at).sqrMagnitude > 0.01f) rt.position = at;
            AzuBind(rt);
        }

        // AzuEPI's own inventory screen (its wood panel, character page and preview, tab buttons, side panels): ours draws
        // the inventory, so they go unseen (a group, put back on unload); they keep working underneath
        // (its slot cells live on its character page: that page stays, its preview and name go)
        private static readonly string[] AzuPanelNames = { "AzuEPI_PlayerBkg", "AzuEPI_PlayerPreview", "AzuEPI_CharacterName", "AzuEPI_CharacterPageBkg", "AzuEPI_ToggleButtonsGlg", "AzuEPI_DropAllButton" };
        private static readonly string[] AzuScreens = { "AzuEPI_VanityPanel", "AzuEPI_StatsPanel", "AzuEPI_LoadoutPanel", "AzuEPI_LoadoutsRootPanel" };   // opened from our header: restyled while open
        private static readonly List<InventoryElement> _azuSlots = new List<InventoryElement>();   // its slot cells, moved out of our grid
        private static readonly List<InventoryElement> _azuExtras = new List<InventoryElement>(), _azuQuick = new List<InventoryElement>();   // of those, other mods' slots (not on the doll, not quick), and its quick slots
        private static readonly List<CanvasGroup> _azuHidden = new List<CanvasGroup>(); private static float _azuScan;
        private static bool _azuScreenShown; private static readonly List<(CanvasGroup cg, bool ours, float alpha, bool blocks)> _underScreen = new List<(CanvasGroup, bool, float, bool)>();
        private static void AzuPanels(InventoryGui g)
        {
            // its character page (where its slot cells live) sits in the crafting panel, which Valheim hides while a chest is
            // open: the slots went with it. Moved up beside it, in place (the cells are placed in screen space anyway)
            var page = g.m_crafting.Find("AzuEPI_CharacterPage");
            if (page != null) { var from = g.m_crafting; int at = from.GetSiblingIndex(); _l.Do(() => page.SetParent(from.parent, true), () => { if (page != null && from != null) page.SetParent(from, true); }); page.SetSiblingIndex(at + 1); }
            bool screen = false;   // one of its screens open: our crafting panel steps away (it covers it, as it hides Valheim's)
            foreach (var n in AzuScreens) { var s = g.m_crafting.transform.Find(n); if (s != null && s.gameObject.activeSelf) { screen = true; break; } }
            if (screen != _azuScreenShown)
            {   // everything on the crafting side that is not its own (our recipes, search, filters, tabs, title) goes unseen
                _azuScreenShown = screen;
                if (screen)
                    foreach (Transform c in g.m_crafting.transform)
                    {
                        if (c.name.StartsWith("AzuEPI_") || c.name == "jatify-panel") continue;   // our panel and its border stay: its screens stand on it
                        var cg = c.GetComponent<CanvasGroup>(); bool ours = cg == null; if (ours) cg = c.gameObject.AddComponent<CanvasGroup>();
                        _underScreen.Add((cg, ours, cg.alpha, cg.blocksRaycasts)); cg.alpha = 0f; cg.blocksRaycasts = false;
                    }
                else
                {   // back exactly as they were (Valheim's own list stays hidden by ours)
                    foreach (var (cg, ours, a, b) in _underScreen) { if (cg == null) continue; if (ours) UnityEngine.Object.Destroy(cg); else { cg.alpha = a; cg.blocksRaycasts = b; } }
                    _underScreen.Clear();
                }
            }
            if (Time.unscaledTime < _azuScan) return; _azuScan = Time.unscaledTime + 1f;
            _azuSlots.Clear();
            foreach (var e in g.GetComponentsInChildren<InventoryElement>(true)) if (e.name.StartsWith("AzuEPI_Slot")) _azuSlots.Add(e);
            var inv = Player.m_localPlayer?.GetInventory(); _azuExtras.Clear();
            _azuQuick.Clear();
            if (inv != null) foreach (var e in _azuSlots) { var sl = Azu.SlotAt(inv, e.Position.x, e.Position.y); if (sl.Known && sl.Quick) _azuQuick.Add(e); else if (sl.Known && Azu.DollSlot(sl) == null) _azuExtras.Add(e); }
            System.Comparison<InventoryElement> order = (a, b) => a.Position.y != b.Position.y ? a.Position.y.CompareTo(b.Position.y) : a.Position.x.CompareTo(b.Position.x);
            _azuExtras.Sort(order); _azuQuick.Sort(order);
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(AzuScreens, t.name) >= 0 && t.gameObject.activeInHierarchy) { if (t.parent == g.m_crafting.transform && t.GetSiblingIndex() != t.parent.childCount - 1) t.SetAsLastSibling(); EpicSkin.Skin(t); var bg = t.parent == g.m_crafting.transform ? t.GetComponent<Image>() : null; if (bg != null && bg.color.a > 0f) bg.color = Color.clear; continue; }   // its own backdrop off: ours shows   // over our crafting panel, which it replaces while open
                if (System.Array.IndexOf(AzuPanelNames, t.name) < 0) continue;
                var cg = t.GetComponent<CanvasGroup>(); if (cg == null) { cg = t.gameObject.AddComponent<CanvasGroup>(); _azuHidden.Add(cg); }
                if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false; }
            }
        }
        internal static void AzuRestore() { if (_azuScreenShown) { _azuScreenShown = false; foreach (var (cg, ours, a, b) in _underScreen) { if (cg == null) continue; if (ours) UnityEngine.Object.Destroy(cg); else { cg.alpha = a; cg.blocksRaycasts = b; } } _underScreen.Clear(); } foreach (var cg in _azuHidden) if (cg != null) UnityEngine.Object.Destroy(cg); _azuHidden.Clear(); }

        private static void LayBelts()
        {
            int key = SpecialRows.Start * 2 + (Folded ? 1 : 0);
            if (_beltLine == null || _beltsLaid == key) return;
            _beltsLaid = key;
            foreach (var l in _beltLabels) if (l != null && l.gameObject.activeSelf != (SpecialRows.On && !Folded)) l.gameObject.SetActive(SpecialRows.On && !Folded);   // AzuEPI: its cells carry their own names
            if (_for != null && SpecialRows.On)
                foreach (Transform c in _for.m_playerGrid.m_gridRoot) { var e = c.GetComponent<InventoryElement>(); if (e != null && e.Position.y >= SpecialRows.Start && e.Position.y < SpecialRows.EqRow) e.gameObject.SetActive(!Folded && e.Position.x < SpecialRows.RowWidth(e.Position.y - SpecialRows.Start)); }
            if (_fold != null) { _fold.gameObject.SetActive(SpecialRows.On); _foldText.text = Folded ? "Show bars" : "Hide bars"; }
            _trash?.MoveTo(914f - 160f, Azu.Drives ? PanelBottom - 32f - 8f : TrashTop - 32f);   // AzuEPI: its quick slots fill the row under the grid, so the bottom right corner
            if (BeltRows)
            {
                _beltLine.At(285, BeltTop - 32f - 15f, 529, 1);
                _fold?.At(285, BeltTop - 32f - 15f - 11f, 84, 22);   // on the line, its left end (user)
                for (int i = 0; i < _beltLabels.Count; i++) if (_beltLabels[i] != null) _beltLabels[i].At(285 + (i == 2 ? MeadsX : 0f), BeltTop - 32f + (i == 1 ? 0f : BeltLabel + Pitch) + 2f, 120, 14);   // labels: Food 0, Tools 1, Meads 2
            }
            else if (BeltBlocks)
            {
                _beltLine.At(285, GridBottom + 16f - 32f, 529, 1);
                _fold?.At(285, GridBottom + 16f - 32f - 11f, 84, 22);
                for (int i = 0; i < _beltLabels.Count; i++) if (_beltLabels[i] != null) _beltLabels[i].At(285 + i * BlockStep, BlockTop - 20f - 32f, 127, 14);
            }
            else
            {
                _beltLine.At(19, 479, 240, 1);
                _fold?.At(19, 479 - 11f, 84, 22);
                for (int i = 0; i < _beltLabels.Count; i++) if (_beltLabels[i] != null) _beltLabels[i].At(19 + i * 90, 494, 60, 14);
            }
        }
        private const float Pitch = 67f;

        private static RectTransform _frame;
        private static TextMeshProUGUI _invLabel; private static string _countsShown;
        // design .label: mono caps on the left, a quiet note on the right; returns the note (the inventory counts live there)
        private static TextMeshProUGUI SectionLabel(RectTransform frame, string text, string note, float x, float y, float w, ThemeData t)
        {
            var l = Kit.Text(frame, "jatify-label", text, Fonts.Mono, 11f, Theme.C(t.mute)); l.characterSpacing = 8f; l.rectTransform.At(x, y, w, 14);
            var n = Kit.Text(frame, "jatify-label-note", note, Fonts.Mono, 11f, Theme.C(t.mute)); n.characterSpacing = 8f; n.alignment = TextAlignmentOptions.TopRight; n.rectTransform.At(x, y, w, 14);
            return n;
        }

        private static TextMeshProUGUI _sortText; private static float _lastSort = -9f;
        private static float Action(RectTransform head, float x, string label, string icon, ThemeData t, System.Action on)
        {
            var (b, f) = Kit.Box(head, "jatify-act", Theme.C(t.raised), Theme.C(t.glassLine), 8);
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Medium, 12.5f, Theme.C(t.dim)); tx.alignment = TextAlignmentOptions.MidlineLeft;
            float w = Kit.W(tx) + 14f + 21f;
            var rt = b.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(x, 0f); rt.sizeDelta = new Vector2(w, 30f);
            var ic = Kit.IconImg(b.rectTransform, icon, Theme.C(t.dim), 16f); ic.rectTransform.At(8, 7, 16, 16);
            tx.rectTransform.At(28, 0, w - 28, 30);
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f;
            var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.35f); btn.colors = cb;
            btn.onClick.AddListener(() => on());
            if (label.StartsWith("Sort")) _sortText = tx;
            return x + w + 6f;
        }

        private static float IconAction(RectTransform head, float x, string icon, string title, string text, string azuButton, GameObject tipPrefab, ThemeData t)
        {
            var (b, f) = Kit.Box(head, "jatify-azu-" + title, Theme.C(t.raised), Theme.C(t.glassLine), 8);
            var rt = b.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(x, 0f); rt.sizeDelta = new Vector2(30f, 30f);
            Kit.IconImg(b.rectTransform, icon, Theme.C(t.dim), 16f).rectTransform.At(7, 7, 16, 16);
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f;
            var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.35f); btn.colors = cb;
            btn.onClick.AddListener(() =>
            {
                var g = InventoryGui.instance; if (g == null) return;
                foreach (var tr in g.GetComponentsInChildren<Button>(true)) if (tr.name == azuButton) { tr.onClick.Invoke(); _azuScan = 0f; return; }   // restyled on the next frame, not a second later
                Log.Warn("AzuEPI: no " + azuButton + " (its option may be off)");
            });
            if (tipPrefab != null) { var tip = b.gameObject.AddComponent<UITooltip>(); tip.m_tooltipPrefab = tipPrefab; tip.Set(title, text); }
            return x + 36f;
        }

        private static RectTransform Chip(Transform head, float right, float w, string label, ThemeData t, out TextMeshProUGUI value)
        {
            var (o, _) = Kit.Box(head, label, Theme.C(t.raised), Theme.C(t.glassLine), 8);
            var rt = o.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right - 4f, 0f); rt.sizeDelta = new Vector2(w, 30f);
            // design: the Blackmetal shield's own icon for Armour, Valheim's weight icon for Weight
            var shield = label == "Armour" ? ObjectDB.instance?.GetItemPrefab("ShieldBlackmetal")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
            var ic = shield != null ? Kit.Img(rt, "Icon", shield, Color.white, false) : Kit.IconImg(rt, "weight_icon_32", Theme.C(t.dim), 18f);
            ic.preserveAspect = true; ic.rectTransform.At(8, 6, 18, 18);
            var l = Kit.Text(rt, "Label", label, Fonts.Medium, 12.5f, Theme.C(t.dim)); l.alignment = TextAlignmentOptions.MidlineLeft;
            l.rectTransform.Fill(); l.rectTransform.offsetMin = new Vector2(31f, 0f);
            value = Kit.Text(rt, "Value", "", Fonts.Mono, 12.5f, Theme.C(t.text)); value.alignment = TextAlignmentOptions.MidlineRight;
            value.rectTransform.Fill(); value.rectTransform.offsetMax = new Vector2(-10f, 0f);
            return rt;
        }

        // Called after Valheim updates a grid: restyle any slot that is still vanilla.
        internal static void StyleGrid(InventoryGrid grid)
        {
            if (_t == null || _for == null || (grid != _for.m_playerGrid && grid != ContainerGrid(_for))) return;
            var root = grid.m_gridRoot;
            for (int i = 0; i < root.childCount; i++)
            {
                var el = root.GetChild(i).GetComponent<InventoryElement>();
                if (el == null || el.transform.Find("jatify-fill") != null) continue;
                StyleSlot(el, grid == _for.m_playerGrid);
            }
        }

        private static void StyleSlot(InventoryElement e, bool player)
        {
            var t = _t;
            var rt = (RectTransform)e.transform;
            {   // lock marks: player cells and chest cells alike
                var dim = Kit.Panel(rt, "jatify-dim", new Color(0f, 0f, 0f, 0.38f), 10); dim.rectTransform.Fill(); dim.enabled = false; dim.raycastTarget = false;
                var lk = Kit.Img(rt, "jatify-lock", Kit.Icon("lock"), Theme.C(t.dim), false); lk.rectTransform.anchorMin = lk.rectTransform.anchorMax = lk.rectTransform.pivot = new Vector2(1f, 1f);
                lk.rectTransform.anchoredPosition = new Vector2(-4f, -4f); lk.rectTransform.sizeDelta = new Vector2(12f, 12f); lk.enabled = false; lk.raycastTarget = false;
            }
            if (player)
            {
                var d = CellDesign(e.Position.x, e.Position.y);   // vanilla put it at (x, -y) * pitch from the first cell
                rt.anchoredPosition += new Vector2(d.x - e.Position.x * Pitch, -(d.y - e.Position.y * Pitch));
                if (SpecialRows.On && (e.Position.y == SpecialRows.EqRow || (e.Position.y >= SpecialRows.Start && e.Position.y < SpecialRows.EqRow && (Folded || e.Position.x >= SpecialRows.RowWidth(e.Position.y - SpecialRows.Start))))) e.gameObject.SetActive(false); // reserved rows are 6 wide; the worn-armour row is hidden (AzuEPI's cells: never hidden here)
            }
            rt.sizeDelta = new Vector2(Slot, Slot);
            var bg = e.GetComponent<Image>();
            bg.sprite = Kit.Rounded(10); bg.type = Image.Type.Sliced; bg.color = Theme.C(t.glassLine);   // the outline
            var fill = Kit.Panel(rt, "jatify-fill", Theme.C(t.raised), 9); fill.rectTransform.Fill(1f); fill.transform.SetSiblingIndex(0);
            if (e.m_dropFocus != null) e.m_dropFocus.enabled = false; // vanilla keeps it faintly on every slot, which made our slots lighter than the equipment ones
            Round(e.m_equiped, new Color(0.13f, 0.84f, 0.49f, 0.12f));
            Round(e.m_queued, new Color(0.95f, 0.44f, 0.23f, 0.45f));
            if (e.m_selected != null) { var s = e.m_selected.GetComponent<Image>(); Round(s, Theme.C(t.accent)); ((RectTransform)e.m_selected.transform).Fill(-3f); }
            ((RectTransform)e.m_icon.transform).Fill(12f); e.m_icon.preserveAspect = true;
            Label(e.m_amount, Fonts.Mono, 11f, Theme.C(t.text), TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-7f, 6f), new Vector2(50f, 16f));
            var bind = rt.Find("binding")?.GetComponent<TMP_Text>();
            if (bind != null) Label(bind, Fonts.Mono, 10f, Theme.C(t.mute), TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(7f, -5f), new Vector2(16f, 14f));
            Label(e.m_quality, Fonts.Mono, 10f, Theme.C(t.stamina), TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-7f, -5f), new Vector2(16f, 14f));
            var star = Kit.Img(rt, "jatify-star", Kit.Star(), Theme.C(t.stamina), false); var sr = star.rectTransform;   // design: star + number
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(1f, 1f); sr.anchoredPosition = new Vector2(-18f, -7f); sr.sizeDelta = new Vector2(9f, 9f); star.enabled = false;
            if (e.m_food != null) e.m_food.rectTransform.localScale = Vector3.zero;    // the design has no food marker
            var dur = (RectTransform)e.m_durability.transform;
            dur.anchorMin = new Vector2(0f, 0f); dur.anchorMax = new Vector2(1f, 0f); dur.pivot = new Vector2(0.5f, 0f);
            dur.offsetMin = new Vector2(6f, 4f); dur.offsetMax = new Vector2(-6f, 7f);
            var track = dur.GetComponent<Image>(); track.sprite = Kit.Rounded(2); track.type = Image.Type.Sliced; track.color = new Color(1f, 1f, 1f, 0.1f);
            var barImg = e.m_durability.m_bar.GetComponent<Image>(); barImg.sprite = Kit.Rounded(2); barImg.type = Image.Type.Sliced; barImg.color = Theme.C(t.accent);
            BarColor(e.m_durability) = Theme.C(t.accent);
            e.m_durability.SetWidth(Slot - 12f);
        }

        private static void Round(Image i, Color c) { if (i == null) return; i.sprite = Kit.Rounded(10); i.type = Image.Type.Sliced; i.color = c; }

        private static void Label(TMP_Text x, TMP_FontAsset font, float size, Color c, TextAlignmentOptions align, Vector2 corner, Vector2 offset, Vector2 box)
        {
            if (x == null) return;
            if (font != null) x.font = font;
            x.fontSize = size; x.color = c; x.alignment = align; x.enableAutoSizing = false;
            var r = x.rectTransform; r.anchorMin = r.anchorMax = r.pivot = corner; r.anchoredPosition = offset; r.sizeDelta = box;
        }

        // Header numbers, refreshed while the inventory is open.
        internal static void Tick()
        {
            if (_for != null && _eq != null && !InventoryGui.IsVisible()) { _eq.Idle(); _trash?.Place(_for.m_playerGrid); }   // the character camera must not run with the inventory closed
            if (!SpecialRows.On) AzuQuickBar();
            if (_for == null || _frame == null || !InventoryGui.IsVisible()) return;
            var p = Player.m_localPlayer; if (p == null) return;
            ChestPanel.Tick();
            _eq?.Tick(p);
            if (_for != null && _t != null) PaintTabs(_for, _t);
            _trash?.Place(_for.m_playerGrid);
            _trash?.TickDelete();
            var inv = p.GetInventory(); int used = 0, locked = 0, slots = 0;
            Vector2 origin = default; bool azu = !SpecialRows.On;
            if (azu) { AzuPanels(_for); foreach (Transform c in _for.m_playerGrid.m_gridRoot) { var e0 = c.GetComponent<InventoryElement>(); if (e0 != null && e0.Position.x == 0 && e0.Position.y == 0) { origin = ((RectTransform)c).anchoredPosition; break; } } }
            foreach (Transform c in _for.m_playerGrid.m_gridRoot)
            {
                var el = c.GetComponent<InventoryElement>(); var lk = c.Find("jatify-lock")?.GetComponent<Image>();
                if (el != null && azu && el.Position.y >= SpecialRows.Start && lk == null) { AzuCell(c, el, inv, origin); continue; }   // cells it added later (more slots), never styled: still ours to hide
                if (el == null || lk == null) continue;
                bool isLocked = InvActions.Locked(el.Position.x, el.Position.y), ownLock = !azu || InvActions.Locks.Contains(el.Position.y * 128 + el.Position.x);   // AzuEPI's favourites: its border shows them, not our padlock
                if (azu) { var fb = el.m_queued != null && el.m_queued.transform.childCount > 0 ? el.m_queued.transform.Find("AzuEPIFavoritingBorder")?.GetComponent<Image>() : null; if (fb != null && fb.sprite != Kit.Ring(10)) { fb.sprite = Kit.Ring(10); fb.type = Image.Type.Sliced; } }   // its square border: our rounded ring, its colours
                isLocked &= ownLock;
                if (lk.enabled != isLocked) { lk.enabled = isLocked; var dm = c.Find("jatify-dim")?.GetComponent<Image>(); if (dm) dm.enabled = isLocked; }
                var st = c.Find("jatify-star")?.GetComponent<Image>();
                if (st) { bool q = el.m_quality != null && el.m_quality.isActiveAndEnabled && el.m_quality.text.Length > 0 && el.m_quality.text != "1"; if (st.enabled != q) st.enabled = q; }
                if (azu && el.Position.y >= SpecialRows.Start) AzuCell(c, el, inv, origin);
                if (el.Position.y >= 1 && el.Position.y < SpecialRows.Start) { slots++; if (isLocked) locked++; if (inv.GetItemAt(el.Position.x, el.Position.y) != null) used++; }
            }
            if (azu)
            {   // its slot cells, wherever it parented them: placed (in screen space) where our layout puts that cell
                RectTransform cell00 = null; foreach (Transform c in _for.m_playerGrid.m_gridRoot) { var e0 = c.GetComponent<InventoryElement>(); if (e0 != null && e0.Position.x == 0 && e0.Position.y == 0) { cell00 = (RectTransform)c; break; } }
                if (cell00 != null) foreach (var e in _azuSlots) if (e != null) AzuSlotCell(e, inv, cell00, (RectTransform)_for.m_playerGrid.m_gridRoot);
            }
            var counts = $"{slots} SLOTS · USED {used} · LOCKED {locked}";
            if (_invLabel != null && counts != _countsShown) { _countsShown = counts; _invLabel.text = counts; }
            LayBelts();   // the row count can change (settings, Valheim's own rows)
            int w = Mathf.RoundToInt(p.GetInventory().GetTotalWeight()), m = Mathf.RoundToInt(p.GetMaxCarryWeight());
            if (_weight != null && (w != _wShown || m != _wMaxShown))
            {
                _wShown = w; _wMaxShown = m; _weight.text = w + "/" + m;
                float k = m > 0 ? Mathf.Clamp01((float)w / m) : 0f;
                _weightFill.rectTransform.anchorMax = new Vector2(k, 1f);
                _weightFill.color = Theme.C(k > 1f - 0.001f ? _t.health : k > 0.85f ? _t.ember : _t.stamina);
            }
            _stats?.Place(!BeltColumns && _eq != null && !_eq.OnSkills, StatsTop - 32f + Equipment.ExtraRowsBelow(_azuExtras.Count));   // under other mods' slots (AzuEPI)
            _stats?.Tick(p, _eq != null ? _eq.Block : 0f);
        }

        // Design coordinates (1080p, top-left): inventory grid area at (474, 88) = panel left 160 + 14
        // padding + 300 equipment column, top 32 + 56 header; crafting at (1044, 204).
        private static readonly Vector3[] _c = new Vector3[4];
        internal static void Layout()
        {
            if (_for == null || !InventoryGui.IsVisible() || Root.Canvas == null) return;
            PlaceGrid();
            PlaceFrame();
            bool chest = _for.m_container != null && _for.m_container.gameObject.activeInHierarchy;
            Place(_for.m_crafting, 1044f, 204f);
            ChestPanel.Layout();
            if (_craftFade != null) { _craftFade.alpha = chest ? 0f : 1f; _craftFade.blocksRaycasts = !chest; _craftFade.interactable = !chest; }
            Place(_for.m_info, 1080f, 39f); // inside the design's 670 x 140 character panel at (1030, 32); vanilla's name sits above its box
        }

        // the first action-bar cell's top-left to 445, 123 (design)
        private static void PlaceGrid()
        {
            foreach (Transform c in _for.m_playerGrid.m_gridRoot)
            {
                var el = c.GetComponent<InventoryElement>();
                if (el == null || el.Position.x != 0 || el.Position.y != 0) continue;
                var canvas = (RectTransform)Root.Canvas.transform;
                var target = canvas.TransformPoint(new Vector3(445f - canvas.rect.width / 2f, canvas.rect.height / 2f - 123f, 0f));
                ((RectTransform)c).GetWorldCorners(_c);
                _for.m_player.position += target - _c[1];
                return;
            }
        }

        // our frame: top-left at 160, 32, sized and scaled in design pixels
        private static void PlaceFrame()
        {
            if (_frame == null) return;
            var canvas = (RectTransform)Root.Canvas.transform;
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f); _frame.pivot = new Vector2(0f, 1f);
            _frame.sizeDelta = new Vector2(850f, FrameH);
            float k = canvas.lossyScale.x / _frame.parent.lossyScale.x;
            if (Mathf.Abs(_frame.localScale.x - k) > 0.0001f) _frame.localScale = new Vector3(k, k, 1f);
            _frame.position = canvas.TransformPoint(new Vector3(160f - canvas.rect.width / 2f, canvas.rect.height / 2f - 32f, 0f));
        }

        private static void Place(RectTransform rt, float x, float y)
        {
            if (rt == null) return;
            var canvas = (RectTransform)Root.Canvas.transform;
            // target: the design point in our canvas -> screen/world space
            var target = canvas.TransformPoint(new Vector3(x - canvas.rect.width / 2f, canvas.rect.height / 2f - y, 0f));
            rt.GetWorldCorners(_c); // [1] = top-left
            rt.position += target - _c[1];
        }

        internal static void Restore()
        {
            var g = _for;
            if (g != null)
            {
                var grid = g.m_playerGrid;
                if (grid != null) { grid.m_elementSpace = _spaceWas; GridW(grid) = 0; } // Valheim rebuilds vanilla slots from its prefab
                var cg = ContainerGrid(g); if (cg != null && _chestSpaceWas > 0f) { cg.m_elementSpace = _chestSpaceWas; GridW(cg) = 0; }
                if (g.m_player != null) g.m_player.anchoredPosition = _posWas;
                if (g.m_crafting != null) g.m_crafting.anchoredPosition = _craftWas;
                if (g.m_info != null) g.m_info.anchoredPosition = _infoWas;
            }
            foreach (var gr in _off) if (gr != null) gr.enabled = true;
            _l.Undo();
            foreach (var go in _ours) if (go != null) Object.Destroy(go);
            ChestPanel.Restore();
            _eq?.Destroy(); _eq = null; _tabs.Clear(); _tabOn = null; _badge = null; _badgeShown = -1;
            _trash?.Destroy(); _trash = null; _stats?.Destroy(); _stats = null;
            _off.Clear(); _ours.Clear(); _frame = null; _beltLine = null; _fold = null; _foldText = null; _beltLabels.Clear(); _for = null; _armor = _weight = null; _weightFill = null;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGridPatch
    {
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private static void Postfix(InventoryGrid __instance)
        {
            InventoryStyle.StyleGrid(__instance);
            // Food / Tools / Meads cells: their keys as keycaps, like the action bars (user)
            var g = InventoryGui.instance; var t = Plugin.Instance?.ThemeData;
            if (g != null && __instance == g.m_playerGrid && SpecialRows.Keys != null && t != null)
                foreach (var e in Elements(__instance))
                {
                    int r = e.Position.y - SpecialRows.Start, x = e.Position.x;
                    if (SpecialRows.On && r >= 0 && r < SpecialRows.Count && x < SpecialRows.Width) HudView.CapOn((RectTransform)e.transform, HudView.Short(SpecialRows.Keys[r, x].Value), t);
                }
            // design: just the count ("50"), not vanilla's "38/50"
            foreach (var e in Elements(__instance))
            {
                var tx = e.m_amount; if (tx == null || !tx.enabled) continue;
                var s = tx.text; int cut = s.IndexOf('/'); if (cut > 0) tx.text = s.Substring(0, cut);
            }
        }
    }
}
