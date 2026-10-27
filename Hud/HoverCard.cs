using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // What you look at (Valheim's hover text, "Reinforced Chest\n[E] Open (Hold to stack)") as a Jatify card
    // right of the crosshair: the name in bold, each "[key] action" as a key cap and the action, other lines
    // dimmed. Valheim still writes its text every frame; we read it and keep its own label hidden.
    // Over a chest the peek card shows this instead (ChestPeek uses Actions).
    internal static class HoverCard
    {
        // a key hint: "[<color=yellow><b>E</b></color>] Open" (vanilla) or any [ ] with colour tags in it, in either order (mods write
        // "[<b><color=yellow>LeftShift</color> + <color=yellow>E</color></b>] Add coal from Inventory & Nearby Containers")
        private static readonly Regex KeyLine = new Regex(@"^\s*\[(?=[^\]]*<color)(.*?)\]\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex FirstColor = new Regex(@"^\s*<color=(#[0-9a-fA-F]{6,8})>", RegexOptions.Compiled);
        internal static Color TitleColor = Color.white;
        internal static string Title = ""; internal static readonly List<(string key, string what)> Actions = new List<(string, string)>(); internal static readonly List<string> Notes = new List<string>();
        private static RectTransform _root; private static CanvasGroup _cg; private static string _raw;

        internal static void Parse(string raw)
        {
            Title = ""; TitleColor = Color.white; Actions.Clear(); Notes.Clear();
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var line in raw.Split('\n'))
            {
                var m = KeyLine.Match(line);
                if (m.Success) { Actions.Add((Tags.Replace(m.Groups[1].Value, "").Trim(), Tags.Replace(m.Groups[2].Value, "").Trim())); continue; }
                var plain = Tags.Replace(line, "").Trim(); if (plain.Length == 0) continue;
                if (Title.Length == 0 && Actions.Count == 0) { Title = plain; var c = FirstColor.Match(line); TitleColor = c.Success && ColorUtility.TryParseHtmlString(c.Groups[1].Value, out var tc) ? tc : Color.white; }   // a name's colour (EpicLoot's rarity) stays
                else Notes.Add(plain);
            }
        }

        internal static void Tick()
        {
            var h = Hud.instance; if (h == null || h.m_hoverName == null || Root.Canvas == null) { Destroy(); return; }
            var raw = h.m_hoverName.isActiveAndEnabled ? h.m_hoverName.text : "";
            bool show = !string.IsNullOrEmpty(raw) && ChestPeek.Showing == false && !InventoryGui.IsVisible() && !TextInput.IsVisible();
            if (_root == null) Build();
            _cg.alpha = Mathf.MoveTowards(_cg.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 10f);
            if (raw == _raw) return;
            _raw = raw; Parse(raw);
            if (!string.IsNullOrEmpty(raw)) Fill();
        }

        private static void Build()
        {
            _root = Kit.Node(Root.Canvas.transform, "jatify-hover");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f); _root.pivot = new Vector2(0f, 1f); _root.anchoredPosition = new Vector2(28f, -12f);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
        }

        private static void Fill()
        {
            var t = Plugin.Instance.ThemeData;
            foreach (Transform c in _root) Object.Destroy(c.gameObject);
            var (bg, _) = Card.Box(_root, "Card", t);
            float y = Card.Pad - 2f, w = 160f;
            var parts = new List<RectTransform>();
            if (Title.Length > 0) { var n = Kit.Text(bg.rectTransform, "Name", Title, Fonts.Bold, Card.Title, TitleColor); n.rectTransform.At(Card.Pad, y, 320, 20); w = Mathf.Max(w, Kit.W(n) + 2 * Card.Pad); y += 24f; }
            foreach (var note in Notes) { var n = Kit.Text(bg.rectTransform, "Note", note, Fonts.Medium, Card.Body, Theme.C(t.dim)); n.rectTransform.At(Card.Pad, y, 320, 18); w = Mathf.Max(w, Kit.W(n) + 2 * Card.Pad); y += 20f; }
            foreach (var (key, what) in Actions) { float rw = Row(bg.rectTransform, key, what, Card.Pad, y, t); w = Mathf.Max(w, rw + 2 * Card.Pad); y += 24f; }
            bg.rectTransform.At(0, 0, Mathf.Min(w, 360f), y + Card.Pad - 6f);
        }

        // a key cap and its action; returns the width used
        internal static float Row(RectTransform parent, string key, string what, float x, float y, ThemeData t)
        {
            var (cap, _) = Kit.Box(parent, "Key", new Color(1f, 1f, 1f, 0.08f), Theme.C(t.line2), 5);
            var kt = Kit.Text(cap.rectTransform, "K", key, Fonts.Mono, 11f, Theme.C(t.text)); kt.alignment = TextAlignmentOptions.Center; kt.rectTransform.Fill();
            float kw = Mathf.Max(20f, Kit.W(kt) + 12f); cap.rectTransform.At(x, y + 1, kw, 19);
            var wt = Kit.Text(parent, "What", what, Fonts.Medium, 12.5f, Theme.C(t.dim)); wt.alignment = TextAlignmentOptions.MidlineLeft;
            float ww = Kit.W(wt); wt.rectTransform.At(x + kw + 8f, y, ww + 4f, 21);
            return kw + 8f + ww;
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; _raw = null; }
    }
}
