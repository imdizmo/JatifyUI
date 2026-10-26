using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Panel knotwork (design "Panels with knotwork", setting Panels.Knotwork), very soft:
    // - Corners: the big windows get the blocky eternity knot sitting on their top-left and bottom-right corner points, the
    //   panel's own edges there a 1 px green line fading out by 62 % of the way; those two corners go to a 3 px radius.
    // - Divider: an existing section line gets a small knot in its middle (shield in the inventory, upright trinity elsewhere),
    //   the line fading out from it both ways at 45 %; the plain line comes back when the setting is off.
    // - AboveTitle: one upright trinity knot over a dialog's title.
    // The ornaments are created once per window, hidden or shown when the setting changes; they live under the window's own
    // objects, so they go when it does. The textures (the icons, the vertical fade, the two-radius boxes) are freed on unload.
    internal static class Knots
    {
        internal const float CornerKnot = 22f, CornerSmall = 3f, EdgeReach = 0.62f, Shield = 14f, Trinity = 11f, DialogKnot = 18f;
        private static bool Show => Cfg.Knotwork == null || Cfg.Knotwork.Value;

        private sealed class Box { internal Image Line, Fill; internal int R; }
        private sealed class Swap { internal GameObject Orn; internal Graphic Plain; }
        private static readonly List<GameObject> _orn = new List<GameObject>();   // shown while Knotwork is on
        private static readonly List<Swap> _swaps = new List<Swap>();              // knotted divider over a plain line: one or the other
        private static readonly List<Box> _boxes = new List<Box>();                // boxes whose knotted corners are 3 px while on
        private static readonly Dictionary<int, Sprite> _ring2 = new Dictionary<int, Sprite>(), _round2 = new Dictionary<int, Sprite>();
        private static Sprite _fadeV;
        private static int _pruneAt = 64;

        internal static void Bind()
        {
            if (Cfg.Knotwork == null) return;
            Cfg.Knotwork.SettingChanged -= OnSetting; Cfg.Knotwork.SettingChanged += OnSetting;
        }
        private static void OnSetting(object s, System.EventArgs e) => Apply();

        internal static void Apply()
        {
            Prune(true);
            bool on = Show;
            foreach (var g in _orn) if (g.activeSelf != on) g.SetActive(on);
            foreach (var w in _swaps) { if (w.Orn.activeSelf != on) w.Orn.SetActive(on); if (w.Plain != null) w.Plain.enabled = !on; }
            foreach (var b in _boxes) Corners(b, on);
            Changed?.Invoke();
        }
        // windows whose layout makes room for an ornament (the dialogs' knot) lay themselves out again
        internal static event System.Action Changed;

        // dead entries (windows rebuilt, tooltips destroyed) go now and then, so the lists stay small
        private static void Prune(bool now)
        {
            if (!now && _orn.Count + _swaps.Count + _boxes.Count < _pruneAt) return;
            _orn.RemoveAll(g => g == null);
            _swaps.RemoveAll(w => w.Orn == null);
            _boxes.RemoveAll(b => b.Line == null);
            _pruneAt = Mathf.Max(64, (_orn.Count + _swaps.Count + _boxes.Count) * 2);
        }

        private static GameObject Track(GameObject g) { Prune(false); _orn.Add(g); g.SetActive(Show); return g; }

        // The window corners on a Kit.Box (line = its outer ring image, fill = its inset fill, r = the radius it was made with).
        // host: draw them in that parent instead (same rect as the box, last sibling): for a window whose content covers its edges
        internal static void Corners(Image line, Image fill, int r, Color accent, Transform host = null)
        {
            if (line == null) return;
            RectTransform root;
            if (host == null) root = Kit.Node(line.transform, "jatify-corners").Fill();
            else
            {
                var src = line.rectTransform; root = Kit.Node(host, "jatify-corners");
                root.anchorMin = src.anchorMin; root.anchorMax = src.anchorMax; root.pivot = src.pivot; root.anchoredPosition = src.anchoredPosition; root.sizeDelta = src.sizeDelta;
            }
            var edge = new Color(accent.r, accent.g, accent.b, 0.75f);
            float s = CornerKnot, a = s / 2f - 1f;   // the edge lines start just inside the knot (design: 10 px for a 22 px knot)
            Line(root, true, new Vector2(0f, 1f), new Vector2(EdgeReach, 1f), new Vector2(a, -1f), new Vector2(0f, 0f), edge, true);        // top, fading right
            Line(root, false, new Vector2(0f, 1f - EdgeReach), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, -a), edge, false);  // left, fading down
            Line(root, true, new Vector2(1f - EdgeReach, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(-a, 1f), edge, false);   // bottom, fading left
            Line(root, false, new Vector2(1f, 0f), new Vector2(1f, EdgeReach), new Vector2(-1f, a), new Vector2(0f, 0f), edge, true);       // right, fading up
            var k = new Color(accent.r, accent.g, accent.b, 0.85f);
            foreach (var at in new[] { new Vector2(0f, 1f), new Vector2(1f, 0f) })
            {
                var i = Kit.Img(root, "Knot", Kit.Icon("eternity"), k, false); i.preserveAspect = true;
                var rt = i.rectTransform; rt.anchorMin = rt.anchorMax = at; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(s, s);
            }
            root.SetAsLastSibling();
            Track(root.gameObject);
            var b = new Box { Line = line, Fill = fill, R = r }; _boxes.Add(b); Corners(b, Show);
        }

        // a 1 px fading line on a stretch rect; flip: the opaque end at the start (left / top) instead of the end
        private static void Line(RectTransform parent, bool horizontal, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c, bool flip)
        {
            var i = Kit.Img(parent, horizontal ? "EdgeH" : "EdgeV", horizontal ? Kit.FadeLine() : FadeV(), c, false);
            var rt = i.rectTransform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f); rt.offsetMin = oMin; rt.offsetMax = oMax;
            if (flip) rt.localScale = horizontal ? new Vector3(-1f, 1f, 1f) : new Vector3(1f, -1f, 1f);
        }

        private static void Corners(Box b, bool on)
        {
            if (b.Line == null) return;
            b.Line.sprite = on ? Ring2(b.R) : Kit.Ring(b.R);
            if (b.Fill != null) { int fr = Mathf.RoundToInt(Mathf.Max(2f, b.R - 1f)); b.Fill.sprite = on ? Round2(fr) : Kit.Rounded(fr); }
        }

        // A section divider (design .kdiv) over a plain line: the plain line's image is off while Knotwork is on, the knotted one
        // is its child, so it follows the line wherever it is laid out. Knot in the middle, an 8 px gap, then the line either
        // side fading out towards the ends; 45 % overall.
        internal static RectTransform Divider(Graphic plain, bool shield, Color accent)
        {
            if (plain == null) return null;
            float ks = shield ? Shield : Trinity;
            var root = Kit.Node(plain.transform, "jatify-kdiv");
            root.anchorMin = new Vector2(0f, 0.5f); root.anchorMax = new Vector2(1f, 0.5f); root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(0f, ks);
            Fill(root, ks, shield, accent);
            Prune(false); _swaps.Add(new Swap { Orn = root.gameObject, Plain = plain });
            root.gameObject.SetActive(Show); plain.enabled = !Show;
            return root;
        }

        // a free-standing divider at x, y (its centre line), w wide, in a top-left laid parent
        internal static RectTransform DividerAt(Transform parent, float x, float y, float w, bool shield, Color accent)
        {
            float ks = shield ? Shield : Trinity;
            var root = Kit.Node(parent, "jatify-kdiv").At(x, y - ks / 2f, w, ks);
            Fill(root, ks, shield, accent);
            Track(root.gameObject);
            return root;
        }

        // an untracked divider (its owner decides when it shows: the tooltips, whose topic comes and goes)
        internal static RectTransform Loose(Transform parent, float w, bool shield, Color accent)
        {
            float ks = shield ? Shield : Trinity;
            var root = Kit.Node(parent, "jatify-kdiv"); root.sizeDelta = new Vector2(w, ks);
            Fill(root, ks, shield, accent);
            return root;
        }
        internal static bool On => Show;

        private static void Fill(RectTransform root, float ks, bool shield, Color accent)
        {
            const float gap = 8f, a = 0.45f;
            var lc = new Color(accent.r, accent.g, accent.b, a * 0.7f);
            var l = Kit.Img(root, "L", Kit.FadeLine(), lc, false).rectTransform;   // transparent at the end, 70 % green by the knot
            l.anchorMin = new Vector2(0f, 0.5f); l.anchorMax = new Vector2(0.5f, 0.5f); l.pivot = new Vector2(0.5f, 0.5f);
            l.offsetMin = new Vector2(0f, -0.5f); l.offsetMax = new Vector2(-(ks / 2f + gap), 0.5f);
            var r = Kit.Img(root, "R", Kit.FadeLine(), lc, false).rectTransform;
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(1f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(ks / 2f + gap, -0.5f); r.offsetMax = new Vector2(0f, 0.5f); r.localScale = new Vector3(-1f, 1f, 1f);
            var k = Kit.Img(root, "Knot", Kit.Icon(shield ? "shield-knot" : "knot-up"), new Color(accent.r, accent.g, accent.b, a), false); k.preserveAspect = true;
            var kr = k.rectTransform; kr.anchorMin = kr.anchorMax = kr.pivot = new Vector2(0.5f, 0.5f); kr.anchoredPosition = Vector2.zero; kr.sizeDelta = new Vector2(ks, ks);
        }

        // Dialogs (design .dlg .knot): one upright trinity knot, 18 px at 45 %, centred above the title; gap: between the knot's
        // foot and the top of the title's text
        internal static RectTransform AboveTitle(TMP_Text title, Color accent, float gap = 6f)
        {
            if (title == null) return null;
            var tr = title.rectTransform;
            var k = Kit.Img(tr, "jatify-dlgknot", Kit.Icon("knot-up"), new Color(accent.r, accent.g, accent.b, 0.45f), false); k.preserveAspect = true;
            var rt = k.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0f); rt.sizeDelta = new Vector2(DialogKnot, DialogKnot);
            float textTop = title.fontSize * 0.5f;   // the glyphs' top above the rect's midline, near enough for one line
            rt.anchoredPosition = new Vector2(0f, textTop + gap);
            Track(k.gameObject);
            return rt;
        }

        // the dialog knot at the top centre of a top-left laid box, its top y px down
        internal static RectTransform DialogTop(RectTransform box, float y, Color accent)
        {
            var k = Kit.Img(box, "jatify-dlgknot", Kit.Icon("knot-up"), new Color(accent.r, accent.g, accent.b, 0.45f), false); k.preserveAspect = true;
            var rt = k.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); rt.sizeDelta = new Vector2(DialogKnot, DialogKnot);
            rt.anchoredPosition = new Vector2(0f, -y);
            Track(k.gameObject);
            return rt;
        }

        // 0 at the bottom, opaque at the top
        private static Sprite FadeV()
        {
            if (_fadeV != null) return _fadeV;
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "jatify-fadeline-v", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[64]; for (int y = 0; y < 64; y++) px[y] = new Color32(255, 255, 255, (byte)(y * 255 / 63));
            tex.SetPixels32(px); tex.Apply(false, true);
            _fadeV = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100f); _fadeV.name = tex.name;
            return _fadeV;
        }

        // Kit.Rounded / Kit.Ring with the top-left and bottom-right corners at the small radius (the knots sit on them)
        private static Sprite Round2(int r) => Two(_round2, r, false);
        private static Sprite Ring2(int r) => Two(_ring2, r, true);
        private static Sprite Two(Dictionary<int, Sprite> cache, int r, bool ring)
        {
            r = Mathf.Clamp(r, 2, 32);
            if (cache.TryGetValue(r, out var sp) && sp != null) return sp;
            int n = r * 2 + 4; float sm = Mathf.Min(CornerSmall, r);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = (ring ? "jatify-ring2-" : "jatify-round2-") + r };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    bool left = fx < n / 2f, top = fy > n / 2f;                 // y up: row 0 is the bottom
                    float rc = left == top ? sm : r;                            // top-left and bottom-right small
                    float lo = rc + 2f, hi = n - rc - 2f;
                    float dx = Mathf.Max(lo - fx, 0f, fx - hi), dy = Mathf.Max(lo - fy, 0f, fy - hi);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - rc;
                    if (ring)
                    {
                        bool flat = dx <= 0f && dy <= 0f;
                        float inside = flat ? Mathf.Min(fx - 2f, fy - 2f, n - 2f - fx, n - 2f - fy) : -d;
                        d = Mathf.Max(d, inside - 1.5f);
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
            sp.name = tex.name;
            return cache[r] = sp;
        }

        internal static void Unload()
        {
            if (Cfg.Knotwork != null) Cfg.Knotwork.SettingChanged -= OnSetting;
            Changed = null;
            foreach (var w in _swaps) if (w.Plain != null) w.Plain.enabled = true;
            foreach (var b in _boxes) Corners(b, false);
            _orn.Clear(); _swaps.Clear(); _boxes.Clear(); _pruneAt = 64;
            foreach (var s in _ring2.Values) Kill(s);
            foreach (var s in _round2.Values) Kill(s);
            _ring2.Clear(); _round2.Clear();
            Kill(_fadeV); _fadeV = null;
        }

        private static void Kill(Sprite s) { if (s == null) return; Object.Destroy(s.texture); Object.Destroy(s); }
    }
}
