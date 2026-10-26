using System.Collections.Generic;
using UnityEngine;

namespace Jatify.UI
{
    // Hides the vanilla HUD parts Jatify UI replaces, without deleting or disabling them: each gets a
    // CanvasGroup at alpha 0 that is re-applied every frame (Valheim's animators may touch them).
    // Restore() removes exactly what was added, so unloading the mod brings vanilla back.
    internal static class VanillaHud
    {
        private static readonly List<CanvasGroup> _added = new List<CanvasGroup>();
        private static Hud _for;

        internal static void Tick()
        {
            var h = Hud.instance;
            if (h == null) { _added.Clear(); _for = null; return; }
            if (_for != h) { Restore(); _for = h; Hide(h.m_healthPanel); Hide(h.m_staminaBar2Root); Hide(h.m_eitrBarRoot); Hide(h.m_statusEffectListRoot); Hide(h.m_gpRoot); Hide(h.GetComponentInChildren<HotkeyBar>(true)); Hide(h.m_saveIcon != null ? h.m_saveIcon.transform : null); Hide(h.m_hoverName != null ? h.m_hoverName.transform : null); Hide(h.m_badConnectionIcon != null ? h.m_badConnectionIcon.transform : null); Hide(h.m_eventBar != null ? h.m_eventBar.transform : null); Hide(h.m_mountPanel != null ? h.m_mountPanel.transform : null); var kh = Object.FindFirstObjectByType<KeyHints>(); if (kh != null) { Hide(kh.m_combatHints.transform); Hide(kh.m_inventoryHints.transform); Hide(kh.m_inventoryWithContainerHints.transform); Hide(kh.m_buildHints.transform); } }
            foreach (var g in _added) if (g != null) { g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false; }
        }

        private static void Hide(Component c)
        {
            if (c == null || c.GetComponent<CanvasGroup>() != null) return; // never take over a group the game owns
            _added.Add(c.gameObject.AddComponent<CanvasGroup>());
        }

        internal static void Restore()
        {
            foreach (var g in _added) if (g != null) Object.Destroy(g);
            _added.Clear(); _for = null;
        }
    }
}
