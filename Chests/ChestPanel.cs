using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The open chest (design "New: chest"): it takes the right-hand column (character + crafting fade out). Header: the
    // chest's icon tile, its name in Norse and "16 of 24 slots · 9 stacks merge"; icon buttons (take all, top up, put all,
    // stack similar | sort, rename when the container can be renamed, close), each with a card on hover saying what it does
    // and its key. A search that dims what doesn't match. Valheim's own grid underneath, its columns and rows the
    // container's; the panel is sized to it. Foot: the weight (a bar of what you could still carry) and Used for crafting.
    // Moving items: whole stacks, Valheim's own AddItem, the source lets go
    // only when the target took it. Chest slot locks (Alt + click) live on the chest (its ZDO), so they
    // are the same for everyone; locked slots are skipped by Take and Sort.
    internal static class ChestPanel
    {
        internal const string LockKey = "jatify.locks", NoCraftKey = "jatify.nocraft";
        private const float MinW = 560f, Left = 1030f, Top = 32f, Pad = 14f, Btn = 32f, Search = 32f, GridTop = 12f + 34f + 10f + Search + 10f, Foot = 12f + 22f + 12f, Pitch = 67f;
        private static readonly AccessTools.FieldRef<InventoryGui, Container> Current = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> Grid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static readonly AccessTools.FieldRef<Container, ZNetView> NView = AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly System.Action<Inventory> Changed = inv => AccessTools.Method(typeof(Inventory), "Changed").Invoke(inv, new object[] { false, false });
        internal static readonly string[] Sorts = { "Name", "Weight", "Category" };

        private static InventoryGui _g; private static ThemeData _t;
        private static RectTransform _frame, _tools, _rename, _searchBox, _onlyTile, _wbar, _sw, _tip;
        private static Image _icon, _wFill, _craftSw, _craftLine, _craftKnob; private static TextMeshProUGUI _name, _sub, _wText, _craftText, _tipTitle, _tipText;
        private static readonly List<(RectTransform box, TextMeshProUGUI t)> _caps = new List<(RectTransform, TextMeshProUGUI)>();
        private static TMP_InputField _search; private static string _query = "", _found;
        private static CanvasGroup _infoFade;
        private static Container _shown; private static int _used = -1, _total, _stacks, _wKg = -1, _wCap; private static bool _craft, _canRename;
        internal static int Sort = 0; private static float _lastSort = -9f;
        private static readonly HashSet<int> _locks = new HashSet<int>(); private static string _locksRaw;

        internal static Container Open => _g != null && _g.m_container != null && _g.m_container.gameObject.activeInHierarchy ? Current(_g) : null;

        // ---- build ------------------------------------------------------------------------------
        internal static void Build(InventoryGui g, ThemeData t, Ledger l)
        {
            _g = g; _t = t; var c = g.m_container; if (c == null) return;
            foreach (var n in new[] { "Bkg", "Darken", "Weight/bkg", "Weight/weight_icon", "ContainerScroll" }) l.Off(c.Find(n)?.GetComponent<Graphic>());
            // Valheim's name, weight and two buttons step aside: ours do their jobs
            foreach (var rt in new[] { (RectTransform)g.m_containerName?.transform, c.Find("Weight") as RectTransform, (RectTransform)g.m_stackAllButton?.transform, (RectTransform)g.m_takeAllButton?.transform })
                l.Rect(rt, r => r.localScale = Vector3.zero);
            // the grid's own scroll view (mask + scrollbar): off while the whole grid fits on screen (Layout), back on unload
            var gr = Grid(g); var sr = gr?.GetComponent<ScrollRect>(); var mask = gr?.GetComponent<RectMask2D>();
            l.Do(() => { }, () => { if (sr) sr.enabled = true; if (mask) mask.enabled = true; });
            l.Rect(c.Find("ContainerScroll") as RectTransform, r => r.localScale = Vector3.zero);
            if (g.m_info != null) _infoFade = g.m_info.GetComponent<CanvasGroup>() ?? l.Added(g.m_info.gameObject.AddComponent<CanvasGroup>());

            _frame = Kit.Node(c, "jatify-chest"); l.Own(_frame); _frame.SetAsFirstSibling();
            var (line, pfill) = Kit.Box(_frame, "Panel", Theme.C(t.glass), Theme.C(t.line2), 14); line.rectTransform.Fill(); line.raycastTarget = true;
            Knots.Corners(line, pfill, 14, Theme.C(t.accent));   // design: the knotted corners of the big windows
            // header: icon tile, name, slots line
            var (tile, _) = Kit.Box(_frame, "IconTile", new Color(0.173f, 0.173f, 0.196f, 0.85f), new Color(0, 0, 0, 0), 8); tile.rectTransform.At(Pad, 12, 34, 34);
            _icon = Kit.Img(tile.rectTransform, "I", null, Color.white, false); _icon.rectTransform.At(4, 4, 26, 26); _icon.preserveAspect = true;
            _name = Kit.Text(_frame, "Name", "", Fonts.Norse, 20f, Color.white); _name.characterSpacing = 5f; _name.overflowMode = TextOverflowModes.Ellipsis; _name.alignment = TextAlignmentOptions.BottomLeft;
            _sub = Kit.Text(_frame, "Sub", "", Fonts.Mono, 11.5f, Theme.C(t.mute)); _sub.overflowMode = TextOverflowModes.Ellipsis;
            // tools: icon buttons right-aligned, a card under each on hover
            _tools = Kit.Node(_frame, "Tools");
            float x = 0f;
            x = Icon("take-all", "Take all", "Takes every stack out of the chest into your bag (locked slots stay).", () => Keys(HotKeys.TakeAll, "Ctrl E"), () => Say(Take(false)), x);
            x = Icon("from-chest", "Top up", "Fills up the stacks you already carry from this chest.", () => Keys(HotKeys.TakeSimilar, null), () => Say(Take(true)), x);
            x = Icon("put-all", "Put all", "Puts everything in your bag into the chest (not the action bar, worn or locked slots).", () => Keys(HotKeys.PutAll, null), () => Say(Put(false)), x);
            x = Icon("stack", "Stack similar", "Moves everything you carry that this chest already has into it.", () => Keys(HotKeys.PutSimilar, "Ctrl Q"), () => Say(Put(true)), x);
            var sep = Kit.Img(_tools, "Sep", null, new Color(1f, 1f, 1f, 0.12f)); sep.rectTransform.At(x + 4f, 5, 1, 22); x += 9f + 4f;
            x = Icon("sort-ud", "Sort", null, () => Keys(HotKeys.ChestSort, null), () =>
            {   // click sorts; click again within 2 s switches the order and sorts again (as the inventory's Sort)
                if (Time.unscaledTime - _lastSort < 2f) Sort = (Sort + 1) % Sorts.Length;
                _lastSort = Time.unscaledTime; Say(DoSort()); _tipShow?.Invoke();   // the card names the order now in use
            }, x);
            float rx = x; x = Icon("rename", "Rename", "Gives this container a name of your own.", () => "", Rename, x); _rename = (RectTransform)_tools.GetChild(_tools.childCount - 1);
            _renameW = x - rx;
            x = Icon("x", "Close", "Closes the chest and your inventory.", () => "Esc", () => InventoryGui.instance?.Hide(), x);
            _toolsW = x - 4f; _close = (RectTransform)_tools.GetChild(_tools.childCount - 1); _closeX = rx + _renameW;
            // search: dims the slots whose item doesn't match
            var (box, _) = Kit.Box(_frame, "jatify-search", new Color(0.173f, 0.173f, 0.196f, 0.6f), new Color(1f, 1f, 1f, 0.08f), 8); _searchBox = box.rectTransform;
            var mag = Kit.IconImg(box.rectTransform, "search", Theme.C(t.mute), 15f); mag.rectTransform.At(10, 8.5f, 15, 15);
            var area = Kit.Node(box.rectTransform, "Area").Fill(); area.offsetMin = new Vector2(32, 3); area.offsetMax = new Vector2(-10, -3); area.gameObject.AddComponent<RectMask2D>();
            var tx = Kit.Text(area, "Text", "", Fonts.Medium, 12.5f, Color.white); tx.rectTransform.Fill(); tx.alignment = TextAlignmentOptions.MidlineLeft;
            var ph = Kit.Text(area, "Placeholder", "Search this chest…", Fonts.Medium, 12.5f, Theme.C(t.mute)); ph.rectTransform.Fill(); ph.alignment = TextAlignmentOptions.MidlineLeft;
            box.raycastTarget = true;   // the field only gets clicks through a raycast graphic
            _search = box.gameObject.AddComponent<TMP_InputField>(); _search.textViewport = area; _search.textComponent = tx; _search.placeholder = ph; _search.fontAsset = Fonts.Medium; _search.pointSize = 12.5f;
            Kit.Caret(_search); _search.onValueChanged.AddListener(q => { _query = (q ?? "").Trim(); _found = null; });
            _onlyTile = ChestAssign.BuildTile(_frame, t);   // "Only <item>": drop an item on it to assign the chest
            // foot: weight (what's in it, a bar of what you could still carry) and the crafting switch
            _wbar = Kit.Node(_frame, "Weight");
            var wl = Kit.Text(_wbar, "L", "Weight", Fonts.Medium, 12f, Theme.C(t.dim)); wl.alignment = TextAlignmentOptions.MidlineLeft; wl.rectTransform.At(0, 0, 46, 22);
            var track = Kit.Panel(_wbar, "Bar", new Color(1f, 1f, 1f, 0.1f), 2); track.rectTransform.At(52, 8.5f, 90, 5);
            _wFill = Kit.Panel(track.rectTransform, "V", Theme.C(t.stamina), 2); var fr = _wFill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            _wText = Kit.Text(_wbar, "V", "", Fonts.Mono, 12f, Theme.C(t.dim)); _wText.alignment = TextAlignmentOptions.MidlineLeft; _wText.rectTransform.At(150, 0, 120, 22);
            Hover(track, "Weight", "The chest's load; the bar is how much of what you can still carry it is.", () => "");
            _sw = Kit.Node(_frame, "Crafting");
            var (sw, swf) = Kit.Box(_sw, "Switch", Theme.C(t.raised2), Theme.C(t.line2), 8); sw.rectTransform.At(0, 3.5f, 26, 15); _craftSw = swf; _craftLine = sw;
            _craftKnob = Kit.Img(sw.rectTransform, "Knob", Kit.Circle(), Theme.C(t.dim), false); _craftKnob.rectTransform.At(2, 2, 11, 11);
            _craftText = Kit.Text(_sw, "T", "", Fonts.Medium, 12f, Theme.C(t.dim)); _craftText.alignment = TextAlignmentOptions.MidlineLeft; _craftText.rectTransform.At(33, 0, 150, 22);
            var hit = Kit.Img(_sw, "Hit", null, new Color(0, 0, 0, 0)); hit.rectTransform.Fill(); hit.raycastTarget = true; hit.gameObject.AddComponent<Button>().onClick.AddListener(ToggleCraft);
            Hover(hit, null, null, () => "");
            // the hover card (design .tipc): over the grid, so it lives on the container's last sibling, not in our frame
            var (card, _) = Kit.Box(c, "jatify-chesttip", new Color(0.047f, 0.047f, 0.055f, 0.97f), new Color(1f, 1f, 1f, 0.16f), 9); _tip = card.rectTransform; l.Own(_tip);
            _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f); _tip.pivot = new Vector2(1f, 1f);
            _tipTitle = Kit.Text(_tip, "T", "", Fonts.Bold, 13f, Color.white);
            _tipText = Kit.Text(_tip, "D", "", Fonts.Medium, 12f, Theme.C(t.dim)); _tipText.textWrappingMode = TextWrappingModes.Normal; _tipText.lineSpacing = 4f;
            _caps.Clear();
            for (int i = 0; i < 3; i++)
            {   // design kbd: mono 10.5 on a dark key with a light edge
                var (kb, _) = Kit.Box(_tip, "Key", new Color(0.173f, 0.173f, 0.196f, 0.9f), new Color(1f, 1f, 1f, 0.18f), 5);
                var kt = Kit.Text(kb.rectTransform, "K", "", Fonts.Mono, 10.5f, Color.white); kt.alignment = TextAlignmentOptions.Center; kt.rectTransform.Fill();
                _caps.Add((kb.rectTransform, kt));
            }
            _tip.gameObject.SetActive(false);
            _shown = null; _used = -1; _locksRaw = null; _query = ""; _found = null; _tipFor = null;
        }

        private static float _toolsW, _renameW, _craftW = 110f, _closeX; private static RectTransform _close;

        // a 32 px icon button; keys: what to show on its card ("" none)
        private static float Icon(string icon, string title, string what, System.Func<string> keys, System.Action on, float x)
        {
            var (b, f) = Kit.Box(_tools, title, new Color(0.173f, 0.173f, 0.196f, 0.85f), new Color(1f, 1f, 1f, 0.1f), 8); b.rectTransform.At(x, 0, Btn, Btn);
            var ic = Kit.IconImg(b.rectTransform, icon, new Color(0.91f, 0.91f, 0.925f), 17f); ic.rectTransform.At(7.5f, 7.5f, 17, 17);
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f; btn.onClick.AddListener(() => on());
            var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.35f); btn.colors = cb;
            Hover(b, what == null ? null : title, what, keys, b, ic);   // Sort: its card names the order in use
            return x + Btn + 4f;
        }

        // the card under a control while the pointer is on it; the button lights green (design .ib.on)
        private static Graphic _tipFor; private static System.Action _tipShow;
        private static void Hover(Graphic g, string title, string what, System.Func<string> keys, Image ring = null, Image icon = null)
        {
            var h = g.gameObject.AddComponent<Campfire.CampfireUi.Hover>();
            h.Enter = () =>
            {
                _tipFor = g;
                if (ring != null) ring.color = Theme.C(_t.accent); if (icon != null) icon.color = Theme.C(_t.accent);
                _tipShow = () => ShowTip(g.rectTransform, title ?? (g.transform.parent == _sw ? "Used for crafting" : "Sort · " + Sorts[Sort]), what ?? (g.transform.parent == _sw ? CraftWhat() : SortWhat()), keys());
                _tipShow();
            };
            h.Exit = () =>
            {
                if (ring != null) ring.color = new Color(1f, 1f, 1f, 0.1f); if (icon != null) icon.color = new Color(0.91f, 0.91f, 0.925f);
                if (_tipFor == g) { _tipFor = null; _tipShow = null; if (_tip != null) _tip.gameObject.SetActive(false); }
            };
        }
        private static string CraftWhat() => Open != null && !NoCraft(Open) ? "Nearby stations can take materials from it. Click to keep them out." : "Stations never take from it. Click to let them.";
        private static string SortWhat() => $"Merges stacks, then orders the chest by {Sorts[Sort].ToLowerInvariant()} (locked slots stay). Click again within 2 s for {Sorts[(Sort + 1) % Sorts.Length].ToLowerInvariant()}.";

        private static void ShowTip(RectTransform at, string title, string what, string keys)
        {
            if (_tip == null) return;
            const float w = 210f, pad = 10f;
            _tip.gameObject.SetActive(true); _tip.SetAsLastSibling(); _tip.localScale = _frame.localScale;
            _tipTitle.text = title; _tipTitle.rectTransform.At(pad, 9, w - 2 * pad, 17);
            _tipText.text = what; float h = _tipText.GetPreferredValues(what, w - 2 * pad, 0f).y; _tipText.rectTransform.At(pad, 30, w - 2 * pad, h);
            float y = 30f + h + 4f;
            var ks = string.IsNullOrEmpty(keys) ? new string[0] : keys.Split(' '); float kx = pad;
            for (int i = 0; i < _caps.Count; i++)
            {
                bool on = i < ks.Length; _caps[i].box.gameObject.SetActive(on); if (!on) continue;
                _caps[i].t.text = ks[i]; float kw = Kit.W(_caps[i].t) + 10f; _caps[i].box.At(kx, y + 2f, kw, 17); kx += kw + 4f;
            }
            if (ks.Length > 0) y += 22f;
            _tip.sizeDelta = new Vector2(w, y + 8f);
            // under the control, its right edge 6 px past the control's (design: top 38, right -6)
            var r = at.rect; _tip.position = at.TransformPoint(new Vector3(r.xMax + 6f, r.yMin - 6f, 0f));
        }

        // a key cap per key: the action's own keys from the settings, else ours
        private static string Keys(BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> k, string fixedKey)
        {
            var s = k != null ? HudView.Short(k.Value) : ""; if (s.Length == 0) s = fixedKey; if (string.IsNullOrEmpty(s)) return "";
            return s;
        }

        // containers that can be named (another mod's TextReceiver on them): Valheim's own text box
        private static void Rename()
        {
            var c = Open; var r = c != null ? c.GetComponentInParent<TextReceiver>() : null;
            if (r != null && TextInput.instance != null) TextInput.instance.RequestText(r, "$hud_rename", 32);
        }

        // ---- every frame (LateUpdate, after Valheim laid the grid out) -----------------------------
        internal static void Layout()
        {
            var c = Open; if (_frame == null) return;
            if (_infoFade != null) { bool hide = c != null; _infoFade.alpha = hide ? 0f : 1f; _infoFade.blocksRaycasts = !hide; _infoFade.interactable = !hide; }
            if (c == null || Root.Canvas == null) return;
            var inv = c.GetInventory(); int cols = inv.GetWidth(), rows = inv.GetHeight();
            // tall, narrow containers (wardrobe 5 x 10) are shown wider; only the cells move, each keeps its slot
            int shownCols = Wide(cols, rows), shownRows = (cols * rows + shownCols - 1) / shownCols;
            Reflow(Grid(_g), shownCols);
            cols = shownCols; rows = shownRows;
            var canvas = (RectTransform)Root.Canvas.transform;
            float gridW = cols * Pitch - 7f, gridH = rows * Pitch - 7f, pw = Mathf.Max(MinW, gridW + 2f * Pad);   // the panel is the grid's width (at least the header's)
            float h = GridTop + gridH + Foot, left = Mathf.Min(Left, canvas.rect.width - 16f - pw);
            // ponytail: a container taller than the screen (modded) keeps vanilla's scroll view and the panel stops at the screen edge
            bool fits = Top + h <= canvas.rect.height - 16f; if (!fits) h = canvas.rect.height - 16f - Top;
            var gsr = Grid(_g).GetComponent<ScrollRect>(); var gmask = Grid(_g).GetComponent<RectMask2D>();
            if (gsr && gsr.enabled == fits) { if (fits) gsr.verticalNormalizedPosition = 1f; gsr.enabled = !fits; }
            if (gmask && gmask.enabled == fits) gmask.enabled = !fits;
            // the vanilla grid: first cell at its design spot, centred under the header
            var grid = Grid(_g);
            foreach (Transform cell in grid.m_gridRoot)
            {
                var el = cell.GetComponent<InventoryElement>(); if (el == null || el.Position.x != 0 || el.Position.y != 0) continue;
                var target = canvas.TransformPoint(new Vector3(left + (pw - gridW) / 2f - canvas.rect.width / 2f, canvas.rect.height / 2f - (Top + GridTop), 0f));
                ((RectTransform)cell).GetWorldCorners(_c);
                _g.m_container.position += target - _c[1];
                break;
            }
            // our frame: design pixels, top-left at 1030, 32 (further left when a wide chest would leave the screen)
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f); _frame.pivot = new Vector2(0f, 1f); _frame.sizeDelta = new Vector2(pw, h);
            float k = canvas.lossyScale.x / _frame.parent.lossyScale.x;
            if (Mathf.Abs(_frame.localScale.x - k) > 0.0001f) _frame.localScale = new Vector3(k, k, 1f);
            _frame.position = canvas.TransformPoint(new Vector3(left - canvas.rect.width / 2f, canvas.rect.height / 2f - Top, 0f));
            float tw = _toolsW - (_canRename ? 0f : _renameW);
            _tools.At(pw - Pad - tw, 13, tw, Btn);
            _name.rectTransform.At(58, 10, pw - 58 - tw - Pad - 12, 23); _sub.rectTransform.At(58, 33, pw - 58 - tw - Pad - 12, 16);
            float only = 190f; _searchBox.At(Pad, 56, pw - 2 * Pad - only - 8, Search); _onlyTile.At(pw - Pad - only, 57, only, 30);
            _wbar.At(Pad, h - 12 - 22, 280, 22); _sw.At(pw - Pad - 33 - _craftW - 2, h - 12 - 22, 33 + _craftW + 2, 22);
        }
        private static readonly Vector3[] _c = new Vector3[4];

        private const int MaxCols = 10;   // the panel widens for it (Layout)
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        // columns to show: vanilla's unless the container is taller than wide; then the most, up to MaxCols, that fill
        // every row (wardrobe 5 x 10 -> 10 x 5); vanilla's when none does (no short last row, user)
        internal static int Wide(int cols, int rows)
        {
            if (rows <= cols || cols >= MaxCols) return cols;
            int n = cols * rows;
            for (int k = MaxCols; k > cols; k--) if (n % k == 0) return k;
            return cols;
        }

        // vanilla places the cells once, row by row in slot order (index = y * width + x); we place them again in shownCols columns
        private static void Reflow(InventoryGrid grid, int shownCols)
        {
            var els = Elements(grid); if (els.Count == 0) return;
            var origin = ((RectTransform)els[0].transform).anchoredPosition; float sp = grid.m_elementSpace;
            for (int i = 0; i < els.Count; i++)
            {
                var rt = (RectTransform)els[i].transform; var want = origin + new Vector2(i % shownCols * sp, -(i / shownCols) * sp);
                if (rt.anchoredPosition != want) rt.anchoredPosition = want;
            }
        }

        internal static void Tick()
        {
            var c = Open; if (c == null || _frame == null) { _shown = null; return; }
            var inv = c.GetInventory();
            if (c != _shown)
            {
                _shown = c; _used = -1; _locksRaw = null; _found = null;
                if (_search != null && _search.text.Length > 0) _search.text = "";
                var piece = c.GetComponentInParent<Piece>(); _icon.sprite = piece != null ? piece.m_icon : null; _icon.enabled = _icon.sprite != null;
                _canRename = c.GetComponentInParent<TextReceiver>() != null; _rename.gameObject.SetActive(_canRename);
                _close.anchoredPosition = new Vector2(_canRename ? _closeX : _closeX - _renameW, _close.anchoredPosition.y);   // Close takes Rename's place when there is none
            }
            ReadLocks(c); ChestAssign.TickTile(c, _t);
            // numbers: refreshed when the contents change (counted without allocating)
            var items = inv.GetAllItems(); int stacks = 0; foreach (var it in items) stacks += it.m_stack;
            var p = Player.m_localPlayer; int kg = Mathf.RoundToInt(inv.GetTotalWeight()), cap = p != null ? Mathf.RoundToInt(p.GetMaxCarryWeight() - p.GetInventory().GetTotalWeight()) : 0;
            bool craft = !NoCraft(c);
            if (items.Count != _used || stacks != _stacks || kg != _wKg || cap != _wCap || craft != _craft)
            {
                _used = items.Count; _stacks = stacks; _total = inv.GetWidth() * inv.GetHeight(); _wKg = kg; _wCap = cap; _craft = craft; _found = null;
                var r = c.GetComponentInParent<TextReceiver>(); var own = r != null ? r.GetText() : null;
                _name.text = !string.IsNullOrEmpty(own) ? Kit.Plain(own) : Localization.instance.Localize(c.m_name);
                int merge = Mergeable(items);
                _sub.text = $"{_used} of {_total} slots" + (merge > 0 ? $" · {merge} stack{(merge == 1 ? "" : "s")} merge" : "");
                float k = kg / Mathf.Max(1f, cap); _wFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1f);
                _wFill.color = Theme.C(k > 1f ? _t.health : k > 0.85f ? _t.ember : _t.stamina);
                _wText.text = $"{kg} kg";
                _craftText.text = "Used for crafting"; _craftW = Kit.W(_craftText); _craftText.color = Theme.C(craft ? _t.dim : _t.mute);
                _craftSw.color = craft ? Theme.C(_t.accent) : Theme.C(_t.raised2); _craftLine.color = craft ? Theme.C(_t.accent) : Theme.C(_t.line2);
                _craftKnob.color = craft ? new Color(0f, 0.4f, 0.13f) : Theme.C(_t.dim); _craftKnob.rectTransform.At(craft ? 13 : 2, 2, 11, 11);
            }
            // lock marks and the search on the chest's slots
            bool search = _found != _query; if (search) _found = _query;
            foreach (Transform cell in Grid(_g).m_gridRoot)
            {
                var el = cell.GetComponent<InventoryElement>(); var lk = cell.Find("jatify-lock")?.GetComponent<Image>(); if (el == null || lk == null) continue;
                bool on = IsLocked(el.Position.x, el.Position.y);
                if (lk.enabled != on) { lk.enabled = on; var dm = cell.Find("jatify-dim")?.GetComponent<Image>(); if (dm) dm.enabled = on; }
                if (search) Find(cell, inv.GetItemAt(el.Position.x, el.Position.y));
            }
            // keys (design key bar): Ctrl + Q puts similar, Ctrl + E takes everything
            if (!Typing.Active && (ZInput.GetKey(KeyCode.LeftControl, true) || ZInput.GetKey(KeyCode.RightControl, true)))
            {
                if (ZInput.GetKeyDown(KeyCode.Q, true)) Say(Put(true));
                else if (ZInput.GetKeyDown(KeyCode.E, true)) Say(Take(false));
            }
        }

        internal static void DevSearch(string q) { if (_search != null) _search.text = q ?? ""; }

        // stacks that would go if equal ones were merged (Sort merges them)
        private static int Mergeable(List<ItemDrop.ItemData> items)
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i]; if (a.m_shared.m_maxStackSize <= 1) continue;
                bool first = true; int sum = 0, count = 0;
                for (int j = 0; j < items.Count; j++) { var b = items[j]; if (b.m_shared.m_name != a.m_shared.m_name || b.m_quality != a.m_quality || b.m_worldLevel != a.m_worldLevel) continue; if (j < i) { first = false; break; } sum += b.m_stack; count++; }
                if (first) n += count - Mathf.CeilToInt(sum / (float)a.m_shared.m_maxStackSize);
            }
            return n;
        }

        // the search on one slot: a dark veil when its item doesn't match, a green ring when it does (nothing while empty)
        private static void Find(Transform cell, ItemDrop.ItemData it)
        {
            bool q = _query.Length > 0, hit = q && it != null && Localization.instance.Localize(it.m_shared.m_name).IndexOf(_query, System.StringComparison.OrdinalIgnoreCase) >= 0;
            var miss = cell.Find("jatify-miss")?.GetComponent<Image>(); var ring = cell.Find("jatify-hit")?.GetComponent<Image>();
            if (miss == null && q) { miss = Kit.Panel(cell, "jatify-miss", new Color(0f, 0f, 0f, 0.62f), 10); miss.rectTransform.Fill(); ring = Kit.Img(cell, "jatify-hit", Kit.Ring(10), Theme.C(_t.accent)); ring.rectTransform.Fill(-1f); }
            if (miss != null) { miss.enabled = q && !hit; miss.transform.SetAsLastSibling(); }
            if (ring != null) { ring.enabled = hit; ring.transform.SetAsLastSibling(); }
        }

        private static void Say(string s) { if (!string.IsNullOrEmpty(s)) ActionToast.Show(s, !s.StartsWith("Nothing") && !s.Contains("full") && !s.StartsWith("No ")); _used = -1; }

        // ---- chest slot locks and the crafting switch (on the chest's ZDO) ------------------------
        private static ZDO Zdo(Container c) { var nv = c != null ? NView(c) : null; return nv != null && nv.IsValid() ? nv.GetZDO() : null; }

        private static void ReadLocks(Container c)
        {
            var raw = Zdo(c)?.GetString(LockKey, "") ?? "";
            if (raw == _locksRaw) return;
            _locksRaw = raw; _locks.Clear();
            foreach (var part in raw.Split(',')) { var xy = part.Split('.'); if (xy.Length == 2 && int.TryParse(xy[0], out var x) && int.TryParse(xy[1], out var y)) _locks.Add(y * 128 + x); }
        }

        internal static bool IsLocked(int x, int y) => _locks.Contains(y * 128 + x);

        // a lock on any chest (not just the open one), read from its ZDO
        internal static bool IsLockedIn(Container c, int x, int y)
        {
            var raw = Zdo(c)?.GetString(LockKey, "") ?? ""; if (raw.Length == 0) return false;
            return ("," + raw + ",").Contains("," + x + "." + y + ",");
        }

        internal static void ToggleLock(int x, int y)
        {
            var c = Open; var z = Zdo(c); if (z == null || !NView(c).IsOwner()) return;
            ReadLocks(c); int k = y * 128 + x; bool on = !_locks.Remove(k); if (on) _locks.Add(k);
            z.Set(LockKey, string.Join(",", _locks.Select(v => (v % 128) + "." + (v / 128))));
            Say(on ? "Slot locked: sorting and Take leave it alone" : "Slot unlocked");
        }

        internal static bool NoCraft(Container c) => Zdo(c)?.GetString(NoCraftKey, "") == "1";

        private static void ToggleCraft()
        {
            var c = Open; var z = Zdo(c); if (z == null || !NView(c).IsOwner()) return;
            z.Set(NoCraftKey, NoCraft(c) ? "" : "1"); _used = -1; _tipShow?.Invoke();
        }

        // ---- moving ---------------------------------------------------------------------------
        // player cells that Put may take from: bag rows, not the hotbar, reserved rows, locked cells or worn items
        private static bool Putable(ItemDrop.ItemData it) => it != null && !it.m_equipped && !it.m_shared.m_questItem && it.m_gridPos.y >= 1 && it.m_gridPos.y < SpecialRows.Start && !InvActions.Locked(it.m_gridPos.x, it.m_gridPos.y);

        private static int Move(Inventory from, Inventory to, List<ItemDrop.ItemData> items)
        {
            int n = 0; bool toBag = SpecialRows.IsPlayer(to);
            foreach (var it in items)
            {
                // into your inventory: a free bag cell first (user: never the action bar unless the bag is full);
                // Valheim's AddItem still tops up stacks you carry before it uses the cell
                var cell = toBag ? BagCell(to) : new Vector2i(-1, -1);
                if (cell.x >= 0 ? to.AddItem(it, cell) : to.AddItem(it)) { from.RemoveItem(it); n++; }
            }
            Changed(from); Changed(to);
            return n;
        }

        private static Vector2i BagCell(Inventory bag)
        {
            for (int y = 1; y < SpecialRows.Start; y++)
                for (int x = 0; x < bag.GetWidth(); x++) if (bag.GetItemAt(x, y) == null && !InvActions.Locked(x, y)) return new Vector2i(x, y);
            return new Vector2i(-1, -1);
        }

        internal static string Put(bool similar)
        {
            var c = Open; var p = Player.m_localPlayer; if (c == null || p == null) return null;
            var chest = c.GetInventory(); var bag = p.GetInventory();
            var names = new HashSet<string>(chest.GetAllItems().Select(i => i.m_shared.m_name));
            var assigned = ChestAssign.SharedName(c); if (assigned != null) names.Add(assigned);
            var list = bag.GetAllItems().Where(i => Putable(i) && (!similar || names.Contains(i.m_shared.m_name)) && ChestAssign.Allows(c, i)).ToList();
            if (list.Count == 0) return similar ? "Nothing you carry is in this chest" : "Nothing to put away";
            int n = Move(bag, chest, list);
            return n == 0 ? "The chest is full" : $"Put {n} stack{(n == 1 ? "" : "s")} in the chest" + (n < list.Count ? $" · {list.Count - n} did not fit" : "");
        }

        internal static string Take(bool similar)
        {
            var c = Open; var p = Player.m_localPlayer; if (c == null || p == null) return null;
            var chest = c.GetInventory(); var bag = p.GetInventory();
            var list = chest.GetAllItems().Where(i => !IsLocked(i.m_gridPos.x, i.m_gridPos.y)).ToList();
            if (!similar)
            {
                if (list.Count == 0) return "Nothing to take";
                int n = Move(chest, bag, list);
                return n == 0 ? "Inventory is full" : $"Took {n} stack{(n == 1 ? "" : "s")}" + (n < list.Count ? $" · {list.Count - n} did not fit" : "");
            }
            // Similar: only tops up stacks you already carry
            int moved = 0;
            foreach (var it in list)
            {
                while (it.m_stack > 0)
                {
                    var open = bag.GetAllItems().FirstOrDefault(b => b.m_gridPos.y < SpecialRows.Start && b.m_shared.m_name == it.m_shared.m_name && b.m_quality == it.m_quality && b.m_worldLevel == it.m_worldLevel && b.m_stack < b.m_shared.m_maxStackSize);
                    if (open == null) break;
                    int k = Mathf.Min(open.m_shared.m_maxStackSize - open.m_stack, it.m_stack);
                    open.m_stack += k; moved += k;
                    if (k >= it.m_stack) { chest.RemoveItem(it); break; }
                    it.m_stack -= k;
                }
            }
            Changed(chest); Changed(bag);
            return moved == 0 ? "No stacks of yours to top up" : $"Topped up your stacks with {moved} item{(moved == 1 ? "" : "s")}";
        }

        // merge equal stacks, then order by the chosen key into the unlocked cells (locked ones stay put)
        internal static string DoSort()
        {
            var c = Open; if (c == null) return null;
            var inv = c.GetInventory(); int w = inv.GetWidth(), h = inv.GetHeight();
            var items = inv.GetAllItems().Where(i => !IsLocked(i.m_gridPos.x, i.m_gridPos.y)).ToList();
            foreach (var grp in items.GroupBy(i => (i.m_shared.m_name, i.m_quality, i.m_worldLevel)).Where(g => g.Count() > 1 && g.First().m_shared.m_maxStackSize > 1))
            {
                var stacks = grp.ToList(); int total = stacks.Sum(s => s.m_stack), max = stacks[0].m_shared.m_maxStackSize;
                for (int i = 0; i < stacks.Count; i++) { int k = Mathf.Min(max, total); stacks[i].m_stack = k; total -= k; }
                foreach (var empty in stacks.Where(s => s.m_stack == 0)) { inv.RemoveItem(empty); items.Remove(empty); }   // their items now live in the stacks above
            }
            System.Func<ItemDrop.ItemData, ItemDrop.ItemData, int> cmp = Sort == 0 ? (a, b) => string.Compare(N(a), N(b), System.StringComparison.OrdinalIgnoreCase)
                : Sort == 1 ? (a, b) => { int r = (b.GetWeight()).CompareTo(a.GetWeight()); return r != 0 ? r : string.Compare(N(a), N(b), System.StringComparison.OrdinalIgnoreCase); }
                : (a, b) => { int r = ((int)a.m_shared.m_itemType).CompareTo((int)b.m_shared.m_itemType); return r != 0 ? r : string.Compare(N(a), N(b), System.StringComparison.OrdinalIgnoreCase); };
            items.Sort((a, b) => cmp(a, b));
            var free = new List<Vector2i>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (!IsLocked(x, y)) free.Add(new Vector2i(x, y));
            for (int i = 0; i < items.Count && i < free.Count; i++) items[i].m_gridPos = free[i];
            Changed(inv);
            return $"Sorted by {Sorts[Sort].ToLowerInvariant()}";
        }

        private static string N(ItemDrop.ItemData i) => Localization.instance.Localize(i.m_shared.m_name);

        internal static void Restore() { _frame = null; _g = null; _infoFade = null; _shown = null; _tip = null; _tipFor = null; _search = null; _query = ""; _found = null; _tipShow = null; }
    }
}
