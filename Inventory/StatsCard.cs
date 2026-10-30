using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // Character stats under the hands row (user's pick for the empty space): armour and block (weight is in the header),
    // movement, comfort, max health / stamina / eitr, and the resistances your gear and effects give
    // (Valheim's own GetDamageModifiers, so meads and set bonuses count). Shown only when the panel has room.
    internal sealed class StatsCard
    {
        private readonly RectTransform _root;
        private readonly TextMeshProUGUI _left, _right, _resist;
        private readonly ThemeData _t;
        private string _key; private float _next;
        internal const float Needed = 210f;
        internal float Height => 210f + (_fxCount > 0 ? 34f + ((_fxCount + 6) / 7) * 36f : 0f);   // grows with the effects block
        private readonly RectTransform _fx; private readonly TextMeshProUGUI _fxLabel;
        private int _fxCount; private string _fxKey;
        private readonly List<StatusEffect> _effects = new List<StatusEffect>();

        internal StatsCard(RectTransform frame, ThemeData t)
        {
            _t = t;
            _root = Kit.Node(frame, "jatify-stats");
            // no line or knot over it: on its own tab it sits right under the tabs (user)
            var l = Kit.Text(_root, "Label", "STATS", Fonts.Mono, 11f, Theme.C(t.mute)); l.characterSpacing = 8f; l.rectTransform.At(0, 15, 240, 14);
            // two columns of "Label  value" lines
            _left = Kit.Text(_root, "Left", "", Fonts.Medium, 12.5f, Theme.C(t.dim)); _left.richText = true; _left.lineSpacing = 18f; _left.rectTransform.At(0, 36, 116, 90);
            _right = Kit.Text(_root, "Right", "", Fonts.Medium, 12.5f, Theme.C(t.dim)); _right.richText = true; _right.lineSpacing = 18f; _right.rectTransform.At(124, 36, 116, 90);
            var rl = Kit.Text(_root, "ResistLabel", "RESISTANCES", Fonts.Mono, 11f, Theme.C(t.mute)); rl.characterSpacing = 8f; rl.rectTransform.At(0, 120, 240, 14);
            _resist = Kit.Text(_root, "Resist", "", Fonts.Medium, 12.5f, Theme.C(t.dim)); _resist.richText = true; _resist.textWrappingMode = TextWrappingModes.Normal; _resist.lineSpacing = 14f; _resist.rectTransform.At(0, 140, 240, 70);
            // effects you have now (user): ailments first in red frames, then buffs; hover for Valheim's tooltip
            _fxLabel = Kit.Text(_root, "EffectsLabel", "EFFECTS", Fonts.Mono, 11f, Theme.C(t.mute)); _fxLabel.characterSpacing = 8f; _fxLabel.rectTransform.At(0, 214, 240, 14);
            _fx = Kit.Node(_root, "Effects").At(0, 234, 240, 120);
        }

        internal void Place(bool show, float top)
        {
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (show) _root.At(19, top, 240, Height);
        }

        private void TickEffects(Player p)
        {
            _effects.Clear(); p.GetSEMan().GetHUDStatusEffects(_effects);
            var list = new List<StatusEffect>();
            foreach (var se in _effects) if (se.m_icon != null && HudView.IsDebuff(se)) list.Add(se);
            foreach (var se in _effects) if (se.m_icon != null && !HudView.IsDebuff(se)) list.Add(se);
            var key = string.Join(",", list.Select(se => se.name));
            _fxCount = list.Count;
            _fxLabel.gameObject.SetActive(list.Count > 0);
            if (key == _fxKey) return;
            _fxKey = key;
            foreach (Transform c in _fx) Object.Destroy(c.gameObject);
            var prefab = InventoryGui.instance != null ? InventoryGui.instance.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>()?.m_tooltipPrefab : null;
            for (int i = 0; i < list.Count; i++)
            {
                var se = list[i]; bool bad = HudView.IsDebuff(se);
                var (b, _) = Kit.Box(_fx, se.name, Theme.C(_t.glass), Theme.C(bad ? _t.health : _t.line2), 7); b.rectTransform.At((i % 7) * 34, (i / 7) * 36, 30, 30);
                var ic = Kit.Img(b.rectTransform, "I", se.m_icon, Color.white, false); ic.rectTransform.At(3, 3, 24, 24); ic.preserveAspect = true;
                b.raycastTarget = true;
                if (prefab != null)
                {
                    var tip = b.gameObject.AddComponent<UITooltip>(); tip.m_tooltipPrefab = prefab;
                    var text = se.GetTooltipString();
                    tip.Set(Localization.instance.Localize(se.m_name), Localization.instance.Localize(string.IsNullOrEmpty(text) ? "" : text));
                }
            }
        }

        private static string Hex(string c) => "#" + ColorUtility.ToHtmlStringRGB(Theme.C(c));
        private string Pair(string label, string value) => $"{label}  <color=#FFFFFF>{value}</color>";

        internal void Tick(Player p, float block)
        {
            if (p == null || !_root.gameObject.activeInHierarchy || Time.unscaledTime < _next) return;
            TickEffects(p);
            _next = Time.unscaledTime + 0.5f;
            int armour = Mathf.RoundToInt(p.GetBodyArmor());
            float w = p.GetInventory().GetTotalWeight(), max = p.GetMaxCarryWeight();
            int move = Mathf.RoundToInt(p.GetEquipmentMovementModifier() * 100f);
            var mods = p.GetDamageModifiers();
            var res = new List<string>();
            foreach (var (name, m) in new[] { ("Blunt", mods.m_blunt), ("Slash", mods.m_slash), ("Pierce", mods.m_pierce), ("Fire", mods.m_fire), ("Frost", mods.m_frost), ("Lightning", mods.m_lightning), ("Poison", mods.m_poison), ("Spirit", mods.m_spirit) })
            {
                var word = Word(m); if (word == null) continue;
                bool good = m == HitData.DamageModifier.Resistant || m == HitData.DamageModifier.VeryResistant || m == HitData.DamageModifier.SlightlyResistant || m == HitData.DamageModifier.Immune || m == HitData.DamageModifier.Ignore;
                res.Add($"{name} <color={(good ? Hex(_t.accent) : Hex(_t.ember))}>{word}</color>");
            }
            var key = $"{armour}|{block}|{w:0}|{max:0}|{move}|{p.GetComfortLevel()}|{p.GetMaxHealth():0}|{p.GetMaxStamina():0}|{p.GetMaxEitr():0}|{string.Join(",", res)}";
            if (key == _key) return;
            _key = key;
            var moveText = move == 0 ? "0%" : (move > 0 ? "+" : "−") + Mathf.Abs(move) + "%";
            _left.text = Pair("Armour", armour.ToString()) + "\n" + Pair("Comfort", p.GetComfortLevel().ToString()) + "\n" + Pair("Health", p.GetMaxHealth().ToString("0"));   // weight: the header chip
            _right.text = Pair("Block", Mathf.RoundToInt(block).ToString()) + "\n" + Pair("Speed", moveText) + "\n" + Pair("Stamina", p.GetMaxStamina().ToString("0")) + "\n" + Pair("Eitr", p.GetMaxEitr().ToString("0"));
            _resist.text = res.Count == 0 ? $"<color={Hex(_t.mute)}>No resistances</color>" : string.Join("   ", res);
        }

        private static string Word(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.Resistant: return "resistant";
                case HitData.DamageModifier.VeryResistant: return "very resistant";
                case HitData.DamageModifier.SlightlyResistant: return "slightly resistant";
                case HitData.DamageModifier.Immune: case HitData.DamageModifier.Ignore: return "immune";
                case HitData.DamageModifier.Weak: return "weak";
                case HitData.DamageModifier.VeryWeak: return "very weak";
                case HitData.DamageModifier.SlightlyWeak: return "slightly weak";
                default: return null;
            }
        }

        internal void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); }
    }
}
