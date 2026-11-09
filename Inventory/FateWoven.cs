using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Fate Woven (processq): with Jatify UI it opens its panel where crafting sits, from a button it floats beside our panel.
    // We give it a tab under the column's tabs instead, calling its public FateWovenUi.Toggle (their suggestion, 2026-11-07),
    // and park its own button under our tab: once that button's parent changes, Fate Woven stops placing and showing it.
    // Soft: by reflection, nothing hard-referenced.
    internal static class FateWoven
    {
        private static bool? _on; private static Action _toggle; private static Func<bool> _open;
        private static Transform _iconOn; private static float _lookAt;
        private static GameObject _parked; private static Sprite _icon;

        internal static bool On { get { if (_on == null) Bind(); return _on.Value; } }

        private static void Bind()
        {
            _on = false;
            try { BindNow(); }
            catch (Exception e) { _on = false; _toggle = null; _open = null; Log.Warn("Fate Woven found, but its panel can't be opened from Jatify: " + e.Message); }
        }
        private static void BindNow()
        {
            var t = Type.GetType("ValheimWildcard.FateWovenUi, FateWoven");
            var m = t?.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (m == null || m.ReturnType != typeof(void)) return;
            _toggle = (Action)Delegate.CreateDelegate(typeof(Action), m);
            // lit while its panel is open: WildcardBoard.AnyOpen (public on an internal class); without it the tab just isn't lit
            var g = t.Assembly.GetType("ValheimWildcard.UI.WildcardBoard")?.GetProperty("AnyOpen", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
            if (g != null && g.ReturnType == typeof(bool)) _open = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), g);
            _on = true;
            Log.Info("Fate Woven found: its panel opens from a tab on the inventory's left edge" + (_open == null ? " (no open state: the tab isn't lit)" : ""));
        }

        internal static void Toggle()
        {
            try { _toggle?.Invoke(); } catch (Exception e) { Log.Warn("Fate Woven: Toggle failed: " + e.Message); }
        }

        // a bag's tab clicked: the bag wins the spot even when it was already open underneath
        internal static void Close() { if (Open) Toggle(); }

        internal static bool Open
        {
            get { try { return _open != null && _open(); } catch { _open = null; return false; } }
        }

        // its panel takes crafting's spot, where our chest panel also sits: the newest one shows. Opening the panel hides the chest
        // panel (the chest stays open; closing the panel shows it again), opening a chest (or a backpack) closes the panel
        private static readonly AccessTools.FieldRef<InventoryGui, Container> Cur = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> Grid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static object _shown; private static CanvasGroup _cg; private static bool _hid;
        internal static bool Covers => _hid;   // the chest panel is hidden under its panel
        // our inventory look going (a restyle, or the part switched off): the chest panel shows again
        internal static void Unhide()
        {
            if (_cg != null && _hid) { _cg.alpha = 1f; _cg.blocksRaycasts = true; _cg.interactable = true; }
            _hid = false; _shown = null;
        }
        internal static void Share(InventoryGui g)
        {
            if (g == null || g.m_container == null) return;
            bool up = g.m_container.gameObject.activeInHierarchy;
            object now = !up ? null : (object)Cur(g) ?? Grid(g)?.GetInventory();
            bool open = Open;
            if (now != null && now != _shown && open) { Toggle(); open = Open; }
            _shown = now;
            bool hide = up && open;
            if (_cg == null || _cg.transform != g.m_container)
            {   // only once the panel has shown: Valheim's UIGroupHandler takes the CanvasGroup it finds on its first activation and keeps it
                // uninteractable while the chest isn't the gamepad's group, so every chest button ignored clicks (user: Take all, Put all)
                if (!up) return;
                _cg = g.m_container.GetComponent<CanvasGroup>() ?? g.m_container.gameObject.AddComponent<CanvasGroup>(); _hid = !hide;   // a rebuilt panel: set it once
            }
            if (hide == _hid) return;
            _hid = hide;
            _cg.alpha = hide ? 0f : 1f; _cg.blocksRaycasts = !hide; _cg.interactable = !hide;
        }

        // its ability bar places itself over Valheim's stamina bar, which Jatify hides and which jumps while building or repairing:
        // the ability bar jumped with it (user). Jatify sets it instead, its lowest part (the level strip) Gap px over Jatify's
        // health bars and the held item's name, in every mode. Fate Woven places it in its Update; this runs after (LateUpdate)
        private const float Gap = 40f;
        private static RectTransform _bar; private static float _barLook, _partsAt, _moveAt, _heldY = float.NaN; private static bool _placed;
        private static System.Reflection.PropertyInfo _moveProp; private static System.Reflection.FieldInfo _offField; private static bool _moveLooked;
        // Fate Woven's HudBar.Move.Offset: off zero once the player moved the bar themselves (it then leaves out its own lift too)
        private static bool Placed()
        {
            try
            {
                if (!_moveLooked) { _moveLooked = true; var hb = Type.GetType("ValheimWildcard.UI.HudBar, FateWoven"); _moveProp = hb?.GetProperty("Move", BindingFlags.NonPublic | BindingFlags.Static); _offField = _moveProp?.PropertyType.GetField("Offset"); }
                var mv = _moveProp?.GetValue(null); return mv != null && _offField != null && ((Vector2)_offField.GetValue(mv)).sqrMagnitude >= 0.01f;
            }
            catch { _moveProp = null; return false; }
        }
        private static readonly System.Collections.Generic.List<RectTransform> _parts = new System.Collections.Generic.List<RectTransform>();
        private static readonly Vector3[] _c4 = new Vector3[4];
        internal static void HoldBar()
        {
            if (Player.m_localPlayer == null || Root.Hud == null || Root.Canvas == null || !On) return;
            if (_bar == null)
            {
                if (Time.unscaledTime < _barLook) return;
                _barLook = Time.unscaledTime + 1f;
                _bar = GameObject.Find("WildcardAbilityBar")?.transform as RectTransform; if (_bar == null) return;
                _partsAt = 0f; _heldY = float.NaN;
            }
            if (!_bar.gameObject.activeInHierarchy || _bar.parent == null) return;
            if (Time.unscaledTime >= _moveAt) { _moveAt = Time.unscaledTime + 0.25f; _placed = Placed(); }
            if (_placed) return;   // the player placed it with Fate Woven's own move mode: theirs
            if (Time.unscaledTime >= _partsAt) { _partsAt = Time.unscaledTime + 0.5f; _bar.GetComponentsInChildren(false, _parts); }   // its slots and level strip, read again twice a second
            float low = float.MaxValue, top = float.MinValue;
            foreach (var r in _parts) { if (r == null || r == _bar || !r.gameObject.activeInHierarchy || r.rect.height < 0.5f) continue; r.GetWorldCorners(_c4); low = Mathf.Min(low, _c4[0].y); }
            foreach (var r in Root.Hud.HealthParts) { if (r == null || !r.gameObject.activeInHierarchy) continue; r.GetWorldCorners(_c4); top = Mathf.Max(top, _c4[1].y); }
            // our health bars hidden (inventory, chest, a picked item): the height it had, else it jumped up over the inventory as it
            // faded out (user: "equipment slots" flashing behind the top and crafting panels)
            if (low == float.MaxValue || top == float.MinValue) { if (!float.IsNaN(_heldY) && _bar.anchoredPosition.y != _heldY) _bar.anchoredPosition = new Vector2(_bar.anchoredPosition.x, _heldY); return; }
            float dy = (top + Gap * Root.Canvas.transform.lossyScale.y - low) / Mathf.Max(0.0001f, _bar.parent.lossyScale.y);
            if (Mathf.Abs(dy) > 0.5f) _bar.anchoredPosition += new Vector2(0f, dy);
            _heldY = _bar.anchoredPosition.y;
        }

        // its floating button, hidden in a holder of ours on the inventory (it outlives our restyles: under the tab it went with
        // every colour change); once its parent changes, Fate Woven stops placing and showing it. Its icon becomes the tab's.
        // Looked for twice a second until found (it builds it the first time the inventory opens)
        internal static void Park(RectTransform tab)
        {
            if (tab == null || _iconOn == tab || Time.unscaledTime < _lookAt) return;
            _lookAt = Time.unscaledTime + 0.5f;
            if (_parked == null)
            {
                if (InventoryGui.instance == null) return;
                var hold = InventoryGui.instance.transform.Find("jatify-fatewoven-parked");   // parked already (a reload of ours): Find skips hidden objects
                var go = hold != null ? hold.Find("WildcardFateWovenButton")?.gameObject : GameObject.Find("WildcardFateWovenButton"); if (go == null) return;
                _icon = go.transform.Find("icon")?.GetComponent<Image>()?.sprite;
                if (hold == null) { var h = new GameObject("jatify-fatewoven-parked"); h.SetActive(false); h.transform.SetParent(InventoryGui.instance.transform, false); hold = h.transform; }
                go.transform.SetParent(hold, false); go.SetActive(false);
                _parked = go;
            }
            var mine = tab.Find("Clip/Tab/Icon")?.GetComponent<Image>();
            if (_icon != null && mine != null) { mine.sprite = _icon; mine.enabled = true; }
            _iconOn = tab;
        }
    }
}
