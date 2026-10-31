using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Building blocks: rounded boxes (generated 9-slice sprites), circles, the quality star, text.
    // Generated textures are cached and destroyed on unload.
    // One card style for every floating panel (hover card, tooltips, chest preview, placing card, text box): glass with
    // a 1 px outline, 12 px corners, 14 px padding, a bold 15 title, 12.5 body in the dim colour, mono 10.5 for details
    internal static class Card
    {
        internal const int R = 12;
        internal const float Pad = 14f, Title = 15f, Body = 12.5f, Meta = 10.5f;
        internal static (Image line, Image fill) Box(Transform parent, string name, ThemeData t) => Kit.Box(parent, name, Theme.C(t.glass), Theme.C(t.glassLine), R);
    }

    internal static class Kit
    {
        private static readonly Dictionary<int, Sprite> _round = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _ring = new Dictionary<int, Sprite>();
        private static Sprite _circle, _star, _lock, _fadeLine, _glow;
        private static readonly System.Collections.Generic.Dictionary<(Color, Color, Color), Sprite> _vgrads = new System.Collections.Generic.Dictionary<(Color, Color, Color), Sprite>();   // a few, one per look (boss bar, rarity wash)
        private static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        // The design's icons (its SVG symbols rendered to 64 px white PNGs, embedded); tint with Image.color.
        // Falls back to a Valheim sprite of that name, so its own icons (weight_icon_32, walknut_bw...) work too.
        internal static Sprite Icon(string name)
        {
            if (_icons.TryGetValue(name, out var s) && s != null) return s;
            using (var st = typeof(Kit).Assembly.GetManifestResourceStream("icons/" + name + ".png"))
            {
                if (st != null)
                {
                    var bytes = new byte[st.Length]; st.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "jatify-icon-" + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    if (!LoadPng(tex, bytes)) { Object.Destroy(tex); return null; }
                    s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f); s.name = tex.name;
                    return _icons[name] = s;
                }
            }
            foreach (var v in Resources.FindObjectsOfTypeAll<Sprite>()) if (v.name == name) { _vanilla[name] = v; return v; }
            return null;
        }
        // a picture shipped in the DLL (assets/whatsnew/<name>.jpg); not cached: the caller keeps it and frees it with its window
        internal static Sprite Picture(string name)
        {
            using (var st = typeof(Kit).Assembly.GetManifestResourceStream("whatsnew/" + name + ".jpg"))
            {
                if (st == null) return null;
                var bytes = new byte[st.Length]; st.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "jatify-picture-" + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!LoadPng(tex, bytes)) { Object.Destroy(tex); return null; }
                var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f); s.name = tex.name; return s;
            }
        }

        // ImageConversion.LoadImage by reflection: its assembly targets a newer netstandard than we can reference.
        private static System.Func<Texture2D, byte[], bool, bool> _load;
        private static bool LoadPng(Texture2D tex, byte[] bytes)
        {
            if (_load == null)
            {
                var m = HarmonyLib.AccessTools.Method("UnityEngine.ImageConversion:LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (m == null) return false;
                _load = (System.Func<Texture2D, byte[], bool, bool>)System.Delegate.CreateDelegate(typeof(System.Func<Texture2D, byte[], bool, bool>), m);
            }
            return _load(tex, bytes, false);
        }
        private static readonly Dictionary<string, Sprite> _vanilla = new Dictionary<string, Sprite>(); // not ours: never destroyed

        internal static Image IconImg(Transform parent, string name, Color c, float size)
        {
            var i = Img(parent, "Icon-" + name, Icon(name), c, false); i.preserveAspect = true; i.rectTransform.sizeDelta = new Vector2(size, size); return i;
        }

        internal static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        // Place a rect by its top-left corner inside its parent (the parent's top-left is 0,0; y grows down).
        internal static RectTransform At(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        internal static RectTransform Fill(this RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        internal static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced = true)
        {
            var img = Node(parent, name).gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = false;
            img.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            return img;
        }

        internal static Image Panel(Transform parent, string name, Color fill, float radius) => Img(parent, name, Rounded(Mathf.RoundToInt(radius)), fill);

        // A rounded box with a 1 px outline: the outer image is a ring (only the edge band is opaque), the
        // fill sits inset by 1 px. A see-through fill therefore shows the world, not the outline colour.
        internal static (Image line, Image fill) Box(Transform parent, string name, Color fill, Color line, float radius)
        {
            var o = Img(parent, name, Ring(Mathf.RoundToInt(radius)), line);
            var f = Panel(o.transform, "Fill", fill, Mathf.Max(2f, radius - 1f));
            f.rectTransform.Fill(1f);
            return (o, f);
        }

        // text from other players (names, portal tags, shared pins) shown literally: their rich-text tags can't restyle or spoof our UI
        // every '<' gets a zero-width space after it, so no tag can form (stripping tags can be dodged: case, nesting)
        internal static string Plain(string s) => (s ?? "").Replace("\\", "\\\u200B").Replace("<", "<\u200B");   // and every '\\' so TMP's \\u003C escapes can't make one

        internal static TextMeshProUGUI Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color)
        {
            var t = Quiet(Node(parent, name).gameObject, font ?? Fonts.Medium);
            t.fontSize = size; t.color = color; t.text = text;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        // a text whose font is set before it wakes: switched on with no font, TextMeshPro looks for its default (LiberationSans
        // SDF, which Valheim doesn't ship) and logs a warning, one per text we make (user report: ~190 lines on load)
        internal static TextMeshProUGUI Quiet(GameObject go, TMP_FontAsset font)
        {
            bool was = go.activeSelf; go.SetActive(false);
            var t = go.AddComponent<TextMeshProUGUI>(); if (font != null) t.font = font;
            go.SetActive(was);
            return t;
        }

        internal static Sprite Rounded(int r)
        {
            r = Mathf.Clamp(r, 2, 32);
            if (_round.TryGetValue(r, out var s) && s != null) return s;
            int n = r * 2 + 4;                       // 2 px margin on each side, one column/row of flat middle
            float lo = r + 2f, hi = n - r - 2f;      // centres of the corner circles
            s = Make("jatify-round-" + r, n, (fx, fy) =>
            {
                float dx = Mathf.Max(lo - fx, 0f, fx - hi), dy = Mathf.Max(lo - fy, 0f, fy - hi);
                return Mathf.Sqrt(dx * dx + dy * dy) - r;                  // signed distance to the edge
            }, new Vector4(r + 2, r + 2, r + 2, r + 2));
            _round[r] = s;
            return s;
        }

        // The outline of Rounded(r): opaque in a 1.5 px band along the edge, clear inside.
        internal static Sprite Ring(int r)
        {
            r = Mathf.Clamp(r, 2, 32);
            if (_ring.TryGetValue(r, out var s) && s != null) return s;
            int n = r * 2 + 4; float lo = r + 2f, hi = n - r - 2f;
            s = Make("jatify-ring-" + r, n, (fx, fy) =>
            {
                float dx = Mathf.Max(lo - fx, 0f, fx - hi), dy = Mathf.Max(lo - fy, 0f, fy - hi);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;                    // < 0 inside the rounded rect
                bool flatInside = dx <= 0f && dy <= 0f;                         // the flat middle (stretched when sliced)
                float inside = flatInside ? Mathf.Min(fx - 2f, fy - 2f, n - 2f - fx, n - 2f - fy) : -d; // distance to the edge
                return Mathf.Max(d, inside - 1.5f);                             // keep only the band 0..1.5 px from the edge
            }, new Vector4(r + 2, r + 2, r + 2, r + 2));
            _ring[r] = s;
            return s;
        }

        // Padlock: body plus shackle, 16 px, tinted by the image colour.
        internal static Sprite Lock() => _lock != null ? _lock : _lock = Make("jatify-lock", 32, (fx, fy) =>
        {
            float body = Mathf.Max(Mathf.Abs(fx - 16f) - 9f, Mathf.Abs(fy - 10f) - 7.5f);           // y up: body in the lower half
            float ring = Mathf.Abs(Vector2.Distance(new Vector2(fx, fy), new Vector2(16f, 18f)) - 6f) - 1.6f;
            if (fy < 17f) ring = 99f;                                                              // only the upper arc
            return Mathf.Min(body, ring);
        }, Vector4.zero);

        internal static Sprite Circle() => _circle != null ? _circle : _circle = Make("jatify-circle", 64, (fx, fy) => Vector2.Distance(new Vector2(fx, fy), new Vector2(32f, 32f)) - 30f, Vector4.zero);

        // Five-pointed star for item quality (neither shipped font has the glyph).
        internal static Sprite Star() => _star != null ? _star : _star = Make("jatify-star", 32, (fx, fy) =>
        {
            var p = new Vector2(fx - 16f, fy - 15.2f);
            float a = Mathf.Atan2(p.x, p.y), r = p.magnitude;
            float k = Mathf.Repeat(a + Mathf.PI / 5f, 2f * Mathf.PI / 5f) - Mathf.PI / 5f; // angle to nearest point
            float edge = Mathf.Lerp(14.5f, 6.2f, Mathf.Abs(k) / (Mathf.PI / 5f));
            return r - edge;
        }, Vector4.zero);

        // The approved name ornament (assets/ornaments: a trinity knot, its tip pointing away from the name, a line out of the tip
        // fading to nothing): w wide, h tall (24 in the design), the knot at the right end, so it sits left of a name; mirror: the
        // right-hand copy. Anchored to its parent's centre; Flank puts a pair either side of a text.
        internal static RectTransform Ornament(Transform parent, string name, Color c, float w, float h, bool mirror, bool fade = true, bool line = true)
        {
            var rt = Node(parent, name); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(w, h);
            if (mirror) rt.localScale = new Vector3(-1f, 1f, 1f);
            float k = h / 24f;
            if (line)
            {   // design: transparent into 80 % green; fade off: a solid line; line off: the knot alone (nameplates, user)
                var ln = Img(rt, "Line", fade ? FadeLine() : null, new Color(c.r, c.g, c.b, c.a * 0.8f), false).rectTransform;
                ln.anchorMin = ln.anchorMax = ln.pivot = new Vector2(0f, 0.5f); ln.anchoredPosition = Vector2.zero; ln.sizeDelta = new Vector2(Mathf.Max(0f, w - 45f * k), 1.1f * k);
            }
            var knotImg = Img(rt, "Knot", Icon("knot"), c, false); var knot = knotImg.rectTransform;   // 46 x 24 view box, its own 1.1 stub out of the tip meets the line
            knot.anchorMin = knot.anchorMax = knot.pivot = new Vector2(1f, 0.5f); knot.anchoredPosition = Vector2.zero; knot.sizeDelta = new Vector2(46f * k, h);
            if (!line)
            {   // the knot alone: the picture's left 57.6 % is the line's start drawn in (from x 106 of 184): only its right part shows
                knotImg.type = Image.Type.Filled; knotImg.fillMethod = Image.FillMethod.Horizontal; knotImg.fillOrigin = (int)Image.OriginHorizontal.Right; knotImg.fillAmount = 0.43f;
            }
            foreach (var g in rt.GetComponentsInChildren<Image>()) { var sh = g.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.6f); sh.effectDistance = new Vector2(0f, -1f); }   // a dark edge under knot and line: they read over bright sky and grass (user)
            return rt;
        }

        // a pair of ornaments either side of a centred text half wide, gap away from it
        internal static void Flank(RectTransform l, RectTransform r, float half, float gap, float y = 0f)
        {
            if (l == null || r == null) return;
            float w = l.sizeDelta.x; l.anchoredPosition = new Vector2(-half - gap - w / 2f, y); r.anchoredPosition = new Vector2(half + gap + w / 2f, y);
        }

        internal static void OrnamentWidth(RectTransform o, float w)
        {
            if (o == null || Mathf.Abs(o.sizeDelta.x - w) < 0.5f) return;
            o.sizeDelta = new Vector2(w, o.sizeDelta.y);
            var line = (RectTransform)o.GetChild(0); line.sizeDelta = new Vector2(Mathf.Max(0f, w - 45f * o.sizeDelta.y / 24f), line.sizeDelta.y);
        }

        // panel headings (design .htitle): a centred title between the knots, their lines fading out towards the panel's edges.
        // edge: how far the lines reach past the title's own rect on each side
        internal static (RectTransform l, RectTransform r) Heading(TMP_Text title, float edge, Color c, float y = 0f)
        {
            float half = W(title) / 2f, w = Mathf.Max(60f, title.rectTransform.sizeDelta.x / 2f + edge - half - 12f);
            var l = Ornament(title.transform, "jatify-knot-l", c, w, 24f, false); var r = Ornament(title.transform, "jatify-knot-r", c, w, 24f, true);
            Flank(l, r, half, 12f, y);
            return (l, r);
        }

        // 0 on the left, opaque on the right, tinted by the image colour: the ornaments' lines
        internal static Sprite FadeLine()
        {
            if (_fadeLine != null) return _fadeLine;
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { name = "jatify-fadeline", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[64]; for (int x = 0; x < 64; x++) px[x] = new Color32(255, 255, 255, (byte)(x * 255 / 63));
            tex.SetPixels32(px); tex.Apply(false, true);
            _fadeLine = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f), 100f); _fadeLine.name = tex.name;
            return _fadeLine;
        }

        // A soft halo, sliced: opaque inside, fading out over the outer 16 px. Size the image 16 px larger than what glows on
        // each side (CSS drop-shadow / box-shadow blur: the boss bar's pulse, the cheat strip's ember glow).
        internal static Sprite Glow() => _glow != null ? _glow : _glow = Make("jatify-glow", 64, (fx, fy) =>
        {
            float dx = Mathf.Max(16f - fx, 0f, fx - 48f), dy = Mathf.Max(16f - fy, 0f, fy - 48f);
            float a = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 16f);
            return 0.5f - a * a * a;   // Make turns 0.5 - dist into alpha; cubed: strong by the edge, gone well before the end
        }, new Vector4(31, 31, 31, 31));

        // top to bottom, white: a bar's shading (tint it white so these colours show)
        internal static Sprite VGradient(Color top, Color mid, Color bottom)
        {
            if (_vgrads.TryGetValue((top, mid, bottom), out var have) && have != null) return have;
            var tex = new Texture2D(1, 32, TextureFormat.RGBA32, false) { name = "jatify-vgrad", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[32];
            for (int y = 0; y < 32; y++) { float f = 1f - y / 31f; px[y] = f < 0.45f ? Color.Lerp(top, mid, f / 0.45f) : Color.Lerp(mid, bottom, (f - 0.45f) / 0.55f); }   // row 0 is the bottom
            tex.SetPixels(px); tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, 1, 32), new Vector2(0.5f, 0.5f), 100f); sp.name = tex.name;
            return _vgrads[(top, mid, bottom)] = sp;
        }

        private static Sprite Make(string name, int n, System.Func<float, float, float> dist, Vector4 border)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - dist(x + 0.5f, y + 0.5f)) * 255f));
            tex.SetPixels32(px);
            tex.Apply(false, true); // no longer readable: frees the CPU copy
            var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = name;
            return s;
        }

        // Text width that works on inactive panels too: TMP reports 0 for text whose object is not active
        // (our inventory and crafting skins are built before Valheim's panel is), so measure those on a
        // hidden, always-active TextMeshPro with the same font, size, style and spacing.
        private static TextMeshProUGUI _measure;
        // a field added from code had no text component yet when it first enabled, which is when TMP builds the caret:
        // enabling it again builds it (user: no blinking | after Ctrl+F)
        internal static void Caret(TMPro.TMP_InputField f)
        {
            f.customCaretColor = true; f.caretColor = Color.white; f.caretWidth = 2; f.caretBlinkRate = 1.0f;
            f.enabled = false; f.enabled = true;
        }

        internal static float W(TMP_Text t)
        {
            if (t == null) return 0f;
            if (t.isActiveAndEnabled) { float pw = t.preferredWidth; if (pw > 0f || string.IsNullOrEmpty(t.text)) return pw; }
            if (_measure == null)
            {
                var go = new GameObject("jatify-measure"); Object.DontDestroyOnLoad(go);
                var cv = go.AddComponent<Canvas>(); cv.enabled = false;
                _measure = Quiet(Node(go.transform, "T").gameObject, t.font);
                _measure.textWrappingMode = TextWrappingModes.NoWrap; _measure.raycastTarget = false;
            }
            _measure.font = t.font; _measure.fontSize = t.fontSize; _measure.fontStyle = t.fontStyle;
            _measure.characterSpacing = t.characterSpacing; _measure.richText = t.richText;
            return _measure.GetPreferredValues(t.text).x;
        }

        // Valheim's input reports small wheel steps; its own lists use 1000-3000. One speed for all of ours.
        internal const float ScrollSpeed = 1500f;

        internal static void Unload()
        {
            if (_measure != null) Object.Destroy(_measure.transform.parent.gameObject);
            _measure = null;
            foreach (var s in _round.Values) Kill(s);
            foreach (var s in _ring.Values) Kill(s);
            _round.Clear(); _ring.Clear();
            Kill(_circle); Kill(_star); Kill(_lock); Kill(_fadeLine); Kill(_glow); foreach (var v in _vgrads.Values) Kill(v); _vgrads.Clear(); _circle = _star = _lock = _fadeLine = _glow = null;
            foreach (var s in _icons.Values) Kill(s);
            _icons.Clear(); _vanilla.Clear();
        }

        private static void Kill(Sprite s) { if (s == null) return; Object.Destroy(s.texture); Object.Destroy(s); }
    }
}
