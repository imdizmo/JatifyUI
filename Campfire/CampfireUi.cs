using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.Campfire
{
    // Campfire's parts from the Jatify design (Start screen and Campfire's windows), built on JatifyLook:
    // Jatify UI's colours, fonts and rounded corners when it is installed, the design's own colours and Valheim's fonts
    // otherwise. Coordinates are top-left in pixels unless a function says otherwise; every canvas is 1920 x 1080.
    internal static class CampfireUi
    {
        internal const float RefW = 1920f, RefH = 1080f;
        internal static readonly Color NoColor = new Color(0f, 0f, 0f, 0f), White = UnityEngine.Color.white,
            Ink = new Color(0.024f, 0.082f, 0.051f),        // --accent-ink: text on green
            SegInk = new Color(0.067f, 0.067f, 0.067f),     // text on a white (picked) segment
            LineSoft = new Color(1f, 1f, 1f, 0.09f),        // --line: rows, fields
            Shade = new Color(0f, 0f, 0f, 0.75f);

        internal static Color C(string token) => JatifyLook.C(token);

        // A font to use when neither Jatify nor Valheim's fonts are loaded yet (the game's first loading screen).
        internal static TMP_FontAsset Fallback;

        // The frame Enter last submitted a field or closed a window: the screen behind must not take that Enter too.
        internal static int EnterUsed = -10;
        internal static bool EnterFresh => Time.frameCount - EnterUsed < 3;

        // ---- placing ---------------------------------------------------------------------------------------------------
        internal static RectTransform Node(Transform parent, string name) => JatifyLook.Node(parent, name);
        internal static RectTransform Place(RectTransform r, float x, float y, float w, float h) => JatifyLook.Place(r, x, y, w, h);
        internal static RectTransform Fill(RectTransform r, float inset = 0f) => JatifyLook.Fill(r, inset);
        internal static void Clear(Transform t) => JatifyLook.Clear(t);

        // hung from the parent's point (ax, ay: 0..1, y up) by the same point of the box; x / y move right / up
        internal static RectTransform At(RectTransform r, float ax, float ay, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(ax, ay); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        // a screen-space canvas scaled like the design (1080 units tall at any resolution)
        internal static RectTransform MakeCanvas(string name, int order, bool clicks = true)
        {
            var go = clicks ? new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH); scaler.matchWidthOrHeight = 1f;
            return Fill(Node(go.transform, name + "Root"));
        }

        // ---- pictures --------------------------------------------------------------------------------------------------
        internal static Image Img(Transform parent, Sprite s, Color c, string name = "Image") => JatifyLook.Img(parent, s, c, name);
        internal static Image Box(Transform parent, string name, Color fill, Color line, int radius) => JatifyLook.Box(parent, name, fill, line, radius);
        internal static Image Glass(Transform parent, string name) => Box(parent, name, C("glass"), C("glassLine"), 12);
        internal static void Rule(Transform parent, float x, float y, float w) { var r = Img(parent, null, LineSoft, "Rule"); Place(r.rectTransform, x, y, w, 1f); }

        // A gradient strip (left to right, or top to bottom), drawn once per name: the vignettes and the loading bar.
        private static readonly Dictionary<string, Sprite> _gradients = new Dictionary<string, Sprite>();
        internal static Sprite Gradient(string name, bool vertical, params (float at, Color c)[] stops)
        {
            if (_gradients.TryGetValue(name, out var s) && s != null) return s;
            const int n = 128;
            var tex = new Texture2D(vertical ? 1 : n, vertical ? n : 1, TextureFormat.RGBA32, false) { name = "campfire-" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++)
            {
                float k = i / (n - 1f); if (vertical) k = 1f - k;   // texture rows start at the bottom; stops read top down
                var c = stops[0].c;
                for (int j = 1; j < stops.Length; j++)
                    if (k <= stops[j].at) { c = UnityEngine.Color.Lerp(stops[j - 1].c, stops[j].c, Mathf.InverseLerp(stops[j - 1].at, stops[j].at, k)); break; }
                    else c = stops[j].c;
                if (vertical) tex.SetPixel(0, i, c); else tex.SetPixel(i, 0, c);
            }
            tex.Apply();
            return _gradients[name] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        // unloaded: the generated gradients and glyphs freed (a hot reload would otherwise keep each load's copies)
        internal static void Unload()
        {
            foreach (var g in _gradients.Values) if (g != null) { UnityEngine.Object.Destroy(g.texture); UnityEngine.Object.Destroy(g); }
            _gradients.Clear();
            Glyphs.Unload();
        }

        // The design's .cf-vig: darker at the sides and the bottom so the panels read over the fire.
        internal static void Vignette(Transform parent)
        {
            var dark = new Color(0.02f, 0.02f, 0.024f, 0.7f); var light = new Color(0.02f, 0.02f, 0.024f, 0.25f);
            Fill(Img(parent, Gradient("vig-h", false, (0f, dark), (0.28f, light), (0.72f, light), (1f, dark)), White, "Vignette").rectTransform);
            Fill(Img(parent, Gradient("vig-v", true, (0f, NoColor), (0.65f, NoColor), (1f, dark)), White, "VignetteLow").rectTransform);
        }

        // ---- text ------------------------------------------------------------------------------------------------------
        // fonts: "medium", "bold", "mono", "title" (Valheim's Norse: titles, names) or "lore" (Averia Serif, always italic)
        internal static TextMeshProUGUI Text(Transform parent, string s, string font, float size, Color c, float x, float y, float w, float h, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = JatifyLook.Text(parent, "", font, size, c, align); Place(t.rectTransform, x, y, w, h);
            if (t.font == null && Fallback != null) t.font = Fallback;
            if (font == "lore") t.fontStyle = FontStyles.Italic;
            if (font == "title") t.characterSpacing = 3f;
            Set(t, s); return t;
        }

        // text that wraps to its width, top aligned, never cut
        internal static TextMeshProUGUI Para(Transform parent, string s, string font, float size, Color c, float x, float y, float w, float h)
        {
            var t = Text(parent, s, font, size, c, x, y, w, h, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Overflow; return t;
        }

        // Valheim's Norse and some fallbacks lack a few characters the design uses: swap those for plain ones.
        private static readonly (char ch, string alt)[] Alts = { ('·', "-"), ('…', "..."), ('×', "x") };
        internal static void Set(TMP_Text t, string s)
        {
            if (!string.IsNullOrEmpty(s) && t.font != null)
                foreach (var (ch, alt) in Alts)
                    if (s.IndexOf(ch) >= 0 && !t.font.HasCharacter(ch, true, true)) s = s.Replace(ch.ToString(), alt);
            t.text = s ?? "";
        }

        // A soft drop shadow (the design's text-shadow): a dark copy just under it, drawn first. SetShadowed keeps both.
        internal static TextMeshProUGUI Shadowed(Transform parent, string s, string font, float size, Color c, float x, float y, float w, float h, TextAlignmentOptions align)
        {
            var sh = Text(parent, s, font, size, Shade, x + 1f, y + 2f, w, h, align); sh.name = "Shadow";
            var t = Text(parent, s, font, size, c, x, y, w, h, align); return t;
        }
        internal static void SetShadowed(TMP_Text t, string s)
        {
            Set(t, s);
            int i = t.transform.GetSiblingIndex();
            if (i > 0 && t.transform.parent.GetChild(i - 1).name == "Shadow") Set(t.transform.parent.GetChild(i - 1).GetComponent<TMP_Text>(), s);
        }

        internal static float Width(TMP_Text t) => JatifyLook.Width(t);
        internal static float Measure(string s, string font, float size)
        {
            var t = JatifyLook.Text(null, "", font, size, White); Set(t, s); float w = Width(t); UnityEngine.Object.Destroy(t.gameObject); return w;
        }

        // a section label in mono caps (the design's .label)
        internal static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w)
        {
            var t = Text(parent, text.ToUpperInvariant(), "mono", 11f, C("mute"), x, y, w, 18f); t.characterSpacing = 8f; return t;
        }

        // ---- input -----------------------------------------------------------------------------------------------------
        // clicks: left, double (left twice) and right
        internal sealed class Clicks : MonoBehaviour, IPointerClickHandler
        {
            public Action Left, Double, Right;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right) { Right?.Invoke(); return; }
                if (e.button != PointerEventData.InputButton.Left) return;
                if (e.clickCount >= 2 && Double != null) Double(); else Left?.Invoke();
            }
        }
        internal static Clicks On(Graphic g, Action left, Action dbl = null, Action right = null)
        {
            g.raycastTarget = true; var c = g.gameObject.AddComponent<Clicks>(); c.Left = left; c.Double = dbl; c.Right = right; return c;
        }

        // pointer over / out
        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Enter, Exit;
            private bool _inside;
            public void OnPointerEnter(PointerEventData e) { _inside = true; Enter?.Invoke(); }
            public void OnPointerExit(PointerEventData e) { _inside = false; Exit?.Invoke(); }
            private void OnDisable() { if (_inside) { _inside = false; Exit?.Invoke(); } }   // repainted away under the pointer
        }
        internal static Hover OnHover(Graphic g, Action enter, Action exit)
        {
            g.raycastTarget = true; var h = g.gameObject.AddComponent<Hover>(); h.Enter = enter; h.Exit = exit; return h;
        }

        // A hint by the pointer while it is over g (one per canvas, glass, wraps at 360).
        private static RectTransform _tipBox; private static TextMeshProUGUI _tipText;
        internal static void Tip(Graphic g, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            OnHover(g, () => ShowTip(g, text), () => { if (_tipBox != null) _tipBox.gameObject.SetActive(false); });
        }
        private static void ShowTip(Graphic g, string text)
        {
            var canvas = g.canvas != null ? g.canvas.rootCanvas : null; if (canvas == null) return;
            var croot = (RectTransform)canvas.transform;
            if (_tipBox == null || _tipBox.parent != croot)
            {
                if (_tipBox != null) UnityEngine.Object.Destroy(_tipBox.gameObject);
                var box = Glass(croot, "CampfireTip"); box.raycastTarget = false; _tipBox = box.rectTransform;
                _tipText = Para(_tipBox, "", "medium", 13f, C("dim"), 12f, 8f, 336f, 20f);
            }
            Set(_tipText, text);
            var size = _tipText.GetPreferredValues(_tipText.text, 336f, 0f);
            float w = Mathf.Min(336f, size.x) + 24f, h = size.y + 16f;
            _tipText.rectTransform.sizeDelta = new Vector2(w - 24f, h - 16f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(croot, Input.mousePosition, null, out var local);
            var half = croot.rect.size / 2f;
            var pos = local + new Vector2(14f, 14f);
            pos.x = Mathf.Min(pos.x, half.x - w - 8f); pos.y = Mathf.Min(pos.y, half.y - h - 8f);
            _tipBox.anchorMin = _tipBox.anchorMax = new Vector2(0.5f, 0.5f); _tipBox.pivot = Vector2.zero; _tipBox.anchoredPosition = pos; _tipBox.sizeDelta = new Vector2(w, h);
            _tipBox.gameObject.SetActive(true); _tipBox.SetAsLastSibling();
        }

        // the box's 1 px outline (Jatify only) or, without one, its fill: lit while the pointer is over it
        internal static void HoverLine(Image box, Color hot)
        {
            var line = box.transform.Find("Line")?.GetComponent<Image>();
            if (line != null) { var idle = line.color; OnHover(box, () => line.color = hot, () => line.color = idle); }
            else { var idle = box.color; OnHover(box, () => box.color = idle * 1.25f, () => box.color = idle); }
        }

        // A text field (search, password, names): a raised box, an icon when Jatify has one, a placeholder, a caret, a
        // green outline while typing. Enter calls submit.
        internal static TMP_InputField Field(Transform parent, string placeholder, float x, float y, float w, float h, string icon,
            Action<string> changed = null, Action submit = null, bool password = false, int limit = 0, float size = 14f)
        {
            var box = Box(parent, "Field", C("raised"), LineSoft, 9); Place(box.rectTransform, x, y, w, h);
            var glyph = icon != null ? JatifyLook.Icon(icon) : null; float lead = 12f;
            if (glyph != null) { var m = Img(box.transform, glyph, C("mute"), "Icon"); Place(m.rectTransform, 10f, (h - 18f) / 2f, 18f, 18f); lead = 36f; }
            var area = Node(box.transform, "Area"); Fill(area); area.offsetMin = new Vector2(lead, 2f); area.offsetMax = new Vector2(-10f, -2f); area.gameObject.AddComponent<RectMask2D>();
            var tx = JatifyLook.Text(area, "", "medium", size, White); Fill(tx.rectTransform); if (tx.font == null) tx.font = Fallback;
            var ph = JatifyLook.Text(area, placeholder, "medium", size, C("mute")); Fill(ph.rectTransform); if (ph.font == null) ph.font = Fallback;
            var f = box.gameObject.AddComponent<TMP_InputField>(); f.textViewport = area; f.textComponent = tx; f.placeholder = ph; f.fontAsset = tx.font; f.pointSize = size;
            f.lineType = TMP_InputField.LineType.SingleLine;
            if (password) { f.contentType = TMP_InputField.ContentType.Password; f.inputType = TMP_InputField.InputType.Password; }
            if (limit > 0) f.characterLimit = limit;
            f.customCaretColor = true; f.caretColor = White; f.caretWidth = 2; f.enabled = false; f.enabled = true;   // TMP builds the caret on enable
            if (changed != null) f.onValueChanged.AddListener(s => changed(s ?? ""));
            f.onSubmit.AddListener(_ => { EnterUsed = Time.frameCount; submit?.Invoke(); });
            box.gameObject.AddComponent<FocusLine>().Init(f, box);
            return f;
        }
        // a field under its label (the dialogs' .cf-field): 62 px tall
        internal static TMP_InputField LabelledField(Transform parent, string label, string placeholder, float y, float w, string icon = null, Action submit = null, bool password = false, int limit = 0)
        {
            Label(parent, label, 0f, y, w);
            return Field(parent, placeholder, 0f, y + 22f, w, 40f, icon, null, submit, password, limit, 15f);
        }
        internal static void Focus(TMP_InputField f) { if (f == null) return; f.Select(); f.ActivateInputField(); }

        private sealed class FocusLine : MonoBehaviour
        {
            private TMP_InputField _f; private Image _line, _fill; private bool _was;
            internal void Init(TMP_InputField f, Image fill) { _f = f; _fill = fill; _line = fill.transform.Find("Line")?.GetComponent<Image>(); Paint(false); }
            private void Update() { bool on = _f != null && _f.isFocused; if (on != _was) Paint(on); }
            private void Paint(bool on)
            {
                _was = on;
                if (_line != null) _line.color = on ? C("accent") : LineSoft;
                else _fill.color = on ? C("raised2") : C("raised");
            }
        }

        // ---- controls --------------------------------------------------------------------------------------------------
        // segmented buttons: the picked one white; w > 0 shares that width out evenly, else each fits its label
        internal static float Seg(Transform parent, float x, float y, float w, string[] labels, int on, Action<int> pick, float size = 13.5f)
        {
            var box = Box(parent, "Seg", C("raised"), LineSoft, 8); box.raycastTarget = false;
            float bx = 2f, each = w > 0f ? (w - 4f - 2f * (labels.Length - 1)) / labels.Length : 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i; bool sel = i == on;
                float bw = each > 0f ? each : Mathf.Max(32f, Measure(labels[i], "medium", size) + 20f);
                var b = Box(box.transform, "B", sel ? White : NoColor, NoColor, 6); Place(b.rectTransform, bx, 2f, bw, 26f);
                Fill(Text(b.transform, labels[i], "medium", size, sel ? SegInk : C("dim"), 0f, 0f, bw, 26f, TextAlignmentOptions.Center).rectTransform);
                if (!sel) On(b, () => pick(k));
                bx += bw + 2f;
            }
            Place(box.rectTransform, x, y, bx, 30f);
            return bx;
        }

        // an on / off switch (38 x 22)
        internal sealed class OnOff
        {
            private readonly Image _track, _knob, _line; private readonly Action<bool> _changed;
            private bool _on, _enabled = true;
            internal bool Value => _on;
            internal RectTransform Root => _track.rectTransform;
            internal OnOff(Transform parent, float x, float y, bool on, Action<bool> changed)
            {
                _changed = changed;
                _track = Box(parent, "OnOff", C("raised2"), C("line2"), 11); Place(_track.rectTransform, x, y, 38f, 22f);
                _line = _track.transform.Find("Line")?.GetComponent<Image>();
                _knob = Box(_track.transform, "Knob", C("dim"), NoColor, 8); _knob.raycastTarget = false;
                On(_track, Toggle);
                Set(on);
            }
            internal void Toggle() { if (!_enabled) return; Set(!_on); _changed?.Invoke(_on); }
            internal void Set(bool on)
            {
                _on = on;
                var a = _enabled ? 1f : 0.4f;
                _track.color = Alpha(on ? C("accent") : C("raised2"), a);
                if (_line != null) _line.color = Alpha(on ? C("accent") : C("line2"), a);
                _knob.color = Alpha(on ? Ink : C("dim"), a);
                Place(_knob.rectTransform, on ? 19f : 3f, 3f, 16f, 16f);
            }
            internal void SetEnabled(bool e) { _enabled = e; Set(_on); }
            private static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
        }
        // a switch with its label beside it (.cf-sw); the label toggles it too
        internal static OnOff SwitchRow(Transform parent, string label, float x, float y, bool on, Action<bool> changed, float w = 160f)
        {
            var sw = new OnOff(parent, x, y, on, changed);
            var t = Text(parent, label, "medium", 13f, C("dim"), x + 47f, y - 3f, w - 47f, 28f);
            On(t, sw.Toggle);
            return sw;
        }

        // A slider 0..1: a thin track, green up to the knob.
        internal static Slider SliderBar(Transform parent, float x, float y, float w, float value, Action<float> changed)
        {
            var root = Place(Node(parent, "Slider"), x, y, w, 20f);
            var hit = root.gameObject.AddComponent<Image>(); hit.color = NoColor;
            var track = Box(root, "Track", C("raised2"), NoColor, 3); track.raycastTarget = false; At(track.rectTransform, 0f, 0.5f, 0f, 0f, 0f, 6f); track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            var fillArea = Node(root, "FillArea"); At(fillArea, 0f, 0.5f, 0f, 0f, 0f, 6f); fillArea.anchorMax = new Vector2(1f, 0.5f);
            var fill = Box(fillArea, "Fill", C("accent"), NoColor, 3); fill.raycastTarget = false; fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Fill(Node(root, "HandleArea")); handleArea.offsetMin = new Vector2(8f, 0f); handleArea.offsetMax = new Vector2(-8f, 0f);
            var knob = Box(handleArea, "Knob", White, NoColor, 8); knob.rectTransform.sizeDelta = new Vector2(16f, -4f);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = knob.rectTransform; s.targetGraphic = knob; s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = 0f; s.maxValue = 1f; s.value = value;
            s.onValueChanged.AddListener(v => changed(v));
            return s;
        }

        // ---- buttons ---------------------------------------------------------------------------------------------------
        // The main action: green, or red for deletes. Disabled: raised, muted.
        internal static Image Primary(Transform parent, string label, float x, float y, float w, float h, Action a, bool enabled = true, bool danger = false, float size = 14f)
        {
            var fill = !enabled ? C("raised2") : danger ? C("health") : C("accent");
            var b = Box(parent, "Primary", fill, NoColor, 10); Place(b.rectTransform, x, y, w, h);
            Fill(Text(b.transform, label, "bold", size, !enabled ? C("mute") : danger ? White : Ink, 0f, 0f, w, h, TextAlignmentOptions.Center).rectTransform);
            if (enabled) { On(b, a); OnHover(b, () => b.color = fill * 1.12f, () => b.color = fill); }
            return b;
        }
        // The quieter button (.sbtn): raised, outlined, green outline on hover. A leading picture when given one.
        internal static Image Secondary(Transform parent, string label, float x, float y, float w, float h, Action a, bool enabled = true, float size = 13f, Sprite icon = null, float iconTurn = 0f)
        {
            var b = Box(parent, "Secondary", C("raised"), C("line2"), 8); Place(b.rectTransform, x, y, w, h);
            var c = enabled ? White : C("mute");
            if (icon != null && string.IsNullOrEmpty(label))
            {
                var i = Img(b.transform, icon, c, "Icon"); At(i.rectTransform, 0.5f, 0.5f, 0f, 0f, 14f, 14f); i.rectTransform.localEulerAngles = new Vector3(0f, 0f, iconTurn);
            }
            else if (icon != null)
            {
                float tw = Measure(label, "medium", size), total = 10f + 6f + tw, x0 = (w - total) / 2f;
                var i = Img(b.transform, icon, c, "Icon"); At(i.rectTransform, 0f, 0.5f, x0 + 5f, 0f, 10f, 10f); i.rectTransform.pivot = new Vector2(0.5f, 0.5f); i.rectTransform.localEulerAngles = new Vector3(0f, 0f, iconTurn);   // turned about its centre (Place pivots top left, so a turn swung it up out of line)
                Text(b.transform, label, "medium", size, c, x0 + 16f, 0f, tw + 4f, h);
            }
            else Fill(Text(b.transform, label, "medium", size, c, 0f, 0f, w, h, TextAlignmentOptions.Center).rectTransform);
            if (enabled) { On(b, a); HoverLine(b, C("accent")); }
            return b;
        }
        // a button's width for its label: the design's padding on both sides
        internal static float BtnW(string label, float size, float pad) => Mathf.Ceil(Measure(label, "medium", size)) + 2f * pad;

        // A row of quiet buttons (.cf-acts): the first, when flex, takes the room the others leave.
        internal static void Acts(Transform parent, float y, float w, bool flexFirst, params (string label, Action a, bool enabled)[] items)
        {
            const float gap = 6f, pad = 10f, size = 12.5f;
            var widths = new float[items.Length]; float fixedW = 0f;
            for (int i = 0; i < items.Length; i++) { widths[i] = BtnW(items[i].label, size, pad); if (i > 0 || !flexFirst) fixedW += widths[i]; }
            if (flexFirst) widths[0] = Mathf.Max(widths[0], w - fixedW - gap * (items.Length - 1));
            float x = 0f;
            var icons = new Sprite[items.Length];
            for (int i = 1; i < items.Length; i++) if ((icons[i] = JatifyLook.Icon(IconFor(items[i].label))) != null) widths[i] = 34f;   // Saves, Delete... as icons (user), their names as hints
            if (flexFirst) { fixedW = 0f; for (int i = 1; i < items.Length; i++) fixedW += widths[i]; widths[0] = Mathf.Max(BtnW(items[0].label, size, pad), w - fixedW - gap * (items.Length - 1)); }
            for (int i = 0; i < items.Length; i++) { if (i == 0) Secondary(parent, items[i].label, x, y, widths[i], 34f, items[i].a, items[i].enabled, size); else Quiet(parent, items[i].label, x, y, widths[i], 34f, items[i].a, items[i].enabled, size, icons[i]); x += widths[i] + gap; }
        }
        // ponytail: by label; only these read clearly as a picture. Without Jatify (no icons) they stay words.
        private static string IconFor(string label) => label == "Saves" ? "save" : label == "Delete" || label == "Remove" ? "trash_icon" : null;   // trash: Valheim's, as Jatify's inventory uses
        // the quietest button (.cf-acts after the first): just its label, dim; white on a raised fill while hovered
        internal static Image Quiet(Transform parent, string label, float x, float y, float w, float h, Action a, bool enabled = true, float size = 12.5f, Sprite icon = null)
        {
            var b = Box(parent, "Quiet", NoColor, NoColor, 8); Place(b.rectTransform, x, y, w, h);
            Graphic t;
            if (icon != null) { var im = Img(b.transform, icon, enabled ? C("dim") : C("mute"), "Icon"); im.preserveAspect = true; t = im; float s = icon.name.StartsWith("trash") ? 24f : 18f; At(t.rectTransform, 0.5f, 0.5f, 0f, 0f, s, s); Tip(b, label); }   // the trash a touch bigger (user)
            else { t = Text(b.transform, label, "medium", size, enabled ? C("dim") : C("mute"), 0f, 0f, w, h, TextAlignmentOptions.Center); Fill(t.rectTransform); }
            if (enabled) { On(b, a); OnHover(b, () => { b.color = C("raised"); t.color = White; }, () => { b.color = NoColor; t.color = C("dim"); }); }
            return b;
        }

        // ---- the shared window (.cf-dlg): a dark scrim, a glass panel, the Norse title, the body, buttons bottom right ----
        internal sealed class Window
        {
            private static readonly List<Window> _open = new List<Window>();
            internal static bool AnyOpen { get { _open.RemoveAll(w => w.Root == null || !w.Root.gameObject.activeSelf); return _open.Count > 0; } }
            internal const float Side = 24f, Top = 68f, Bottom = 72f;

            internal readonly RectTransform Root, Panel, Body, Foot;
            private readonly TextMeshProUGUI _title;
            private readonly Action _close;
            internal float W { get; private set; }
            internal float H { get; private set; }
            internal float BodyW => W - 2f * Side;
            internal float BodyH => H - Top - Bottom;
            internal bool Shown => Root != null && Root.gameObject.activeSelf;

            internal Window(Transform parent, string name, float w, float h, Action close)
            {
                _close = close;
                Root = Fill(Node(parent, name));
                var scrim = Img(Root, null, new Color(0.02f, 0.02f, 0.024f, 0.62f), "Scrim"); Fill(scrim.rectTransform); scrim.raycastTarget = true;
                Panel = Glass(Root, "Panel").rectTransform;
                _title = Text(Panel, "", "title", 28f, White, Side, 16f, w - 2f * Side, 46f);
                Body = Node(Panel, "Body"); Foot = Node(Panel, "Foot");
                SetSize(w, h);
                Root.gameObject.SetActive(false);
            }

            internal void SetSize(float w, float h)
            {
                W = w; H = h;
                At(Panel, 0.5f, 0.5f, 0f, 0f, w, h);
                Place(_title.rectTransform, Side, 16f, w - 2f * Side, 46f);
                Place(Body, Side, Top, BodyW, BodyH);
                Place(Foot, Side, h - 20f - 40f, BodyW, 40f);
            }

            internal void SetTitle(string s) => Set(_title, s);

            internal void Show(bool on)
            {
                if (Root == null) return;
                if (!on && Root.gameObject.activeSelf) EnterUsed = Time.frameCount;
                Root.gameObject.SetActive(on);
                if (on) { Root.SetAsLastSibling(); if (!_open.Contains(this)) _open.Add(this); }
                else _open.Remove(this);
            }

            // Cancel / Close (quiet) and the action (green, red when danger) bottom right; an extra quiet one bottom left.
            internal void Buttons(string cancel, string action = null, Action act = null, bool danger = false, string extra = null, Action onExtra = null)
            {
                Clear(Foot);
                float x = BodyW;
                if (action != null) { float aw = Mathf.Max(110f, BtnW(action, 14f, 22f)); x -= aw; Primary(Foot, action, x, 0f, aw, 40f, act, true, danger); x -= 8f; }
                float cw = Mathf.Max(96f, BtnW(cancel, 13f, 22f)); x -= cw;
                Secondary(Foot, cancel, x, 0f, cw, 40f, () => _close?.Invoke());
                if (extra != null) Secondary(Foot, extra, 0f, 0f, Mathf.Max(96f, BtnW(extra, 13f, 22f)), 40f, onExtra);
            }
        }
    }
}
