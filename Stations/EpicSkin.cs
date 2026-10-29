using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // EpicLoot's enchanting table in our look, in place: its layout, lists and logic stay its own; we swap the wood
    // panel, buttons, item cells and fonts. Every part we change carries a SkinMark with what it was, so unloading puts
    // it all back, and parts EpicLoot rebuilds (its lists) take the look again on the next pass.
    internal static class EpicSkin
    {
        internal sealed class SkinMark : MonoBehaviour
        {
            internal Sprite Sprite; internal Material Mat; internal Image.Type Type; internal Color Color, BicDefault, BicDisabled; internal bool Bic;
            internal TMP_FontAsset Font; internal Color TextColor; internal bool Scrim; internal float Size;
            internal bool Btn; internal Selectable.Transition Trans; internal ColorBlock Colors; internal SpriteState Sprites; internal Color BtcDisabled, BtcDefaultWas, BtcMeshWas; internal Color IconDisabled; internal ButtonImageColor IconBic;
            internal bool Own; internal bool Lay; internal Component Stc; internal Color StcDefault, StcDisabled; internal RectOffset Pad; internal TextAnchor Align; internal float Spacing;
        }

        private static bool _tableLooked;

        private static System.Type _tableUi, _stcType; private static bool _stcLooked; private static float _next;
        private static readonly List<Image> _imgs = new List<Image>(); private static readonly List<TMP_Text> _txts = new List<TMP_Text>(); private static readonly List<Selectable> _btns = new List<Selectable>();
        private static readonly AccessTools.FieldRef<ButtonImageColor, Color> BicDefault = AccessTools.FieldRefAccess<ButtonImageColor, Color>("m_defaultColor");

        private static readonly List<(Selectable b, Image face, Color color)> _tabs = new List<(Selectable, Image, Color)>();   // every control we restyled, with its face colour (destroyed ones drop out)
        private static GameObject _dim, _panel; private static bool _mapOff;

        // while its table is open EpicLoot reports the big map as open, so our HUD (our minimap frame with it) steps away;
        // Valheim's small map, really still up, would show bare in its place: it goes too, and comes back after
        private static void SmallMap(bool tableOpen)
        {
            var mm = Minimap.instance; if (mm == null || mm.m_smallRoot == null) { _mapOff = false; return; }
            if (tableOpen && mm.m_smallRoot.activeSelf) { mm.m_smallRoot.SetActive(false); _mapOff = true; }
            else if (!tableOpen && _mapOff) { _mapOff = false; mm.m_smallRoot.SetActive(mm.m_mode == Minimap.MapMode.Small); }
        }
        internal static void Tick()
        {
            if (!Epic.On) return;
            bool open = _panel != null && _panel.activeInHierarchy;
            if (_dim != null && _dim.activeSelf != open) _dim.SetActive(open);
            SmallMap(open);
            Dock();
            TextsDialogLook();
            PaintTabs();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.4f;   // its lists rebuild when you pick a tab or an item; a pass is cheap
            if (!_tableLooked) { _tableLooked = true; _tableUi = Types.Find("EpicLoot_UnityLib.EnchantingTableUI"); }   // once (without EpicLoot it is never found)
            var ui = _tableUi != null ? Traverse.Create(_tableUi).Property("instance").GetValue() as Component : null;
            var panel = ui != null ? ui.transform.Find("Panel") : null;
            if (panel == null || !panel.gameObject.activeInHierarchy) return;
            Skin(ui.transform);   // the panel, and its result / augment cards (made on the window's root)
            Center(panel.Find("Tabs"));
            if (_dim == null)
            {   // the world dims behind it, as behind our inventory: coloured text needs the dark (its own Scrim comes and goes)
                var d = Kit.Img(panel.parent, "jatify-dim", null, new Color(0f, 0f, 0f, 0.55f), false); d.raycastTarget = false;
                d.rectTransform.anchorMin = Vector2.zero; d.rectTransform.anchorMax = Vector2.one; d.rectTransform.offsetMin = d.rectTransform.offsetMax = Vector2.zero;
                d.transform.SetSiblingIndex(panel.GetSiblingIndex()); d.gameObject.AddComponent<SkinMark>().Own = true; _dim = d.gameObject; _panel = panel.gameObject;
            }
        }

        // Valheim's texts dialog, where EpicLoot's compendium pages open (our Texts page lists them): our green for the picked
        // row instead of orange, list text at our size, a solid panel over a darker world
        private static float _nextTexts;
        private static void TextsDialogLook()
        {
            var d = InventoryGui.instance != null ? InventoryGui.instance.m_textsDialog : null;
            if (d == null || !d.gameObject.activeInHierarchy || Time.unscaledTime < _nextTexts) return;
            _nextTexts = Time.unscaledTime + 0.3f; var t = Plugin.Instance.ThemeData;
            foreach (var i in d.GetComponentsInChildren<Image>(true))
            {
                if (i.GetComponent<SkinMark>() != null) continue;
                Color? want = i.name == "selected" ? new Color(Theme.C(t.accent).r, Theme.C(t.accent).g, Theme.C(t.accent).b, 0.22f)
                    : i.name == "darken" ? new Color(0f, 0f, 0f, 0.6f)
                    : i.name == "bkg" && i.transform.parent != null && i.transform.parent.name == "Texts_frame" ? new Color(Theme.C(t.glass).r, Theme.C(t.glass).g, Theme.C(t.glass).b, 0.97f) : (Color?)null;
                if (want == null) continue;
                var m = i.gameObject.AddComponent<SkinMark>(); m.Sprite = i.sprite; m.Mat = i.material == i.defaultMaterial ? null : i.material; m.Type = i.type; m.Color = i.color;
                if (i.name == "selected") { i.sprite = Kit.Rounded(8); i.type = Image.Type.Sliced; }
                i.color = want.Value;
            }
            foreach (var x in d.GetComponentsInChildren<TMP_Text>(true))
            {
                if (x.name != "name" || x.fontSize <= 15f || x.transform.parent == null || !x.transform.parent.name.StartsWith("SkillElement")) continue;
                var m = x.GetComponent<SkinMark>() ?? x.gameObject.AddComponent<SkinMark>(); if (m.Size == 0f) { m.Size = x.fontSize; m.Font = x.font; m.TextColor = x.color; }
                x.enableAutoSizing = false; x.fontSize = 14f;
            }
        }

        // EpicLoot's trader panels (Haldor: MerchantPanel, Hildir: TemperPanel) sit under the store screen
        internal static Transform TraderPanel()
        {
            var g = StoreGui.instance; if (!Epic.On || g == null || !StoreGui.IsVisible()) return null;
            foreach (var n in new[] { "MerchantPanel", "TemperPanel" }) { var t = g.transform.Find(n); if (t != null && t.gameObject.activeInHierarchy) return t; }
            return null;
        }

        // ... docked 12 px right of our store window (it would sit under it otherwise), our look on it, every frame:
        // EpicLoot places it from its own drag setting
        private static readonly Vector3[] _wc = new Vector3[4], _pc = new Vector3[4];
        private static void Dock()
        {
            var panel = TraderPanel() as RectTransform; var win = StoreWindow.Win;
            if (panel == null || win == null) return;
            win.GetWorldCorners(_wc); panel.GetWorldCorners(_pc);
            float gap = 12f * win.lossyScale.x, left = _wc[2].x + gap, room = Screen.width - 8f - left, width = _pc[2].x - _pc[0].x;
            if (width > room + 1f && room > 0f) { panel.localScale *= room / width; panel.GetWorldCorners(_pc); }   // a narrow screen: smaller, never under us
            float dx = left - _pc[0].x, dy = _wc[1].y - _pc[1].y;   // tops aligned
            if (Mathf.Abs(dx) > 0.5f || Mathf.Abs(dy) > 0.5f) panel.position += new Vector3(dx, dy, 0f);
            if (Time.unscaledTime >= _nextTrader) { _nextTrader = Time.unscaledTime + 0.4f; Skin(panel); }
        }
        private static float _nextTrader;

        // the open tab (Valheim: the one you can't press) in our accent, every frame so a click shows at once
        private static void PaintTabs()
        {
            if (_tabs.Count == 0) return;
            var open = Color.Lerp(Theme.C(Plugin.Instance.ThemeData.raised), Theme.C(Plugin.Instance.ThemeData.accent), 0.22f);
            for (int i = _tabs.Count - 1; i >= 0; i--)
            {
                var (b, img, face) = _tabs[i]; if (b == null || img == null) { _tabs.RemoveAt(i); continue; }
                if (!b.isActiveAndEnabled) continue;
                var want = b.IsInteractable() ? face : Tab(b.transform) ? open : new Color(face.r, face.g, face.b, face.a * 0.4f);   // other controls: unavailable fades
                if (img.color != want) img.color = want;
                if (img.overrideSprite != img.sprite) img.overrideSprite = null;   // a sprite swap from before we took over (the open tab's framed sprite) stays until cleared
            }
        }

        // the tab buttons: one centred column, evenly spaced
        private static void Center(Transform tabs)
        {
            var lay = tabs != null ? tabs.GetComponent<VerticalLayoutGroup>() : null; var mark = tabs != null ? tabs.GetComponent<SkinMark>() : null;
            if (lay == null || mark == null || mark.Lay) return;
            mark.Lay = true; mark.Pad = new RectOffset(lay.padding.left, lay.padding.right, lay.padding.top, lay.padding.bottom); mark.Align = lay.childAlignment; mark.Spacing = lay.spacing;
            lay.padding = new RectOffset(0, 0, 0, 0); lay.childAlignment = TextAnchor.MiddleCenter; lay.spacing = 10f;
        }

        internal static void Skin(Transform root)   // also AzuEPI's panels (same Valheim sprites)
        {
            var t = Plugin.Instance.ThemeData;
            root.GetComponentsInChildren(true, _imgs);
            foreach (var i in _imgs) Style(i, t);
            root.GetComponentsInChildren(true, _txts);
            foreach (var x in _txts) { Style(x, t); if (x.name == "TotalQuantity") Chip(x); }
            root.GetComponentsInChildren(true, _btns);
            foreach (var b in _btns) Style(b, t);
            _imgs.Clear(); _txts.Clear(); _btns.Clear();
        }

        // which of its sprites become ours: panel, tab strip, buttons, item cells (the rest: icons, glows, marks, stay)
        private static (Sprite sprite, Color color)? Look(Image i, ThemeData t)
        {
            var name = i.sprite != null ? i.sprite.name : null;
            if (i.name == "Handle" && i.transform.parent != null && i.GetComponentInParent<Scrollbar>() != null) return (Kit.Rounded(6), Theme.C(t.line2));   // a scrollbar thumb: ours, not its orange
            if (i.name == "Selected" && (name == "item_background" || name == "GenericItemBg")) { var a = Theme.C(t.accent); return (Kit.Rounded(8), new Color(a.r, a.g, a.b, 0.12f)); }   // a picked list row: our accent, not its orange
            switch (name)
            {
                case "CheckMark": return (i.sprite, Theme.C(t.accent));   // its bright green tick in our green
                case "woodpanel_large": case "woodpanel_512x512": case "crafting_panel_bkg": case "woodpanel_crafting_240": var g = Theme.C(t.glass); return (Kit.Rounded(14), new Color(g.r, g.g, g.b, 0.97f));   // over the open world, not over our dimmed inventory screen (512: its result and augment cards)
                case "Background" when i.GetComponent<Scrollbar>() != null: return (Kit.Rounded(8), new Color(0f, 0f, 0f, 0.35f));   // a scrollbar track
                case "TabBackground": return (Kit.Rounded(10), new Color(0f, 0f, 0f, 0f));   // no strip: the tab buttons stand on the panel
                case "button": return (Kit.Rounded(8), Theme.C(t.raised));
                case "item_background": case "GenericItemBg":
                    var c = i.color; bool plain = c.r > 0.95f && c.g > 0.95f && c.b > 0.95f, dark = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.4f;
                    return (Kit.Rounded(8), plain ? Theme.C(t.raised) : dark ? new Color(0f, 0f, 0f, 0.35f) : new Color(c.r, c.g, c.b, 0.35f));   // wells and tracks: our dark; a rarity tint keeps its hue, softly
                default: return null;
            }
        }

        private static void Style(Image i, ThemeData t)
        {
            var mark = i.GetComponent<SkinMark>();
            if (mark != null) { if (i.sprite == mark.Sprite) Redo(i, mark, t); return; }   // EpicLoot set its own sprite again
            var look = Look(i, t); if (look == null) return;
            mark = i.gameObject.AddComponent<SkinMark>(); mark.Sprite = i.sprite; mark.Mat = i.material == i.defaultMaterial ? null : i.material; mark.Type = i.type; mark.Color = i.color;
            var bic = i.GetComponent<ButtonImageColor>();
            if (bic != null && bic.m_image == i) { mark.Bic = true; mark.BicDefault = BicDefault(bic); mark.BicDisabled = bic.m_disabledColor; }
            Apply(i, look.Value, bic != null && bic.m_image == i ? bic : null, t);
        }

        private static void Redo(Image i, SkinMark mark, ThemeData t)
        {
            var was = i.sprite; i.sprite = mark.Sprite; var look = Look(i, t); i.sprite = was;
            if (look != null) Apply(i, look.Value, mark.Bic ? i.GetComponent<ButtonImageColor>() : null, t);
        }

        private static void Apply(Image i, (Sprite sprite, Color color) look, ButtonImageColor bic, ThemeData t)
        {
            bool same = i.sprite == look.sprite; i.sprite = look.sprite; i.material = null; if (!same) i.type = Image.Type.Sliced; i.color = look.color;   // Valheim's lit-panel material would wash our flat fill out
            if (bic != null) { BicDefault(bic) = look.color; var r = Theme.C(t.raised); bic.m_disabledColor = Tab(i.transform) ? Theme.C(t.accentSoft) : new Color(r.r, r.g, r.b, 0.45f); }   // it repaints the button every frame; a "disabled" tab is the open one
        }

        private static bool Orange(Color c) => c.r > 0.85f && c.g > 0.45f && c.g < 0.8f && c.b < 0.4f;   // its #FFA13C / #FFB75C

        // Valheim's tab strip shows the open tab as a button you can't press
        private static bool Tab(Transform tr) => tr.parent != null && tr.parent.name == "Tabs";
        private static readonly AccessTools.FieldRef<ButtonTextColor, Color> BtcDefault = AccessTools.FieldRefAccess<ButtonTextColor, Color>("m_defaultColor");
        private static readonly AccessTools.FieldRef<ButtonTextColor, Color> BtcMesh = AccessTools.FieldRefAccess<ButtonTextColor, Color>("m_defaultMeshColor");   // its second default, written last: the label flickered back to orange

        // hover / press: a shade of our colour instead of its wood sprites; the open tab in our accent; tab labels readable
        private static void Style(Selectable b, ThemeData t)
        {
            var img = b.targetGraphic as Image; var mark = img != null ? img.GetComponent<SkinMark>() : null;
            if (mark == null || mark.Btn) return;   // only buttons whose face we restyled, once
            mark.Btn = true; _tabs.Add((b, img, img.color)); mark.Trans = b.transition; mark.Colors = b.colors; mark.Sprites = b.spriteState;
            b.transition = Selectable.Transition.ColorTint; img.overrideSprite = null;
            var cb = ColorBlock.defaultColorBlock;
            cb.normalColor = new Color(0.82f, 0.82f, 0.82f); cb.highlightedColor = Color.white; cb.selectedColor = new Color(0.82f, 0.82f, 0.82f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f); cb.disabledColor = Color.white; cb.fadeDuration = 0.08f; b.colors = cb;
            bool tab = Tab(b.transform);
            var btc = b.GetComponent<ButtonTextColor>();
            if (btc != null)
            {
                mark.BtcDisabled = btc.m_disabledColor; mark.BtcDefaultWas = BtcDefault(btc); btc.m_disabledColor = tab ? Theme.C(t.accent) : Theme.C(t.mute);
                mark.BtcMeshWas = BtcMesh(btc);
                if (Orange(mark.BtcDefaultWas)) BtcDefault(btc) = Theme.C(t.accent);
                if (Orange(mark.BtcMeshWas)) BtcMesh(btc) = Theme.C(t.accent);   // its orange labels (Sacrifice, Enchant, ...) in our green
            }
            foreach (var bic in b.GetComponents<ButtonImageColor>())
                if (bic.m_image != null && bic.m_image != img) { mark.IconBic = bic; mark.IconDisabled = bic.m_disabledColor; bic.m_disabledColor = tab ? Theme.C(t.accent) : Theme.C(t.mute); }   // the icon
            if (!_stcLooked) { _stcLooked = true; _stcType = Types.Find("EpicLoot_UnityLib.SelectableTextColor"); }   // once: without EpicLoot (AzuEPI's screens) it is never found
            var stc = _stcType != null ? b.GetComponent(_stcType) : null;   // EpicLoot's: repaints the label every frame from its own default
            if (stc != null)
            {
                var tr = Traverse.Create(stc); mark.Stc = stc; mark.StcDefault = tr.Field("_defaultColor").GetValue<Color>(); mark.StcDisabled = tr.Field("DisabledColor").GetValue<Color>();
                if (Orange(mark.StcDefault)) tr.Field("_defaultColor").SetValue(Theme.C(t.accent));
                tr.Field("DisabledColor").SetValue(Theme.C(t.mute));
            }
            if (tab) foreach (var x in b.GetComponentsInChildren<TMP_Text>(true)) { var xm = x.GetComponent<SkinMark>() ?? x.gameObject.AddComponent<SkinMark>(); if (xm.Size == 0f) { xm.Size = x.fontSize; if (xm.Font == null) { xm.Font = x.font; xm.TextColor = x.color; } } x.enableAutoSizing = false; x.fontSize = 11f; }
        }

        private static void Style(TMP_Text x, ThemeData t)
        {
            var n = x.font != null ? x.font.name : ""; bool title = n.StartsWith("Valheim-Norsebold");
            var want = title ? Fonts.Norse : x.font == null || n.Contains("Averia") || n.StartsWith("LiberationSans") ? Fonts.Medium : null;   // no font at all (some of AzuEPI's labels): drawn invisible
            bool orange = Orange(x.color);   // its orange labels in our green
            if ((want == null || (x.font == want && !(title && x.color != Theme.C(t.text)))) && !orange) return;   // its own and ours stay
            var mark = x.GetComponent<SkinMark>();
            if (mark == null) { mark = x.gameObject.AddComponent<SkinMark>(); mark.Font = x.font ?? Fonts.Medium; mark.TextColor = x.color; }
            if (want != null) x.font = want;
            if (title || orange && !OnButton(x)) x.color = Theme.C(t.text); else if (orange) x.color = Theme.C(t.accent);   // titles and plain list text in our text colour; button labels in our green
        }

        // a label on a button whose face we restyled (images are styled before texts)
        private static bool OnButton(TMP_Text x) { var b = x.GetComponentInParent<Selectable>(); return b != null && b.targetGraphic is Image && b.targetGraphic.GetComponent<SkinMark>() != null; }   // a text that is its own button (a list header) is plain text

        // a count over an item icon (costs: red when you lack it) on a dark chip, so it reads on any icon
        private static void Chip(TMP_Text x)
        {
            var parent = x.transform.parent; if (parent == null) return;
            var chip = parent.Find("jatify-qty") as RectTransform;
            if (chip == null)
            {
                var img = Kit.Img(parent, "jatify-qty", Kit.Rounded(6), new Color(0f, 0f, 0f, 0.9f), false); chip = img.rectTransform; img.type = Image.Type.Sliced;
                chip.gameObject.AddComponent<SkinMark>().Own = true;
                chip.SetSiblingIndex(x.transform.GetSiblingIndex());   // just under the number
                chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0.5f, 0.5f);
            }
            var bounds = x.textBounds;   // where the number is drawn (grid cells: under the icon; list rows: at the right)
            bool on = x.isActiveAndEnabled && !string.IsNullOrEmpty(x.text) && bounds.size.x > 0f; if (chip.gameObject.activeSelf != on) chip.gameObject.SetActive(on);
            if (on) { chip.position = x.rectTransform.TransformPoint(bounds.center); chip.sizeDelta = new Vector2(Mathf.Max(22f, bounds.size.x + 14f), Mathf.Max(20f, bounds.size.y + 8f)); }
        }

        // unload: everything as EpicLoot made it
        internal static void Restore()
        {
            foreach (var m in Object.FindObjectsByType<SkinMark>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m == null) continue;
                if (m.Own) { Object.Destroy(m.gameObject); continue; }   // ours (count chips)
                var i = m.GetComponent<Image>();
                if (i != null && m.Scrim) i.color = m.Color;
                if (i != null && m.Sprite != null && !m.Scrim) { i.sprite = m.Sprite; i.material = m.Mat; i.type = m.Type; i.color = m.Color; var bic = i.GetComponent<ButtonImageColor>(); if (m.Bic && bic != null) { BicDefault(bic) = m.BicDefault; bic.m_disabledColor = m.BicDisabled; } }
                var lay = m.GetComponent<VerticalLayoutGroup>();
                if (lay != null && m.Lay) { lay.padding = m.Pad; lay.childAlignment = m.Align; lay.spacing = m.Spacing; }
                var b = m.GetComponent<Selectable>();
                if (m.Stc != null) { var tr = Traverse.Create(m.Stc); tr.Field("_defaultColor").SetValue(m.StcDefault); tr.Field("DisabledColor").SetValue(m.StcDisabled); }
                if (b != null && m.Btn) { b.transition = m.Trans; b.colors = m.Colors; b.spriteState = m.Sprites; var btc = b.GetComponent<ButtonTextColor>(); if (btc != null) { btc.m_disabledColor = m.BtcDisabled; BtcDefault(btc) = m.BtcDefaultWas; BtcMesh(btc) = m.BtcMeshWas; } if (m.IconBic != null) m.IconBic.m_disabledColor = m.IconDisabled; }
                var x = m.GetComponent<TMP_Text>();
                if (x != null && m.Font != null) { x.font = m.Font; x.color = m.TextColor; }
                if (x != null && m.Size > 0f) x.fontSize = m.Size;
                Object.Destroy(m);
            }
            var g = StoreGui.instance; if (g != null) foreach (var n in new[] { "MerchantPanel", "TemperPanel" }) { var t = g.transform.Find(n); if (t != null) t.localScale = Vector3.one; }   // a narrow screen shrank it
            SmallMap(false); _tableUi = null; _tabs.Clear(); _dim = null; _panel = null;
        }
    }
}
