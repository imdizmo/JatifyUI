using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Valheim's split-stack window and its popup (yes / no, ok, a text box) were still wooden panels (user). Quick Stack Store's
    // trash prompt is a copy of the split window and Fate Woven's first-time tips use the popup, so both take this look too.
    // Restyled in place (ledger): a dark card centred on the screen, Jatify's fonts, buttons and slider. The split window is styled
    // as soon as the inventory exists, so a copy made before it was ever opened is styled as well.
    internal static class DialogStyle
    {
        private static readonly Ledger _split = new Ledger(), _pop = new Ledger();
        private static Object _splitFor, _popFor; private static UnifiedPopup _popup;

        internal static void Tick()
        {
            var g = InventoryGui.instance;
            if (g != null && g.m_splitDialog != null && _splitFor != g.m_splitDialog)
            {
                _split.Undo(); _splitFor = g.m_splitDialog;
                Split(g.m_splitDialog.transform, Plugin.Instance.ThemeData);
            }
            if (g != null && g.m_splitDialog != null && g.m_splitDialog.gameObject.activeInHierarchy && g.m_splitDialog.GetComponent<Canvas>() is Canvas cv && !cv.overrideSorting)
            {   // set while hidden, Unity drops it
                cv.overrideSorting = true; cv.sortingOrder = (g.transform.GetComponentInParent<Canvas>()?.rootCanvas.sortingOrder ?? 600) + 5;
            }
            if (g != null)
                for (int i = 0; i < g.transform.childCount; i++)
                {   // copies of it (Quick Stack Store's trash prompt) are made from the hidden one: their sorting is dropped too
                    var ch = g.transform.GetChild(i);
                    if (ch.gameObject.activeSelf && ch.name.StartsWith("SplitDialog(Clone)") && ch.GetComponent<Canvas>() is Canvas cc && !cc.overrideSorting) { cc.overrideSorting = true; cc.sortingOrder = (g.transform.GetComponentInParent<Canvas>()?.rootCanvas.sortingOrder ?? 600) + 5; }
                }
            if (_popup != null && _popFor != _popup) { _pop.Undo(); _popFor = _popup; Popup(_popup.transform, Plugin.Instance.ThemeData); }
        }

        // a theme change: built again with the new colours on the next tick
        internal static void Restore() { _split.Undo(); _pop.Undo(); _splitFor = _popFor = null; }

        [HarmonyPatch(typeof(UnifiedPopup), nameof(UnifiedPopup.Push))]
        private static class PopupShown
        {
            private static void Prefix()
            {   // never in the popup's way: a failed restyle leaves it Valheim's
                try { if (_popup == null) _popup = Traverse.Create(typeof(UnifiedPopup)).Field("instance").GetValue<UnifiedPopup>(); if (_popup != null && _popFor != _popup) Tick(); }
                catch (System.Exception e) { _popFor = _popup; Log.Warn("popup restyle failed: " + e.Message); }
            }
        }

        private static Color Solid(ThemeData t) { var c = Theme.C(t.glass); c.a = Mathf.Max(c.a, 0.97f); return c; }   // over the inventory a see-through card lets the slots show through
        private static Color Ink => new Color(0.02f, 0.02f, 0.02f);

        private static void Split(Transform root, ThemeData t)
        {
            var win = root.Find("win_bkg") as RectTransform; if (win == null) return;
            // over the inventory's edge tabs, which sit a step above its canvas (Quick Stack Store's copy takes this canvas too)
            if (root.GetComponent<Canvas>() == null)
            {
                int over = (root.parent != null ? root.parent.GetComponentInParent<Canvas>()?.rootCanvas.sortingOrder : null) ?? 600;
                var cv = _split.Added(root.gameObject.AddComponent<Canvas>()); cv.overrideSorting = true; cv.sortingOrder = over + 5;
                _split.Added(root.gameObject.AddComponent<GraphicRaycaster>());
            }
            foreach (var n in new[] { "PanelNormalPosition", "PanelTouchPosition" })
                if (root.Find(n) is RectTransform at) _split.Rect(at, r => { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero; });   // the window opens from here: the screen's middle
            _split.Rect(win, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(400f, 236f); });
            foreach (var n in new[] { "bkg", "border", "border (1)" }) _split.Off(win.Find(n)?.GetComponent<Graphic>());
            var (line, _) = Kit.Box(win, "jatify-card", Solid(t), Theme.C(t.line2), 14); _split.Own(line); line.rectTransform.Fill(); line.transform.SetAsFirstSibling();

            if (win.Find("Icon_bkg") is RectTransform ib)
            {
                _split.Img(ib.GetComponent<Image>(), Kit.Rounded(10), Theme.C(t.raised));
                _split.Rect(ib, r => Place(r, 20f, 18f, 56f, 56f));
                if (ib.Find("Icon") is RectTransform ic) _split.Rect(ic, r => { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(6f, 6f); r.offsetMax = new Vector2(-6f, -6f); });
            }
            if (win.Find("Text")?.GetComponent<TMP_Text>() is TMP_Text title)
            {
                _split.Txt(title, Fonts.Bold, 18f, Color.white); _split.Align(title, TextAlignmentOptions.MidlineLeft);
                _split.Rect(title.rectTransform, r => Place(r, 90f, 20f, 290f, 26f));
            }
            if (win.Find("amount")?.GetComponent<TMP_Text>() is TMP_Text amount)
            {
                _split.Txt(amount, Fonts.Mono, 14f, Theme.C(t.dim)); _split.Align(amount, TextAlignmentOptions.MidlineLeft);
                _split.Rect(amount.rectTransform, r => Place(r, 90f, 48f, 290f, 22f));
            }
            if (win.Find("Slider") is RectTransform sl)
            {
                _split.Rect(sl, r => Place(r, 20f, 98f, 360f, 24f));
                _split.Img(sl.Find("Background")?.GetComponent<Image>(), Kit.Rounded(5), Theme.C(t.raised));
                _split.Img(sl.Find("Fill Area/Fill")?.GetComponent<Image>(), Kit.Rounded(5), Theme.C(t.accent));
                var handle = sl.Find("Handle Slide Area/Handle") as RectTransform;
                if (handle != null)
                {
                    _split.Img(handle.GetComponent<Image>(), Kit.Circle(), Color.white, Image.Type.Simple);
                    _split.Rect(handle, r => r.sizeDelta = new Vector2(22f, -2f));
                }
                if (sl.Find("Handle Slide Area") is RectTransform area) _split.Rect(area, r => { r.offsetMin = new Vector2(11f, r.offsetMin.y); r.offsetMax = new Vector2(-11f, r.offsetMax.y); });   // the round handle's half width each side
            }
            // OK dark with accent writing, not filled: Quick Stack Store writes its Trash in red on this button
            Button(_split, win.Find("Button_cancel") as RectTransform, 20f, 176f, 174f, 40f, Theme.C(t.raised), Color.white);
            Button(_split, win.Find("Button_ok") as RectTransform, 206f, 176f, 174f, 40f, Theme.C(t.raised), Theme.C(t.accent));
        }

        private static void Popup(Transform root, ThemeData t)
        {
            var box = root.Find("PopupBlockingBackground/Popup") as RectTransform; if (box == null) return;
            var bkg = box.Find("bkg") as RectTransform;
            if (bkg != null)
            {
                _pop.Img(bkg.GetComponent<Image>(), Kit.Rounded(14), Solid(t));
                _pop.Rect(bkg, r => { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; });
                var ring = Kit.Img(box, "jatify-line", Kit.Ring(14), Theme.C(t.line2)); _pop.Own(ring); ring.rectTransform.Fill(); ring.raycastTarget = false;
                ring.transform.SetSiblingIndex(bkg.GetSiblingIndex() + 1);
            }
            if (box.Find("HeaderText")?.GetComponent<TMP_Text>() is TMP_Text head)
            {
                _pop.Txt(head, Fonts.Bold, 20f, Color.white);
                _pop.Rect(head.rectTransform, r => { r.anchoredPosition = new Vector2(0f, -14f); r.sizeDelta = new Vector2(-40f, 40f); });
            }
            if (box.Find("BodyText")?.GetComponent<TMP_Text>() is TMP_Text body) _pop.Txt(body, Fonts.Medium, 15f, Theme.C(t.text));
            foreach (var (n, primary) in new[] { ("ButtonYes", true), ("ButtonConfirm", true), ("ButtonOk", true), ("ButtonNo", false) })
                Button(_pop, box.Find(n) as RectTransform, float.NaN, 0f, 0f, 0f, primary ? Theme.C(t.accent) : Theme.C(t.raised), primary ? Ink : Color.white);
            if (box.Find("GUIInputField") is RectTransform field)
            {
                _pop.Img(field.GetComponent<Image>(), Kit.Rounded(8), Theme.C(t.raised));
                _pop.Txt(field.Find("Text Area/Text")?.GetComponent<TMP_Text>(), Fonts.Medium, 15f, Color.white);
                _pop.Txt(field.Find("Text Area/Placeholder")?.GetComponent<TMP_Text>(), Fonts.Medium, 15f, Theme.C(t.mute));
            }
        }

        // x NaN: the button keeps its place and size, only its look changes
        private static void Button(Ledger l, RectTransform b, float x, float y, float w, float h, Color fill, Color ink)
        {
            if (b == null) return;
            if (!float.IsNaN(x)) l.Rect(b, r => Place(r, x, y, w, h));
            l.Img(b.GetComponent<Image>(), Kit.Rounded(9), fill);
            l.Txt(b.Find("Text")?.GetComponent<TMP_Text>(), Fonts.Bold, 14f, ink);
        }

        // top-left design pixels inside the parent
        private static void Place(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
    }
}
