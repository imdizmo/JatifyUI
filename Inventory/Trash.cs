using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Trash slot (design: under the grid, beside the item detail):
    // clicking it while dragging an item asks to confirm, then unequips the item if needed and removes
    // exactly the dragged amount. Nothing is removed without the confirm click.
    internal sealed class Trash
    {
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragInv = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");
        private static readonly AccessTools.FieldRef<InventoryGui, int> DragAmount = AccessTools.FieldRefAccess<InventoryGui, int>("m_dragAmount");
        private static readonly System.Reflection.MethodInfo SetupDrag = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");
        private readonly RectTransform _tile, _dialog; private readonly Image _icon;
        private readonly TextMeshProUGUI _ask;
        private readonly ThemeData _t;
        private Inventory _inv; private ItemDrop.ItemData _item; private int _amount;
        private readonly Image _tick;
        private readonly RectTransform _box, _body; private readonly TextMeshProUGUI _sub;

        // Knotwork on: the box grows by the knot's row and its content moves down under it
        private void Layout()
        {
            if (_box == null) { Knots.Changed -= Layout; return; }
            float k = Knots.On ? 26f : 0f;
            _box.sizeDelta = new Vector2(420f, 180f + k); _body.At(0, k, 420, 180);
            var al = k > 0f ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;   // the title and its line centred under the knot (design .dlg)
            if (_ask != null) _ask.alignment = al;
            if (_sub != null) _sub.alignment = al;
        }

        // ask, or (Don't ask again) destroy at once
        private void Ask()
        {
            if (!Cfg.ConfirmDelete.Value) { Confirm(); return; }
            _tick.enabled = false;
            _dialog.gameObject.SetActive(true);
        }

        internal Trash(RectTransform panel, ThemeData t)
        {
            _t = t;
            // design .trash: 60 px tile beside the item detail, Valheim's trash can, ember on hover
            var (line, fill) = Kit.Box(panel, "jatify-trash", Theme.C(t.glass), Theme.C(t.line2), 10);   // a lip of the panel: its glass, out past its edge
            _tile = line.rectTransform.At(754, 820, 60, 60);
            var ic = _icon = Kit.Img(_tile, "Icon", Kit.Icon("trash_icon"), new Color(1f, 1f, 1f, 0.7f), false); ic.rectTransform.At(17, 17, 26, 26); ic.preserveAspect = true;
            line.raycastTarget = true;
            // its own canvas one above the inventory's: the bag grid's root reaches under the tile and took every click and drop
            // (#15, still broken after the drag-release fix; seen with a pointer probe). Sibling order alone doesn't beat it.
            var tc = line.gameObject.AddComponent<Canvas>(); tc.overrideSorting = true; tc.sortingOrder = (panel.GetComponentInParent<Canvas>()?.rootCanvas.sortingOrder ?? 600) + 1; line.gameObject.AddComponent<GraphicRaycaster>();
            var b = line.gameObject.AddComponent<Button>(); b.targetGraphic = line; b.onClick.AddListener(OnClick);
            _hover = line.gameObject.AddComponent<Hover>();
            var cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = Theme.C(t.ember) * 3f; b.colors = cb;   // the outline turns ember on hover
            var tip = line.gameObject.AddComponent<UITooltip>(); var vt = InventoryGui.instance?.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>();
            if (vt != null) { tip.m_tooltipPrefab = vt.m_tooltipPrefab; tip.Set("Trash", "Drag an item here to destroy it."); }

            // confirm dialog, centred on the screen, on our own top canvas
            _dialog = Kit.Node(Root.Canvas.transform, "jatify-trash-confirm").Fill();
            var cv = _dialog.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 32001; _dialog.gameObject.AddComponent<GraphicRaycaster>();
            var dim = _dialog.gameObject.AddComponent<Image>(); dim.color = new Color(0, 0, 0, 0.45f);
            var (box, _) = Kit.Box(_dialog, "Box", Theme.C(t.glass), Theme.C(t.line2), 14);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = box.rectTransform.pivot = new Vector2(0.5f, 0.5f); box.rectTransform.sizeDelta = new Vector2(420f, 180f);
            _box = box.rectTransform; _body = Kit.Node(_box, "Body").At(0, 0, 420, 180);
            Knots.DialogTop(_box, 16f, Theme.C(t.accent));   // design .dlg .knot: one knot above the title
            _ask = Kit.Text(_body, "Ask", "", Fonts.Bold, 17f, Color.white); _ask.rectTransform.At(22, 20, 376, 26);
            var sub = _sub = Kit.Text(_body, "Sub", "This cannot be undone.", Fonts.Medium, 13f, Theme.C(t.dim)); sub.rectTransform.At(22, 50, 376, 18);
            // "Don't ask again": saved only when you press Destroy
            var (chk, _) = Kit.Box(_body, "DontAsk", Theme.C(t.raised), Theme.C(t.line2), 5); chk.rectTransform.At(22, 82, 18, 18);
            _tick = Kit.IconImg(chk.rectTransform, "check", Theme.C(t.accent), 14f); _tick.rectTransform.At(2, 2, 14, 14); _tick.enabled = false;
            var cl = Kit.Text(_body, "DontAskLabel", "Don't ask again", Fonts.Medium, 13f, Theme.C(t.dim)); cl.rectTransform.At(48, 80, 300, 22); cl.alignment = TextAlignmentOptions.MidlineLeft;
            var hit = Kit.Img(_body, "DontAskHit", null, new Color(0, 0, 0, 0)); hit.rectTransform.At(18, 78, 180, 26); hit.raycastTarget = true;
            hit.gameObject.AddComponent<Button>().onClick.AddListener(() => _tick.enabled = !_tick.enabled);
            Btn(_body, "Cancel", 22, Theme.C(t.raised), Color.white, () => Close());
            Btn(_body, "Destroy", 218, Theme.C(t.health), Color.white, Confirm);
            Knots.Changed += Layout; Layout();
            _dialog.gameObject.SetActive(false);
        }

        private static void Btn(RectTransform p, string label, float x, Color bg, Color fg, System.Action on)
        {
            var img = Kit.Panel(p, label, bg, 9); img.rectTransform.At(x, 120, 180, 40); img.raycastTarget = true;
            var tx = Kit.Text(img.rectTransform, "T", label, Fonts.Bold, 14f, fg); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
            img.gameObject.AddComponent<Button>().onClick.AddListener(() => on());
        }

        internal void OnClick()
        {
            var g = InventoryGui.instance; if (g == null) return;
            var item = DragItem(g); var inv = DragInv(g); int amount = DragAmount(g);
            if (item == null && _heldItem != null && _heldInv != null && _heldInv.ContainsItem(_heldItem)) { item = _heldItem; inv = _heldInv; amount = _heldAmount; }   // let go over the tile: Valheim may have dropped it from the cursor first
            if (item == null) return;
            _inv = inv; _item = item; _amount = Mathf.Clamp(amount, 1, item.m_stack);
            SetupDrag.Invoke(g, new object[] { null, null, 1 });                   // drop it from the cursor first
            _ask.text = $"Destroy {Localization.instance.Localize(item.m_shared.m_name)}{(_amount > 1 ? " ×" + _amount : "")}?";
            Ask();
        }

        private static readonly System.Reflection.MethodInfo Hovered = AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");

        // Del over an item: the same confirm as dropping it on the trash.
        internal void TickDelete()
        {
            var g = InventoryGui.instance;
            if (g == null || !InventoryGui.IsVisible() || Typing.Active || (_dialog && _dialog.gameObject.activeSelf) || !ZInput.GetKeyDown(KeyCode.Delete, true)) return;
            var el = Hovered.Invoke(g.m_playerGrid, null) as InventoryElement; if (el == null) return;
            var inv = g.m_playerGrid.GetInventory(); var item = inv.GetItemAt(el.Position.x, el.Position.y); if (item == null) return;
            _inv = inv; _item = item; _amount = item.m_stack;
            _ask.text = $"Destroy {Localization.instance.Localize(item.m_shared.m_name)}{(_amount > 1 ? " ×" + _amount : "")}?";
            Ask();
        }

        internal RectTransform Tile => _tile;
        internal void DevShow(bool on) { _inv = null; _item = null; _ask.text = "Destroy Wood ×10?"; _tick.enabled = false; _dialog.gameObject.SetActive(on); }   // dev: look only, nothing to destroy

        internal void Confirm()
        {
            if (_tick != null && _tick.enabled && _dialog.gameObject.activeSelf) Cfg.ConfirmDelete.Value = false;   // "Don't ask again" ticked
            var p = Player.m_localPlayer;
            if (p != null && _inv != null && _item != null && _inv.ContainsItem(_item))
            {
                if (_inv == p.GetInventory() && p.IsItemEquiped(_item)) p.UnequipItem(_item, false);
                int n = Mathf.Min(_amount, _item.m_stack);
                Log.Info($"trash: destroyed {_item.m_shared.m_name} x{n}" + (Cfg.ConfirmDelete.Value ? "" : " (no confirm)"));   // so a lost item can always be traced
                _inv.RemoveItem(_item, n);
            }
            Close();
        }

        private void Close() { _inv = null; _item = null; if (_dialog) _dialog.gameObject.SetActive(false); }

        internal void MoveTo(float x, float y) { if (_tile != null) { _tile.At(x, y, 52, 60); _tile.SetAsLastSibling(); if (_icon != null) _icon.rectTransform.At(15, 17, 26, 26); } }   // frame (design) pixels; on top, so no row built after it covers it

        internal void Place(InventoryGrid grid)
        {
            if (!InventoryGui.IsVisible() && _dialog && _dialog.gameObject.activeSelf) Close();
            TickRelease();
        }

        // Valheim also moves items by holding the button and letting go over the target: letting go over the trash counts as
        // dropping on it (#15: only a full click on the tile did). A press on the tile itself is its Button's click.
        private bool _downOnTile; private ItemDrop.ItemData _heldItem; private Inventory _heldInv; private int _heldAmount;
        private void TickRelease()
        {
            if (_tile == null || !InventoryGui.IsVisible() || (_dialog && _dialog.gameObject.activeSelf)) return;
            var g = InventoryGui.instance;
            if (ZInput.GetMouseButtonDown(0)) _downOnTile = Over();
            if (g != null && DragItem(g) != null) { _heldItem = DragItem(g); _heldInv = DragInv(g); _heldAmount = DragAmount(g); }   // what was on the cursor last, for a release
            else if (!ZInput.GetMouseButton(0) && !ZInput.GetMouseButtonUp(0)) _heldItem = null;
            if (!ZInput.GetMouseButtonUp(0) || _downOnTile || !Over()) return;
            if (g != null && (DragItem(g) != null || _heldItem != null)) OnClick();
            _heldItem = null;
        }
        // the pointer is on the tile itself, not on something drawn over it (the event system's enter / exit, as for any button)
        private bool Over() => _tile.gameObject.activeInHierarchy && _hover != null && _hover.On;
        private Hover _hover;
        private sealed class Hover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            internal bool On;
            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => On = true;
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => On = false;
            private void OnDisable() => On = false;
        }

        internal void Destroy() { if (_tile) Object.Destroy(_tile.gameObject); if (_dialog) Object.Destroy(_dialog.gameObject); }
    }
}
