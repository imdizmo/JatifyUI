using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The Colours section's custom colour (user: "a colour picker with some preview"): a saturation / brightness square, a hue
    // strip (and an alpha strip for the panel colour), Current and Original swatches, R G B / H S V fields (0-1) and a hex
    // field, beside a sample of the UI drawn with the colour being picked. Apply writes it to theme.json; the UI restyles.
    internal sealed class ColourPicker
    {
        internal static ColourPicker Open;

        private readonly RectTransform _root;
        private readonly ThemeData _t; private readonly string _field; private readonly bool _alpha;
        private readonly Color _orig, _default; private readonly Action<string> _apply;
        private float _h, _s, _v, _a;
        private Texture2D _svTex; private float _svHue = -1f;
        private readonly Image _sv, _svMark, _hueMark, _alphaFill, _alphaMark, _cur;
        private readonly Dictionary<string, TMP_InputField> _fields = new Dictionary<string, TMP_InputField>();
        private readonly List<Graphic> _sample = new List<Graphic>();   // the sample's parts drawn in the picked colour
        private Graphic _accentGlow;
        private bool _writing;
        private readonly Image _dim; private readonly RectTransform _panel; private readonly TMP_Text _title;
        private bool _preview, _kept, _dirty; private float _liveAt;

        // field: ThemeData's field; alpha: panel colours carry it
        // dim: how dark the screen goes behind (edit mode keeps the real UI in view)
        internal static void Show(Transform parent, ThemeData t, string field, string label, bool alpha, Action<string> apply, float dim = 0.55f)
        {
            Open?.Close();
            Open = new ColourPicker(parent, t, field, label, alpha, apply, dim);
        }

        private ColourPicker(Transform parent, ThemeData t, string field, string label, bool alpha, Action<string> apply, float dimA)
        {
            _t = t; _field = field; _alpha = alpha; _apply = apply;
            _orig = Theme.C((string)typeof(ThemeData).GetField(field).GetValue(t));
            _default = Theme.C((string)typeof(ThemeData).GetField(field).GetValue(new ThemeData()));
            _root = Kit.Node(parent, "ColourPicker").Fill();
            var dim = _dim = Kit.Img(_root, "Dim", null, new Color(0f, 0f, 0f, dimA)); dim.rectTransform.Fill(); dim.raycastTarget = true;   // clicks stay in the picker
            var solid = Theme.C(t.glass); solid.a = 1f;   // the settings page behind showed through the see-through panel colour
            var (panel, _) = Kit.Box(_root, "Panel", solid, Theme.C(t.line2), 14); var p = _panel = panel.rectTransform; p.At(630, 300, 660, 424); panel.raycastTarget = true;
            var title = _title = Kit.Text(p, "Title", "COLOUR · " + label.ToUpperInvariant(), Fonts.Mono, 12f, Theme.C(t.mute)); title.characterSpacing = 6f; title.rectTransform.At(20, 14, 600, 18);

            // saturation (x) / brightness (y) for the hue
            _sv = Kit.Img(p, "SV", null, Color.white, false); _sv.rectTransform.At(20, 44, 240, 240); _sv.raycastTarget = true;
            _sv.gameObject.AddComponent<DragArea>().On = uv => { _s = uv.x; _v = uv.y; Changed(); };
            _svMark = Kit.Img(_sv.rectTransform, "Mark", Kit.Ring(6), Color.white, false); _svMark.type = Image.Type.Sliced; Pin(_svMark.rectTransform, 12, 12);
            // hue, top to bottom like the reference (red at both ends)
            var hue = Kit.Img(p, "Hue", Sprite.Create(HueTex(), new Rect(0, 0, 1, 128), new Vector2(0.5f, 0.5f)), Color.white, false); hue.rectTransform.At(270, 44, 20, 240); hue.raycastTarget = true;
            hue.gameObject.AddComponent<DragArea>().On = uv => { _h = uv.y; Changed(); };
            _hueMark = Kit.Img(hue.rectTransform, "Mark", null, Color.white, false); Pin(_hueMark.rectTransform, 26, 3);
            float x = 300f;
            if (alpha)
            {   // opacity: the colour from clear (bottom) to solid (top) over a dark well
                var well = Kit.Panel(p, "AlphaWell", new Color(0.05f, 0.05f, 0.06f, 1f), 3); well.rectTransform.At(300, 44, 20, 240);
                _alphaFill = Kit.Img(well.rectTransform, "Fill", Sprite.Create(AlphaTex(), new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f)), Color.white, false); _alphaFill.rectTransform.Fill(); _alphaFill.raycastTarget = true;
                _alphaFill.gameObject.AddComponent<DragArea>().On = uv => { _a = uv.y; Changed(); };
                _alphaMark = Kit.Img(well.rectTransform, "Mark", null, Color.white, false); Pin(_alphaMark.rectTransform, 26, 3);
                x = 330f;
            }
            // Current over Original (a click on Original takes it back)
            Label(p, "Current", x + 6, 44); _cur = Swatch(p, x + 6, 62, Color.white);
            Label(p, "Original", x + 6, 116); var orig = Swatch(p, x + 6, 134, _orig);
            orig.raycastTarget = true; orig.gameObject.AddComponent<Button>().onClick.AddListener(() => Set(_orig));
            if (Player.m_localPlayer != null) Button(p, "Preview", x + 6, 196, 120, false, Preview);   // in game: the real UI in this colour
            // the sample: this colour where the UI uses it, next to the others as they are
            Sample(p, x + 136, 44, 640 - (x + 136));

            // R G B / H S V (0-1), A for panel colours, the hex
            string[] row1 = { "R", "G", "B" }, row2 = { "H", "S", "V" };
            for (int i = 0; i < 3; i++) { Field(p, row1[i], 20 + i * 102, 300, 96); Field(p, row2[i], 20 + i * 102, 336, 96); }
            if (alpha) Field(p, "A", 326, 300, 96);
            Field(p, "#", 20, 372, 300);
            // buttons
            Button(p, "Default", 336, 372, 92, false, () => Set(_default));
            Button(p, "Cancel", 434, 372, 92, false, Close);
            Button(p, "Apply", 532, 372, 108, true, () => { _kept = true; if (_preview) Plugin.Instance.SetColour(_field, Hex(Picked())); else _apply(Hex(Picked())); Close(); });
            Set(_orig);
        }

        internal void Close()
        {
            if (_preview && !_kept) Plugin.Instance.RevertColours();   // previewed, not applied: the saved colours come back
            _preview = false;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            if (_svTex != null) UnityEngine.Object.Destroy(_svTex);
            if (Open == this) Open = null;
        }

        private Color Picked() { var c = UnityEngine.Color.HSVToRGB(_h, _s, _v); c.a = _alpha ? _a : 1f; return c; }
        private string Hex(Color c) => "#" + (_alpha ? ColorUtility.ToHtmlStringRGBA(c) : ColorUtility.ToHtmlStringRGB(c));

        private void Set(Color c) { UnityEngine.Color.RGBToHSV(c, out _h, out _s, out _v); _a = c.a; Changed(); }

        // preview: the settings page and the menu go, the inventory opens, the picker moves aside (drag it by any free spot) and
        // every change restyles the real UI without saving. Apply saves; Cancel, or the inventory closing, puts it back
        private void Preview()
        {
            if (_preview || Player.m_localPlayer == null || InventoryGui.instance == null) return;
            _preview = true; _dirty = true;
            _root.SetParent(Root.Canvas.transform, false);
            var cv = _root.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 32500;   // over the inventory's panels
            _root.gameObject.AddComponent<GraphicRaycaster>();
            _dim.enabled = false; _dim.raycastTarget = false;
            _panel.At(1920f - 660f - 40f, 90f, 660f, 424f);
            _panel.gameObject.AddComponent<Mover>();
            _title.text += "  ·  PREVIEW";
            var me = Open; Open = null; ConfigPage.Open?.Close(); Open = me;   // the page closes its picker: not this one
            if (Menu.instance != null && Menu.IsVisible()) Menu.instance.Hide();
            if (!InventoryGui.IsVisible()) InventoryGui.instance.Show(null);
            _root.gameObject.AddComponent<Ticker>().Tick = Live;
        }

        private void Live()
        {
            if (InventoryGui.instance == null || !InventoryGui.IsVisible()) { Close(); return; }   // Esc or Tab: back as it was
            if (!_dirty || Time.unscaledTime < _liveAt) return;
            _dirty = false; _liveAt = Time.unscaledTime + 0.1f;   // the whole UI restyles: ten times a second at most while dragging
            Plugin.Instance.PreviewColour(_field, Hex(Picked()));
        }

        private sealed class Ticker : MonoBehaviour { internal Action Tick; private void Update() => Tick?.Invoke(); }
        private sealed class Mover : MonoBehaviour, IDragHandler
        {
            public void OnDrag(PointerEventData e) { var c = GetComponentInParent<Canvas>(); ((RectTransform)transform).anchoredPosition += e.delta / (c != null ? c.scaleFactor : 1f); }
        }

        private void Changed()
        {
            _dirty = true;
            var c = Picked();
            if (_svHue != _h) { _svHue = _h; SvTex(); }
            Place(_svMark.rectTransform, _s, _v);
            Place(_hueMark.rectTransform, 0.5f, _h);
            if (_alpha) { var o = c; o.a = 1f; _alphaFill.color = o; Place(_alphaMark.rectTransform, 0.5f, _a); }
            _cur.color = c;
            foreach (var g in _sample) if (g != null) g.color = c;
            if (_field == "accent" && _accentGlow != null) _accentGlow.color = new Color(c.r, c.g, c.b, 0x24 / 255f);
            if (_writing) return;
            _writing = true;   // the fields show the colour; one being typed in keeps its text
            Show("R", c.r); Show("G", c.g); Show("B", c.b); Show("H", _h); Show("S", _s); Show("V", _v); if (_alpha) Show("A", _a);
            if (_fields.TryGetValue("#", out var hx) && !hx.isFocused) hx.text = Hex(c);
            _writing = false;
        }

        private void Show(string k, float v) { if (_fields.TryGetValue(k, out var f) && !f.isFocused) f.text = v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }

        // a typed value: the colour follows
        private void Typed(string k, string s)
        {
            if (_writing) return;
            if (k == "#") { if (ColorUtility.TryParseHtmlString(s.StartsWith("#") ? s : "#" + s, out var hc)) { if (!_alpha) hc.a = 1f; Set(hc); } return; }
            if (!float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return;
            v = Mathf.Clamp01(v); var c = Picked();
            switch (k)
            {
                case "R": c.r = v; Set(c); break; case "G": c.g = v; Set(c); break; case "B": c.b = v; Set(c); break;
                case "H": _h = v; Changed(); break; case "S": _s = v; Changed(); break; case "V": _v = v; Changed(); break; case "A": _a = v; Changed(); break;
            }
        }

        // ---- parts -------------------------------------------------------------------------------------------
        private void Field(RectTransform p, string k, float x, float y, float w)
        {
            var (b, _) = Kit.Box(p, "F" + k, Theme.C(_t.raised), Theme.C(_t.line2), 7); b.rectTransform.At(x, y, w, 30);
            var lab = Kit.Text(b.rectTransform, "K", k == "#" ? "" : k + ":", Fonts.Mono, 12f, Theme.C(_t.mute)); lab.rectTransform.At(8, 0, 22, 30); lab.alignment = TextAlignmentOptions.MidlineLeft;
            var area = Kit.Node(b.rectTransform, "Area").Fill(); area.offsetMin = new Vector2(k == "#" ? 10 : 28, 4); area.offsetMax = new Vector2(-8, -4); area.gameObject.AddComponent<RectMask2D>();
            var tx = Kit.Text(area, "Text", "", Fonts.Mono, 12.5f, UnityEngine.Color.white); tx.rectTransform.Fill(); tx.alignment = TextAlignmentOptions.MidlineLeft;
            var f = b.gameObject.AddComponent<TMP_InputField>(); f.textViewport = area; f.textComponent = tx; f.fontAsset = Fonts.Mono; f.pointSize = 12.5f; Kit.Caret(f);
            f.onEndEdit.AddListener(s => Typed(k, s)); f.onSubmit.AddListener(s => Typed(k, s));
            _fields[k] = f;
        }

        private void Label(RectTransform p, string text, float x, float y) { var l = Kit.Text(p, text, text, Fonts.Medium, 12.5f, Theme.C(_t.dim)); l.rectTransform.At(x, y, 120, 16); }

        private Image Swatch(RectTransform p, float x, float y, Color c)
        {
            var (b, _) = Kit.Box(p, "Swatch", new Color(0.05f, 0.05f, 0.06f, 1f), Theme.C(_t.line2), 7); b.rectTransform.At(x, y, 120, 44);
            var s = Kit.Panel(b.rectTransform, "C", c, 6); s.rectTransform.Fill(2f); return s;
        }

        private void Button(RectTransform p, string text, float x, float y, float w, bool primary, Action on)
        {
            var (b, f) = Kit.Box(p, text, primary ? Theme.C(_t.accent) : Theme.C(_t.raised), primary ? Theme.C(_t.accent) : Theme.C(_t.line2), 8); b.rectTransform.At(x, y, w, 32);
            var tx = Kit.Text(b.rectTransform, "T", text, Fonts.Bold, 13.5f, primary ? new Color(0.02f, 0.08f, 0.05f) : Theme.C(_t.text)); tx.alignment = TextAlignmentOptions.Center; tx.rectTransform.Fill();
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<UnityEngine.UI.Button>(); btn.targetGraphic = f; btn.onClick.AddListener(() => on());
        }

        // a slice of the UI, every part in the theme's colour except the one being picked, which follows the picker
        private void Sample(RectTransform p, float x, float y, float w)
        {
            Color C(string field) => Theme.C((string)typeof(ThemeData).GetField(field).GetValue(_t));
            T Mine<T>(T g, string field) where T : Graphic { if (field == _field) _sample.Add(g); return g; }
            var (line, fill) = Kit.Box(p, "Sample", C("glass"), C("glassLine"), 10); line.rectTransform.At(x, y, w, 312);
            Mine(fill, "glass"); Mine(line, "glassLine");
            var r = line.rectTransform; float yy = 12f;
            TextMeshProUGUI Txt(string s, TMP_FontAsset font, float size, string field, float h) { var t = Mine(Kit.Text(r, field, s, font, size, C(field)), field); t.rectTransform.At(12, yy, w - 24, h + 6f); t.overflowMode = TextOverflowModes.Ellipsis; yy += h + 4f; return t; }   // a box shorter than the font's line hides it
            Txt("Stone Axe", Fonts.Bold, 16f, "text", 20);
            var d = Txt("A crude axe for tree-felling.", Fonts.Lore, 13f, "dim", 18); d.fontStyle = FontStyles.Italic;
            var lb = Txt("DAMAGE & DEFENCE", Fonts.Mono, 10.5f, "mute", 14); lb.characterSpacing = 6f;
            Txt("Missing: Wood ×5", Fonts.Medium, 12.5f, "warnText", 17);
            Txt("Ready to craft", Fonts.Medium, 12.5f, "accent", 17);
            Txt("Low durability", Fonts.Medium, 12.5f, "ember", 17);
            yy += 4f;
            foreach (var bar in new[] { "health", "stamina", "eitr" })
            {   // a bar each, on its track
                var track = Kit.Panel(r, bar + "Track", new Color(0f, 0f, 0f, 0.45f), 4); track.rectTransform.At(12, yy, w - 24, 8);
                var fl = Mine(Kit.Panel(track.rectTransform, bar, C(bar), 4), bar); var fr = fl.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0.7f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
                yy += 14f;
            }
            yy += 4f;
            var (slot, sf) = Kit.Box(r, "Slot", C("raised"), C("line2"), 8); slot.rectTransform.At(12, yy, 36, 36); Mine(sf, "raised"); Mine(slot, "line2");
            var (slot2, sf2) = Kit.Box(r, "Slot2", C("raised2"), C("glassLine"), 8); slot2.rectTransform.At(52, yy, 36, 36); Mine(sf2, "raised2");
            var (sel, selFill) = Kit.Box(r, "Picked", C("accentSoft"), C("accent"), 8); sel.rectTransform.At(92, yy, 36, 36); Mine(sel, "accent"); _accentGlow = selFill;
            // four 36 px slots, 4 apart: fit the narrowest sample (174)
            var (warn, warnFill) = Kit.Box(r, "Warn", C("emberSoft"), C("ember"), 8); warn.rectTransform.At(132, yy, 36, 36); Mine(warnFill, "emberSoft");
            line.rectTransform.sizeDelta = new Vector2(w, yy + 36f + 12f);   // as tall as what it shows
        }

        // ---- textures and marks ------------------------------------------------------------------------------
        private void SvTex()
        {
            const int n = 64;
            if (_svTex == null) { _svTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; _sv.sprite = Sprite.Create(_svTex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f)); }
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = UnityEngine.Color.HSVToRGB(_h, x / (n - 1f), y / (n - 1f));
            _svTex.SetPixels32(px); _svTex.Apply(false);
        }

        private static Texture2D _hue, _alphaRamp;
        internal static Texture2D HueTex()
        {
            if (_hue != null) return _hue;
            _hue = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++) _hue.SetPixel(0, y, UnityEngine.Color.HSVToRGB(y / 127f, 1f, 1f));
            _hue.Apply(false); return _hue;
        }
        // the picker's tile in the Colours rows: a hue wheel, white in the middle, its edge smoothed
        private static Sprite _wheel;
        internal static Sprite Wheel()
        {
            if (_wheel != null) return _wheel;
            const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float h = Mathf.Atan2(dy, dx) / (2f * Mathf.PI); if (h < 0f) h += 1f;
                var c = UnityEngine.Color.HSVToRGB(h, Mathf.Clamp01(r * 1.3f), 1f); c.a = Mathf.Clamp01((1f - r) * n * 0.5f);
                tex.SetPixel(x, y, c);
            }
            tex.Apply(false);
            return _wheel = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
        private static Texture2D AlphaTex()
        {
            if (_alphaRamp != null) return _alphaRamp;
            _alphaRamp = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) _alphaRamp.SetPixel(0, y, new Color(1f, 1f, 1f, y / 63f));   // tinted by the image colour
            _alphaRamp.Apply(false); return _alphaRamp;
        }

        private static void Pin(RectTransform r, float w, float h) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(w, h); }
        private static void Place(RectTransform mark, float u, float v) { mark.anchorMin = mark.anchorMax = new Vector2(u, v); mark.anchoredPosition = Vector2.zero; }

        // press or drag inside: where, from 0 to 1 on each axis (bottom left 0, 0)
        internal sealed class DragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            internal Action<Vector2> On;
            public void OnPointerDown(PointerEventData e) => Handle(e);
            public void OnDrag(PointerEventData e) => Handle(e);
            private void Handle(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp)) return;
                var r = rt.rect; On?.Invoke(new Vector2(Mathf.Clamp01(Mathf.InverseLerp(r.xMin, r.xMax, lp.x)), Mathf.Clamp01(Mathf.InverseLerp(r.yMin, r.yMax, lp.y))));
            }
        }
    }
}
