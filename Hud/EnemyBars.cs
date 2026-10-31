using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Nameplates over creatures, bosses, other players and mounts in our style
    // (PvP colours for players, the design's tokens): Valheim's four templates are restyled once, so every bar it makes
    // afterwards has our font, rounded bars on a dark track, gold stars, an ember boss name. Other players'
    // names are red with PvP on, blue with it off. Colours of the bars themselves stay Valheim's (it sets them).
    // Everything through the ledger; unloading restores the templates (bars already shown keep the look until
    // they are made again).
    internal static class EnemyBars
    {
        internal static ConfigEntry<bool> On, PvpColors, Knots, Numbers, BossIcon;
        internal static ConfigEntry<float> NameScale;
        private static readonly Ledger _l = new Ledger();
        private static EnemyHud _for;

        internal static void Bind(ConfigFile c)
        {
            On = c.Bind("HUD", "Nameplates", true, "Creature, boss and player nameplates in Jatify's style.");
            PvpColors = c.Bind("HUD", "PlayerPvpColors", true, "Other players' names: red with PvP on, blue with it off.");
            NameScale = c.Bind("HUD", "NameplateScale", 1f, new ConfigDescription("Size of the names above creatures.", new AcceptableValueRange<float>(0.6f, 2f)));
            Knots = c.Bind("HUD", "NameplateOrnaments", true, "The small knots either side of creature and boss names.");
            Numbers = c.Bind("HUD", "NameplateNumbers", false, "Health as numbers (now / most) under creature and boss bars; off (the default): the bar alone.");
            BossIcon = c.Bind("HUD", "BossIcon", false, "The boss's trophy in a ring at its bar's left end; off (the default): the bar alone.");
            foreach (var e in new ConfigEntryBase[] { Knots, Numbers, BossIcon, NameScale }) e.GetType().GetEvent("SettingChanged")?.AddEventHandler(e, (System.EventHandler)((_, __) => { if (_for != null) Restore(); }));   // made again with the new look next frame
        }

        internal static void Tick()
        {
            var e = EnemyHud.instance;
            if (e == null || !On.Value) { if (_for != null) Restore(); return; }
            if (e == _for) { Cover(); return; }
            Restore(); _for = e;
            if (e.m_hudRoot != null)
            {
                var root = e.m_hudRoot; var had = root.GetComponent<CanvasGroup>();
                _cover = had ?? root.AddComponent<CanvasGroup>(); var added = had == null; var cg = _cover;
                _l.Do(() => { }, () => { if (cg != null) { if (added) Object.Destroy(cg); else cg.alpha = 1f; } });
            }
            var t = Plugin.Instance.ThemeData;
            Style(e.m_baseHud, t, false); Style(e.m_baseHudMount, t, false); Style(e.m_baseHudPlayer, t, false); Style(e.m_baseHudBoss, t, true);
            Creature(e.m_baseHud, t); Boss(e.m_baseHudBoss, t);
            Rebuild(e);
        }

        // nameplates under the windows: Valheim draws them after the inventory, so a horse's name sat on the bag, the rename box
        // and the pause menu (user)
        private static CanvasGroup _cover;
        private static void Cover()
        {
            if (_cover == null) return;
            bool covered = InventoryGui.IsVisible() || Menu.IsVisible() || TextInput.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible();
            _cover.alpha = covered ? 0f : 1f;
        }

        // names of what we add to the templates (PlateFx finds them on each copy)
        internal const string KnotL = "jatify-knot-l", KnotR = "jatify-knot-r", HpName = "jatify-hp", HpBack = "jatify-hpback", IconName = "jatify-icon", GlowName = "jatify-glow";

        // Creature nameplates (detail pass, approved): the name in Valheim's Norse between light-red trinity knots (user: as the boss's), a 110 x 6 bar, the level
        // stars (Valheim's own sprites) right-aligned in the corner under it, "cur / max" under its left corner in combat or hovered.
        // Valheim's own Aware / Alerted icons stay where they are (it switches them), Aware at the design's 26 px.
        // User: first the design's sizes x1.4, then finer, closer to the original (an 18 px name, a 110 x 5 bar, 12 px stars, 10.5 px numbers).
        // Coordinates are the template's (centre origin, y up): name centred at 21, icons at 44.
        private static void Creature(GameObject tpl, ThemeData t)
        {
            if (tpl == null) return;
            var tr = tpl.transform; float s = NameScale.Value;
            var name = tr.Find("Name")?.GetComponent<TMP_Text>();
            if (name != null)
            {
                _l.Txt(name, Fonts.Norse, 18f * s, Color.white); Spacing(name, 4f); Mat(name, Fonts.Shadowed(Fonts.Norse));
                _l.Rect(name.rectTransform, r => r.anchoredPosition = new Vector2(r.anchoredPosition.x, 21f));
                Color c = new Color32(0xFF, 0x8A, 0x6B, 0xFF);   // the boss knots' light red (user: both red)
                if (Knots.Value) _l.Own(Kit.Ornament(name.transform, KnotL, c, 9f * s, 11f * s, false, false, false)); if (Knots.Value) _l.Own(Kit.Ornament(name.transform, KnotR, c, 9f * s, 11f * s, true, false, false));   // the knot alone (user)   // tiny and fine (user)
            }
            Bars(tr, 110f, 5f);
            Rim(tr, Theme.C(t.glassLine));   // like our HUD bars (user): a thin light outline round a dark pill
            Stars(tr, "level_2", 1, 55f, -7f, 12f); Stars(tr, "level_3", 2, 55f, -7f, 12f);
            _l.Rect(tr.Find("Aware") as RectTransform, r => { r.sizeDelta = new Vector2(28f, 28f * 138f / 191f); r.anchoredPosition = new Vector2(r.anchoredPosition.x, 44f); });   // the sprite is 191 x 138; Valheim squeezes it into 60 x 60
            var alert = tr.Find("Alerted")?.GetComponent<TMP_Text>();
            if (alert != null) { var f0 = alert.fontSize; _l.Do(() => alert.fontSize = 32f, () => { if (alert != null) alert.fontSize = f0; }); _l.Rect(alert.rectTransform, r => r.anchoredPosition = new Vector2(r.anchoredPosition.x, 46f)); }
            if (Numbers.Value)
            {
                HpBackdrop(tr);
                var hp = _l.Own(Kit.Text(tr, HpName, "", Fonts.Mono, 10.5f, new Color(1f, 1f, 1f, 0.9f))); hp.alignment = TextAlignmentOptions.MidlineLeft; Mat(hp, Fonts.Edged(Fonts.Mono));
                var hr = hp.rectTransform; hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f); hr.pivot = new Vector2(0f, 0.5f); hr.anchoredPosition = new Vector2(-55f, -7f); hr.sizeDelta = new Vector2(90f, 12f);
            }
            _l.Added(tpl.AddComponent<PlateFx>());
        }

        // The boss bar (user: first bigger, then finer, closer to the original): 480 x 10, drawn like our HUD bars (a dark pill with a thin
        // light outline, our health red flat inside), no ticks or glow; a 28 px name in Norse between small light-red knots (no lines); the boss's trophy in a 44 px
        // red ring centred on the bar's left end (the bar stays centred); "Forsaken" under its left end, "cur / max" under its right.
        // Valheim's slow (yellow) health bar stays as it is: our own ghost chunk isn't approved.
        private static void Boss(GameObject tpl, ThemeData t)
        {
            if (tpl == null) return;
            var tr = tpl.transform; var red = Theme.C(t.health);
            var name = tr.Find("Name")?.GetComponent<TMP_Text>();
            if (name != null)
            {
                _l.Txt(name, Fonts.Norse, 28f, new Color32(0xFF, 0xE2, 0xD6, 0xFF)); Spacing(name, 5f);
                Mat(name, Fonts.Shadowed(Fonts.Norse));   // no red glow (user): the creatures' plain shadow
                _l.Rect(name.rectTransform, r => r.anchoredPosition = new Vector2(r.anchoredPosition.x, 22f));
                var kc = new Color32(0xFF, 0x8A, 0x6B, 0xFF);   // the Forsaken tag's lighter red: the deep red vanished on a bright sky (user)
                if (Knots.Value) { _l.Own(Kit.Ornament(name.transform, KnotL, kc, 15f, 18f, false, false, false)); _l.Own(Kit.Ornament(name.transform, KnotR, kc, 15f, 18f, true, false, false)); }   // the knot alone (user)   // small and fine (user), still seen
            }
            Bars(tr, 480f, 10f);
            var health = tr.Find("Health") as RectTransform; if (health == null) return;
            _l.Img(health.Find("bkg")?.GetComponent<Image>(), Kit.Rounded(5), new Color(0f, 0f, 0f, 0.45f));   // like our HUD bars (user): a dark pill, a thin light outline
            Rim(tr, Theme.C(t.glassLine), 5);
            var fast = health.Find("health_fast")?.GetComponent<GuiBar>();
            if (fast != null && fast.m_bar != null) _l.Img(fast.m_bar.GetComponent<Image>(), Kit.Rounded(5), red, Image.Type.Sliced);   // our health bar's own red, flat (user); no glow behind it
            if (BossIcon.Value)
            {   // the trophy badge (a setting, off by default)
                var icon = _l.Own(Kit.Node(health, IconName)); icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f); icon.pivot = new Vector2(0.5f, 0.5f);   // centred on the bar's left end, over it (user: touching the bar)
                icon.anchoredPosition = Vector2.zero; icon.sizeDelta = new Vector2(44f, 44f);   // user: 52, then bigger, then finer
                Kit.Img(icon, "Ring", Kit.Circle(), red, false).rectTransform.Fill();
                Kit.Img(icon, "Fill", Kit.Circle(), new Color32(0x1C, 0x0A, 0x0B, 0xFF), false).rectTransform.Fill(2f);
                var pic = Kit.Img(icon, "Pic", null, Color.white, false); pic.preserveAspect = true; pic.rectTransform.Fill(7f); pic.enabled = false;
                var letter = Kit.Text(icon, "Letter", "", Fonts.Norse, 22f, Color.white); letter.alignment = TextAlignmentOptions.Center; letter.rectTransform.Fill();
            }
            var tag = _l.Own(Kit.Text(health, "jatify-forsaken", "FORSAKEN", Fonts.Norse, 11f, new Color32(0xFF, 0x8A, 0x6B, 0xFF))); tag.characterSpacing = 12f; tag.alignment = TextAlignmentOptions.MidlineLeft;
            Mat(tag, Fonts.Shadowed(Fonts.Norse));
            var gr = tag.rectTransform; gr.anchorMin = gr.anchorMax = Vector2.zero; gr.pivot = new Vector2(0f, 1f); gr.anchoredPosition = new Vector2(BossIcon.Value ? 26f : 0f, -5f); gr.sizeDelta = new Vector2(200f, 14f);   // clear of the badge's lower half, when it shows
            if (Numbers.Value)
            {
                HpBackdrop(health);
                var hp = _l.Own(Kit.Text(health, HpName, "", Fonts.Mono, 11f, new Color(1f, 1f, 1f, 0.9f))); hp.alignment = TextAlignmentOptions.MidlineRight; Mat(hp, Fonts.Edged(Fonts.Mono));
                var hr = hp.rectTransform; hr.anchorMin = hr.anchorMax = new Vector2(1f, 0f); hr.pivot = new Vector2(1f, 1f); hr.anchoredPosition = new Vector2(0f, -5f); hr.sizeDelta = new Vector2(200f, 14f);
            }
            _l.Added(tpl.AddComponent<PlateFx>());
        }

        // a soft dark blob behind the HP numbers (user: readable over sky and grass): our mono font's atlas has too little padding for
        // a TMP underlay wider than a pixel. Drawn before the numbers; PlateFx sizes it to them.
        private static void HpBackdrop(Transform parent)
        {
            var bg = _l.Own(Kit.Img(parent, HpBack, Kit.Glow(), new Color(0f, 0f, 0f, 0.45f)));
            var cv = parent.GetComponentInParent<Canvas>(true); if (cv != null) bg.pixelsPerUnitMultiplier = cv.referencePixelsPerUnit / 100f;
            bg.enabled = false;
        }

        // the health bar and its GuiBars to w x h (a GuiBar takes its full width from its bar when the copy wakes)
        private static void Bars(Transform tr, float w, float h)
        {
            var health = tr.Find("Health") as RectTransform; if (health == null) return;
            _l.Rect(health, r => r.sizeDelta = new Vector2(w, h));
            foreach (var g in health.GetComponentsInChildren<GuiBar>(true))
            {
                if (g.m_bar != null) _l.Rect(g.m_bar, r => r.sizeDelta = new Vector2(w, h));
                var gr = (RectTransform)g.transform; if (gr.anchorMin != Vector2.zero || gr.anchorMax != Vector2.one) _l.Rect(gr, r => r.sizeDelta = new Vector2(w, h));
            }
        }

        // Valheim's "darken" behind the bar as a 1 px rim
        private static void Rim(Transform tr, Color c, int r = 3)
        {
            var dk = tr.Find("Health/darken")?.GetComponent<Image>(); if (dk == null) return;
            _l.Img(dk, Kit.Rounded(r), c); _l.Rect(dk.rectTransform, r => { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(-1f, -1f); r.offsetMax = new Vector2(1f, 1f); });
        }

        // Valheim's stars (craft_icon_32 with craft_icon inside), S px, 1 px apart, their right edge at right, centred at y
        private static void Stars(Transform tr, string lv, int n, float right, float y, float S)
        {
            var c = tr.Find(lv) as RectTransform; if (c == null) return;
            float w = n * S + (n - 1);
            _l.Rect(c, r => { r.sizeDelta = new Vector2(w, S); r.anchoredPosition = new Vector2(right - w / 2f, y); });
            int i = 0;
            foreach (Transform st in c)
            {
                float x = n == 1 ? 0f : i++ == 0 ? -(S + 1f) / 2f : (S + 1f) / 2f;
                _l.Rect(st as RectTransform, r => { r.sizeDelta = new Vector2(S, S); r.anchoredPosition = new Vector2(x, 0f); });
                foreach (Transform inner in st) _l.Rect(inner as RectTransform, r => { r.sizeDelta = new Vector2(S * 0.8f, S * 0.8f); r.anchoredPosition = Vector2.zero; });
            }
        }

        private static void Spacing(TMP_Text x, float em100) { var s0 = x.characterSpacing; _l.Do(() => x.characterSpacing = em100, () => { if (x != null) x.characterSpacing = s0; }); }
        private static void Mat(TMP_Text x, Material m) { if (m == null) return; var m0 = x.fontSharedMaterial; _l.Do(() => x.fontSharedMaterial = m, () => { if (x != null) x.fontSharedMaterial = m0; }); }

        // the huds on screen are copies of the templates as they were: drop them, Valheim makes them again next frame from the
        // templates as they are now (ours, or vanilla after unloading: a hot reload leaves none of our parts behind)
        private static void Rebuild(EnemyHud e)
        {
            var huds = Huds(e); if (huds == null || huds.Count == 0) return;
            foreach (DictionaryEntry kv in huds) if (GuiOf(kv.Value) is GameObject go && go != null) Object.Destroy(go);
            huds.Clear();
        }
        private static System.Reflection.FieldInfo _gui;
        internal static object GuiOf(object hudData) => (_gui ?? (_gui = AccessTools.Field(hudData.GetType(), "m_gui")))?.GetValue(hudData);
        internal static IDictionary HudsOf(EnemyHud e) => Huds(e);

        // the creature's trophy icon, if it drops one (boss badge, mount portrait)
        internal static Sprite Trophy(Character c)
        {
            var cd = c != null ? c.GetComponent<CharacterDrop>() : null; if (cd == null) return null;
            foreach (var d in cd.m_drops)
            {
                var it = d?.m_prefab != null ? d.m_prefab.GetComponent<ItemDrop>() : null;
                if (it != null && it.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) return it.m_itemData.GetIcon();
            }
            return null;
        }

        private static void Style(GameObject tpl, ThemeData t, bool boss)
        {
            if (tpl == null) return;
            var name = tpl.transform.Find("Name")?.GetComponent<TMP_Text>();
            if (name != null) _l.Txt(name, Fonts.Bold, name.fontSize * NameScale.Value, boss ? Theme.C(t.ember) : Color.white);
            var health = tpl.transform.Find("Health");
            if (health != null)
                foreach (var img in health.GetComponentsInChildren<Image>(true))
                {   // our rounded shape; the colour stays (Valheim sets the bar colours itself), the track darker
                    bool track = img.transform == health || img.name.ToLowerInvariant().Contains("bkg") || img.name.ToLowerInvariant().Contains("background");
                    if (img.type == Image.Type.Filled) continue;   // a fill bar: its sprite and type are how it shows health
                    _l.Img(img, Kit.Rounded(3), track ? new Color(0.03f, 0.03f, 0.04f, 0.75f) : img.color, Image.Type.Sliced);
                }
            foreach (var n in new[] { "Health/HealthText", "Stamina/StaminaText" })
            { var tx = tpl.transform.Find(n)?.GetComponent<TMP_Text>(); if (tx != null) _l.Txt(tx, Fonts.Mono, tx.fontSize, Color.white); }
            foreach (var lv in new[] { "level_2", "level_3" })
            {
                var star = tpl.transform.Find(lv); if (star == null) continue;
                foreach (var img in star.GetComponentsInChildren<Image>(true)) { var c0 = img.color; var to = Theme.C(t.stamina); to.a = c0.a; _l.Do(() => img.color = to, () => { if (img != null) img.color = c0; }); }
            }
        }

        internal static void Restore() { var e = _for; _l.Undo(); _for = null; if (e != null) Rebuild(e); }

        // other players' names by PvP (Valheim leaves them white)
        private static readonly AccessTools.FieldRef<EnemyHud, IDictionary> Huds = AccessTools.FieldRefAccess<EnemyHud, IDictionary>("m_huds");
        private static System.Reflection.FieldInfo _nameField; private static float _pvpNext;

        internal static void PvpTick()
        {
            var e = EnemyHud.instance; if (e == null || !On.Value || !PvpColors.Value || Time.unscaledTime < _pvpNext) return;
            _pvpNext = Time.unscaledTime + 0.25f;   // four times a second is plenty for a colour
            var t = Plugin.Instance.ThemeData;
            foreach (DictionaryEntry kv in Huds(e))
            {
                if (!(kv.Key is Player pl) || pl == Player.m_localPlayer) continue;
                if (_nameField == null) _nameField = kv.Value.GetType().GetField("m_name");
                if (_nameField?.GetValue(kv.Value) is TMP_Text nm) { var want = pl.IsPVPEnabled() ? Theme.C(t.health) : new Color(0.45f, 0.68f, 1f); if (nm.color != want) nm.color = want; }
            }
        }
    }

    // On every nameplate Valheim copies from our templates: keeps the knots either side of the name as wide as it, shows
    // "cur / max" (creatures: alerted or hovered; bosses: always), the boss's trophy, the boss bar's slow red pulse.
    // Nothing allocates per frame: the HP text is rewritten only when a number changes.
    internal sealed class PlateFx : MonoBehaviour
    {
        private TMP_Text _name, _hp, _letter; private RectTransform _kl, _kr; private Image _glow, _pic, _hpBack;
        private float _gap; private bool _boss, _found; private int _tries, _cur = -1, _max = -1; private string _shown;
        private Character _c; private BaseAI _ai;

        private void Awake()
        {
            _name = transform.Find("Name")?.GetComponent<TMP_Text>();
            if (_name != null) { _kl = _name.transform.Find(EnemyBars.KnotL) as RectTransform; _kr = _name.transform.Find(EnemyBars.KnotR) as RectTransform; }
            var health = transform.Find("Health");
            _boss = health != null && health.Find("jatify-forsaken") != null;   // the badge is a setting: the tag marks a boss
            _hp = (transform.Find(EnemyBars.HpName) ?? health?.Find(EnemyBars.HpName))?.GetComponent<TMP_Text>();
            _hpBack = (transform.Find(EnemyBars.HpBack) ?? health?.Find(EnemyBars.HpBack))?.GetComponent<Image>();
            _glow = health?.Find(EnemyBars.GlowName)?.GetComponent<Image>();
            _pic = health?.Find(EnemyBars.IconName + "/Pic")?.GetComponent<Image>(); _letter = health?.Find(EnemyBars.IconName + "/Letter")?.GetComponent<TMP_Text>();
            _gap = _boss ? 14f : _kl != null ? _kl.sizeDelta.y / 3f : 8f;   // design: 8 px beside an 18 px name, 12 beside the boss's
        }

        // which character this copy is for: Valheim adds it to its list right after making it
        private void Find()
        {
            var e = EnemyHud.instance; if (e == null) return;
            foreach (DictionaryEntry kv in EnemyBars.HudsOf(e)) if (ReferenceEquals(EnemyBars.GuiOf(kv.Value), gameObject)) { _c = kv.Key as Character; break; }
            if (_c == null) { _found = ++_tries > 30; return; }
            _found = true; _ai = _c.GetBaseAI();
            if (_boss && _pic != null)
            {
                var s = EnemyBars.Trophy(_c); _pic.sprite = s; _pic.enabled = s != null;
                if (s == null && _letter != null) { var n = Localization.instance.Localize(_c.m_name); _letter.text = n.Length > 0 ? n.Substring(0, 1) : ""; }
            }
        }

        // the blob behind the numbers: as wide as they are, 12 px past them each way, on the same anchors
        private void Back()
        {
            if (_hpBack == null) return;
            _hpBack.enabled = _cur >= 0; if (_cur < 0) return;
            var h = _hp.rectTransform; var b = _hpBack.rectTransform; float w = _hp.preferredWidth;
            b.anchorMin = h.anchorMin; b.anchorMax = h.anchorMax; b.pivot = h.pivot;
            b.anchoredPosition = h.anchoredPosition + new Vector2(h.pivot.x < 0.5f ? -12f : 12f, (h.pivot.y - 0.5f) * 16f); b.sizeDelta = new Vector2(w + 24f, h.sizeDelta.y + 16f);
        }

        private void LateUpdate()
        {
            if (!_found) Find();
            if (_name != null && _kl != null && _name.text != _shown) { _shown = _name.text; Kit.Flank(_kl, _kr, Kit.W(_name) / 2f, _gap); }
            if (_glow != null) { var c = _glow.color; c.a = 0.3f + 0.25f * (0.5f - 0.5f * Mathf.Cos(Time.time * 2f * Mathf.PI / 2.6f)); _glow.color = c; }   // design: drop-shadow .45 -> .75 every 2.6 s; softer here, over a bright sky it was a pink box
            if (_hp == null || _c == null) return;
            var p = Player.m_localPlayer;
            bool show = _boss || (_ai != null && _ai.IsAlerted()) || (p != null && p.GetHoverCreature() == _c);
            int cur = show ? Mathf.CeilToInt(_c.GetHealth()) : -1, max = show ? Mathf.CeilToInt(_c.GetMaxHealth()) : -1;
            if (cur != _cur || max != _max) { _cur = cur; _max = max; _hp.text = cur < 0 ? "" : cur + " / " + max; Back(); }
        }
    }
}
