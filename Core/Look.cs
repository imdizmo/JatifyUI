using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // Jatify's small public API for other mods: the user's own (MapTeleport, AdminHammer...) and anyone else's. Mods find
    // this class at runtime (reflection, see MapTeleport's JatifyLook.cs) and fall back to their own look without Jatify,
    // so they never compile against Jatify and survive its updates. Keep it small; change it only by adding, never by
    // renaming or removing. No overloads: a reflection lookup by name must find exactly one method.
    // A callback that throws is logged with its id and dropped; it never breaks Jatify.
    public static class Look
    {
        // 1: Color, Font, Rounded, Ring, Icon. 2: header buttons, WindowRect + LayoutChanged, status bars, SetReserve,
        // ThemeChanged, Keys.
        public const int Version = 2;

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

        // ---- 2 -----------------------------------------------------------------------------------------------------

        // after the player's theme changed (theme.json or the settings page): read the colours again
        public static event Action ThemeChanged;

        // a window or bar below moved, opened, closed or changed size; checked every frame while anything listens
        public static event Action LayoutChanged;

        // A square icon button in the inventory header, in Jatify's style, after Jatify's own buttons; lower order first.
        // icon: your sprite, shown in its own colours. title and tooltip: the hover card. lit: optional (null), true while
        // your window is open (the button shows lit). The same id again replaces it. False when an argument is missing.
        public static bool AddHeaderButton(string id, Sprite icon, string title, string tooltip, Action onClick, int order, Func<bool> lit)
        {
            if (string.IsNullOrEmpty(id) || onClick == null) return false;
            Buttons.RemoveAll(b => b.Id == id);
            Buttons.Add(new HeadButton { Id = id, Icon = icon, Title = title ?? id, Tip = tooltip ?? "", Click = onClick, Order = order, Lit = lit });
            Buttons.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            InventoryStyle.ModButtonsChanged();
            return true;
        }
        public static void RemoveHeaderButton(string id) { if (Buttons.RemoveAll(b => b.Id == id) > 0) InventoryStyle.ModButtonsChanged(); }

        // Where Jatify draws a window now, in screen pixels (origin bottom left, like Input.mousePosition); Rect.zero while
        // it isn't shown. "inventory" (the panel with the tabs on its edges), "crafting" (the right-hand window: crafting
        // and Jatify's pages), "character" (the panel above it), "chest", "health" (the health, stamina, eitr and gauge
        // bars), "hotbar" (the 1-8 bar and the bars beside it).
        public static Rect WindowRect(string name) => Windows(name);

        // A bar in the HUD's health column (the row under stamina, which it shares with eitr and other gauges), shown
        // while max > 0. label: short caps text on the bar ("SHIELD"). value and max: read every frame.
        public static bool AddStatusBar(string id, string label, Color colour, Func<float> value, Func<float> max, int order)
        {
            if (string.IsNullOrEmpty(id) || value == null || max == null) return false;
            Bars.RemoveAll(b => b.Id == id);
            Bars.Add(new ModBar { Id = id, Label = (label ?? "").ToUpperInvariant(), Colour = colour, Value = value, Max = max, Order = order });
            Bars.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            return true;
        }
        public static void RemoveStatusBar(string id) => Bars.RemoveAll(b => b.Id == id);

        // A reserved part at the end of "health", "stamina" or "eitr": amount (in points, read every frame) of the bar's
        // maximum the player can't use, drawn darker. null clears it.
        public static void SetReserve(string bar, Func<float> amount)
        {
            if (bar != "health" && bar != "stamina" && bar != "eitr") return;
            if (amount == null) Reserves.Remove(bar); else Reserves[bar] = amount;
        }

        // Jatify's keys that are set, by setting name ("ToolSlot1", "QuickStack"...) to the key as text ("Z + LeftAlt"),
        // so you can warn about clashes. A new copy each call.
        public static Dictionary<string, string> Keys()
        {
            var d = new Dictionary<string, string>();
            var cfg = Plugin.Instance?.Config; if (cfg == null) return d;
            foreach (var kv in cfg)
            {
                var v = kv.Value.BoxedValue; string s = null;
                if (v is BepInEx.Configuration.KeyboardShortcut k && k.MainKey != KeyCode.None) s = k.ToString();
                else if (v is KeyCode c && c != KeyCode.None) s = c.ToString();
                if (s != null) d[kv.Key.Key] = s;
            }
            return d;
        }

        // ---- inside Jatify -------------------------------------------------------------------------------------------

        internal sealed class HeadButton { internal string Id, Title, Tip; internal Sprite Icon; internal Action Click; internal int Order; internal Func<bool> Lit; }
        internal sealed class ModBar { internal string Id, Label; internal Color Colour; internal Func<float> Value, Max; internal int Order; }
        internal static readonly List<HeadButton> Buttons = new List<HeadButton>();
        internal static readonly List<ModBar> Bars = new List<ModBar>();
        internal static readonly Dictionary<string, Func<float>> Reserves = new Dictionary<string, Func<float>>();

        // a mod's callback: its exception is logged once and the entry dropped
        internal static bool Try(string id, Action f, Action drop)
        {
            try { f(); return true; }
            catch (Exception e) { Log.Warn($"mod callback '{id}' failed and was switched off: {e.GetType().Name}: {e.Message}"); drop?.Invoke(); return false; }
        }
        // per-frame reads: no closures (the HUD allocates nothing per frame); failed: the caller drops the entry
        internal static float Read(string id, Func<float> f, out bool failed)
        {
            failed = false;
            try { var v = f(); return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v; }
            catch (Exception e) { Log.Warn($"mod callback '{id}' failed and was switched off: {e.GetType().Name}: {e.Message}"); failed = true; return 0f; }
        }
        internal static float Reserve(string bar)
        {
            if (Reserves.Count == 0 || !Reserves.TryGetValue(bar, out var f)) return 0f;
            var v = Read("reserve " + bar, f, out var failed); if (failed) Reserves.Remove(bar);
            return Mathf.Max(0f, v);
        }

        internal static void FireTheme() { if (ThemeChanged != null) Try("ThemeChanged", () => ThemeChanged(), () => ThemeChanged = null); }

        private static readonly string[] Names = { "inventory", "crafting", "character", "chest", "health", "hotbar" };
        private static readonly Rect[] _was = new Rect[Names.Length];
        internal static void TickLayout()
        {
            if (LayoutChanged == null) return;
            bool moved = false;
            for (int i = 0; i < Names.Length; i++) { var r = Windows(Names[i]); if (r != _was[i]) { _was[i] = r; moved = true; } }
            if (moved) Try("LayoutChanged", () => LayoutChanged(), () => LayoutChanged = null);
        }

        private static Rect Windows(string name)
        {
            switch (name)
            {
                case "inventory": return InventoryGui.IsVisible() ? ScreenRect(InventoryStyle.Frame, true) : Rect.zero;
                case "crafting": return InventoryGui.IsVisible() && ChestPanel.Open == null ? ScreenRect(CraftingStyle.Panel, false) : Rect.zero;
                case "character": return InventoryGui.IsVisible() ? ScreenRect(InventoryStyle.InfoPanel, false) : Rect.zero;
                case "chest": return ChestPanel.Open != null ? ScreenRect(ChestPanel.Frame, false) : Rect.zero;
                case "health": return Root.Hud != null ? Union(Root.Hud.HealthParts) : Rect.zero;
                case "hotbar": return Root.Hud != null ? Union(Root.Hud.HotbarParts) : Rect.zero;
                default: return Rect.zero;
            }
        }

        private static readonly Vector3[] _c = new Vector3[4];
        private static readonly List<RectTransform> _parts = new List<RectTransform>();
        private static Rect ScreenRect(RectTransform rt, bool children)
        {
            if (rt == null) return Rect.zero;
            _parts.Clear(); _parts.Add(rt);
            if (children) foreach (Transform c in rt) if (c is RectTransform r) _parts.Add(r);
            return Union(_parts);
        }
        private static Rect Union(List<RectTransform> parts)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var rt in parts)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                var canvas = rt.GetComponentInParent<Canvas>(); if (canvas == null) continue;
                var cam = canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                rt.GetWorldCorners(_c);
                foreach (var w in _c)
                {
                    var p = RectTransformUtility.WorldToScreenPoint(cam, w);
                    x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
                }
            }
            return x0 == float.MaxValue ? Rect.zero : Rect.MinMaxRect(x0, y0, x1, y1);
        }
    }
}
