using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // Damage numbers (detail pass, approved): Valheim's floating numbers in its Norse lettering with a soft shadow. White normally,
    // amber and larger on a weak spot, muted blue and smaller when resisted or immune, green heals, dim blocks; damage you take in
    // health red. They rise and fade in 0.8 s (Valheim: 1.5 s). Each text is restyled once, as Valheim makes it.
    [HarmonyPatch(typeof(DamageText), "AddInworldText")]
    internal static class DamageNumbers
    {
        internal static ConfigEntry<bool> On;
        internal static void Bind(ConfigFile c) => On = c.Bind("HUD", "DamageNumbers", true, "Damage numbers in Jatify's style: Norse lettering, amber for weak spots, blue when resisted, red for damage you take.");

        private static readonly AccessTools.FieldRef<DamageText, IList> Texts = AccessTools.FieldRefAccess<DamageText, IList>("m_worldTexts");
        private static FieldInfo _field, _duration, _gui;

        private static void Postfix(DamageText __instance, DamageText.TextType type, string text, bool mySelf)
        {
            if (On == null || !On.Value) return;
            var list = Texts(__instance); if (list == null || list.Count == 0) return;
            var inst = list[list.Count - 1];
            if (_field == null) { var it = inst.GetType(); _field = AccessTools.Field(it, "m_textField"); _duration = AccessTools.Field(it, "m_duration"); }
            if (!(_field?.GetValue(inst) is TMP_Text tx)) return;
            var t = Plugin.Instance.ThemeData;
            float size = Mathf.Max(tx.fontSize, 12f), k = 1.875f;   // Valheim: 16 near, 8 beyond 10 m (Bonus x1.5); x1.875 is the design's 30 px; far ones not below 3/4 of near
            Color c = Color.white;
            if (mySelf && type <= DamageText.TextType.Immune) c = text == "0" ? Theme.C(t.mute) : Theme.C(t.health);
            else switch (type)
            {
                case DamageText.TextType.Weak: c = Theme.C(t.stamina); k = 2.5f; break;   // design: 40 px
                case DamageText.TextType.Resistant: case DamageText.TextType.Immune: c = new Color32(0x9F, 0xB6, 0xC8, 0xFF); k = 1.375f; break;   // design: 22 px
                case DamageText.TextType.Heal: c = Theme.C(t.accent); break;
                case DamageText.TextType.Blocked: case DamageText.TextType.TooHard: c = Theme.C(t.dim); k = 1.5f; break;
                case DamageText.TextType.Bonus: c = Theme.C(t.ember); k = 1.875f; break;
            }
            tx.font = Fonts.Norse; var m = Fonts.Shadowed(Fonts.Norse); if (m != null) tx.fontSharedMaterial = m;
            tx.color = c; tx.enableAutoSizing = false; tx.fontSize = size * k;   // the template auto-sizes, which ignored the size
            if (type != DamageText.TextType.Bonus) _duration?.SetValue(inst, 0.8f);   // Valheim fades by the cube of the time, so most of it stays readable
        }

        // unloading: drop the numbers on screen (they hold our shadow material, destroyed with the fonts)
        internal static void Clear()
        {
            var d = DamageText.instance; if (d == null) return;
            var list = Texts(d); if (list == null || list.Count == 0) return;
            if (_gui == null) _gui = AccessTools.Field(list[0].GetType(), "m_gui");
            foreach (var inst in list) if (_gui?.GetValue(inst) is GameObject go && go != null) Object.Destroy(go);
            list.Clear();
        }
    }
}
