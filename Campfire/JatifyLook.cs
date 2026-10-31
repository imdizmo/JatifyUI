using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify
{
    // Jatify UI's look for Campfire: Jatify.UI.Look and Kit called directly (Campfire is a module of Jatify UI now; finding
    // Look by type name would also find the older copies a hot reload leaves behind). Before Jatify's fonts and theme are
    // there (the game's first loading screen) the design's colours and Valheim's fonts stand in.
    internal static class JatifyLook
    {
        internal static bool On => UI.Plugin.Instance != null;

        // the design's tokens (:root), used before Jatify's theme is loaded
        private static readonly Dictionary<string, string> Plain = new Dictionary<string, string>
        {
            ["glass"] = "#262323C5", ["glassLine"] = "#FFFFFF26", ["line2"] = "#FFFFFF33", ["raised"] = "#2A2B30F2", ["raised2"] = "#2C2C32E6",
            ["text"] = "#ECEEED", ["dim"] = "#B4B4BA", ["mute"] = "#80808A", ["accent"] = "#EC8219", ["accentSoft"] = "#EC821924",
            ["ember"] = "#F2703A", ["emberSoft"] = "#F2703A29", ["health"] = "#E5484D", ["stamina"] = "#F5B942", ["eitr"] = "#740DFF",
        };
        internal static Color C(string token)
        {
            var c = UI.Look.Color(token); if (c.a > 0f) return c;
            return Plain.TryGetValue(token, out var hex) && ColorUtility.TryParseHtmlString(hex, out var p) ? p : UnityEngine.Color.white;
        }
        internal static Color Color(string token, Color fallback) { var c = UI.Look.Color(token); return c.a > 0f ? c : fallback; }

        private static TMP_FontAsset _valheim;
        // "medium", "bold", "mono", "title" (panel titles) or "lore" (descriptions); Valheim's own fonts until Jatify's are loaded
        internal static TMP_FontAsset Font(string kind) => Ours(kind) ?? (kind == "title" ? Game("Valheim-Norsebold") : kind == "lore" ? Game("Valheim-AveriaSerifLibre") : null) ?? Valheim();
        private static TMP_FontAsset Ours(string kind) { var f = UI.Look.Font(kind); return f != null ? f : null; }   // a destroyed font (Jatify unloaded) counts as none
        private static TMP_FontAsset Game(string name) { foreach (var t in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) if (t.name == name) return t; return null; }
        internal static TMP_FontAsset Font(string kind, TMP_FontAsset fallback) => Ours(kind) ?? fallback;
        private static TMP_FontAsset Valheim()
        {
            if (_valheim != null) return _valheim;
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) if (t.name.StartsWith("Valheim-AveriaSansLibre")) return _valheim = t;
            return _valheim = TMP_Settings.defaultFontAsset;
        }
        internal static Sprite Rounded(int radius) => UI.Kit.Rounded(radius);
        internal static Sprite Ring(int radius) => UI.Kit.Ring(radius);
        internal static Sprite Icon(string name) => string.IsNullOrEmpty(name) ? null : UI.Kit.Icon(name);   // none asked (a button without one): none, as the reflection shim answered

        // ---- building blocks -------------------------------------------------------------------------------------------
        internal static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); if (parent != null) go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // top-left placement inside the parent, in pixels
        internal static RectTransform Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return r;
        }
        internal static RectTransform Fill(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f); r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        internal static Image Img(Transform parent, Sprite s, Color c, string name = "Image")
        {
            var i = Node(parent, name).gameObject.AddComponent<Image>(); i.sprite = s; i.color = c; i.raycastTarget = false; return i;
        }

        // a rounded fill with its 1 px outline (Jatify), a plain fill without; returns the fill (a click target)
        internal static Image Box(Transform parent, string name, Color fill, Color line, int radius)
        {
            var root = Node(parent, name); var ring = Ring(radius);
            var f = root.gameObject.AddComponent<Image>(); f.sprite = Rounded(radius); f.type = Image.Type.Sliced; f.color = fill; f.raycastTarget = true;
            if (ring != null) { var o = Img(root, ring, line, "Line"); o.type = Image.Type.Sliced; Fill(o.rectTransform); }
            return f;
        }

        internal static TextMeshProUGUI Text(Transform parent, string s, string font, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = Node(parent, "Text").gameObject; go.SetActive(false);   // its font set before it wakes: no "LiberationSans SDF not found" warning
            var t = go.AddComponent<TextMeshProUGUI>(); t.font = Font(font); go.SetActive(true); t.fontSize = size; t.color = c; t.text = s; t.alignment = align; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis; t.richText = true;
            return t;
        }
        internal static float Width(TMP_Text t) => t.GetPreferredValues(t.text).x;

        internal static Button Click(Graphic target, Action on)
        {
            var b = target.gameObject.AddComponent<Button>(); b.targetGraphic = target;
            var cb = b.colors; cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); cb.selectedColor = UnityEngine.Color.white; b.colors = cb;
            b.onClick.AddListener(() => on()); return b;
        }

        // a vertical list: returns its content (top anchored; set its height to what you put in)
        internal static RectTransform Scroll(Transform parent, string name, float x, float y, float w, float h)
        {
            var view = Place(Node(parent, name), x, y, w, h);
            view.gameObject.AddComponent<RectMask2D>(); var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f);
            var content = Node(view, "Content"); content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = content; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 1500f; sr.inertia = false;   // Jatify UI's (Kit.ScrollSpeed): Valheim's input module scrolls in tiny steps
            return content;
        }

        // a search field: a raised box, a magnifier when Jatify has one, a placeholder
        internal static TMP_InputField Field(Transform parent, string placeholder, float x, float y, float w, Action<string> changed)
        {
            var box = Box(parent, "Field", C("raised"), C("line2"), 8); Place(box.rectTransform, x, y, w, 34f);
            var mag = Icon("search"); float lead = 12f;
            if (mag != null) { var m = Img(box.transform, mag, C("mute"), "Search"); Place(m.rectTransform, 10f, 8.5f, 17f, 17f); lead = 34f; }
            var area = Node(box.transform, "Area"); Fill(area); area.offsetMin = new Vector2(lead, 4f); area.offsetMax = new Vector2(-10f, -4f); area.gameObject.AddComponent<RectMask2D>();
            var tx = Text(area, "", "medium", 14f, UnityEngine.Color.white); Fill(tx.rectTransform);
            var ph = Text(area, placeholder, "medium", 14f, C("mute")); Fill(ph.rectTransform);
            var f = box.gameObject.AddComponent<TMP_InputField>(); f.textViewport = area; f.textComponent = tx; f.placeholder = ph; f.fontAsset = tx.font; f.pointSize = 14f;
            f.customCaretColor = true; f.caretColor = UnityEngine.Color.white; f.caretWidth = 2; f.enabled = false; f.enabled = true;   // TMP builds the caret on enable
            f.onValueChanged.AddListener(s => changed(s ?? ""));
            return f;
        }

        // a thin progress bar
        internal static void Bar(Transform parent, float x, float y, float w, float h, float k, Color fill)
        {
            var track = Box(parent, "Bar", new Color(1f, 1f, 1f, 0.08f), new Color(0f, 0f, 0f, 0f), (int)Mathf.Max(2f, h / 2f)); track.raycastTarget = false; Place(track.rectTransform, x, y, w, h);
            if (k <= 0f) return;
            var f = Box(track.transform, "Fill", fill, new Color(0f, 0f, 0f, 0f), (int)Mathf.Max(2f, h / 2f)); f.raycastTarget = false;
            var r = f.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(Mathf.Clamp01(k), 1f); r.offsetMin = r.offsetMax = Vector2.zero;
        }

        internal static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject); }
    }
}
