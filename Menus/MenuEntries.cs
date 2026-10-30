using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // "Mods" and "Jatify UI" in the pause menu: clones of vanilla's Settings entry (same look, same
    // layout behaviour), opening Jatify UI's pages. Owned by MenuStyle's ledger, so unloading removes them.
    internal static class MenuEntries
    {
        internal static void Add(Menu m, Ledger l, ThemeData t) => AddTo(m.m_root.Find("Menu/MenuEntries"), l, t);

        // any menu list with a "Settings" entry (the pause menu, the title menu)
        // the title screen, above its bottom-left links (Changelog, the licence, Player.log), in Jatify's look, not Valheim's (user):
        // Jatify's Discord as an icon button, and "What's new" in white, tilted, pulsing towards you in a loop like a splash text
        internal static void AddLinks(Transform links, Ledger l)
        {
            if (links == null) return; var t = Plugin.Instance.ThemeData; var lr = (RectTransform)links;
            var row = l.Own(Kit.Node(links.parent, "jatify-title-links"));
            row.anchorMin = lr.anchorMin; row.anchorMax = lr.anchorMax; row.pivot = new Vector2(0f, 0f); row.sizeDelta = new Vector2(300f, 48f);
            row.anchoredPosition = lr.anchoredPosition + new Vector2(-lr.sizeDelta.x * lr.pivot.x, lr.sizeDelta.y * (1f - lr.pivot.y) + 14f);
            // Discord: a round glass button with the logo
            var (b, f) = Kit.Box(row, "Discord", Theme.C(t.glass), Theme.C(t.line2), 22); b.rectTransform.At(0, 2, 44, 44); b.raycastTarget = true;
            var ic = Kit.IconImg(b.rectTransform, "discord-logo", Color.white, 24f); ic.rectTransform.At(10, 10, 24, 24);
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f; var cb = btn.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.35f); btn.colors = cb;
            btn.onClick.AddListener(() => Application.OpenURL(Links.Discord));
            // its name beside it (user), clickable too
            var name = Kit.Text(row, "Name", "Jatify UI", Fonts.Norse, 20f, Color.white); name.alignment = TextAlignmentOptions.MidlineLeft; name.raycastTarget = true;
            name.fontSharedMaterial = Fonts.Shadowed(Fonts.Norse) ?? name.fontSharedMaterial; name.rectTransform.At(54, 2, 120, 44);
            name.gameObject.AddComponent<Button>().onClick.AddListener(() => Application.OpenURL(Links.Discord));
            var accent = Theme.C(t.accent);   // the accent on hover, as the menu's entries (user): the icon and the name light together
            foreach (var go in new[] { b.gameObject, name.gameObject }) { var h = go.AddComponent<HoverTint>(); h.Lit = accent; h.Parts = new Graphic[] { ic, name }; }
            // What's new: white, tilted, pulsing; a click opens the notes
            var wn = Kit.Text(row, "WhatsNew", "What's new!", Fonts.Norse, 26f, Color.white); wn.alignment = TextAlignmentOptions.MidlineLeft; wn.raycastTarget = true;
            wn.fontSharedMaterial = Fonts.Shadowed(Fonts.Norse) ?? wn.fontSharedMaterial;
            var wr = wn.rectTransform; wr.anchorMin = wr.anchorMax = new Vector2(0f, 0f); wr.pivot = new Vector2(0.5f, 0.5f); wr.sizeDelta = new Vector2(170f, 40f); wr.anchoredPosition = new Vector2(70f, 58f);   // resting on the icon's top edge, a splash on it (user)
            wr.localEulerAngles = new Vector3(0f, 0f, 8f);
            wn.gameObject.AddComponent<Pulse>();
            { var h = wn.gameObject.AddComponent<HoverTint>(); h.Lit = accent; h.Parts = new Graphic[] { wn }; }
            wn.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (!WhatsNew.Open) WhatsNew.Show(false); });
        }

        private sealed class HoverTint : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            internal Graphic[] Parts; internal Color Lit;
            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { foreach (var g in Parts) if (g != null) g.color = Lit; }
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { foreach (var g in Parts) if (g != null) g.color = Color.white; }
            private void OnDisable() { if (Parts != null) foreach (var g in Parts) if (g != null) g.color = Color.white; }
        }

        // bigger and smaller in a loop, as if coming at you (unscaled time: the title screen has no game clock)
        private sealed class Pulse : MonoBehaviour
        {
            private void Update() { float k = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 3.2f); transform.localScale = new Vector3(k, k, 1f); }
        }

        internal static void AddTo(Transform entries, Ledger l, ThemeData t, bool whatsNew = true)
        {
            var settings = entries?.Find("Settings");
            if (settings == null) return;
            int i = settings.GetSiblingIndex();
            if (whatsNew) Clone(settings, "What's new", i + 1, l, () => { if (!WhatsNew.Open) WhatsNew.Show(false); });   // the update notes again, any time (user); the title screen has it bottom left
            Clone(settings, "Jatify UI", i + 1, l, () => ConfigPage.Show(Root.Canvas.transform, t, false));
            Clone(settings, "Mods", i + 1, l, () => ConfigPage.Show(Root.Canvas.transform, t, true));
        }

        internal static void Clone(Transform from, string label, int index, Ledger l, System.Action open)
        {
            var go = Object.Instantiate(from.gameObject, from.parent);
            go.name = "jatify-" + label; go.transform.SetSiblingIndex(index);
            l.Own(go.transform);
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();          // drop vanilla's "open settings" listener
            b.onClick.AddListener(() => open());
            var tx = go.GetComponentInChildren<TMP_Text>(true); if (tx != null) tx.text = label;
            foreach (var loc in go.GetComponentsInChildren<Localize>(true)) Object.Destroy(loc); // keep our label
        }
    }
}
