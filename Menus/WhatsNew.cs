using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // "What's new" (user): once a character is in the world after an update, a window with the changes. Close stays locked until
    // the reader has scrolled to the end. Seen is kept per install as a hash of the notes, so editing the notes shows them again.
    internal static class WhatsNew
    {
        internal static bool Open => _root != null;
        private static RectTransform _root, _content;
        private static ScrollRect _scroll;
        private static Image _closeBg; private static TextMeshProUGUI _closeText;
        private static bool _read, _checked;
        private static readonly System.Collections.Generic.List<Sprite> _pictures = new System.Collections.Generic.List<Sprite>();
        private static void FreePictures() { foreach (var sp in _pictures) if (sp != null) { Object.Destroy(sp.texture); Object.Destroy(sp); } _pictures.Clear(); }

        private const string Notes =
@"<b>Inventory</b>
[inventory]
- Tools, Food and Meads sit in one strip along the bottom of the panel.
- Food and Meads now have 3 slots each. Anything that was in slots 4 to 6 moved into your bag, or was dropped at your feet if the bag was full.
- Tabs on the panel's edges: the helmet hides the character column, the crossed axe and sword hides the Tools, Food and Meads rows, and the key pouch and the trash have tabs of their own.
- A new Stats tab, beside Character and Skills, holds your stats, resistances and effects.
- PvP moved to the top panel, next to the settings button.
- The trash works: drop an item on it, or click it while holding one.

<b>HUD</b>
[dock]
- Food and Meads have their own bar, right of the action bar.
- The guardian power sits right of the health bars, and it's bigger.
[buffs]
- Buffs are Valheim's plain icons, a little bigger. Ailments move under the buffs when there are many.
- The minimap has no panel behind it, and its text has a dark edge so it reads over snow.
- Nameplates no longer show through the inventory, menus or text boxes.
- Seasonality's time left shows under the season.

<b>Crafting and building</b>
- Weapons and Armour show a second row of kinds (Swords, Axes, Helmets...).
- Recipes you can't craft yet are as readable as the rest, and the list has a scroll bar.
- The search box starts empty at each station.
- Long stat lines wrap, and the item details scroll when they run long.
- The build menu has an Everything category, shows the piece's name by the cursor, and no longer stalls with every piece unlocked.
- The cultivator and hoe sort their pieces into Crops, Saplings and Terrain.

<b>Elsewhere</b>
- Map pins: the name box is easy to see.
- Find servers opens the community server list in Jatify's look.
[servers]
- Deleting a cloud character or world works.
- The Mods settings list scrolls.
- With ExtraSlots installed, Jatify leaves its slots alone.

<b>Where to change them</b>
Press Esc, then Jatify UI. The HUD section has the new switches: Food timers as buffs, Buff frames, Minimap panel, Compass minimal, Power panel and Key hints. Action bar panels now covers every bar.
[settings-hud]
The Inventory section remembers whether the character column and the Tools, Food and Meads rows are folded away (the tabs on the panel's edges do the same).
[settings-inv]
These notes are always there under Esc, What's new.

Thanks for the reports and ideas!";

        internal static BepInEx.Configuration.ConfigEntry<string> Seen;
        internal static void Bind(BepInEx.Configuration.ConfigFile c) => Seen = c.Bind("Panels", "WhatsNewSeen", "", "Which What's new notes you have read (set by the game; clear it to see them again).");

        private static string Key => Notes.GetStableHashCode().ToString();

        internal static void Tick()
        {
            if (_root != null) { TickOpen(); return; }
            if (_checked || Seen == null || Player.m_localPlayer == null || Root.Canvas == null) return;
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
            var title = Kit.Text(b, "Title", "What's new in Jatify UI", Fonts.Norse, 24f, Color.white); title.alignment = TextAlignmentOptions.Center; title.rectTransform.At(0, 22, 620, 34);
            var sub = Kit.Text(b, "Sub", mustRead ? "Scroll to the end to close" : "The changes in this version", Fonts.Medium, 13f, Theme.C(t.dim)); sub.alignment = TextAlignmentOptions.Center; sub.rectTransform.At(0, 56, 620, 20);
            // the notes in a scrolling well
            var view = Kit.Node(b, "View").At(28, 88, 564, 470); view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            _content = Kit.Node(view, "Content"); _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            // the notes in pieces: text, and a picture where a line reads [name] (assets/whatsnew/<name>.jpg)
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
            _content.sizeDelta = new Vector2(0f, h + 4f);
            _scroll = view.gameObject.AddComponent<ScrollRect>(); _scroll.content = _content; _scroll.horizontal = false; _scroll.scrollSensitivity = Kit.ScrollSpeed; _scroll.movementType = ScrollRect.MovementType.Clamped;
            var bar = Kit.Node(b, "Bar").At(598, 88, 4, 470);
            var thumb = Kit.Panel(bar, "Handle", Theme.C(t.line2), 2); thumb.rectTransform.Fill();
            var sb = bar.gameObject.AddComponent<Scrollbar>(); sb.direction = Scrollbar.Direction.BottomToTop; sb.handleRect = thumb.rectTransform; sb.targetGraphic = thumb;
            _scroll.verticalScrollbar = sb; _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            // Close: locked until the end has been seen
            _closeBg = Kit.Panel(b, "Close", Theme.C(t.raised2), 9); _closeBg.rectTransform.At(210, 578, 200, 42); _closeBg.raycastTarget = true;
            _closeText = Kit.Text(_closeBg.rectTransform, "T", "Close", Fonts.Bold, 15f, Theme.C(t.mute)); _closeText.alignment = TextAlignmentOptions.Center; _closeText.rectTransform.Fill();
            _closeBg.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (_read) Close(); });
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

        internal static void Close()
        {
            if (Seen != null) Seen.Value = Key;
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
