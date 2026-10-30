using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The equipment column on the left of the inventory panel (design: paper doll). Built inside the
    // vanilla inventory panel so it moves with it and Valheim's tooltip works. Each slot shows the
    // equipped item and its durability; hover = vanilla item tooltip; click = unequip.
    // ponytail: no drag-to-equip yet (right-click in the grid equips, as in vanilla); the middle shows
    // Armour and Block until the live character render exists.
    internal sealed class Equipment
    {
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Head = F("m_helmetItem"), Cape = F("m_shoulderItem"), Chest = F("m_chestItem"),
            Legs = F("m_legItem"), Utility = F("m_utilityItem"), Trinket = F("m_trinketItem"), Main = F("m_rightItem"), Off = F("m_leftItem"), Ammo = F("m_ammoItem");
        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F(string n) => AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>(n);

        private readonly EqSlot[] _slots;
        private static Equipment _current;

        // the worn item under the pointer on the doll (other mods' "hovered item" shortcuts: EpicLoot's socket overlay)
        internal static ItemDrop.ItemData UnderPointer()
        {
            var e = _current; if (e == null || e._root == null || !e._root.gameObject.activeInHierarchy) return null;
            foreach (var s in e._slots) { var it = s.UnderPointer(); if (it != null) return it; }
            return null;
        }
        internal float Block;                                  // for the stats card
        internal bool OnSkills => _page == 1;
        internal bool OnStats => _page == 2;   // stats, resistances and effects on a tab of their own (user), the panel shorter for it
        private readonly RectTransform _root, _doll;
        private readonly SkillsList _skills;
        private int _page; private bool _onSkills => _page == 1;
        internal static bool ForceSkills; // dev
        private Image _tabChar, _tabSkills, _tabCharI, _tabSkillsI, _tabStats, _tabStatsI; private TextMeshProUGUI _tabCharT, _tabSkillsT, _tabStatsT; private RectTransform _pvp;
        private readonly ThemeData _t;

        private void Tabs(ThemeData t)
        {
            var line = Kit.Img(_root, "TabLine", null, Theme.C(t.glassLine)); line.rectTransform.At(0, 34, 240, 1);
            (_tabChar, _tabCharT, _tabCharI) = Tab("Character", "host_helmet_icon", 0, 96, () => _page = 0, t);
            (_tabSkills, _tabSkillsT, _tabSkillsI) = Tab("Skills", "walknut_bw", 98, 68, () => _page = 1, t);
            (_tabStats, _tabStatsT, _tabStatsI) = Tab("Stats", "tab-book", 168, 68, () => _page = 2, t);
            // PvP: Valheim's pvp icon and a mini switch; flips vanilla's toggle, which applies it. It sits in the top panel, left of the
            // settings button (user; MovePvp), once that panel is built
            var pvp = _pvp = Kit.Node(_root, "PvP").At(187, 50, 53, 34);
            _pvpIcon = Kit.Img(pvp, "Icon", Kit.Icon("pvp_off"), Color.white, false); _pvpIcon.rectTransform.At(2, 8, 18, 18); _pvpIcon.preserveAspect = true;
            var (sw, swf) = Kit.Box(pvp, "Switch", Theme.C(t.raised2), Theme.C(t.line2), 7); sw.rectTransform.At(25, 10, 26, 14); _pvpSw = swf; _pvpSwLine = sw;
            _pvpKnob = Kit.Img(sw.rectTransform, "Knob", Kit.Circle(), Theme.C(t.dim), false); _pvpKnob.rectTransform.At(3, 3, 8, 8);
            var hit = pvp.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            pvp.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                var g = InventoryGui.instance;
                if (g != null && g.m_pvp != null && g.m_pvp.interactable) g.m_pvp.isOn = !g.m_pvp.isOn;
            });
        }
        private Image _pvpIcon, _pvpSw, _pvpSwLine, _pvpKnob; private int _pvpShown = -1;
        internal RectTransform MovePvp(Transform to, Vector2 centre)
        {
            _pvp.SetParent(to, false); _pvp.anchorMin = _pvp.anchorMax = _pvp.pivot = new Vector2(0.5f, 0.5f); _pvp.anchoredPosition = centre;
            return _pvp;
        }

        private void PaintPvp()
        {
            var g = InventoryGui.instance; if (g == null || g.m_pvp == null) return;
            int s = (g.m_pvp.isOn ? 1 : 0) + (g.m_pvp.interactable ? 2 : 0);
            if (s == _pvpShown) return;
            _pvpShown = s; bool on = g.m_pvp.isOn;
            _pvpIcon.sprite = Kit.Icon(on ? "pvp_on" : "pvp_off"); _pvpIcon.color = new Color(1f, 1f, 1f, on ? 1f : 0.7f);
            _pvpSw.color = on ? Theme.C(_t.emberSoft) : Theme.C(_t.raised2); _pvpSwLine.color = Theme.C(on ? _t.ember : _t.line2);
            _pvpKnob.color = on ? Theme.C(_t.ember) : Theme.C(_t.dim);
            _pvpKnob.rectTransform.At(on ? 14 : 2, 2, 8, 8);
            var cg = _pvpIcon.transform.parent.GetComponent<CanvasGroup>() ?? _pvpIcon.transform.parent.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = g.m_pvp.interactable ? 1f : 0.45f;   // vanilla locks it in combat and for a while after
        }

        // returns (underline, label, icon)
        private (Image, TextMeshProUGUI, Image) Tab(string label, string icon, float x, float w, System.Action on, ThemeData t)
        {
            var hit = Kit.Img(_root, label, null, new Color(0, 0, 0, 0)); hit.rectTransform.At(x, 0, w, 34); hit.raycastTarget = true;
            bool helm = icon == "host_helmet_icon";
            var ic = Kit.Img(hit.rectTransform, "Icon", Kit.Icon(icon), Color.white, false); ic.preserveAspect = true; ic.rectTransform.At(8, helm ? 10 : 9, helm ? 20 : 16, helm ? 14 : 16);
            var tx = Kit.Text(hit.rectTransform, "T", label, Fonts.Medium, 13f, Theme.C(t.dim)); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.rectTransform.At(helm ? 35 : 31, 0, w - 30, 34);
            var ul = Kit.Panel(hit.rectTransform, "On", Theme.C(t.accent), 1); ul.rectTransform.At(10, 33, w - 20, 2);
            hit.gameObject.AddComponent<Button>().onClick.AddListener(() => on());
            return (ul, tx, ic);
        }
        private readonly KeyPouch _pouch;

        // Design: key slot beside Ammo (first key's icon, total count); click opens a 4 x 3 pouch.
        // The keys live in ModSlots' rows 1-3, so they don't take bag space.
        private sealed class KeyPouch
        {
            private readonly Image _icon, _hint; private readonly TextMeshProUGUI _count;
            private readonly RectTransform _pop; private readonly TextMeshProUGUI _sub;
            private readonly EqSlot[] _cells = new EqSlot[12];
            private string _shown;

            internal KeyPouch(RectTransform doll, ThemeData t, GameObject tooltipPrefab)
            {
                var (line, fill) = Kit.Box(doll, "Keys", Theme.C(t.raised), Theme.C(t.glassLine), 9);
                var rt = line.rectTransform.At(ModSlots.RingsOn ? 126 : Azu.Drives ? 52 : 192, 288, 46, 46); line.raycastTarget = true;   // AzuEPI: after the ammo, other mods' slots after it; no rings: after the hands and ammo
                _hint = Kit.IconImg(rt, "key", new Color(1f, 1f, 1f, 0.3f), 24f); _hint.rectTransform.At(11, 11, 24, 24);   // our swamp-key drawing (assets/svg/key.svg)
                _icon = Kit.Img(rt, "Icon", null, Color.white, false); _icon.rectTransform.Fill(8f); _icon.preserveAspect = true; _icon.enabled = false;
                _count = Kit.Text(rt, "Count", "", Fonts.Mono, 10.5f, Color.white); _count.alignment = TextAlignmentOptions.BottomRight; _count.rectTransform.Fill(5f);
                var btn = line.gameObject.AddComponent<Button>(); btn.targetGraphic = fill;
                bool justPut = false;
                Press.On(line.gameObject, () =>
                {   // press while carrying a key: into the pouch
                    justPut = false;
                    var p = Player.m_localPlayer; var g = InventoryGui.instance; if (p == null || g == null) return;
                    var drag = DragItem(g); if (drag == null) return;
                    bool ok = false;
                    for (int i = 0; i < 12 && !ok; i++) if (ModSlots.At(p, i % 4, ModSlots.KeyRow + i / 4) == null) ok = ModSlots.Put(p, drag, i % 4, ModSlots.KeyRow + i / 4);
                    if (ok) SetupDrag.Invoke(g, new object[] { null, null, 1 });
                    else p.Message(MessageHud.MessageType.Center, ModSlots.IsKey(drag) ? "The key pouch is full" : "Only keys go in the key pouch", 0, ModSlots.IsKey(drag) ? drag.GetIcon() : Kit.Icon("key"));
                    justPut = true;
                },
                drag: () =>
                {   // drag from the slot: the key it shows comes with you
                    var p = Player.m_localPlayer; var g = InventoryGui.instance; if (p == null || g == null || DragItem(g) != null) return;
                    for (int i = 0; i < 12; i++) if (ModSlots.At(p, i % 4, ModSlots.KeyRow + i / 4) != null) { ModSlots.PickUp(p, i % 4, ModSlots.KeyRow + i / 4); return; }
                },
                click: () => { if (justPut) return; _pop.gameObject.SetActive(!_pop.gameObject.activeSelf); _pop.SetAsLastSibling(); });
                var tip = line.gameObject.AddComponent<UITooltip>(); tip.m_tooltipPrefab = tooltipPrefab; tip.Set("Keys", "Keys don't use backpack space.");
                // popup: header, 4 x 3 grid, note
                var (pl, _) = Kit.Box(doll, "KeyPouch", Theme.C(t.glass), Theme.C(t.line2), 12);
                _pop = pl.rectTransform.At(30, 80, 208, 200); pl.raycastTarget = true;
                var h = Kit.Text(_pop, "Title", "Keys", Fonts.Bold, 14f, Color.white); h.rectTransform.At(12, 8, 80, 20);
                _sub = Kit.Text(_pop, "Sub", "", Fonts.Mono, 10.5f, Theme.C(t.mute)); _sub.rectTransform.At(56, 10, 110, 16);
                var (xb, xf) = Kit.Box(_pop, "Close", Theme.C(t.raised), Theme.C(t.glassLine), 6); xb.rectTransform.At(178, 8, 22, 22);
                var xi = Kit.IconImg(xb.rectTransform, "x", Theme.C(t.dim), 12f); xi.rectTransform.At(5, 5, 12, 12);
                xb.raycastTarget = true; var xbt = xb.gameObject.AddComponent<Button>(); xbt.targetGraphic = xf; xbt.onClick.AddListener(() => _pop.gameObject.SetActive(false));
                for (int i = 0; i < 12; i++) _cells[i] = new EqSlot(_pop, "Key", null, 12 + (i % 4) * 47, 36 + (i / 4) * 47, t, tooltipPrefab, 42f, i % 4, ModSlots.KeyRow + i / 4);
                var note = Kit.Text(_pop, "Note", "Keys don't use backpack space.", Fonts.Medium, 11f, Theme.C(t.mute)); note.rectTransform.At(12, 178, 190, 16);
                _pop.gameObject.SetActive(false);
            }

            internal void Tick(Player p)
            {
                if (_pop.gameObject.activeSelf) foreach (var c in _cells) c.Tick(p);
                ItemDrop.ItemData first = null; int kinds = 0, total = 0;
                for (int i = 0; i < 12; i++) { var it = ModSlots.At(p, i % 4, ModSlots.KeyRow + i / 4); if (it == null) continue; first = first ?? it; kinds++; total += it.m_stack; }
                var key = (first != null ? first.m_shared.m_name : "") + kinds + "/" + total;
                if (key == _shown) return;
                _shown = key;
                _icon.sprite = first?.GetIcon(); _icon.enabled = first != null; _hint.enabled = first == null;   // the drawing only while the pouch is empty
                _count.text = total > 1 ? total.ToString() : "";
                _sub.text = $"{kinds} kind{(kinds == 1 ? "" : "s")} · {total}";
            }
        }

        private readonly CharacterRender _render;
        private readonly TextMeshProUGUI _name;
        private int _a = -1, _b = -1;

        internal Equipment(RectTransform panel, ThemeData t, GameObject tooltipPrefab)
        {
            _current = this;
            _t = t;
            // area left of the grid: 300 wide, from the panel's top edge down
            _root = Kit.Node(panel, "jatify-equipment").At(19, 67, 240, 420);   // design: x 179, y 99 on screen
            // Character | Skills tabs on top (design), the doll and the skills list below them
            _doll = Kit.Node(_root, "Doll").At(0, 49, 240, 371);
            _skills = new SkillsList(_root, t);
            Tabs(t);
            // 286 wide: left column x 0, figure 66..220, right column x 226; hands row centred under the figure
            _slots = new[]
            {
                new EqSlot(_doll, "Head", Head, 0, 0, t, tooltipPrefab), new EqSlot(_doll, "Cape", Cape, 0, 68, t, tooltipPrefab),
                new EqSlot(_doll, "Chest", Chest, 0, 136, t, tooltipPrefab), new EqSlot(_doll, "Legs", Legs, 0, 204, t, tooltipPrefab),
                new EqSlot(_doll, "Trinket", Trinket, 180, 0, t, tooltipPrefab), new EqSlot(_doll, "Belt", Utility, 180, 68, t, tooltipPrefab),
                new EqSlot(_doll, "Wishbone", null, 180, 136, t, tooltipPrefab, 60f, 0, 0), new EqSlot(_doll, "Wisplight", null, 180, 204, t, tooltipPrefab, 60f, 1, 0),
            }.Concat(ModSlots.RingsOn
                ? new[]
                {   // rings under the two columns (they replaced the hands: weapons show on the action bar and the figure); ammo and keys between
                    new EqSlot(_doll, "Ring", null, 0, 281, t, tooltipPrefab, 60f, ModSlots.RingX, 0), new EqSlot(_doll, "Ring", null, 180, 281, t, tooltipPrefab, 60f, ModSlots.RingX + 1, 0),
                    new EqSlot(_doll, "Ammo", Ammo, 70, 288, t, tooltipPrefab, 46f),
                }
                : Azu.Drives ? new[]
                {   // AzuEPI (it has the ring slots): no hands either (they show on the action bar and the figure); ammo, the key pouch, then other mods' slots
                    new EqSlot(_doll, "Ammo", Ammo, 0, 288, t, tooltipPrefab, 46f),
                }
                : new[]
                {   // no rings in this game (vanilla): the hands, then ammo and the key pouch, as before rings came
                    new EqSlot(_doll, "Main hand", Main, 2, 281, t, tooltipPrefab), new EqSlot(_doll, "Off hand", Off, 70, 281, t, tooltipPrefab),
                    new EqSlot(_doll, "Ammo", Ammo, 138, 288, t, tooltipPrefab, 46f),
                }).ToArray();
            _pouch = new KeyPouch(_doll, t, tooltipPrefab);
            // middle: the numbers that matter for what you wear
            _render = new CharacterRender(_doll, 64, 0, 112, 230);
            // the character's name under the figure (user), in the Norse lettering
            _name = Kit.Text(_doll, "Name", "", Fonts.Norse, 18f, Theme.C(t.text)); _name.alignment = TextAlignmentOptions.Center;
            _name.rectTransform.At(40, 236, 160, 24); _name.overflowMode = TextOverflowModes.Ellipsis;
            // armour and block live in the stats card now (user)
        }

        // where a doll cell is on screen (AzuEPI's own cells are laid over ours), or null while the doll is not showing
        internal RectTransform DollCell(string name) => _doll.gameObject.activeInHierarchy ? _slots.FirstOrDefault(s => s.Name == name)?.Rect : null;

        // other mods' slots (AzuEPI's list) in the ammo / key row and under it, 44 px: placeholders its cells are laid over
        internal const int ExtraPerRow = 5, ExtraBeside = 3; internal const float ExtraPitch = 49f;
        internal static float ExtraRowsBelow(int n) => n <= ExtraBeside ? 0f : (n - ExtraBeside + ExtraPerRow - 1) / ExtraPerRow * ExtraPitch;   // what the stats card moves down
        private readonly List<RectTransform> _extra = new List<RectTransform>();
        internal RectTransform ExtraCell(int i)
        {
            if (!_doll.gameObject.activeInHierarchy) return null;
            while (_extra.Count <= i)
            {   // the first 3 beside the ammo and key pouch, then rows of 5
                int n = _extra.Count, m = n - ExtraBeside;
                _extra.Add(Kit.Node(_doll, "Extra" + n).At(n < ExtraBeside ? 104 + n * 46 : m % ExtraPerRow * ExtraPitch, n < ExtraBeside ? 289 : 289 + ExtraPitch + m / ExtraPerRow * ExtraPitch, 44, 44));
            }
            return _extra[i];
        }

        private void Label(string text, float x, float y, ThemeData t)
        {
            var l = Kit.Text(_doll, text, text, Fonts.Mono, 10.5f, Theme.C(t.dim)); l.characterSpacing = 8f; l.rectTransform.At(x, y, 200, 16);
        }

        private static void Stat(Transform parent, string label, float y, ThemeData t, out TextMeshProUGUI value)
        {
            var l = Kit.Text(parent, label, label, Fonts.Medium, 11.5f, Theme.C(t.mute)); l.alignment = TextAlignmentOptions.MidlineLeft; l.rectTransform.At(0, y, 44, 17);
            value = Kit.Text(parent, label + "Value", "", Fonts.Medium, 12f, Color.white); value.alignment = TextAlignmentOptions.MidlineRight; value.rectTransform.At(40, y, 30, 17);
        }

        internal void Tick(Player p)
        {
            if (p != null && _name != null && _name.text.Length == 0) _name.text = p.GetPlayerName();   // once: a name can't change in the world
            if (ForceSkills) { _page = 1; ForceSkills = false; }
            if (_doll.gameObject.activeSelf != (_page == 0)) _doll.gameObject.SetActive(_page == 0);
            _skills.Show(_onSkills, p);
            _tabStats.enabled = _page == 2; _tabStatsT.color = _page == 2 ? Color.white : Theme.C(_t.dim); _tabStatsI.color = new Color(1f, 1f, 1f, _page == 2 ? 1f : 0.55f);
            _tabChar.enabled = _page == 0; _tabCharT.color = _page == 0 ? Color.white : Theme.C(_t.dim); _tabCharI.color = new Color(1f, 1f, 1f, _page == 0 ? 1f : 0.55f);
            _tabSkills.enabled = _onSkills; _tabSkillsT.color = _onSkills ? Color.white : Theme.C(_t.dim); _tabSkillsI.color = new Color(1f, 1f, 1f, _onSkills ? 1f : 0.55f);
            PaintPvp();
            if (_page != 0) return;
            _render.Tick(p, InventoryGui.IsVisible());
            foreach (var s in _slots) s.Tick(p);
            _pouch.Tick(p);
            var shield = Off(p) != null && Off(p).m_shared.m_blockPower > 0f ? Off(p) : Main(p);
            Block = shield != null ? shield.GetBlockPower(p.GetSkillFactor(Skills.SkillType.Blocking)) : 0f;
        }

        internal void Idle() => _render.Tick(Player.m_localPlayer, false);   // keeps the figure idling out of sight

        internal void Destroy() { _render?.Destroy(); if (_root != null) Object.Destroy(_root.gameObject); }

        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        internal static ItemDrop.ItemData Lifted;   // worn gear picked up from its slot: unequipped once it lands in the bag (EquipDropPatch)
        private static readonly System.Reflection.MethodInfo SetupDrag = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");

        // a clear reason instead of Valheim's missing "$msg_cantplace"
        private static string WhyNot(string slot, ItemDrop.ItemData it)
        {
            var name = Localization.instance.Localize(it.m_shared.m_name);
            switch (slot)
            {
                case "Key": return ModSlots.IsKey(it) ? "That pouch cell is taken" : "Only keys go in the key pouch";
                case "Main hand": case "Off hand": return $"{name} can't be held there";
                case "Wishbone": return "Only a Wishbone goes here";
                case "Wisplight": return "Only a Wisplight goes here";
                case "Ring": return "Only a ring goes here";
                default: return $"{name} doesn't go in the {slot.ToLowerInvariant()} slot";
            }
        }

        private static bool Fits(string slot, ItemDrop.ItemData.ItemType k)
        {
            switch (slot)
            {
                case "Head": return k == ItemDrop.ItemData.ItemType.Helmet;
                case "Cape": return k == ItemDrop.ItemData.ItemType.Shoulder;
                case "Chest": return k == ItemDrop.ItemData.ItemType.Chest;
                case "Legs": return k == ItemDrop.ItemData.ItemType.Legs;
                case "Belt": return k == ItemDrop.ItemData.ItemType.Utility;
                case "Main hand": return k == ItemDrop.ItemData.ItemType.OneHandedWeapon || k == ItemDrop.ItemData.ItemType.TwoHandedWeapon || k == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || k == ItemDrop.ItemData.ItemType.Bow || k == ItemDrop.ItemData.ItemType.Tool || k == ItemDrop.ItemData.ItemType.Torch;
                case "Off hand": return k == ItemDrop.ItemData.ItemType.Shield || k == ItemDrop.ItemData.ItemType.Torch;
                case "Trinket": return k == ItemDrop.ItemData.ItemType.Trinket;
                case "Ammo": return k == ItemDrop.ItemData.ItemType.Ammo;
                default: return false;
            }
        }

        private static Sprite Ghost(string slot)
        {
            string prefab = slot == "Head" ? "HelmetLeather" : slot == "Cape" ? "CapeDeerHide" : slot == "Chest" ? "ArmorLeatherChest" : slot == "Legs" ? "ArmorLeatherLegs"
                : slot == "Belt" ? "BeltStrength" : slot == "Wishbone" ? "Wishbone" : slot == "Wisplight" ? "Demister" : slot == "Ring" ? "SilverRing" : slot == "Main hand" ? "SwordBronze" : slot == "Off hand" ? "ShieldWood" : slot == "Ammo" ? "ArrowWood" : slot == "Key" ? "CryptKey" : null;
            if (ObjectDB.instance == null) return null;
            if (slot == "Trinket")
                foreach (var go in ObjectDB.instance.m_items) { var d = go ? go.GetComponent<ItemDrop>() : null; if (d != null && d.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket) return d.m_itemData.GetIcon(); }
            return prefab != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
        }

        private sealed class EqSlot
        {
            private readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _get;
            private readonly int _cx, _cy;   // a ModSlots cell when _get is null
            private TextMeshProUGUI _count, _spare; private int _shownCount = -1, _shownSpare = -1;   // stacking gear (ammo): the stack in the slot, and "+n" spares of it in the bag
            private readonly Image _icon, _durFill; private readonly RarityBg _rarity;   // rarity (EpicLoot): its background behind the icon, the border tinted
            private readonly RectTransform _dur;
            private readonly Image _hint; private readonly string _name; private readonly RectTransform _rt;
            private readonly UITooltip _tip;
            private ItemDrop.ItemData _item;
            private int _durPct = -1;
            private readonly ThemeData _t;

            internal EqSlot(Transform parent, string name, AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> get, float x, float y, ThemeData t, GameObject tooltipPrefab, float size = 60f, int cx = -1, int cy = -1)
            {
                _get = get; _t = t; _cx = cx; _cy = cy;
                var (line, fill) = Kit.Box(parent, name, Theme.C(t.raised), Theme.C(t.glassLine), size > 50f ? 10 : 9);
                var rt = _rt = line.rectTransform.At(x, y, size, size);
                line.raycastTarget = true;
                var btn = line.gameObject.AddComponent<Button>();
                btn.targetGraphic = fill; btn.transition = Selectable.Transition.ColorTint;
                var cb = btn.colors; cb.highlightedColor = new Color(1.25f, 1.25f, 1.3f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = cb;
                Press.On(line.gameObject, () =>
                {
                    var p = Player.m_localPlayer; var g = InventoryGui.instance;
                    if (p == null || g == null || !InventoryGui.IsVisible()) return;
                    var drag = DragItem(g);
                    if (drag != null)
                    {   // drop on the slot = equip (act on m_dragItem, then end the drag)
                        bool ok = _get == null ? ModSlots.Put(p, drag, _cx, _cy) : Fits(_name, drag.m_shared.m_itemType) && p.GetInventory().ContainsItem(drag) && p.EquipItem(drag, true);
                        if (ok) SetupDrag.Invoke(g, new object[] { null, null, 1 });
                        else p.Message(MessageHud.MessageType.Center, WhyNot(_name, drag), 0, drag.GetIcon());
                        return;
                    }
                    var cur = _get != null ? _get(p) : ModSlots.At(p, _cx, _cy);   // fresh: hidden pouch cells don't tick
                    if (cur == null) return;
                    if (_get == null) ModSlots.PickUp(p, _cx, _cy);   // lift it like a vanilla slot; drop it in the bag, a chest or anywhere
                    else if (p.GetInventory().ContainsItem(cur)) { SetupDrag.Invoke(g, new object[] { cur, p.GetInventory(), cur.m_stack }); Lifted = cur; }   // worn gear: picked up to drag (user); dropped in the bag it comes off
                }).Right = () =>
                {   // right click: take it off (our slots: into the bag)
                    var p = Player.m_localPlayer; if (p == null || !InventoryGui.IsVisible() || DragItem(InventoryGui.instance) != null) return;
                    var cur = _get != null ? _get(p) : ModSlots.At(p, _cx, _cy); if (cur == null) return;
                    if (_get == null && _cy == 0 && !ModSlots.Active(_cx)) ModSlots.Wear(p, _cx);   // one utility at a time: wear this one
                    else if (_get == null) ModSlots.Take(p, _cx, _cy); else p.UnequipItem(cur, true);
                };
                // empty slot: a faint icon of what goes there (design), the name in the tooltip
                string ic = name == "Head" ? "helm" : name == "Cape" ? "cape" : name == "Chest" ? "chest" : name == "Legs" ? "legs" : name == "Belt" ? "belt"
                          : name == "Trinket" ? "rune" : name == "Wishbone" ? "wishbone" : name == "Wisplight" ? "wisp" : name == "Ring" ? "rune" : name == "Key" ? "lock"
                          : name == "Main hand" ? "sword" : name == "Off hand" ? "shield" : "arrow";
                // design: a faint, greyed picture of what goes there; Valheim's own item icons where we have one
                var ghost = Ghost(name);
                _hint = ghost != null ? Kit.Img(rt, "Hint", ghost, new Color(0.55f, 0.55f, 0.6f, 0.3f), false) : Kit.IconImg(rt, ic, new Color(1f, 1f, 1f, 0.22f), size > 50f ? 28f : 22f);
                _hint.preserveAspect = true; if (ghost != null) _hint.rectTransform.sizeDelta = new Vector2(size > 50f ? 34f : 26f, size > 50f ? 34f : 26f);
                _hint.rectTransform.anchorMin = _hint.rectTransform.anchorMax = _hint.rectTransform.pivot = new Vector2(0.5f, 0.5f); _hint.rectTransform.anchoredPosition = Vector2.zero;
                _name = name; if (name == "Key") _hint.enabled = false;
                _icon = Kit.Img(rt, "Icon", null, Color.white, false); _icon.rectTransform.Fill(size > 50f ? 12f : 8f); _icon.preserveAspect = true; _icon.enabled = false;
                _rarity = new RarityBg(rt, _icon, line, size > 50f ? 9 : 8);
                var (dl, _) = Kit.Box(rt, "Durability", new Color(1f, 1f, 1f, 0.1f), new Color(1f, 1f, 1f, 0.1f), 2);
                _dur = dl.rectTransform.At(6, size - 7, size - 12, 3);
                _durFill = Kit.Panel(_dur, "Value", Theme.C(t.accent), 2); var r = _durFill.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
                _dur.gameObject.SetActive(false);
                _count = Kit.Text(rt, "Count", "", Fonts.Mono, size > 50f ? 11f : 10f, Theme.C(t.text)); _count.alignment = TextAlignmentOptions.BottomRight;
                _count.rectTransform.At(4, size - 20, size - 9, 16);
                _spare = Kit.Text(rt, "Spare", "", Fonts.Mono, size > 50f ? 9.5f : 8.5f, Theme.C(t.mute)); _spare.alignment = TextAlignmentOptions.TopRight;
                _spare.rectTransform.At(4, 3, size - 8, 13);   // the top of the slot (user)
                if (tooltipPrefab != null) { _tip = line.gameObject.AddComponent<UITooltip>(); _tip.m_tooltipPrefab = tooltipPrefab; ItemTip.For(_tip, () => _item, worn: true); }   // the detailed card for what is worn
            }

            internal string Name => _name; internal RectTransform Rect => _rt;
            internal ItemDrop.ItemData UnderPointer() => _item != null && _rt != null && _rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_rt, Input.mousePosition, null) ? _item : null;

            internal void Tick(Player p)
            {
                bool azu = (_get == null && _cy == 0 || System.Array.IndexOf(Azu.DollNames, _name) >= 0) && Azu.Drives;   // AzuEPI: its own equipment cells laid here instead of ours (it keeps gear in them worn or not)
                if (_rt.gameObject.activeSelf == azu) _rt.gameObject.SetActive(!azu);
                if (azu) return;
                var it = _get != null ? _get(p) : ModSlots.At(p, _cx, _cy);
                if (it != _item)
                {
                    _item = it;
                    _icon.sprite = it?.GetIcon(); _icon.enabled = it != null; _hint.enabled = it == null && _name != "Key"; _durPct = -1;
                    _rarity.Set(it);
                    if (_tip != null) _tip.Set(it == null ? _name : it.m_shared.m_name, it == null ? "Empty" : it.GetTooltip());
                }
                float a = _get == null && _cy == 0 && !ModSlots.Active(_cx) ? 0.35f : 1f;   // kept here but not worn (one utility at a time)
                if (_icon.color.a != a) _icon.color = new Color(1f, 1f, 1f, a);
                int n = it != null && it.m_shared.m_maxStackSize > 1 ? it.m_stack : 0, spare = 0;   // the stack in the slot, as on any slot; spares of the same ammo in the bag as a dim "+n" over it
                if (n > 0) foreach (var o in p.GetInventory().GetAllItems()) if (o != it && o.m_shared.m_name == it.m_shared.m_name && o.m_quality == it.m_quality) spare += o.m_stack;
                if (n != _shownCount) { _shownCount = n; _count.text = n > 0 ? n.ToString() : ""; }
                if (spare != _shownSpare) { _shownSpare = spare; _spare.text = spare > 0 ? "+" + spare : ""; }
                int d = it != null && it.m_shared.m_useDurability ? Mathf.RoundToInt(100f * Mathf.Clamp01(it.m_durability / Mathf.Max(1f, it.GetMaxDurability()))) : -2;
                if (d != _durPct)
                {
                    _durPct = d; _dur.gameObject.SetActive(d >= 0);
                    if (d >= 0) { _durFill.rectTransform.anchorMax = new Vector2(d / 100f, 1f); _durFill.color = Theme.C(d < 25 ? _t.ember : _t.accent); }
                }
            }
        }
    }

    // Valheim picks up and puts down on mouse PRESS (InventoryGrid.OnLeftDown), not on click: a click only fires
    // when the button is released over the same slot, so pressing a pouch key and letting go over the bag did
    // nothing. Our slots do the same as vanilla's: act on the left press.
    internal sealed class Press : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler,
        UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        private System.Action _on, _drag, _click;
        internal System.Action Right;   // right press
        private bool _dragging;
        private static readonly System.Reflection.MethodInfo Hovered = AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");
        private static readonly System.Reflection.MethodInfo Select = AccessTools.Method(typeof(InventoryGui), "OnSelectedItem");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> Carried = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ChestGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");

        // on: left press (vanilla's pick up / put down); drag: also pick up when a drag starts; click: press and release without moving
        internal static Press On(GameObject go, System.Action on, System.Action drag = null, System.Action click = null) { var p = go.AddComponent<Press>(); p._on = on; p._drag = drag; p._click = click; return p; }
        internal void Fire() => _on?.Invoke();
        internal void Click() => _click?.Invoke();
        internal void DragStart() => _drag?.Invoke();
        private static bool Left(UnityEngine.EventSystems.PointerEventData e) => e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { if (Left(e)) { _dragging = false; _on?.Invoke(); } else if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) Right?.Invoke(); }
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { if (Left(e) && !_dragging) _click?.Invoke(); }
        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e) { if (Left(e)) { _dragging = true; _drag?.Invoke(); } }
        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) { }

        // released after a drag: drop what is in hand where the pointer is, as a press there would
        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!Left(e)) return;
            var g = InventoryGui.instance; if (g == null || Carried(g) == null) return;
            var target = e.pointerCurrentRaycast.gameObject;
            for (var t = target != null ? target.transform : null; t != null; t = t.parent)
            {
                var other = t.GetComponent<Press>(); if (other != null) { if (other != this) other.Fire(); return; }
                if (t.GetComponent<InventoryElement>() != null) break;
            }
            foreach (var grid in new[] { g.m_playerGrid, ChestGrid(g) })
            {
                if (grid == null || !grid.isActiveAndEnabled) continue;
                if (!(Hovered.Invoke(grid, null) is InventoryElement el)) continue;
                var item = grid.GetInventory()?.GetItemAt(el.Position.x, el.Position.y);
                Select.Invoke(g, new object[] { grid, item, el.Position, InventoryGrid.Modifier.Select });
                return;
            }
        }
    }

    // worn gear dragged from its slot and dropped in the bag or a chest comes off (Valheim would keep a moved item worn)
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class EquipDropPatch
    {
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> Drag = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly System.Action<Inventory> Changed = inv => AccessTools.Method(typeof(Inventory), "Changed").Invoke(inv, new object[] { false, false });
        private static void Postfix(InventoryGui __instance, InventoryGrid grid, Vector2i pos)
        {
            var it = Equipment.Lifted; if (it == null || Drag(__instance) == it) return;   // still in hand
            Equipment.Lifted = null; var p = Player.m_localPlayer; var inv = p != null ? p.GetInventory() : null;
            // a move inside the bag puts a copy in the cell (Inventory.MoveItemToThis clones it), and Valheim re-equips the copy
            if (inv != null && !inv.ContainsItem(it)) it = inv.GetAllItems().FirstOrDefault(i => i.m_shared == it.m_shared && p.IsItemEquiped(i)) ?? it;
            // Valheim re-equips a moved item and EqRow.Park then puts it straight back in the hidden row, so its cell says nothing:
            // landing anywhere through a grid (bag or chest) means it comes off; back on its slot goes through Press, not here
            if (p != null && p.IsItemEquiped(it)) p.UnequipItem(it, true);
            // taking it off parks it in the first free cell (EqRow): it goes where it was dropped, if that cell is free
            if (inv != null && grid != null && grid.GetInventory() == inv && inv.ContainsItem(it) && !p.IsItemEquiped(it) && inv.GetItemAt(pos.x, pos.y) == null)
            { it.m_gridPos = pos; Changed(inv); }
            if (Dev.Build) Log.Info($"eqdrag: landed {it.m_shared.m_name} at {it.m_gridPos} equipped {p?.IsItemEquiped(it)}; drop {pos} on {grid?.name} mine {grid?.GetInventory() == inv} has {inv?.ContainsItem(it)} there {inv?.GetItemAt(pos.x, pos.y)?.m_shared.m_name ?? "nothing"}");
        }
        private static void Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            if (Dev.Build && Equipment.Lifted != null) Log.Info($"eqdrag: select {grid?.name} {pos} item {item?.m_shared.m_name} hand {Drag(__instance)?.m_shared.m_name}");
        }
    }

    // dev: who lets go of lifted worn gear
    [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
    internal static class EquipDragTrace
    {
        private static void Prefix(ItemDrop.ItemData item)
        {
            if (Dev.Build && Equipment.Lifted != null && item != Equipment.Lifted) Log.Info($"eqdrag: hand -> {item?.m_shared.m_name ?? "empty"}\n{System.Environment.StackTrace}");
        }
    }
}
