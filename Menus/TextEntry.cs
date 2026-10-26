using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Valheim's text box (signs, portal tags, tamed names; design "New: text input"): it says what you're writing and where
    // (an icon tile, a title and a line under it, from what asked for the text), the field with a green focus ring, a meta row
    // (a hint on the left: rich text for signs, how many portals already use that tag; the count / limit on the right), a live
    // preview for signs, Enter / Esc hints and Cancel / Save. Valheim's panel, field and buttons keep working; they are
    // restyled and moved (ledger), ours are added beside them. Updated only when the text changes.
    internal static class TextEntry
    {
        private const float W = 460f, Pad = 16f, Inner = W - 2f * Pad;
        private static readonly AccessTools.FieldRef<TextInput, TextReceiver> Queued = AccessTools.FieldRefAccess<TextInput, TextReceiver>("m_queuedSign");
        private static readonly Ledger _l = new Ledger();
        private static GameObject _for; private static ThemeData _t;
        private static RectTransform _panel, _field, _ok, _cancel, _head, _meta, _preview, _foot, _knot, _shadow, _chip;
        private static Image _icon; private static TextMeshProUGUI _title, _sub, _hint, _count, _sign;
        private static TMP_InputField _input; private static TMP_Text _topic;
        private static bool _open, _isSign; private static int _kind; private static string _text, _tag;
        private static readonly List<(TMP_Text t, string s)> _labels = new List<(TMP_Text, string)>();   // Valheim localizes its button text again on each open
        private static readonly List<string> _tags = new List<string>();   // the other loaded portals' tags, read when the box opens

        // every frame: cheap while closed
        internal static void Tick(ThemeData t)
        {
            var ti = TextInput.instance; var panel = ti != null ? ti.m_panel : null;
            if (panel == null) { _open = false; return; }
            if (panel != _for) Build(ti, t);
            bool open = panel.activeSelf;
            if (open && !_open) Opened(ti);
            _open = open; if (!open || _input == null) return;
            if (!ReferenceEquals(_input.text, _text)) { _text = _input.text; Changed(); }
        }

        // ---- build once per text box ---------------------------------------------------------------
        private static void Build(TextInput ti, ThemeData t)
        {
            _l.Undo(); Knots.Changed -= Layout; _labels.Clear();
            _for = ti.m_panel; _t = t; _open = false; _text = null;
            var w = ti.m_panel.transform; _panel = (RectTransform)w;
            // the card: glass, a 1 px outline, a soft shadow (design .ti)
            _l.Img(w.GetComponent<Image>(), Kit.Rounded(12), new Color(0.063f, 0.063f, 0.071f, 0.97f));
            var line = Kit.Img(w, "jatify-line", Kit.Ring(12), new Color(1f, 1f, 1f, 0.14f)); _l.Own(line); line.rectTransform.Fill(); line.transform.SetAsFirstSibling();
            var sh = Kit.Img(w, "jatify-shadow", Kit.Glow(), new Color(0f, 0f, 0f, 0.45f)); _l.Own(sh); _shadow = sh.rectTransform; _shadow.SetAsFirstSibling();
            _l.Rect(_panel, r => r.sizeDelta = new Vector2(W, r.sizeDelta.y));
            _topic = ti.m_topic; _l.Rect(_topic != null ? _topic.rectTransform : null, r => r.localScale = Vector3.zero);   // ours says it; Valheim's is the fallback title
            _l.Hide(w.Find("help_Text")?.gameObject);
            // head: icon tile, title, what it is for
            _head = Kit.Node(w, "jatify-head"); _l.Own(_head);
            var (tile, _) = Kit.Box(_head, "Tile", new Color(0.173f, 0.173f, 0.196f, 0.85f), new Color(0, 0, 0, 0), 8); tile.rectTransform.At(0, 0, 34, 34);
            _icon = Kit.Img(tile.rectTransform, "I", null, Theme.C(t.dim), false); _icon.preserveAspect = true; _icon.rectTransform.At(8, 8, 18, 18);
            _title = Kit.Text(_head, "Title", "", Fonts.Norse, 20f, Color.white); _title.characterSpacing = 5f; _title.overflowMode = TextOverflowModes.Ellipsis; _title.rectTransform.At(44, -1, Inner - 44, 23);
            _sub = Kit.Text(_head, "Sub", "", Fonts.Medium, 12f, Theme.C(t.mute)); _sub.overflowMode = TextOverflowModes.Ellipsis; _sub.rectTransform.At(44, 22, Inner - 44, 16);
            // the field: a dark well with a green focus ring and caret
            _field = w.Find("TextField") as RectTransform; _input = _field != null ? _field.GetComponent<TMP_InputField>() : null;
            if (_field != null)
            {
                _l.Img(_field.GetComponent<Image>(), Kit.Rounded(9), new Color(0f, 0f, 0f, 0.35f));
                var glow = Kit.Img(_field, "jatify-glow", Kit.Ring(12), new Color(0.133f, 0.839f, 0.494f, 0.12f)); _l.Own(glow); glow.rectTransform.Fill(-3f); glow.transform.SetAsFirstSibling();
                var ring = Kit.Img(_field, "jatify-ring", Kit.Ring(9), Theme.C(t.accent)); _l.Own(ring); ring.rectTransform.Fill();
                _l.Rect(_field.Find("Text Area") as RectTransform, r => { r.offsetMin = new Vector2(12f, 0f); r.offsetMax = new Vector2(-12f, 0f); });
                foreach (var x in _field.GetComponentsInChildren<TMP_Text>(true)) _l.Txt(x, Fonts.Medium, 15f, x.name == "Placeholder" ? Theme.C(t.mute) : Color.white);
                if (_input != null) { var inp = _input; var (cc, cw, cu) = (inp.caretColor, inp.caretWidth, inp.customCaretColor); _l.Do(() => { inp.customCaretColor = true; inp.caretColor = Theme.C(t.accent); inp.caretWidth = 2; }, () => { if (inp != null) { inp.customCaretColor = cu; inp.caretColor = cc; inp.caretWidth = cw; } }); }
            }
            // meta: a hint, the count
            _meta = Kit.Node(w, "jatify-meta"); _l.Own(_meta);
            _hint = Kit.Text(_meta, "Hint", "", Fonts.Mono, 11.5f, Theme.C(t.mute)); _hint.richText = false; _hint.overflowMode = TextOverflowModes.Ellipsis; _hint.rectTransform.Fill(); _hint.alignment = TextAlignmentOptions.MidlineLeft;
            _count = Kit.Text(_meta, "Count", "", Fonts.Mono, 11.5f, Theme.C(t.mute)); _count.rectTransform.Fill(); _count.alignment = TextAlignmentOptions.MidlineRight;
            // sign preview: the text as the sign shows it
            _preview = Kit.Node(w, "jatify-preview"); _l.Own(_preview);
            var pb = Kit.Img(_preview, "Line", Kit.Ring(9), new Color(1f, 1f, 1f, 0.14f)); pb.rectTransform.Fill();   // design: a dashed edge; ours is solid and faint
            _preview.gameObject.AddComponent<RectMask2D>();
            var pl = Kit.Text(_preview, "L", "Preview", Fonts.Medium, 12.5f, Theme.C(t.dim)); pl.alignment = TextAlignmentOptions.MidlineLeft; pl.rectTransform.At(10, 0, 60, 36);
            var chip = Kit.Panel(_preview, "Sign", new Color(0.231f, 0.165f, 0.11f), 4); _chip = chip.rectTransform;
            _sign = Kit.Text(_chip, "T", "", Fonts.Norse, 16f, new Color(0.914f, 0.843f, 0.69f)); _sign.characterSpacing = 4f; _sign.richText = true; _sign.overflowMode = TextOverflowModes.Ellipsis; _sign.alignment = TextAlignmentOptions.MidlineLeft;
            // foot: Enter / Esc on the left, Cancel and Save (Valheim's buttons) on the right
            _foot = Kit.Node(w, "jatify-foot"); _l.Own(_foot);
            float x0 = 0f; x0 = Cap(_foot, "Enter", x0); x0 = Label(_foot, "save ·", x0); x0 = Cap(_foot, "Esc", x0); Label(_foot, "cancel", x0);
            _ok = w.Find("OK") as RectTransform; _cancel = w.Find("Cancel") as RectTransform;
            Button(_ok, "Save", Theme.C(t.accent), new Color(0.024f, 0.082f, 0.051f));
            Button(_cancel, "Cancel", new Color(0.173f, 0.173f, 0.196f, 0.8f), Color.white);
            // design .dlg .knot: the knot above the box's content while Knotwork is on
            _knot = Knots.DialogTop(_panel, 10f, Theme.C(t.accent)); _l.Own(_knot);
            Knots.Changed += Layout;
        }

        private static float Cap(Transform parent, string key, float x)
        {
            var (b, _) = Kit.Box(parent, "Key", new Color(0.173f, 0.173f, 0.196f, 0.9f), new Color(1f, 1f, 1f, 0.18f), 5);
            var k = Kit.Text(b.rectTransform, "K", key, Fonts.Mono, 10.5f, Color.white); k.alignment = TextAlignmentOptions.Center; k.rectTransform.Fill();
            float w = Kit.W(k) + 10f; b.rectTransform.At(x, 8, w, 17); return x + w + 4f;
        }
        private static float Label(Transform parent, string s, float x)
        {
            var l = Kit.Text(parent, "L", s, Fonts.Medium, 12f, Theme.C(_t.dim)); l.alignment = TextAlignmentOptions.MidlineLeft;
            float w = Kit.W(l); l.rectTransform.At(x, 0, w + 2f, 33); return x + w + 6f;
        }

        private static void Button(RectTransform b, string label, Color fill, Color ink)
        {
            if (b == null) return;
            _l.Img(b.GetComponent<Image>(), Kit.Rounded(9), fill);
            var tx = b.Find("Text")?.GetComponent<TMP_Text>();
            if (tx != null) { _l.Txt(tx, Fonts.Bold, 13f, ink); var was = tx.text; _l.Do(() => tx.text = label, () => { if (tx != null) tx.text = was; }); _labels.Add((tx, label)); }
        }

        // ---- what is being written, read once when the box opens ---------------------------------------
        private static void Opened(TextInput ti)
        {
            var r = Queued(ti); var mb = r as MonoBehaviour;
            _isSign = r is Sign; _tag = null; _tags.Clear();
            string icon = "rename", title, sub = "";
            if (_isSign) { icon = "sign"; title = "Write on the sign"; sub = "Everyone passing by can read it"; _kind = 1; }
            else if (r is TeleportWorld)
            {
                icon = "rename"; title = "Name this portal"; sub = "Portals with the same name connect"; _kind = 2;
                foreach (var tw in Object.FindObjectsByType<TeleportWorld>(FindObjectsSortMode.None)) if (tw != (Object)mb) _tags.Add(tw.GetText() ?? "");   // the loaded ones (what Valheim knows of near you)
            }
            else if (r is Tameable)
            {
                var ch = mb != null ? mb.GetComponent<Character>() : null;
                icon = "paw"; title = "Name your " + (ch != null ? Localization.instance.Localize(ch.m_name) : "companion"); sub = "Shown over its head to everyone"; _kind = 3;
            }
            else { title = _topic != null && _topic.text.Length > 0 ? _topic.text : "Enter text"; _kind = 0; }
            _icon.sprite = Kit.Icon(icon); _title.text = title; _sub.text = sub; _sub.gameObject.SetActive(sub.Length > 0);
            foreach (var (tx, label) in _labels) if (tx != null) tx.text = label;
            _text = null; Layout();
        }

        private static void Changed()
        {
            var s = _text ?? ""; int lim = _input.characterLimit;
            _count.text = lim > 0 ? $"{s.Length} / {lim}" : s.Length.ToString();
            _count.color = Theme.C(lim > 0 && s.Length >= lim ? _t.ember : _t.mute);
            if (_kind == 1) { _hint.text = "Rich text: <b>, <color>, <size>"; _sign.text = s.Length > 0 ? s.Replace("\\n", " ") : " "; float sw = Mathf.Min(Inner - 80f, Kit.W(_sign) + 20f); _chip.At(70, 6, sw, 24); _sign.rectTransform.At(10, 0, sw - 20f, 24); }
            else if (_kind == 2)
            {
                var tag = s.Trim(); int n = 0; foreach (var x in _tags) if (x == tag) n++;
                _hint.text = tag.Length == 0 ? "Type a name to pair it with another portal" : n == 0 ? $"No other portal near uses \"{tag}\" yet" : $"{n} portal{(n == 1 ? "" : "s")} already use{(n == 1 ? "s" : "")} \"{tag}\"";
                _hint.color = Theme.C(n == 1 ? _t.accent : _t.mute);   // exactly one: these two connect
            }
            else _hint.text = "";
        }

        // top to bottom: (knot) head, field, meta, (sign preview), buttons
        private static void Layout()
        {
            if (_panel == null) { Knots.Changed -= Layout; return; }
            float y = Pad + (Knots.On ? 18f : 0f);
            _head.At(Pad, y, Inner, 38); y += 38f + 12f;
            if (_field != null) _field.At(Pad, y, Inner, 42); y += 42f + 12f;
            _meta.At(Pad, y, Inner, 17); y += 15f + 12f;   // 17: an ellipsis text drops a line taller than its box
            _preview.gameObject.SetActive(_isSign);
            if (_isSign) { _preview.At(Pad, y, Inner, 36); y += 36f + 12f; }
            _foot.At(Pad, y, Inner, 33);
            float okW = _ok != null ? Mathf.Max(64f, Kit.W(_ok.Find("Text")?.GetComponent<TMP_Text>()) + 32f) : 0f, caW = _cancel != null ? Mathf.Max(64f, Kit.W(_cancel.Find("Text")?.GetComponent<TMP_Text>()) + 32f) : 0f;
            if (_ok != null) _ok.At(W - Pad - okW, y, okW, 33);
            if (_cancel != null) _cancel.At(W - Pad - okW - 8f - caW, y, caW, 33);
            y += 33f + Pad;
            _panel.sizeDelta = new Vector2(W, y);
            _shadow.At(-16, -16 + 14, W + 32, y + 32);
            if (_knot != null) _knot.anchoredPosition = new Vector2(0f, -10f);
        }

        internal static void Restore() { Knots.Changed -= Layout; _l.Undo(); _for = null; _panel = null; _input = null; _open = false; _tags.Clear(); _labels.Clear(); }
    }
}
