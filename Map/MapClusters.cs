using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Crowded pins (user): three or more pins that overlap on the big map become one badge with their count;
    // hovering it lists them in a card you can move into, hovering a row brings that pin out (bigger, named),
    // clicking the badge keeps the card open, clicking a row zooms to that pin. Also the one place that decides whether a pin marker shows
    // (vanilla only creates markers, it never shows one again once hidden): a hidden kind or a grouped pin
    // is off, anything else on.
    internal static class MapClusters
    {
        internal sealed class Group { internal readonly List<Minimap.PinData> Pins = new List<Minimap.PinData>(); internal Vector2 At; internal Vector3 World; internal RectTransform Badge; }
        internal static readonly List<Group> Groups = new List<Group>();
        private static readonly HashSet<Minimap.PinData> _grouped = new HashSet<Minimap.PinData>();
        private static readonly List<(RectTransform rt, TextMeshProUGUI count, Image icon)> _badges = new List<(RectTransform, TextMeshProUGUI, Image)>();
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        private static readonly AccessTools.FieldRef<Minimap, bool> PinUpdate = AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");
        private static Minimap.PinData _highlight;
        internal static Group Over;   // the badge under the cursor

        // the pin layer gets no pointer events (bug report 2026-10-26: hover and click did nothing), so the badges
        // are hit-tested here, by screen position, like the group card
        internal static Group Under(Vector2 ptr)
        {
            for (int i = Groups.Count - 1; i >= 0; i--)
                if (Groups[i].Badge != null && RectTransformUtility.RectangleContainsScreenPoint(Groups[i].Badge, ptr, null)) return Groups[i];
            return null;
        }

        // a pin picked from a group's card: shown out of the group, bigger, until the cursor leaves its row
        internal static Minimap.PinData Highlight
        {
            get => _highlight;
            set { if (_highlight == value) return; _highlight = value; if (Minimap.instance != null) PinUpdate(Minimap.instance) = true; }
        }

        private static bool Groupable(Minimap.PinData p) =>
            p.m_type != Minimap.PinType.Player && p.m_type != Minimap.PinType.Ping && p.m_type != Minimap.PinType.Shout && p.m_type != Minimap.PinType.EventArea && p.m_type != Minimap.PinType.RandomEvent && p.m_worldSize <= 0f;

        // runs right after vanilla's UpdatePins (positions are fresh)
        internal static void Apply(Minimap m)
        {
            Groups.Clear(); _grouped.Clear();
            bool large = m.m_mode == Minimap.MapMode.Large && MapStyle.AppliedTo(m);
            bool group = large && m.LargeZoom > m.m_minZoom * 1.2f;   // zoomed all the way in, every pin shows (no closer to go)
            var all = Pins(m);
            if (group)
            {   // leader clustering in the pins' own space: a pin and everything within ~ one pin of it
                float r = m.m_pinSizeLarge * 0.8f, r2 = r * r;
                var cand = new List<Minimap.PinData>();
                foreach (var p in all) if (p.m_uiElement != null && Groupable(p) && !KindOff(p)) cand.Add(p);
                var used = new HashSet<Minimap.PinData>();
                foreach (var lead in cand)
                {
                    if (used.Contains(lead)) continue;
                    var g = new Group(); var a = lead.m_uiElement.anchoredPosition;
                    foreach (var q in cand) if (!used.Contains(q) && (q.m_uiElement.anchoredPosition - a).sqrMagnitude <= r2) g.Pins.Add(q);
                    if (g.Pins.Count < 2) continue;   // groups from two
                    foreach (var q in g.Pins) { used.Add(q); _grouped.Add(q); g.At += q.m_uiElement.anchoredPosition; g.World += q.m_pos; }
                    g.At /= g.Pins.Count; g.World /= g.Pins.Count;
                    Groups.Add(g);
                }
            }
            foreach (var p in all)
            {
                if (p.m_uiElement == null) continue;
                bool hl = p == _highlight && large;
                bool show = !KindOff(p) && (!_grouped.Contains(p) || hl);
                if (p.m_uiElement.gameObject.activeSelf != show) p.m_uiElement.gameObject.SetActive(show);
                p.m_uiElement.localScale = hl ? Vector3.one * 1.5f : Vector3.one;
                var name = p.m_NamePinData?.PinNameGameObject;
                if (name != null && (!show || (hl && !name.activeSelf && p.m_name.Length > 0))) name.SetActive(show);   // grouped: no name; picked: its name even when zoomed out
                if (hl) p.m_uiElement.SetAsLastSibling();
            }
            Badges(m);
            if (large && Cfg.HideOverlapNames.Value) HideOverlaps(all);
        }

        // where pin names overlap, the first one wins: yours before shared ones, then by list order (HideOverlapNames)
        private static readonly List<Rect> _taken = new List<Rect>();
        private static void HideOverlaps(List<Minimap.PinData> all)
        {
            _taken.Clear();
            for (int pass = 0; pass < 2; pass++)
                foreach (var p in all)
                {
                    if ((p.m_ownerID != 0L) != (pass == 1)) continue;
                    var go = p.m_NamePinData?.PinNameGameObject; if (go == null || !go.activeSelf) continue;
                    var txt = p.m_NamePinData.PinNameText; var rt = p.m_NamePinData.PinNameRectTransform;
                    float s = rt.localScale.x, w = (txt != null ? txt.preferredWidth : 60f) * s, h = 16f * s;
                    var r = new Rect(rt.anchoredPosition.x - w / 2f, rt.anchoredPosition.y - h - 14f * s, w, h);
                    bool clash = false; foreach (var t in _taken) if (t.Overlaps(r)) { clash = true; break; }
                    if (clash) go.SetActive(false); else _taken.Add(r);
                }
        }

        private static bool KindOff(Minimap.PinData p) { int k = PinKinds.Of(p); return k > 0 && PinKinds.Hidden.Contains(k); }

        private static void Badges(Minimap m)
        {
            var t = Plugin.Instance.ThemeData;
            for (int i = 0; i < Groups.Count; i++)
            {
                if (i >= _badges.Count) _badges.Add(MakeBadge(m, t));
                var (rt, count, icon) = _badges[i]; var g = Groups[i];
                rt.gameObject.SetActive(true); rt.SetAsLastSibling(); rt.anchoredPosition = g.At; g.Badge = rt;
                count.text = g.Pins.Count.ToString(); ((RectTransform)count.transform.parent).sizeDelta = new Vector2(g.Pins.Count >= 10 ? 28f : 20f, 18f);
                icon.sprite = CommonIcon(g); icon.enabled = icon.sprite != null;
            }
            for (int i = Groups.Count; i < _badges.Count; i++) if (_badges[i].rt != null) _badges[i].rt.gameObject.SetActive(false);
            if (Over != null && !Groups.Contains(Over)) Over = null;
        }

        private static Sprite CommonIcon(Group g)
        {
            var n = new Dictionary<Sprite, int>(); Sprite best = null; int bn = 0;
            foreach (var p in g.Pins) { if (p.m_icon == null) continue; n.TryGetValue(p.m_icon, out var c); n[p.m_icon] = ++c; if (c > bn) { bn = c; best = p.m_icon; } }
            return best;
        }

        private static (RectTransform, TextMeshProUGUI, Image) MakeBadge(Minimap m, ThemeData t)
        {
            var ring = Kit.Img(m.m_pinRootLarge, "jatify-group", Kit.Circle(), Theme.C(t.accent), false); ring.raycastTarget = true;
            var rt = ring.rectTransform; rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(40f, 40f);
            var disk = Kit.Img(rt, "Disk", Kit.Circle(), new Color(0.06f, 0.06f, 0.07f, 0.92f), false); disk.rectTransform.Fill(2f);
            var icon = Kit.Img(rt, "I", null, Color.white, false); icon.rectTransform.Fill(8f); icon.preserveAspect = true;
            var pill = Kit.Panel(rt, "Pill", Theme.C(t.accent), 9); var pr = pill.rectTransform; pr.anchorMin = pr.anchorMax = Vector2.one; pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = new Vector2(-4f, -4f); pr.sizeDelta = new Vector2(22f, 18f);
            var count = Kit.Text(pr, "N", "", Fonts.Bold, 12f, new Color(0.02f, 0.08f, 0.05f)); count.alignment = TextAlignmentOptions.Center; count.rectTransform.Fill();
            return (rt, count, icon);
        }

        internal static void Clear()
        {
            foreach (var b in _badges) if (b.rt != null) Object.Destroy(b.rt.gameObject);
            _badges.Clear(); Groups.Clear(); _grouped.Clear(); _highlight = null; Over = null;
            var m = Minimap.instance;   // every marker back on
            if (m != null) foreach (var p in Pins(m)) if (p.m_uiElement != null) { p.m_uiElement.gameObject.SetActive(true); p.m_uiElement.localScale = Vector3.one; }
        }
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class MapClustersAfterPins
    {
        [HarmonyPriority(Priority.Low)]   // after PinKinds has put its icons on
        private static void Postfix(Minimap __instance) => MapClusters.Apply(__instance);
    }
}
