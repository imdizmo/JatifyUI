using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Crafting panel restyle (vanilla logic, our look): glass panel, our tabs, a dark list well with
    // flat recipe rows and a green selection, the detail card, requirement tiles and a green Craft
    // button. Static parts go through the ledger; recipe rows are rebuilt by Valheim whenever the list
    // refreshes, so they are restyled on the fly (Tick) and need no undo.
    internal static class CraftingStyle
    {
        private static readonly Ledger _l = new Ledger();
        private static InventoryGui _for;
        private static ThemeData _t;
        private static readonly List<Button> _tabs = new List<Button>();
        private static RecipeView _view;
        private static Image _craftFace; private static TextMeshProUGUI _craftText;
        private static GuiBar _progress; private static float _progressW;
        private static (Image line, Image fill, Image icon, TextMeshProUGUI text) _repair;
        private static Image _enchant;   // Valheim Enchantment System's button, when installed
        private static TextMeshProUGUI _station; private static RectTransform _knotL, _knotR; private static Image _stationIcon; private static RectTransform _stationChip; private static TMP_Text _topic; private static string _titleShown; private static GameObject _upLocked;
        private static CraftingStation _stationShown; private static int _levelShown = -1; private static bool _stationInit;
        private static DetailCard _card;
        // Restyled recipe rows with their original font: put back on unload (our fonts are destroyed
        // then) and the marker removed, so the next load styles them again. Dead rows are pruned.
        private static readonly List<(TMP_Text name, TMP_FontAsset font, float size, GameObject marker)> _rows = new List<(TMP_Text, TMP_FontAsset, float, GameObject)>();

        internal static void Apply(ThemeData t)
        {
            var g = InventoryGui.instance;
            if (g == null || g.m_crafting == null) return;
            Restore();
            _for = g; _t = t;
            var c = g.m_crafting;
            foreach (var n in new[] { "Bkg", "Darken", "TabsButtons/TabBorder" }) _l.Off(Find<Graphic>(c, n));
            var (line, pfill) = Kit.Box(c, "jatify-panel", Theme.C(t.glass), Theme.C(t.glassLine), 12);
            _l.Own(line); line.rectTransform.SetAsFirstSibling(); line.rectTransform.Fill(-14f); line.raycastTarget = true;
            Knots.Corners(line, pfill, 12, Theme.C(t.accent));

            // design size: 670 x 818 panel = 642 x 790 content; vanilla's list and detail grow with it
            _l.Rect(c, r => r.sizeDelta = new Vector2(642f, 790f));
            var topic = Find<TMP_Text>(c, "topic");
            _l.Txt(topic, Fonts.Norse, 24f, Theme.C(t.text));
            if (topic != null) _l.Rect(topic.rectTransform, r => r.anchoredPosition = new Vector2(0f, -14f));
            // station chip, top right of the panel (design: "No station · by hand" or the station and its level)
            // the station in the title (user): its icon left of the name, "level N" right of it
            _topic = topic;
            _stationChip = Kit.Node(c, "jatify-station"); _l.Own(_stationChip);
            _stationChip.anchorMin = _stationChip.anchorMax = new Vector2(0.5f, 1f); _stationChip.pivot = new Vector2(0.5f, 0.5f); _stationChip.anchoredPosition = new Vector2(0f, -14f); _stationChip.sizeDelta = new Vector2(400f, 30f);   // same centre line as the title
            _stationIcon = Kit.Img(_stationChip, "Icon", null, Color.white, false); _stationIcon.preserveAspect = true;
            _station = Kit.Text(_stationChip, "Level", "", Fonts.Mono, 11.5f, Theme.C(t.mute)); _station.alignment = TextAlignmentOptions.MidlineLeft;
            _knotL = Kit.Ornament(_stationChip, "jatify-knot-l", Theme.C(t.accent), 100f, 24f, false); _knotR = Kit.Ornament(_stationChip, "jatify-knot-r", Theme.C(t.accent), 100f, 24f, true);   // design .htitle, sized in Tick
            _stationShown = null; _levelShown = -1; _stationInit = false;
            _l.Rect(Find<RectTransform>(c, "RecipeList"), r => { r.anchoredPosition = new Vector2(4f, -196f); r.sizeDelta = new Vector2(236f, 590f); });
            _l.Rect(Find<RectTransform>(c, "Decription"), r => { r.anchoredPosition = new Vector2(-4f, -196f); r.sizeDelta = new Vector2(386f, 590f); });
            var ctop = Kit.Img(c, "jatify-ctopline", null, Theme.C(t.glassLine)); _l.Own(ctop); ctop.rectTransform.At(-14, 191, 670, 1);   // design .ctop border
            Knots.Divider(ctop, false, Theme.C(t.accent));   // between the filters and the list
            CraftFilter.Build(c, t, _l);   // search, chips, can craft, list / grid at y 96..166
            _view = new RecipeView(c, t, _l);
            Pages.Build(c, t, _l);          // New Recipes / Index, laid over this panel when picked
            _card = new DetailCard(Find<RectTransform>(c, "Decription"), g, t, _l);
            _l.Rect(Find<RectTransform>(c, "RecipeList/Recipes"), r => r.sizeDelta = new Vector2(236f, r.sizeDelta.y)); // vanilla's list is 187 wide   // search, chips, can craft, list / grid at y 96..166
            var sepLine = Kit.Img(c, "jatify-line", null, Theme.C(t.glassLine)); _l.Own(sepLine);
            sepLine.rectTransform.anchorMin = new Vector2(0f, 1f); sepLine.rectTransform.anchorMax = new Vector2(1f, 1f); sepLine.rectTransform.pivot = new Vector2(0.5f, 1f);
            sepLine.rectTransform.anchoredPosition = new Vector2(0f, -38f); sepLine.rectTransform.sizeDelta = new Vector2(28f, 1f);
            // Craft / Upgrade: moved under the title, left-aligned; selected = green (set in Tick)
            int ti = 0;
            foreach (var b in new[] { g.m_tabCraft, g.m_tabUpgrade })
            {
                if (b == null) continue;
                var br = (RectTransform)b.transform; var parent0 = br.parent; float tw = ti == 0 ? 57f : 95f, tx = ti++ == 0 ? 5f : 66f;   // design .tabs.u: Craft 57, Upgrade 95, 44 tall
                _l.Do(() => br.SetParent(c, false), () => { if (br != null && parent0 != null) br.SetParent(parent0, false); });
                _l.Rect(br, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(tx, -39f); r.sizeDelta = new Vector2(tw, 44f); });
                var btc = b.GetComponent<ButtonTextColor>(); if (btc != null && btc.enabled) _l.Do(() => btc.enabled = false, () => { if (btc != null) btc.enabled = true; });
                var tr0 = b.transition; _l.Do(() => b.transition = Selectable.Transition.None, () => { if (b != null) b.transition = tr0; });
                var bimg = b.GetComponent<Image>(); var mat0 = bimg != null ? bimg.material : null;
                if (bimg != null) _l.Do(() => bimg.material = null, () => { if (bimg != null) bimg.material = mat0; }); // vanilla's button material darkens it
                _tabs.Add(b);
                if (b == g.m_tabUpgrade)
                {
                    var lockedTab = Kit.Img(c, "jatify-upgrade-locked", null, new Color(0, 0, 0, 0)); _l.Own(lockedTab); lockedTab.rectTransform.At(tx, 39, tw, 44);
                    var lt = Kit.Text(lockedTab.rectTransform, "T", "Upgrade", Fonts.Medium, 13.5f, Theme.C(t.mute)); lt.rectTransform.At(12, 0, 60, 44); lt.alignment = TextAlignmentOptions.MidlineLeft;
                    var li = Kit.IconImg(lockedTab.rectTransform, "lock", Theme.C(t.mute), 12f); li.rectTransform.At(72, 16, 12, 12);
                    var tip = lockedTab.gameObject.AddComponent<UITooltip>(); var upTip = g.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>();
                    if (upTip != null) { lockedTab.raycastTarget = true; tip.m_tooltipPrefab = upTip.m_tooltipPrefab; tip.Set("Upgrade", "Needs a crafting station."); }
                    _upLocked = lockedTab.gameObject;
                }
                // our own face over the tab (Valheim dims the open tab as "not clickable"); its text hides
                // design text tab: dim label, white with a green underline when open
                var face = Kit.Img(br, "jatify-face", null, new Color(0, 0, 0, 0)); _l.Own(face); face.rectTransform.Fill(); face.raycastTarget = false;
                var vt = b.GetComponentInChildren<TMP_Text>();
                var ft = Kit.Text(face.rectTransform, "T", vt != null ? Localization.instance.Localize(vt.text) : "", Fonts.Medium, 13.5f, Theme.C(t.dim)); ft.alignment = TextAlignmentOptions.Center; ft.rectTransform.Fill();
                ft.text = ft.text.Length > 0 ? char.ToUpperInvariant(ft.text[0]) + ft.text.Substring(1).ToLowerInvariant() : ft.text;
                var ul = Kit.Panel(face.rectTransform, "On", Theme.C(t.accent), 1); ul.rectTransform.anchorMin = new Vector2(0f, 0f); ul.rectTransform.anchorMax = new Vector2(1f, 0f); ul.rectTransform.pivot = new Vector2(0.5f, 0f);
                ul.rectTransform.offsetMin = new Vector2(12f, -1f); ul.rectTransform.offsetMax = new Vector2(-12f, 1f);
                if (vt != null) _l.Rect(vt.rectTransform, r => r.localScale = Vector3.zero);
                _l.Img(b.GetComponent<Image>(), Kit.Rounded(8), new Color(0, 0, 0, 0));
                // the selected tab is a non-clickable button: vanilla's disabled tint turned our fill black
                var bt = b; var cb0 = bt.colors; var cb = cb0; cb.disabledColor = new Color(1.6f, 1.6f, 1.6f, 1f);
                _l.Do(() => bt.colors = cb, () => { if (bt != null) bt.colors = cb0; });
                _l.Txt(b.GetComponentInChildren<TMP_Text>(), Fonts.Medium, 13f, Theme.C(t.text));
            }
            var well = Find<Image>(c, "RecipeList/Recipes");
            _l.Img(well, Kit.Rounded(10), new Color(0f, 0f, 0f, 0.28f));
            var desc = Find<Image>(c, "Decription");
            _l.Img(desc, Kit.Rounded(10), new Color(0, 0, 0, 0));   // design: the detail sits on the panel itself
            foreach (Transform res in Find<Transform>(c, "Decription/requirements"))
            {
                _l.Img(res.GetComponent<Image>(), Kit.Rounded(10), new Color(0f, 0f, 0f, 0.3f));
                _l.Txt(Find<TMP_Text>(res, "res_name"), Fonts.Medium, 11f, Theme.C(t.dim));
                _l.Txt(Find<TMP_Text>(res, "res_amount"), Fonts.Mono, 12f, Theme.C(t.text));
            }
            if (g.m_craftButton != null)
            {   // our face over Valheim's Craft button (it dims itself when you lack materials)
                var cbt = (RectTransform)g.m_craftButton.transform;
                _l.Img(cbt.GetComponent<Image>(), Kit.Rounded(8), new Color(0f, 0f, 0f, 0f));   // vanilla's frame gone, still the click target
                _craftFace = Kit.Panel(cbt, "jatify-face", Theme.C(t.accent), 8); _l.Own(_craftFace); _craftFace.rectTransform.Fill(); _craftFace.raycastTarget = false;
                _craftText = Kit.Text(_craftFace.rectTransform, "T", "Craft", Fonts.Bold, 14f, new Color(0.02f, 0.08f, 0.05f)); _craftText.alignment = TextAlignmentOptions.Center; _craftText.rectTransform.Fill(); _craftText.enableAutoSizing = true; _craftText.fontSizeMin = 11f; _craftText.fontSizeMax = 14f;
                var vt = g.m_craftButton.GetComponentInChildren<TMP_Text>(); if (vt != null) _l.Rect(vt.rectTransform, r => r.localScale = Vector3.zero);
            }
            // the crafting progress: fills the Craft button's place (vanilla's is 320 wide), our font on it
            _l.Rect(Find<RectTransform>(c, "Decription/craft_button_panel/Progress"), r => { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; });
            var ptext = Find<TMP_Text>(c, "Decription/craft_button_panel/Progress/Text");
            _l.Txt(ptext, Fonts.Bold, 14f, Color.white);
            if (ptext != null) _l.Rect(ptext.rectTransform, r => { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; });
            _progress = Find<GuiBar>(c, "Decription/craft_button_panel/Progress/progressbar"); _progressW = -1f;
            var pg0 = _progress; _l.Do(() => { }, () => { if (pg0 != null) pg0.SetWidth(320f); });   // vanilla's bar is 320 wide
            var bar = Find<Image>(c, "Decription/craft_button_panel/Progress/progressbar/bar");
            _l.Img(bar, Kit.Rounded(8), Theme.C(t.accent));
            _l.Img(Find<Image>(c, "Decription/craft_button_panel/Progress/progressbar/bkg"), Kit.Rounded(8), new Color(0f, 0f, 0f, 0.6f));
            // Valheim's station picture and level star: our station chip says the same ("Forge · level 7")
            foreach (var n in new[] { "station_icon", "Level", "Level/level_text" }) _l.Off(Find<Graphic>(c, n));   // Valheim re-activates them; drawing them off sticks
            // repair: Valheim's button (its logic, its active / interactable state) as a Jatify button at the
            // end of the Craft / Upgrade row; its wooden tile, selection frame and glow hidden
            foreach (var n in new[] { "RepairSimple", "selected_frame/repairpanel" }) _l.Off(Find<Graphic>(c, n));
            if (g.m_repairButtonGlow != null) _l.Off(g.m_repairButtonGlow);
            if (g.m_repairButton != null)
            {
                var rb = g.m_repairButton; var parent0 = rb.transform.parent;
                Sprite vanillaIcon = null;   // Valheim's repair hammer, reused small (user)
                foreach (var im in rb.GetComponentsInChildren<Image>(true)) if (im.sprite != null && im != g.m_repairButtonGlow) { vanillaIcon = im.sprite; if (im.sprite.name.Contains("repair") || im.sprite.name.Contains("hammer")) break; }
                _repair = Pill(c, rb, vanillaIcon ?? Kit.Icon("hammer"), "Repair", new Vector2(-4f, -46f), 100f);
                // Valheim Enchantment System copies the repair button and its wooden tile (74 px higher) when the inventory is
                // built: its button as ours, beside Repair (Tick), its tile hidden
                var en = parent0 != null ? parent0.Find("enchantment_menu")?.GetComponent<Button>() : null;
                if (en != null)
                {
                    Sprite enIcon = null; var eim = en.transform.Find("Image")?.GetComponent<Image>(); if (eim != null) enIcon = eim.sprite;
                    _enchant = Pill(c, en, enIcon, "Enchant", new Vector2(-110f, -46f), 108f).line;
                    if (g.m_repairPanel != null) foreach (Transform sib in g.m_repairPanel.parent) if (sib.name == g.m_repairPanel.name + "(Clone)") foreach (var gr in sib.GetComponentsInChildren<Graphic>(true)) _l.Off(gr);
                    Ves.ScrollTip(g.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>()?.m_tooltipPrefab, _l);   // its scroll slot: our item tooltip
                    // its info panel's list scrolls at our speed (it's at 15 a wheel step: a crawl)
                    if (HarmonyLib.AccessTools.Field(Types.Find("kg.ValheimEnchantmentSystem.UI.Info_UI"), "_view")?.GetValue(null) is Component info && info != null)
                        foreach (var sr in info.GetComponentsInChildren<ScrollRect>(true)) { var r = sr; float was = r.scrollSensitivity; _l.Do(() => r.scrollSensitivity = Kit.ScrollSpeed, () => { if (r != null) r.scrollSensitivity = was; }); }
                }
            }
        }

        // a Valheim button (its logic, its active / interactable state) as a Jatify button at the end of the Craft / Upgrade row
        private static (Image line, Image fill, Image icon, TextMeshProUGUI text) Pill(RectTransform c, Button b, Sprite icon, string label, Vector2 at, float w)
        {
            var bt = (RectTransform)b.transform; var parent0 = bt.parent;
            _l.Do(() => bt.SetParent(c, false), () => { if (bt != null && parent0 != null) bt.SetParent(parent0, false); });
            _l.Rect(bt, r => { r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 1f); r.anchoredPosition = at; r.sizeDelta = new Vector2(w, 30f); });
            foreach (var gr in b.GetComponentsInChildren<Graphic>(true)) _l.Off(gr);
            var tr0 = b.transition; _l.Do(() => b.transition = Selectable.Transition.None, () => { if (b != null) b.transition = tr0; });
            var (face, fill) = Kit.Box(bt, "jatify-" + label.ToLowerInvariant(), Theme.C(_t.raised), Theme.C(_t.glassLine), 8); _l.Own(face); face.rectTransform.Fill(); face.raycastTarget = true;
            var ri = Kit.Img(face.rectTransform, "I", icon, Color.white, false); ri.rectTransform.At(8, 4, 22, 22); ri.preserveAspect = true;
            var tx = Kit.Text(face.rectTransform, "T", label, Fonts.Medium, 12.5f, Theme.C(_t.dim)); tx.alignment = TextAlignmentOptions.MidlineLeft; tx.rectTransform.At(38, 0, w - 42f, 30);   // a little air after the icon
            b.targetGraphic = fill;
            return (face, fill, ri, tx);
        }

        // Restyle recipe rows that Valheim just (re)built. A row we styled has a child named "jatify".
        internal static void Tick()
        {
            if (_for == null || _t == null || !InventoryGui.IsVisible()) return;
            Plugin.Timed("crafting.list", () => _view?.Tick(_for));
            Plugin.Timed("crafting.pages", Pages.Tick);
            var p = Player.m_localPlayer;
            if (_station != null && p != null)
            {
                var st = p.GetCurrentCraftingStation(); int lv = st != null ? st.GetLevel() : 0;
                var title = _topic != null ? _topic.text : "";
                if (!_stationInit || st != _stationShown || lv != _levelShown || title != _titleShown)
                {
                    _stationInit = true; _stationShown = st; _levelShown = lv; _titleShown = title;   // Valheim renames the title a frame later: follow its text
                    _station.text = st == null ? "" : $"level {lv}";
                    _stationIcon.sprite = st != null ? st.m_icon : null; _stationIcon.enabled = _stationIcon.sprite != null;
                    float half = (_topic != null ? Kit.W(_topic) : 80f) / 2f;   // around the centred title
                    _stationIcon.rectTransform.anchorMin = _stationIcon.rectTransform.anchorMax = _stationIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    _stationIcon.rectTransform.anchoredPosition = new Vector2(-half - 18f, 0f); _stationIcon.rectTransform.sizeDelta = new Vector2(26f, 26f);
                    _station.rectTransform.anchorMin = _station.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); _station.rectTransform.pivot = new Vector2(0f, 0.5f);
                    _station.rectTransform.anchoredPosition = new Vector2(half + 10f, -1f); _station.rectTransform.sizeDelta = new Vector2(120f, 30f);
                    // the knots outside the station icon and level, their lines fading out 8 px short of the panel's edges (335 from its centre)
                    float gl = half + (_stationIcon.enabled ? 43f : 12f), gr = half + (st != null ? 22f + Kit.W(_station) : 12f);
                    Kit.OrnamentWidth(_knotL, Mathf.Max(40f, 327f - gl)); Kit.OrnamentWidth(_knotR, Mathf.Max(40f, 327f - gr));
                    _knotL.anchoredPosition = new Vector2(-gl - _knotL.sizeDelta.x / 2f, 0f); _knotR.anchoredPosition = new Vector2(gr + _knotR.sizeDelta.x / 2f, 0f);
                }
            }
            // Upgrade is hidden by Valheim without a station: the design shows it locked instead
            if (_upLocked != null && _for.m_tabUpgrade != null) { bool show = !_for.m_tabUpgrade.gameObject.activeSelf; if (_upLocked.activeSelf != show) _upLocked.SetActive(show); }
            if (ZInput.GetKey(KeyCode.LeftControl, true) && ZInput.GetKeyDown(KeyCode.F, true)) CraftFilter.FocusSearch();
            if (_craftFace != null && _for.m_craftButton != null)
            {
                bool can = _for.m_craftButton.interactable;
                var vt = _for.m_craftButton.GetComponentInChildren<TMP_Text>();
                _craftFace.color = can ? Theme.C(_t.accent) : new Color(1f, 1f, 1f, 0.08f);   // disabled: a faint face, readable on the card
                _craftText.color = can ? new Color(0.02f, 0.08f, 0.05f) : Theme.C(_t.mute);
                _craftText.text = can ? (vt != null ? vt.text : "Craft") : "Missing materials";
            }
            if (_progress != null && _progress.isActiveAndEnabled)
            {   // GuiBar keeps the width it had at Awake: give it the button's
                float w = ((RectTransform)_progress.transform).rect.width;
                if (w > 1f && Mathf.Abs(w - _progressW) > 0.5f) { _progressW = w; _progress.SetWidth(w); }
            }
            if (_repair.line != null && _for.m_repairButton != null)
            {   // green while something you carry can be repaired here (Valheim makes the button interactable then)
                bool can = _for.m_repairButton.interactable;
                _repair.line.color = Theme.C(can ? _t.accent : _t.glassLine); _repair.fill.color = Theme.C(can ? _t.accentSoft : _t.raised);
                _repair.icon.color = new Color(1f, 1f, 1f, can ? 1f : 0.45f);   // Valheim's own colours, faded when there is nothing to repair
                _repair.text.color = can ? Color.white : Theme.C(_t.mute);
            }
            if (_enchant != null && _for.m_repairButton != null)
            {
                var er = (RectTransform)_enchant.transform.parent; float x = _for.m_repairButton.gameObject.activeSelf ? -110f : -4f;
                if (er.anchoredPosition.x != x) er.anchoredPosition = new Vector2(x, -46f);
            }
            Plugin.Timed("crafting.card", () => _card?.Tick());
            foreach (var b in _tabs)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                bool on = !b.interactable;                                   // vanilla marks the open tab as not clickable
                var face = b.transform.Find("jatify-face"); if (face == null) continue;
                face.GetComponentInChildren<TMP_Text>().color = on ? Color.white : Theme.C(_t.dim);
                var ulx = face.Find("On"); if (ulx != null && ulx.gameObject.activeSelf != on) ulx.gameObject.SetActive(on);
            }
            var root = _for.m_recipeListRoot;
            if (root == null) return;
            for (int i = 0; i < root.childCount; i++)
            {
                var row = root.GetChild(i);
                var old = row.Find("jatify");
                var oldName = Find<TMP_Text>(row, "name");
                if (old != null && (oldName == null || oldName.font != null)) continue;   // styled and healthy
                if (old != null) Object.DestroyImmediate(old.gameObject);              // styled by an older load whose font is gone
                var marker = Kit.Node(row, "jatify").gameObject; marker.SetActive(false);
                var bkg = Find<Image>(row, "bkg"); if (bkg != null) bkg.color = new Color(1f, 1f, 1f, 0.03f);
                var sel = Find<Image>(row, "selected"); if (sel != null) { sel.sprite = Kit.Rounded(6); sel.type = Image.Type.Sliced; sel.color = Theme.C(_t.accentSoft); }
                var name = Find<TMP_Text>(row, "name");
                var vanillaFont = name != null && name.font == null ? _for.m_recipeElementPrefab.GetComponentInChildren<TMP_Text>(true)?.font : name?.font;
                if (name != null) { _rows.RemoveAll(r => r.marker == null); _rows.Add((name, vanillaFont, name.fontSize, marker)); name.font = Fonts.Medium; name.fontSize = 13.5f; name.enableAutoSizing = false; }
                var icon = Find<RectTransform>(row, "icon");
                if (CraftFilter.Grid)
                {   // tiles: raised square, icon filling it
                    if (bkg != null) { bkg.sprite = Kit.Rounded(8); bkg.type = Image.Type.Sliced; bkg.color = Theme.C(_t.raised); }
                    if (icon != null) { icon.anchorMin = Vector2.zero; icon.anchorMax = Vector2.one; icon.offsetMin = new Vector2(6, 6); icon.offsetMax = new Vector2(-6, -6); }
                }
                else if (icon != null) { icon.sizeDelta = new Vector2(26f, 26f); icon.anchoredPosition = new Vector2(6f, 0f); }
                if (name != null) name.rectTransform.anchoredPosition = new Vector2(40f, 0f);
            }
        }

        internal static void Restore()
        {
            _tabs.Clear(); _view = null; _card = null; _craftFace = null; _craftText = null; _progress = null; _repair = default; _enchant = null; _station = null; _stationIcon = null; _stationChip = null; _knotL = _knotR = null; _upLocked = null;
            CraftFilter.Destroy(); Pages.Destroy();
            foreach (var (name, font, size, marker) in _rows)
            {
                if (name != null) { name.font = font; name.fontSize = size; }
                if (marker != null) Object.Destroy(marker);
            }
            _rows.Clear();
            _l.Undo(); _for = null;
        }

        private static T Find<T>(Transform root, string path) where T : Component
        {
            var t = root != null ? root.Find(path) : null;
            return t != null ? t.GetComponent<T>() : null;
        }
    }
}
