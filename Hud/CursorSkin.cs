using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The Jatify cursor (user's images, tip on the top-left pixel): the OS cursor, set with
    // Cursor.SetCursor. A second image while the pointer is over something you can
    // click (a button, a slot, a toggle). Settings: JatifyCursor on/off, CursorSize 32 / 48.
    internal static class CursorSkin
    {
        private static bool _on, _hover; private static int _size = -1; private static int _frame;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private static PointerEventData _ped;

        internal static void Tick()
        {
            bool want = Cfg.JatifyCursor.Value;
            if (!want) { if (_on) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); _on = false; } return; }
            int size = Cfg.CursorSize.Value;
            bool hover = _hover;
            if (Cursor.visible && ++_frame % 4 == 0) hover = OverClickable();   // a UI raycast every 4th frame is plenty
            if (_on && size == _size && hover == _hover) return;
            var sp = Kit.Icon((hover ? "cursor-hover-" : "cursor-") + size);
            if (sp == null) return;
            Cursor.SetCursor(sp.texture, Vector2.zero, CursorMode.Auto);
            _on = true; _size = size; _hover = hover;
        }

        private static bool OverClickable()
        {
            var es = EventSystem.current; if (es == null) return false;
            if (_ped == null) _ped = new PointerEventData(es);
            _ped.position = ZInput.pointerPosition; _hits.Clear();   // Valheim's input, not the legacy one
            es.RaycastAll(_ped, _hits);
            if (_hits.Count == 0) return false;
            for (var t = _hits[0].gameObject.transform; t != null; t = t.parent)
            {
                var sel = t.GetComponent<Selectable>(); if (sel != null) return sel.IsInteractable();
                if (t.GetComponent<UIInputHandler>() != null) return true;   // Valheim's inventory slots
            }
            return false;
        }

        internal static void Restore() { if (_on) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); _on = false; _size = -1; }
    }
}
