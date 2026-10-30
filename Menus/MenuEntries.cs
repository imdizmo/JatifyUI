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
        // the title screen's bottom-left links (Changelog, the licence, Player.log): What's new and Jatify Discord above them, in
        // their look (user: not in the big menu). The group keeps its foot where it was and grows upwards.
        internal static void AddLinks(Transform links, Ledger l)
        {
            var from = links?.Find("showChangelog"); if (from == null) return;
            Clone(from, "Jatify Discord", 0, l, () => Application.OpenURL(Links.Discord));
            Clone(from, "What's new", 0, l, () => { if (!WhatsNew.Open) WhatsNew.Show(false); });
            foreach (Transform c in links) if (c.name.StartsWith("jatify-")) { var n = c.Find("newsNotification"); if (n != null) n.gameObject.SetActive(false); }
            var rt = (RectTransform)links; float grow = 2f * 26f;
            var (s0, p0) = (rt.sizeDelta, rt.anchoredPosition);
            l.Do(() => { rt.sizeDelta = s0 + new Vector2(0f, grow); rt.anchoredPosition = p0 + new Vector2(0f, grow * rt.pivot.y); }, () => { if (rt != null) { rt.sizeDelta = s0; rt.anchoredPosition = p0; } });
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
