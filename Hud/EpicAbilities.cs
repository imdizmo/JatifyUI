using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // EpicLoot's abilities (up to three, from what you wear) as our HUD circles beside the guardian power: icon, key,
    // cooldown sweep and seconds, read every frame from EpicLoot's own bar, which stays (it drives them) but unseen.
    internal sealed class EpicAbilities
    {
        private sealed class Circle { internal RectTransform Root; internal Image Icon, Cd; internal TextMeshProUGUI Key, Secs; }
        private readonly Circle[] _c = new Circle[3];
        private Component _bar; private CanvasGroup _hidden; private System.Collections.IList _icons;
        private static bool _barLooked;
        private static System.Type _barType; private static System.Reflection.FieldInfo _fIcon, _fCd, _fKey, _fSecs;

        internal EpicAbilities(RectTransform dock, ThemeData t)
        {
            for (int i = 0; i < 3; i++)
            {   // 44 px circles, 12 px apart, right of the 52 px guardian power, bottoms aligned with it
                var c = _c[i] = new Circle();
                c.Root = Kit.Node(dock, "Ability" + i).At(534 + 18 + 52 + 14 + i * 56, 168 - 8 - 44, 44, 44);
                Kit.Img(c.Root, "Line", Kit.Circle(), Theme.C(t.glassLine), false).rectTransform.Fill();
                Kit.Img(c.Root, "Fill", Kit.Circle(), Theme.C(t.glass), false).rectTransform.Fill(1f);
                c.Icon = Kit.Img(c.Root, "Icon", null, Color.white, false); c.Icon.rectTransform.Fill(9f); c.Icon.preserveAspect = true;
                c.Cd = Kit.Img(c.Root, "Cooldown", Kit.Circle(), new Color(0f, 0f, 0f, 0.65f), false); c.Cd.rectTransform.Fill(1f);
                c.Cd.type = Image.Type.Filled; c.Cd.fillMethod = Image.FillMethod.Radial360; c.Cd.fillOrigin = (int)Image.Origin360.Top; c.Cd.fillClockwise = false;
                c.Secs = Kit.Text(c.Root, "Secs", "", Fonts.Mono, 10.5f, Theme.C(t.text)); c.Secs.alignment = TextAlignmentOptions.Center; c.Secs.rectTransform.Fill();
                var (cap, _) = Kit.Box(c.Root, "Key", Theme.C(t.raised), Theme.C(t.line2), 5); cap.rectTransform.At(12, 35, 20, 18);
                c.Key = Kit.Text(cap.rectTransform, "K", "", Fonts.Mono, 10.5f, Theme.C(t.text)); c.Key.alignment = TextAlignmentOptions.Center; c.Key.rectTransform.Fill();
                c.Root.gameObject.SetActive(false);
            }
        }

        internal void Tick()
        {
            if (_bar == null)
            {
                if (!_barLooked) { _barLooked = true; _barType = Types.Find("EpicLoot.Abilities.AbilityBar"); }   // once (without EpicLoot it never finds one)
                _bar = _barType != null && Hud.instance != null ? Hud.instance.GetComponentInChildren(_barType, true) : null;
                if (_bar == null) { foreach (var c in _c) if (c.Root.gameObject.activeSelf) c.Root.gameObject.SetActive(false); return; }
                _icons = Traverse.Create(_bar).Field("AbilityIcons").GetValue() as System.Collections.IList;
                var it = Types.Find("EpicLoot.Abilities.AbilityIcon");
                _fIcon = AccessTools.Field(it, "Icon"); _fCd = AccessTools.Field(it, "CooldownProgress"); _fKey = AccessTools.Field(it, "Binding"); _fSecs = AccessTools.Field(it, "CooldownText");
                _hidden = _bar.GetComponent<CanvasGroup>() ?? _bar.gameObject.AddComponent<CanvasGroup>(); _hidden.alpha = 0f; _hidden.blocksRaycasts = false;
            }
            for (int i = 0; i < 3; i++)
            {
                var c = _c[i]; var ic = _icons != null && i < _icons.Count ? _icons[i] as Component : null;
                bool on = ic != null && ic.gameObject.activeSelf;
                if (c.Root.gameObject.activeSelf != on) c.Root.gameObject.SetActive(on);
                if (!on) continue;
                var icon = _fIcon?.GetValue(ic) as Image; var cd = _fCd?.GetValue(ic) as Image;
                var key = _fKey?.GetValue(ic) as Text; var secs = _fSecs?.GetValue(ic) as Text;
                if (icon != null && c.Icon.sprite != icon.sprite) c.Icon.sprite = icon.sprite;
                c.Cd.fillAmount = cd != null && cd.gameObject.activeInHierarchy ? cd.fillAmount : 0f;
                var s = secs != null && secs.gameObject.activeInHierarchy ? secs.text : ""; if (c.Secs.text != s) c.Secs.text = s;
                var k = key != null ? key.text : ""; if (c.Key.text != k) c.Key.text = k;
            }
        }

        // unload: EpicLoot's own bar shows again
        internal void Restore() { if (_hidden != null) _hidden.alpha = 1f; _bar = null; }
    }
}
