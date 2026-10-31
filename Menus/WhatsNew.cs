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
@"<b>Look</b>
- Orange is the new default colour. Colours you picked yourself in theme.json stay as they are.

<b>Inventory</b>
[inventory]
- Tools, Food and Meads sit in one strip along the bottom of the panel.
- Food and Meads now have 3 slots each. Anything that was in slots 4 to 6 moved into your bag, or was dropped at your feet if the bag was full.
- Tabs on the panel's edges: the helmet shows or hides the character column, the crossed axe and sword folds the Tools, Food and Meads rows away, and the key pouch, the trash and each worn bag have tabs of their own.
- With a short bag, Tools, Food and Meads sit under it beside the character column, and the panel is shorter.
- The character column no longer has hand slots: what you hold is on the action bar and on the figure.
- A new Stats tab, beside Character and Skills, holds your stats, resistances and effects.
- PvP moved to the top panel, next to the settings button.
- The trash works: drop an item on it, or click it while holding one. Click it again within 15 seconds to bring the item back.

<b>HUD</b>
[dock]
- Food and Meads have their own bar, right of the action bar.
- The guardian power sits right of the health bars, and it's bigger.
[buffs]
- Buffs are Valheim's plain icons, a little bigger. Ailments move under the buffs when there are many.
- The minimap has no panel behind it, and its text has a dark edge so it reads over snow.
- The compass is one faint line with the letters (Compass minimal, now on by default).
- Nameplates and the boss bar are finer: the name with a small red knot each side, and a slim bar in the health bar's red.
- Nameplates no longer show through the inventory, menus or text boxes.
- Seasonality's time left shows under the season.

<b>Crafting and building</b>
- Weapons and Armour show a second row of kinds (Swords, Axes, Helmets...).
- Recipes you can't craft yet are as readable as the rest, and the list has a scroll bar.
- The search box starts empty at each station.
- Long stat lines wrap, and the item details scroll when they run long.
- The build menu has an Everything category, shows the piece's name by the cursor, and no longer stalls with every piece unlocked.
- The cultivator and hoe sort their pieces into Crops, Saplings and Terrain.

<b>Other mods</b>
- ExtraSlots: its slots sit in Jatify's panel and bars in place of ours: armour and utilities on the doll, quick slots and food under Tools and Food, ammo in its own bar bottom left. Slots other mods add (backpacks, jewellery, tomes, lanterns) get a Mod slots tab on the left edge. Our three mead slots stay.
- Bags from Backpacks, AdventureBackpacks and RustyBags: a tab for each one you wear, on the panel's right edge, opens it.
- Quick Stack Store: To chests, Restock, Sort and the trash use its actions, its own buttons are hidden, and its sort and quick stack leave your locked slots, worn gear and slot rows alone.
- Valheim Enchantment System: the Enchant button can be clicked again.
- A mod made for an older Valheim that fails, or one that's known to be broken (Sleepover), now gets a warning. So does a screen size that makes clicks miss the cursor.

<b>Elsewhere</b>
- Map pins: the name box is easy to see.
- Find servers opens the community server list in Jatify's look.
[servers]
- Deleting a cloud character or world works.
- The Mods settings list scrolls.
- With ExtraSlots installed, Jatify leaves its slots alone.

<b>Where to change them</b>
Press Esc, then Jatify UI. The HUD section has the new switches: Food timers as buffs, Buff frames, Minimap panel, Compass minimal, Power panel and Key hints. Action bar panels now covers every bar. Nameplate ornaments, Nameplate numbers and Boss icon set the nameplates and boss bar (numbers and icon are off at first).
[settings-hud]
The Inventory section remembers whether the character column and the Tools, Food and Meads rows are folded away (the tabs on the panel's edges do the same).
[settings-inv]
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
            Links.Row(_content, (548f - 484f) / 2f, h + 6f, t); h += 6f + 44f + 8f;   // Discord and Buy me a coffee at the end of the notes (user)
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
            // Don't show again (user): unticked, the notes come back at the next world load
            _dontShow = false;
            if (Seen == null || Seen.Value != Key)
            {
                var (cb, cbFill) = Kit.Box(b, "DontShow", Theme.C(t.raised), Theme.C(t.line2), 5); cb.rectTransform.At(32, 590, 18, 18); cb.raycastTarget = true;
                var tick = Kit.Panel(cb.rectTransform, "Tick", Theme.C(t.accent), 3); tick.rectTransform.Fill(4f); tick.enabled = false;
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
