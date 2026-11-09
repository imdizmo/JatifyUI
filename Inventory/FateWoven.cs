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
        private static Transform _parkedIn; private static float _lookAt;

        internal static bool On { get { if (_on == null) Bind(); return _on.Value; } }

        private static void Bind()
        {
            _on = false;
            var t = Type.GetType("ValheimWildcard.FateWovenUi, FateWoven");
            var m = t?.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (m == null) return;
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
        internal static void Share(InventoryGui g)
        {
            if (g == null || g.m_container == null) return;
            bool up = g.m_container.gameObject.activeInHierarchy;
            object now = !up ? null : (object)Cur(g) ?? Grid(g)?.GetInventory();
            bool open = Open;
            if (now != null && now != _shown && open) { Toggle(); open = Open; }
            _shown = now;
            bool hide = up && open;
            if (_cg == null || _cg.transform != g.m_container) { _cg = g.m_container.GetComponent<CanvasGroup>() ?? g.m_container.gameObject.AddComponent<CanvasGroup>(); _hid = !hide; }   // a rebuilt panel: set it once
            if (hide == _hid) return;
            _hid = hide;
            _cg.alpha = hide ? 0f : 1f; _cg.blocksRaycasts = !hide; _cg.interactable = !hide;
        }

        // its floating button, moved under our tab and hidden; its icon becomes the tab's. Looked for twice a second until found
        // (it builds it the first time the inventory opens), and again when our tab is rebuilt (its button went with the old one)
        internal static void Park(RectTransform tab)
        {
            if (tab == null || _parkedIn == tab || Time.unscaledTime < _lookAt) return;
            _lookAt = Time.unscaledTime + 0.5f;
            var go = GameObject.Find("WildcardFateWovenButton"); if (go == null) return;
            var ic = go.transform.Find("icon")?.GetComponent<Image>();
            var mine = tab.Find("Clip/Tab/Icon")?.GetComponent<Image>();
            if (ic != null && ic.sprite != null && mine != null) { mine.sprite = ic.sprite; mine.enabled = true; }
            go.transform.SetParent(tab, false); go.SetActive(false);
            _parkedIn = tab;
        }
    }
}
