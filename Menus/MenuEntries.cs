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
        internal static void AddTo(Transform entries, Ledger l, ThemeData t)
        {
            var settings = entries?.Find("Settings");
            if (settings == null) return;
            int i = settings.GetSiblingIndex();
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
