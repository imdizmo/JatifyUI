using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // "What's new" (user): after an update, a window with the changes at every world load until "Don't show again" is ticked. Close
    // stays locked until the reader has scrolled to the end. Seen is kept per install as a hash of the notes, so editing the notes
    // shows them again.
    internal static class WhatsNew
    {
        internal static bool Open => _root != null;
        private static RectTransform _root, _content;
        private static ScrollRect _scroll;
        private static Image _closeBg; private static TextMeshProUGUI _closeText;
        private static bool _read, _checked, _dontShow;
        private static readonly System.Collections.Generic.List<Sprite> _pictures = new System.Collections.Generic.List<Sprite>();
        private static void FreePictures() { foreach (var sp in _pictures) if (sp != null) { Object.Destroy(sp.texture); Object.Destroy(sp); } _pictures.Clear(); }

        private const string Notes =
@"<b>Colours</b>
- Press Esc, then Jatify UI, then Colours: the accent, panels, text, health, stamina and eitr, each with a few ready colours and a hex field.
[colours]
- The colour wheel on each row opens a picker for any other colour, with a slice of the UI drawn in it.
- In a world, the picker's Preview closes the menu and opens your inventory: the real UI takes the colour each time you let go of the mouse. Apply keeps it; Cancel, or closing the inventory, puts the old one back.
- This is a first look: the colours get a bigger rework in the next update.

<b>HUD</b>
- The key hints at the bottom right stack at the screen's right edge, no longer above the action bar.
[hints]
- Looking at a chest, its card opens up and right from the crosshair.
- The building card sits above the right-hand slots, clear of the key hints.
- Pause screens also hide what other mods add to Valheim's HUD.

<b>Windows</b>
- Split stack and Valheim's yes / no popups are Jatify cards now, and so are other mods' that use them (Fate Woven's tips, Quick Stack Store's trash prompt).
[split]

<b>Menus</b>
- Continue lights up on hover like the other entries, and the lists on the Jatify UI and Mods pages slide and light up the same way.

<b>Other mods</b>
{FateWoven}Fate Woven: a tab on the inventory's left edge opens its panel. Its panel and a chest take turns in crafting's spot. Its ability bar sits above the health bars and stays there while you build, repair or open the inventory.
{p0kahontazz.advancedbackpacks}Advanced_Backpacks: crafting and the character panel stay while you wear one of its backpacks. Crafting from chests never takes one of its backpacks, and the trash won't destroy one that still holds items.
{org.bepinex.plugins.backpacks}Backpacks: your pack waits for its tab instead of opening with the inventory; the tab closes it again. A line joins a bag's tab to its open panel; Backpacks' and RustyBags' bags show their icon and a close button there.
- Mod authors get more ways to fit in with Jatify: see For mod authors in the README.

These notes are always there: press Esc, What's new! on the world card.

Thanks for the reports and ideas!";

        internal static BepInEx.Configuration.ConfigEntry<string> Seen;
        internal static void Bind(BepInEx.Configuration.ConfigFile c) => Seen = c.Bind("Panels", "WhatsNewSeen", "", "Which What's new notes you chose not to see again (set by the game; clear it to see them at each world load again).");

        private static string Key => Notes.GetStableHashCode().ToString();

        internal static void Tick()
        {
            if (_root != null) { TickOpen(); return; }
            if (Player.m_localPlayer == null) { _checked = false; return; }   // each world load looks again
            if (_checked || Seen == null || Root.Canvas == null) return;
            if (Hud.instance != null && Hud.IsUserHidden()) return;
            _checked = true;
            if (Seen.Value == Key) return;
            Show();
        }

        // mustRead: after an update, Close waits for the end; from the pause menu (already read) it closes at once
        internal static void Show(bool mustRead = true)
        {
            _mustRead = mustRead; _born = Time.unscaledTime; _closing = -1f;
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-whatsnew").Fill();
            var cv = _root.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 32002; _root.gameObject.AddComponent<GraphicRaycaster>();
            var dim = _root.gameObject.AddComponent<Image>(); dim.color = new Color(0f, 0f, 0f, 0.55f);
            _fade = _root.gameObject.AddComponent<CanvasGroup>(); _fade.alpha = 0f;
            var (box, fill) = Kit.Box(_root, "Box", Theme.C(t.glass), Theme.C(t.line2), 14);
            fill.color = new Color(fill.color.r, fill.color.g, fill.color.b, Mathf.Max(fill.color.a, 0.96f));   // solid: a long read over the world
            var b = _box = box.rectTransform; b.anchorMin = b.anchorMax = b.pivot = new Vector2(0.5f, 0.5f); b.sizeDelta = new Vector2(620f, 640f);
            Knots.DialogTop(b, 16f, Theme.C(t.accent));
            var title = Kit.Text(b, "Title", "What's new in Jatify UI", Fonts.Norse, 24f, Color.white); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 38, 620, 34);   // under the knot
            var sub = Kit.Text(b, "Sub", mustRead ? "Scroll to the end to close" : "The changes in this version", Fonts.Medium, 13f, Theme.C(t.dim)); sub.alignment = TextAlignmentOptions.Center; sub.rectTransform.At(0, 72, 620, 20);
            // the notes in a scrolling well
            var view = Kit.Node(b, "View").At(28, 104, 564, 454); view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            _content = Kit.Node(view, "Content"); _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            // the notes in pieces: text, a picture where a line reads [name] (assets/whatsnew/<name>.jpg), and a mod's icon before a line
            // that starts {guid}
            float h = 0f; var text = new System.Text.StringBuilder();
            void Flush()
            {
                var chunk = text.ToString().Trim('\n'); text.Clear(); if (chunk.Length == 0) return;
                var tx = Kit.Text(_content, "Notes", chunk, Fonts.Medium, 15f, Theme.C(t.text)); tx.richText = true; tx.textWrappingMode = TextWrappingModes.Normal; tx.alignment = TextAlignmentOptions.TopLeft; tx.lineSpacing = 6f;
                float th = tx.GetPreferredValues(chunk, 548f, 0f).y; tx.rectTransform.At(0, h, 548, th); h += th + 10f;
            }
            foreach (var line in Notes.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.StartsWith("{") && l.IndexOf('}') > 1)
                {   // {guid}text: that mod's own icon beside the line, when it is installed; plain text otherwise
                    int e = l.IndexOf('}'); var rest = l.Substring(e + 1); var ic = Kit.ModIcon(l.Substring(1, e - 1));
                    if (ic == null) { text.Append("- ").Append(rest).Append('\n'); continue; }
                    Flush(); _pictures.Add(ic);
                    var (tile, _) = Kit.Box(_content, "ModIcon", new Color(0, 0, 0, 0.3f), Theme.C(t.line2), 6); tile.rectTransform.At(0, h + 1, 30, 30);
                    var im = Kit.Img(tile.rectTransform, "I", ic, Color.white, false); im.rectTransform.Fill(2f); im.preserveAspect = true;
                    var lt = Kit.Text(_content, "Notes", rest, Fonts.Medium, 15f, Theme.C(t.text)); lt.richText = true; lt.textWrappingMode = TextWrappingModes.Normal; lt.alignment = TextAlignmentOptions.TopLeft; lt.lineSpacing = 6f;
                    float lh = Mathf.Max(32f, lt.GetPreferredValues(rest, 508f, 0f).y); lt.rectTransform.At(40, h, 508, lh); h += lh + 10f;
                    continue;
                }
                if (!(l.StartsWith("[") && l.EndsWith("]"))) { text.Append(l).Append('\n'); continue; }
                Flush();
                var sp = Kit.Picture(l.Substring(1, l.Length - 2)); if (sp == null) continue;
                _pictures.Add(sp);
                float w = Mathf.Min(548f, sp.rect.width), ph = w * sp.rect.height / sp.rect.width;
                var (frame, _) = Kit.Box(_content, "Picture", new Color(0, 0, 0, 0.3f), Theme.C(t.line2), 8); frame.rectTransform.At((548f - w) / 2f, h, w, ph);
                var img = Kit.Img(frame.rectTransform, "Image", sp, Color.white, false); img.rectTransform.Fill(1f); img.preserveAspect = true;
                h += ph + 12f;
            }
            Flush();
            Links.Row(_content, (548f - 484f) / 2f, h + 6f, t); h += 6f + 44f + 8f;   // Discord and Buy me a coffee at the end of the notes (user)
            _content.sizeDelta = new Vector2(0f, h + 4f);
            _scroll = view.gameObject.AddComponent<ScrollRect>(); _scroll.content = _content; _scroll.horizontal = false; _scroll.scrollSensitivity = Kit.ScrollSpeed; _scroll.movementType = ScrollRect.MovementType.Clamped;
            var bar = Kit.Node(b, "Bar").At(598, 104, 4, 454);
            var thumb = Kit.Panel(bar, "Handle", Theme.C(t.line2), 2); thumb.rectTransform.Fill();
            var sb = bar.gameObject.AddComponent<Scrollbar>(); sb.direction = Scrollbar.Direction.BottomToTop; sb.handleRect = thumb.rectTransform; sb.targetGraphic = thumb;
            _scroll.verticalScrollbar = sb; _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            // Close: locked until the end has been seen
            _closeBg = Kit.Panel(b, "Close", Theme.C(t.raised2), 9); _closeBg.rectTransform.At(210, 578, 200, 42); _closeBg.raycastTarget = true;
            _closeText = Kit.Text(_closeBg.rectTransform, "T", "Close", Fonts.Bold, 15f, Theme.C(t.mute)); _closeText.alignment = TextAlignmentOptions.Center; _closeText.rectTransform.Fill();
            _closeBg.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (_read) Close(); });
            // Don't show again (user): unticked, the notes come back at the next world load
            _dontShow = false;
            if (Seen == null || Seen.Value != Key)
            {
                var (cb, _) = Kit.Box(b, "DontShow", Theme.C(t.raised), Theme.C(t.line2), 5); cb.rectTransform.At(32, 590, 18, 18); cb.raycastTarget = true;
                var tick = Kit.IconImg(cb.rectTransform, "check", Theme.C(t.accent), 14f); tick.rectTransform.At(2, 2, 14, 14); tick.enabled = false;   // as the trash's Don't ask again
                var lab = Kit.Text(b, "DontShowLabel", "Don't show again", Fonts.Medium, 13f, Theme.C(t.dim)); lab.alignment = TextAlignmentOptions.MidlineLeft; lab.rectTransform.At(58, 586, 140, 26); lab.raycastTarget = true;
                void Flip() { _dontShow = !_dontShow; tick.enabled = _dontShow; }
                cb.gameObject.AddComponent<Button>().onClick.AddListener(Flip); lab.gameObject.AddComponent<Button>().onClick.AddListener(Flip);
            }
            _read = false; if (!mustRead) Unlock();
            Log.Info("what's new: shown");
        }

        private static CanvasGroup _fade; private static RectTransform _box; private static float _born, _closing = -1f; private static bool _mustRead;
        internal static bool MustRead => Open && _mustRead && !_read;

        private static void TickOpen()
        {
            // in: fade and rise 24 px with a slight grow over 0.3 s; out: fade over 0.18 s, then gone
            float k = _closing >= 0f ? 1f - Mathf.Clamp01((Time.unscaledTime - _closing) / 0.18f) : Mathf.Clamp01((Time.unscaledTime - _born) / 0.3f);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);   // ease out
            if (_fade != null) _fade.alpha = e;
            if (_box != null) { _box.anchoredPosition = new Vector2(0f, -24f * (1f - e)); _box.localScale = Vector3.one * (0.96f + 0.04f * e); }
            if (_closing >= 0f) { if (k <= 0f) Finish(); return; }
            if (_read || _scroll == null) return;
            bool end = _content.rect.height <= ((RectTransform)_scroll.transform).rect.height + 1f || _scroll.verticalNormalizedPosition <= 0.01f;
            if (!end) return;
            Unlock();
        }

        private static void Unlock()
        {
            _read = true; var t = Plugin.Instance.ThemeData;
            _closeBg.color = Theme.C(t.accent); _closeText.color = new Color(0.03f, 0.1f, 0.06f);
        }

        internal static void Dismiss() { if (Seen != null) Seen.Value = Key; if (Open) { _dontShow = true; Close(); } }   // as Don't show again + Close (dev: screenshot runs)

        internal static void Close()
        {
            if (Seen != null && _dontShow) Seen.Value = Key;
            if (_closing < 0f) { _closing = Time.unscaledTime; if (_fade != null) _fade.blocksRaycasts = false; }
            Log.Info("what's new: read and closed");
        }

        private static void Finish()
        {
            if (_root != null) Object.Destroy(_root.gameObject); FreePictures();
            _root = null; _content = null; _scroll = null; _closeBg = null; _closeText = null; _fade = null; _box = null; _closing = -1f;
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); FreePictures(); _root = null; _checked = false; }
    }
}
