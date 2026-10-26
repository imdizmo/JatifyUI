using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The HUD from the design: vitals and hotbar docked bottom centre (held item name above, guardian
    // power to the right), buffs and food timers left of the minimap, the compass at the top.
    // Positions are the design's, in 1080p pixels. Nothing allocates per frame: every text is
    // rewritten only when its number changes.
    internal sealed class HudView
    {
        private readonly RectTransform _root;
        private readonly ThemeData _t;
        private readonly TextMeshProUGUI _held, _heldQ;
        private readonly Image _heldStar;
        private readonly Bar _hp, _st, _ei;
        private readonly Slot[] _slots = new Slot[8];
        private readonly Slot[] _tools = new Slot[6], _food = new Slot[6], _meads = new Slot[6];
        private readonly RectTransform _power;
        private readonly Image _powerIcon, _powerCd;
        private readonly RectTransform _buffs;
        private readonly List<Buff> _buffPool = new List<Buff>();
        private readonly List<StatusEffect> _effects = new List<StatusEffect>();
        private readonly Compass _compass;
        private readonly RaidBanner _raid;
        private readonly CheatFlags _flags;
        private readonly MountFrame _mount;
        internal bool RaidShown => _raid != null && _raid.Shown;
        private readonly MinimapFrame _minimap;
        private readonly RectTransform _invKeys, _dock;
        private RectTransform _hintsL, _hintsR, _hintsPlace, _side, _chestKeys;
        internal readonly Deeds.AchHud Ach;
        internal RectTransform MapHole => _root.gameObject.activeSelf ? _minimap.Hole : null;
        private string _heldName;

        internal HudView(Transform canvas, ThemeData t)
        {
            _t = t;
            _root = Kit.Node(canvas, "Hud").Fill();

            // dock: 534 wide (8 slots of 60, gaps of 6, padding 6), bottom 26
            var dock = _dock = Kit.Node(_root, "Dock");
            dock.anchorMin = dock.anchorMax = dock.pivot = new Vector2(0.5f, 0f);
            dock.anchoredPosition = new Vector2(0f, 26f); dock.sizeDelta = new Vector2(534f, 168f);
            _held = Kit.Text(dock, "Held", "", Fonts.Bold, 13f, C(t.text));
            _held.alignment = TextAlignmentOptions.Center; _held.richText = true;
            _held.rectTransform.At(0, 0, 534, 18);
            _heldStar = Kit.Img(dock, "HeldStar", Kit.Star(), C(t.stamina), false); _heldStar.rectTransform.At(0, 4, 10, 10);
            _heldQ = Kit.Text(dock, "HeldQuality", "", Fonts.Mono, 11f, C(t.stamina)); _heldQ.rectTransform.At(0, 0, 20, 18); _heldQ.alignment = TextAlignmentOptions.MidlineLeft;
            _hp = new Bar(dock, "HP", 47, 28, 440, 18, C(t.health), t);
            _st = new Bar(dock, "ST", 47, 52, 440, 14, C(t.stamina), t);
            _ei = new Bar(dock, "EITR", 47, 72, 440, 14, C(t.eitr), t);
            var (bar, barFill) = Kit.Box(dock, "Hotbar", C(t.glass), C(t.glassLine), 12); _barPanels[0] = (bar, barFill);
            bar.rectTransform.At(0, 96, 534, 72);
            for (int i = 0; i < 8; i++) { _slots[i] = new Slot(bar.transform, i, 7 + i * 66, 7, t); _slots[i].Bar = true; }
            _mount = new MountFrame(dock, t);   // left of the HP and ST bars, while riding

            // tools bar: 6 small slots in a glass group, 14 px left of the hotbar, bottoms aligned
            var (tb, tbFill) = Kit.Box(dock, "ToolsBar", C(t.glass), C(t.glassLine), 12); _barPanels[1] = (tb, tbFill);
            tb.rectTransform.At(-14 - 318, 96 + 14, 318, 58);
            for (int i = 0; i < 6; i++) { _tools[i] = new Slot(tb.transform, "", 6 + i * 52, 6, 46f, t).Cap(1); _tools[i].Bar = true; }
            // food and meads: stacked labelled columns on the right edge from y 290
            var side = _side = Kit.Node(_root, "Sidebars");
            side.anchorMin = side.anchorMax = side.pivot = new Vector2(1f, 1f); side.anchoredPosition = new Vector2(-28f, -290f); side.sizeDelta = new Vector2(58f, 700f);
            Column(side, "FOOD", 0f, _food, null, t);
            Column(side, "MEADS", 342f, _meads, null, t);   // key labels: KeyLabels, from the settings
            // guardian power: 52 px circle, 18 px right of the hotbar, 8 px up from its bottom
            _power = Kit.Node(dock, "Power").At(534 + 18, 168 - 8 - 52, 52, 52);
            Kit.Img(_power, "Line", Kit.Circle(), C(t.glassLine), false).rectTransform.Fill();
            Kit.Img(_power, "Fill", Kit.Circle(), C(t.glass), false).rectTransform.Fill(1f);
            _powerIcon = Kit.Img(_power, "Icon", null, Color.white, false); _powerIcon.rectTransform.Fill(10f); _powerIcon.preserveAspect = true;
            _powerCd = Kit.Img(_power, "Cooldown", Kit.Circle(), new Color(0f, 0f, 0f, 0.65f), false);
            _powerCd.rectTransform.Fill(1f); _powerCd.type = Image.Type.Filled; _powerCd.fillMethod = Image.FillMethod.Radial360;
            _powerCd.fillOrigin = (int)Image.Origin360.Top; _powerCd.fillClockwise = false;
            // the key as a keycap, like the corner hints, hanging 8 px below the circle (design: .power .key bottom -8px)
            var (cap, _) = Kit.Box(_power, "Key", C(t.raised), C(t.line2), 5);
            cap.rectTransform.At(16, 42, 20, 18);
            var key = Kit.Text(cap.rectTransform, "K", "F", Fonts.Mono, 10.5f, C(t.text)); key.alignment = TextAlignmentOptions.Center; key.rectTransform.Fill();

            // buffs: right 230, top 24, laid out right to left, wrapping at 420 px
            _buffs = Kit.Node(_root, "Buffs");
            _buffs.anchorMin = _buffs.anchorMax = _buffs.pivot = new Vector2(1f, 1f);
            _buffs.anchoredPosition = new Vector2(-230f, -24f); _buffs.sizeDelta = new Vector2(420f, 260f);

            _compass = new Compass(_root, t);
            _raid = new RaidBanner(_root, t);   // under the compass
            _flags = new CheatFlags(_root, t);   // left of the compass
            _minimap = new MinimapFrame(_root, t);
            Ach = new Deeds.AchHud(_root, t);
            _hintsL = Hints(_root, false, t, ("Tab", "Inventory"), ("M", "Map"), ("Y", "Tracker"));
            _hintsR = Hints(_root, true, t, ("LMB", "Attack"), ("RMB", "Block"), ("MMB", "Secondary"), ("Space", "Dodge"));
            _hintsPlace = Hints(_root, true, t, ("LMB", "Place"), ("MMB", "Remove"), ("RMB", "Menu"), ("Wheel", "Rotate"), ("Q / E", "Snap"));
            _invKeys = KeyBar(_root, t, ("LMB", "Move"), ("RMB", "Use / equip"), ("Shift+LMB", "Split stack"), ("Alt+LMB", "Lock slot"), ("Del", "Delete"), ("Shift", "Craft ×5"), ("Ctrl F", "Search"), ("Esc", "Close"));
            _chestKeys = KeyBar(_root, t, ("LMB", "Move stack"), ("Shift+LMB", "Split stack"), ("Alt+LMB", "Lock slot"), ("Ctrl Q", "Put similar"), ("Ctrl E", "Take everything"), ("Esc", "Close"));   // design: chest open
        }

        // ActionBarPanels (user): the glass panel behind each bar on or off, the slots unchanged; read every tick so a change shows at once
        private readonly (Image line, Image fill)[] _barPanels = new (Image, Image)[2];
        private bool _panelsShown = true;
        private void TickBarPanels()
        {
            bool on = Cfg.SlotBackdrops == null || Cfg.SlotBackdrops.Value; if (on == _panelsShown) return; _panelsShown = on;
            foreach (var (line, fill) in _barPanels) { if (line != null) line.enabled = on; if (fill != null) fill.enabled = on; }
        }

        internal void Tick()
        {
            TickBarPanels();
            var p = Player.m_localPlayer;
            bool show = p != null && Hud.instance != null && !Hud.IsUserHidden() && !Minimap.IsOpen() && Hud.instance.m_rootObject.activeInHierarchy
                        && !Menu.IsVisible() && ConfigPage.Open == null;               // pause screens show the dimmed game, not the HUD
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            _hp.Set(p.GetHealth(), p.GetMaxHealth());
            _st.Set(p.GetStamina(), p.GetMaxStamina());
            float maxE = p.GetMaxEitr();
            if (_ei.Root.gameObject.activeSelf != maxE > 0f) _ei.Root.gameObject.SetActive(maxE > 0f);
            if (maxE > 0f) _ei.Set(p.GetEitr(), maxE);

            var inv = p.GetInventory();
            for (int i = 0; i < 8; i++) _slots[i].Set(inv.GetItemAt(i, 0));
            int sr = SpecialRows.Start; bool have = inv.GetHeight() >= sr + SpecialRows.Count;
            for (int i = 0; i < 6; i++)
            {
                _food[i].Set(have ? inv.GetItemAt(i, sr) : null);
                _tools[i].Set(have ? inv.GetItemAt(i, sr + 1) : null);
                _meads[i].Set(have ? inv.GetItemAt(i, sr + 2) : null);
            }

            var held = p.GetCurrentWeapon();
            if (held != null && held.m_dropPrefab == null) held = null; // bare hands are a weapon too, without a prefab
            var name = held == null ? "" : held.m_shared.m_name + "|" + held.m_quality;
            if (name != _heldName)
            {
                _heldName = name;
                _held.text = held == null ? "" : Localization.instance.Localize(held.m_shared.m_name);
                bool q = held != null && held.m_quality > 1;
                _heldStar.enabled = q; _heldQ.text = q ? held.m_quality.ToString() : "";
                float half = (Kit.W(_held) + (q ? 22f : 0f)) / 2f;             // centre name + star together
                _held.rectTransform.At(267f - half, 0, 534, 18); _held.alignment = TextAlignmentOptions.MidlineLeft;
                _heldStar.rectTransform.At(267f - half + Kit.W(_held) + 6f, 4, 10, 10);
                _heldQ.rectTransform.At(267f - half + Kit.W(_held) + 17f, 0, 20, 18);
            }

            p.GetGuardianPowerHUD(out var se, out var cd);
            if (_power.gameObject.activeSelf != (se != null)) _power.gameObject.SetActive(se != null);
            if (se != null)
            {
                if (_powerIcon.sprite != se.m_icon) _powerIcon.sprite = se.m_icon;
                float max = se.m_cooldown > 0f ? se.m_cooldown : 1f;
                _powerCd.fillAmount = cd > 0f ? Mathf.Clamp01(cd / max) : 0f;
            }

            TickBuffs(p);
            _compass.Tick(); _raid.Tick(); _flags.Tick(); _mount.Tick(p); KeyLabels();
            _minimap.Tick(p);
            Ach.Tick();
            bool invOpen = InventoryGui.IsVisible();
            bool chestOpen = invOpen && ChestPanel.Open != null;
            if (_invKeys.gameObject.activeSelf != (invOpen && !chestOpen)) _invKeys.gameObject.SetActive(invOpen && !chestOpen);
            if (_chestKeys.gameObject.activeSelf != chestOpen) _chestKeys.gameObject.SetActive(chestOpen);
            bool screen = invOpen || StoreGui.IsVisible();   // a full window open: the inventory, or the trader's shop (user: the HUD drew over it)
            foreach (var r in new[] { _dock, _hintsL, _side, _buffs, _compass.Root, _flags.Box }) if (r.gameObject.activeSelf == screen) r.gameObject.SetActive(!screen); // the inventory screen has no dock
            bool placing = !invOpen && p.InPlaceMode();   // hammer, hoe, cultivator: the corner shows placing keys instead of combat
            if (_hintsR.gameObject.activeSelf != (!screen && !placing)) _hintsR.gameObject.SetActive(!screen && !placing);
            if (_hintsPlace.gameObject.activeSelf != placing) _hintsPlace.gameObject.SetActive(placing);
        }

        private void TickBuffs(Player p)
        {
            _effects.Clear();
            p.GetSEMan().GetHUDStatusEffects(_effects);
            var foods = p.GetFoods();
            // debuffs first in the pool (red frames), then buffs and food
            int n = 0, bad = 0;
            foreach (var se in _effects) { if (se.m_icon == null || !IsDebuff(se)) continue; Pool(n++).Set(se.m_icon, se.m_ttl > 0f ? se.GetRemaningTime() : -1f, false, true); bad++; }
            foreach (var se in _effects) { if (se.m_icon == null || IsDebuff(se)) continue; Pool(n++).Set(se.m_icon, se.m_ttl > 0f ? se.GetRemaningTime() : -1f, false, false); }
            foreach (var f in foods)
            {
                var burn = f.m_item.m_shared.m_foodBurnTime;
                Pool(n++).Set(f.m_item.GetIcon(), f.m_time, burn > 0f && f.m_time / burn < 0.3f, false, burn > 0f ? f.m_time / burn : -1f);
            }
            for (int i = 0; i < _buffPool.Count; i++) { bool on = i < n; if (_buffPool[i].Root.gameObject.activeSelf != on) _buffPool[i].Root.gameObject.SetActive(on); }
            // WoW style (user): buffs from the top, right to left, 46 px apart, a new row 64 px lower once 420 px is used;
            // debuffs on their own row, frames level with the bottom edge of the minimap (y 260 on screen, this block starts at 24)
            float x = 0f; int row = 0;
            for (int i = bad; i < n; i++)
            {
                if (x + 40f > 420f) { x = 0f; row++; }
                _buffPool[i].Root.At(420 - 40 - x, row * 64, 40, 58);
                x += 46f;
            }
            for (int i = 0; i < bad; i++) _buffPool[i].Root.At(420 - 40 - i * 46, 260 - 24 - 42, 40, 58);
        }

        // Ailments, as BuffMeDaddy (and the admin hammer) sort them: wet, cold, freezing, burning, poison, smoked...
        private static readonly string[] DebuffWords = { "wet", "burning", "poison", "frost", "lightning", "smoked", "tared", "encumbered", "cold", "freezing", "slimed", "spirit", "staggered", "puke", "fall" };
        internal static bool IsDebuff(StatusEffect se)
        {
            var n = se.name.ToLowerInvariant();
            if (n.StartsWith("potion_") || n.StartsWith("gp_") || n.StartsWith("seteffect_")) return false;   // "frost resistance" meads and the like
            foreach (var w in DebuffWords) if (n.Contains(w)) return true;
            return false;
        }

        // A row of "key + action" hints in a bottom corner. ponytail: fixed labels; read the player's
        // bindings from ZInput when rebinding lands in the settings page.
        private static RectTransform Hints(Transform parent, bool right, ThemeData t, params (string key, string what)[] items)
        {
            var row = Kit.Node(parent, right ? "HintsRight" : "HintsLeft");
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(right ? 1f : 0f, 0f);
            row.anchoredPosition = new Vector2(right ? -28f : 28f, 36f);
            float x = 0f; var parts = new List<RectTransform>();
            foreach (var (key, what) in items)
            {
                var (cap, _) = Kit.Box(row, "Key", C(t.raised), C(t.line2), 5);
                var kt = Kit.Text(cap.rectTransform, "K", key, Fonts.Mono, 10.5f, C(t.text)); kt.alignment = TextAlignmentOptions.Center;
                float kw = Mathf.Max(20f, Kit.W(kt) + 10f);
                cap.rectTransform.At(x, 0, kw, 20); kt.rectTransform.Fill();
                var wt = Kit.Text(row, "What", what, Fonts.Medium, 12f, C(t.dim)); wt.alignment = TextAlignmentOptions.MidlineLeft;
                wt.rectTransform.At(x + kw + 6f, 0, Kit.W(wt) + 2f, 20);
                x += kw + 6f + Kit.W(wt) + 16f;
            }
            row.sizeDelta = new Vector2(x - 16f, 20f);
            var cg = row.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0.85f; cg.blocksRaycasts = false;
            return row;
        }

        // The inventory's key bar (design: centred glass bar at the bottom while the inventory is open).
        private static RectTransform KeyBar(Transform parent, ThemeData t, params (string key, string what)[] items)
        {
            var (bar, _) = Kit.Box(parent, "InventoryKeys", Theme.C(t.glass), Theme.C(t.glassLine), 10);
            var rt = bar.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f); rt.anchoredPosition = new Vector2(0f, 14f);
            // design .keybar: 12.5 px dim labels, 18 px between entries; .key caps: 11 px mono, faint fill, " + " between keys
            float x = 16f;
            foreach (var (key, what) in items)
            {
                var wt = Kit.Text(rt, "What", what, Fonts.Medium, 12.5f, Theme.C(t.dim)); wt.alignment = TextAlignmentOptions.MidlineLeft;
                wt.rectTransform.At(x, 0, Kit.W(wt) + 2f, 38); x += Kit.W(wt) + 6f;
                var parts = key.Split('+');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) { var plus = Kit.Text(rt, "Plus", "+", Fonts.Medium, 12.5f, Theme.C(t.dim)); plus.alignment = TextAlignmentOptions.Center; plus.rectTransform.At(x, 0, 10, 38); x += 16f; }
                    var (cap, _) = Kit.Box(rt, "Key", new Color(1f, 1f, 1f, 0.08f), Theme.C(t.line2), 5);
                    var kt = Kit.Text(cap.rectTransform, "K", parts[i], Fonts.Mono, 11f, Theme.C(t.dim)); kt.alignment = TextAlignmentOptions.Center;
                    float kw = Kit.W(kt) + 12f;
                    cap.rectTransform.At(x, 9.5f, kw, 19); kt.rectTransform.Fill(); x += kw + 6f;
                }
                x += 12f;
            }
            rt.sizeDelta = new Vector2(x - 18f + 16f, 38f);
            rt.gameObject.SetActive(false);
            return rt;
        }

        private static void Column(RectTransform side, string label, float y, Slot[] slots, string[] keys, ThemeData t)
        {
            var l = Kit.Text(side, label, label, Fonts.Mono, 10f, C(t.dim)); l.characterSpacing = 8f; l.alignment = TextAlignmentOptions.Center; l.rectTransform.At(-20, y, 98, 14);
            var (g, _) = Kit.Box(side, label + "Group", C(t.glass), C(t.glassLine), 12); g.rectTransform.At(0, y + 18, 58, 318);
            for (int i = 0; i < slots.Length; i++) slots[i] = new Slot(g.transform, keys != null && i < keys.Length ? keys[i] : "", 6, 6 + i * 52, 46f, t).Cap(2);
        }

        // the slot keys as set (SpecialRows.Keys: food, tools, meads), once a second so a rebind shows without a reload
        private float _nextKeys;
        private void KeyLabels()
        {
            if (SpecialRows.Keys == null || Time.unscaledTime < _nextKeys) return;
            _nextKeys = Time.unscaledTime + 1f;
            var rows = new[] { _food, _tools, _meads };
            for (int r = 0; r < rows.Length; r++)
                for (int i = 0; i < rows[r].Length && i < SpecialRows.Width; i++) rows[r][i]?.Key(Short(SpecialRows.Keys[r, i].Value));
        }

        internal static string Short(BepInEx.Configuration.KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None) return "";
            var m = k.MainKey.ToString(); if (m.StartsWith("Alpha")) m = m.Substring(5);
            foreach (var mod in k.Modifiers) m = (mod == KeyCode.LeftAlt || mod == KeyCode.RightAlt ? "Alt " : mod == KeyCode.LeftShift || mod == KeyCode.RightShift ? "Shift " : mod == KeyCode.LeftControl || mod == KeyCode.RightControl ? "Ctrl " : "") + m;
            return m;
        }

        // a keycap's text: the modifier small and dim, the key white ("Alt Z")
        internal static string CapText(string k, ThemeData t)
        {
            int sp = k.LastIndexOf(' ');
            return sp > 0 ? $"<size=9><color=#{ColorUtility.ToHtmlStringRGB(C(t.dim))}>{k.Substring(0, sp)}</color></size> {k.Substring(sp + 1)}" : k;
        }

        // the same keycap on a cell's top edge (the inventory's Food / Tools / Meads cells, user)
        internal static void CapOn(RectTransform cell, string k, ThemeData t)
        {
            var cap = cell.Find("jatify-kc") as RectTransform; TextMeshProUGUI tx;
            if (cap == null)
            {
                var (b, _) = Kit.Box(cell, "jatify-kc", new Color(0.047f, 0.047f, 0.055f, 0.94f), C(t.line2), 5); cap = b.rectTransform;
                foreach (var g in cap.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                tx = Kit.Text(cap, "T", "", Fonts.Mono, 10.5f, Color.white); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill(); tx.raycastTarget = false; tx.richText = true;
                cap.anchorMin = cap.anchorMax = cap.pivot = new Vector2(0.5f, 1f);
            }
            else tx = cap.Find("T").GetComponent<TextMeshProUGUI>();
            var text = CapText(k, t); cap.gameObject.SetActive(k.Length > 0); if (tx.text == text) return;
            tx.text = text; cap.sizeDelta = new Vector2(Kit.W(tx) + 10f, 16f); cap.anchoredPosition = new Vector2(0f, 8f);   // straddles the top edge
        }

        private Buff Pool(int i)
        {
            while (_buffPool.Count <= i) _buffPool.Add(new Buff(_buffs, _t));
            return _buffPool[i];
        }

        internal void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); }

        internal static Color C(string hex) => Theme.C(hex);

        // ---- parts -------------------------------------------------------------------------------
        private sealed class Bar
        {
            internal readonly RectTransform Root;
            private readonly RectTransform _fill;
            private readonly TextMeshProUGUI _val;
            private readonly float _w, _h;
            private int _shownCur = -1, _shownMax = -1;

            internal Bar(Transform parent, string label, float x, float y, float w, float h, Color color, ThemeData t)
            {
                _w = w; _h = h;
                var (o, _) = Kit.Box(parent, label, new Color(0f, 0f, 0f, 0.45f), C(t.glassLine), h / 2f);
                Root = o.rectTransform.At(x, y, w, h);
                var f = Kit.Panel(Root, "Value", color, h / 2f);
                _fill = f.rectTransform; _fill.anchorMin = new Vector2(0f, 0f); _fill.anchorMax = new Vector2(0f, 1f); _fill.pivot = new Vector2(0f, 0.5f);
                _fill.anchoredPosition = Vector2.zero; _fill.sizeDelta = new Vector2(w, 0f);
                var b = Kit.Text(Root, "Label", label, Fonts.Mono, 10f, new Color(1f, 1f, 1f, 0.75f)); b.characterSpacing = 6f;
                b.rectTransform.At(9, 0, 80, h); b.alignment = TextAlignmentOptions.MidlineLeft;
                _val = Kit.Text(Root, "Number", "", Fonts.Mono, 11f, Color.white);
                _val.rectTransform.At(w - 8 - 120, 0, 120, h); _val.alignment = TextAlignmentOptions.MidlineRight;
            }

            internal void Set(float cur, float max)
            {
                float k = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
                _fill.sizeDelta = new Vector2(k <= 0f ? 0f : Mathf.Max(_h, _w * k), 0f); // never narrower than its rounded ends
                _fill.gameObject.SetActive(k > 0f);
                int c = Mathf.CeilToInt(cur), m = Mathf.CeilToInt(max);
                if (c != _shownCur || m != _shownMax) { _shownCur = c; _shownMax = m; _val.text = _h > 15f ? $"{c} / {m}" : c.ToString(); }
            }
        }

        private sealed class Slot
        {
            private readonly Image _line, _tile, _glow, _icon, _star, _durFill;
            private readonly RectTransform _rt, _dur;
            internal bool Bar;   // a 1-8 or tools slot: its tile follows ActionBarSlotBackdrops
            private int _look = -1;
            private readonly TextMeshProUGUI _count, _quality, _key;
            private readonly float _y;
            private readonly ThemeData _t;
            private ItemDrop.ItemData _item;
            private int _stack = -1, _q = -1, _durPct = -1; private bool _on;

            internal Slot(Transform parent, int i, float x, float y, ThemeData t) : this(parent, (i + 1).ToString(), x, y, 60f, t) { }

            // size 60 (hotbar) or 46 (tools, food, meads); key may be "" for none
            internal Slot(Transform parent, string key, float x, float y, float size, ThemeData t)
            {
                _t = t; _y = y;
                float s = size, inset = s > 50f ? 12f : 8f, pad = s > 50f ? 7f : 5f;
                _glow = Kit.Panel(parent, "Glow" + key, C(t.accentSoft), 13); _glow.rectTransform.At(x - 3, y - 3, s + 6, s + 6); _glow.enabled = false;
                (_line, _tile) = Kit.Box(parent, "Slot" + key, C(t.raised), C(t.glassLine), s > 50f ? 10 : 9);
                _rt = _line.rectTransform.At(x, y, s, s);
                _icon = Kit.Img(_rt, "Icon", null, Color.white, false); _icon.rectTransform.At(inset, inset, s - 2 * inset, s - 2 * inset); _icon.preserveAspect = true; _icon.enabled = false; // an Image without a sprite draws a white square
                var k = _key = Kit.Text(_rt, "Key", key, Fonts.Mono, s > 50f ? 10f : 9.5f, C(t.dim)); k.fontStyle = TMPro.FontStyles.Bold; k.rectTransform.At(pad, pad - 2, s - 2 * pad, 14); k.overflowMode = TextOverflowModes.Overflow;   // bold and brighter: readable on the small slots (user)
                _star = Kit.Img(_rt, "Star", Kit.Star(), C(t.stamina), false); _star.rectTransform.At(s - pad - 19, pad, 9, 9);
                _quality = Kit.Text(_rt, "Quality", "", Fonts.Mono, 10f, C(t.stamina)); _quality.rectTransform.At(s - pad - 8, pad - 2, 8, 14);
                _count = Kit.Text(_rt, "Count", "", Fonts.Mono, s > 50f ? 11f : 10f, C(t.text)); _count.alignment = TextAlignmentOptions.BottomRight; _count.rectTransform.At(s - pad - 50, s - pad - 1 - 16, 50, 16);
                var (dl, df) = Kit.Box(_rt, "Durability", new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0.1f), 2);
                _dur = dl.rectTransform.At(6, s - 4 - 3, s - 12, 3);
                _durFill = Kit.Panel(_dur, "Value", C(t.accent), 2); var r = _durFill.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(1f, 1f); r.offsetMin = r.offsetMax = Vector2.zero;
                df.enabled = false;
                Set(null);
            }

            // the key as a solid keycap outside the slot (design, user: "Shift Z" overflowed, "Alt N" was hard to read):
            // 1 on its top edge (tools), 2 to its left (food, meads); the modifier small and dim, the key white
            private RectTransform _cap; private int _capAt; private string _keyRaw;
            internal Slot Cap(int at)
            {
                _capAt = at; var (b, _) = Kit.Box(_rt, "KeyCap", new Color(0.047f, 0.047f, 0.055f, 0.94f), C(_t.line2), 5); _cap = b.rectTransform;
                _key.transform.SetParent(_cap, false); _key.rectTransform.Fill(); _key.alignment = TextAlignmentOptions.Center;
                _key.font = Fonts.Mono; _key.fontSize = 10.5f; _key.color = Color.white; _key.richText = true;
                var k = _keyRaw ?? _key.text; _keyRaw = null; Key(k); return this;
            }

            internal void Key(string k)
            {
                if (_keyRaw == k) return; _keyRaw = k;
                if (_cap == null) { _key.text = k; return; }
                _key.text = CapText(k, _t);
                _cap.gameObject.SetActive(k.Length > 0); _look = -1; if (k.Length == 0) return;
                float w = Kit.W(_key) + 10f, s = _rt.rect.width;
                if (_capAt == 1) _cap.At((s - w) / 2f, -15f, w, 17f); else _cap.At(-8f - w, (s - 17f) / 2f, w, 17f);
            }

            internal void Set(ItemDrop.ItemData it)
            {
                bool on = it != null && it.m_equipped;
                if (it != _item) { _item = it; _icon.sprite = it?.GetIcon(); _icon.enabled = it != null; _stack = _q = _durPct = -1; }
                int stack = it != null && it.m_shared.m_maxStackSize > 1 ? it.m_stack : 0;
                if (stack != _stack) { _stack = stack; _count.text = stack > 0 ? stack.ToString() : ""; }
                int q = it?.m_quality ?? 0;
                if (q != _q) { _q = q; bool show = q > 1; _star.enabled = show; _quality.text = show ? q.ToString() : ""; }
                int d = it != null && it.m_shared.m_useDurability ? Mathf.RoundToInt(100f * Mathf.Clamp01(it.m_durability / Mathf.Max(1f, it.GetMaxDurability()))) : -2;
                if (d != _durPct)
                {
                    _durPct = d; _dur.gameObject.SetActive(d >= 0);
                    if (d >= 0) { _durFill.rectTransform.anchorMax = new Vector2(d / 100f, 1f); _durFill.color = C(d < 25 ? _t.ember : _t.accent); }
                }
                if (on != _on || _item == null)
                {
                    _on = on;
                    _line.color = C(on ? _t.accent : _t.glassLine);
                    _glow.enabled = on;
                    _rt.anchoredPosition = new Vector2(_rt.anchoredPosition.x, -(_y - (on ? 4f : 0f)));       // selected slot lifts 4 px
                    _glow.rectTransform.anchoredPosition = new Vector2(_glow.rectTransform.anchoredPosition.x, -(_y - 3f - (on ? 4f : 0f)));
                }
                Look();
            }

            // ActionBarSlotBackdrops off (user): no tile, the icon, key, count and durability float on their own; the selected slot
            // keeps its green outline; an empty slot shows nothing. Read every tick: a change in the settings page shows at once.
            private void Look()
            {
                bool tiles = true;   // ponytail: the slot tiles always show; ActionBarPanels switches the bars' glass panels (Tick)
                int look = (tiles ? 1 : 0) | (_item != null ? 2 : 0) | (_on ? 4 : 0);
                if (look == _look) return;
                _look = look; bool shown = tiles || _item != null;
                _tile.enabled = tiles; _line.enabled = tiles || _on;
                _key.enabled = shown; if (_cap != null) _cap.gameObject.SetActive(shown && !string.IsNullOrEmpty(_keyRaw));
            }
        }

        // An effect or food: its icon in a frame, the time left on a small dark pill under it (detail pass: readable over a bright
        // sky; ember in the last two minutes). Food fills from the bottom with what's left of it (ember tint), its frame ember.
        private sealed class Buff
        {
            internal readonly RectTransform Root;
            private readonly Image _line, _icon, _pill;
            private readonly RectTransform _left;
            private readonly TextMeshProUGUI _time;
            private readonly ThemeData _t;
            private int _shown = int.MinValue, _leftPct = -2, _frame = -1; private bool _warn;

            internal Buff(Transform parent, ThemeData t)
            {
                _t = t;
                Root = Kit.Node(parent, "Buff");
                Image fill; (_line, fill) = Kit.Box(Root, "Frame", C(t.glass), C(t.line2), 8);
                _line.rectTransform.At(-1, 0, 42, 42);
                fill.gameObject.AddComponent<Mask>().showMaskGraphic = true;   // the food fill keeps to the frame's round corners
                _left = Kit.Img(fill.rectTransform, "Left", null, new Color(0.949f, 0.439f, 0.227f, 0.22f)).rectTransform;   // design: rgba(242,112,58,.22)
                _left.anchorMin = Vector2.zero; _left.anchorMax = new Vector2(1f, 0f); _left.offsetMin = _left.offsetMax = Vector2.zero; _left.gameObject.SetActive(false);
                _icon = Kit.Img(_line.rectTransform, "Icon", null, Color.white, false); _icon.rectTransform.At(4, 4, 34, 34); _icon.preserveAspect = true;
                _pill = Kit.Panel(Root, "Pill", new Color(0.078f, 0.078f, 0.086f, 0.86f), 8f); _pill.rectTransform.At(0, 45, 40, 16); _pill.enabled = false;   // design .timer: 16 tall, rgba(20,20,22,.86)
                _time = Kit.Text(_pill.rectTransform, "Time", "", Fonts.Mono, 10.5f, Color.white); _time.alignment = TextAlignmentOptions.Center; _time.rectTransform.Fill();
            }

            // left: the share of a food still to go (0..1), -1 for an effect
            internal void Set(Sprite icon, float secondsLeft, bool low, bool bad, float left = -1f)
            {
                if (_icon.sprite != icon) _icon.sprite = icon;
                int s = secondsLeft < 0f ? -1 : Mathf.CeilToInt(secondsLeft);
                int shownKey = s < 0 ? -1 : s >= 60 ? 100000 + Mathf.CeilToInt(s / 60f) : s; // minutes above a minute, seconds below
                if (shownKey != _shown)
                {
                    _shown = shownKey; _time.text = s < 0 ? "" : s >= 60 ? Mathf.CeilToInt(s / 60f) + "m" : s + "s";
                    _pill.enabled = s >= 0;
                    if (s >= 0) { float w = Mathf.Ceil(Kit.W(_time)) + 12f; _pill.rectTransform.At(20f - w / 2f, 45f, w, 16f); }   // centred under the 40 px icon
                }
                bool warn = s >= 0 && s <= 120;
                if (warn != _warn) { _warn = warn; _time.color = warn ? C(_t.ember) : Color.white; }
                int pct = left < 0f ? -1 : Mathf.RoundToInt(Mathf.Clamp01(left) * 100f);
                if (pct != _leftPct)
                {
                    _leftPct = pct; _left.gameObject.SetActive(pct > 0);
                    if (pct > 0) _left.anchorMax = new Vector2(1f, pct / 100f);
                }
                int frame = bad ? 0 : low ? 1 : left >= 0f ? 2 : 3;
                if (frame != _frame)
                {
                    _frame = frame; var e = C(_t.ember);
                    _line.color = frame == 0 ? C(_t.health) : frame == 1 ? e : frame == 2 ? new Color(e.r, e.g, e.b, 0.55f) : C(_t.line2);   // design .eicon.food: ember .55
                }
            }
        }
    }
}
