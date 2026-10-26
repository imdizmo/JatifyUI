using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Jatify.UI
{
    // While one of Jatify UI's text fields has focus, Valheim is told that chat has focus, so it ignores
    // game keys (E, Tab, 1-8...) on its own.
    [HarmonyPatch]
    internal static class Typing
    {
        internal static bool Active
        {
            get
            {
                var go = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (go == null) return false;
                var f = go.GetComponent<TMP_InputField>();
                return f != null && f.isFocused && Ours(go.transform);
            }
        }

        private static bool Ours(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith("jatify") || t.name == "ModsPage" || t.name == "JatifyPage") return true;
            return false;
        }

#if JATIFY_DEV
        private static bool DevPanelOpen => TestPanel.Open;
#else
        private const bool DevPanelOpen = false;
#endif

        [HarmonyPostfix, HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
        private static void ChatFocus(ref bool __result) { if (!__result && (Active || ConfigPage.Open != null || DevPanelOpen || SmelterCard.Open)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        private static void TextVisible(ref bool __result) { if (!__result && (Active || ConfigPage.Open != null || DevPanelOpen || SmelterCard.Open)) __result = true; }   // a settings page over the game: free cursor, no player input
    }
}
