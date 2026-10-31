using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Mods known to be dead on this Valheim, which Jatify UI can't be held to: while one is installed, a big warning each time
    // the game starts, until the player clicks I understand (user). Add a line to Known for another.
    internal static class DeprecatedMods
    {
        private static readonly (string guid, string name, string why)[] Known =
        {
            ("Azumatt.Sleepover", "Sleepover", "It was last updated in November 2024 and no longer works on this version of Valheim: it throws errors every time you sleep."),
        };
        private static bool _checked; private static RectTransform _root;

        internal static void Tick()
        {
            if (_checked || Root.Canvas == null || !Epic.Started) return;   // every plugin has loaded
            _checked = true;
            var hits = Known.Where(k => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(k.guid)).ToList();
            if (hits.Count == 0) return;
            foreach (var h in hits) Log.Warn($"{h.name} ({h.guid}) is deprecated and doesn't work on this Valheim: remove it before playing with Jatify UI");
            Show(hits);
        }

        private static void Show(System.Collections.Generic.List<(string guid, string name, string why)> hits)
        {
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-deprecated").Fill();
            var cv = _root.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 32003; _root.gameObject.AddComponent<GraphicRaycaster>();
            var dim = _root.gameObject.AddComponent<Image>(); dim.color = new Color(0f, 0f, 0f, 0.7f);   // over everything: nothing behind it takes a click
            var (box, fill) = Kit.Box(_root, "Box", Theme.C(t.glass), Theme.C(t.ember), 14);
            fill.color = new Color(fill.color.r, fill.color.g, fill.color.b, Mathf.Max(fill.color.a, 0.97f));
            var b = box.rectTransform; b.anchorMin = b.anchorMax = b.pivot = new Vector2(0.5f, 0.5f);
            Knots.DialogTop(b, 16f, Theme.C(t.ember));
            var title = Kit.Text(b, "Title", "Remove before playing", Fonts.Norse, 26f, Theme.C(t.ember)); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 40, 600, 36);   // under the knot
            var body = string.Join("\n\n", hits.Select(h => $"<b>{h.name}</b> is deprecated. {h.why}"))
                + $"\n\nRemove {(hits.Count == 1 ? "it" : "them")} before playing with Jatify UI. If you keep playing with {(hits.Count == 1 ? "it" : "them")} installed, you do so at your own risk: problems {(hits.Count == 1 ? "it causes" : "they cause")} (lost saves, missing items, errors) are not caused by Jatify UI, and we can't help with them.";
            var tx = Kit.Text(b, "Body", body, Fonts.Medium, 15f, Theme.C(t.text)); tx.richText = true; tx.textWrappingMode = TextWrappingModes.Normal; tx.alignment = TextAlignmentOptions.TopLeft; tx.lineSpacing = 6f;
            float th = tx.GetPreferredValues(body, 536f, 0f).y; tx.rectTransform.At(32, 92, 536, th);
            float bh = 92f + th + 28f;
            var ok = Kit.Panel(b, "Ok", Theme.C(t.raised2), 9); ok.rectTransform.At(200, bh, 200, 42); ok.raycastTarget = true;
            var ot = Kit.Text(ok.rectTransform, "T", "I understand", Fonts.Bold, 15f, Color.white); ot.alignment = TextAlignmentOptions.Center; ot.rectTransform.Fill();
            ok.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            b.sizeDelta = new Vector2(600f, bh + 42f + 26f);
        }

        internal static void Close() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; }
        internal static void Destroy() { Close(); _checked = false; }   // unload: a reload looks again
        internal static void DevShow() { Close(); Show(Known.ToList()); }   // dev: the panel as players see it
    }
}
