using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // The Jatify look for the user's own mods (MapTeleport, AdminHammer...), so they need no other UI mod.
    // Mods find this class at runtime (reflection, see MapTeleport's JatifyLook.cs) and fall back to Valheim's look without
    // Jatify, so they never compile against Jatify and survive its hot reloads. Keep it small; change it only by adding.
    public static class Look
    {
        public const int Version = 1;

        // the theme's colours by token name: glass, glassLine, line2, raised, raised2, text, dim, mute, accent, accentSoft,
        // ember, emberSoft, health, stamina, eitr; clear when unknown
        public static Color Color(string token)
        {
            var t = Plugin.Instance?.ThemeData; var f = t != null ? typeof(ThemeData).GetField(token) : null;
            return f != null && f.FieldType == typeof(string) ? Theme.C((string)f.GetValue(t)) : new Color(0f, 0f, 0f, 0f);
        }

        // "medium" (body), "bold" (buttons, names), "mono" (numbers, keys), "title" (panel titles: Valheim's Norse),
        // "lore" (descriptions: Valheim's Averia Serif)
        public static TMP_FontAsset Font(string kind) => kind == "bold" ? Fonts.Bold : kind == "mono" ? Fonts.Mono : kind == "title" ? Fonts.Norse : kind == "lore" ? Fonts.Lore : Fonts.Medium;

        // a rounded panel fill and its 1 px outline, 9-sliced (Image.Type.Sliced), radius in pixels
        public static Sprite Rounded(int radius) => Kit.Rounded(radius);
        public static Sprite Ring(int radius) => Kit.Ring(radius);

        // Jatify's icon set (white, tint with Image.color): search, lock, check, x, undo, hammer, chest, key, rune...; null when unknown
        public static Sprite Icon(string name) => Kit.Icon(name);
    }
}
