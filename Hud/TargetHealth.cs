using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Jatify.UI
{
    // Target health (off by default): the tree, log, rock or other
    // breakable you aim at, within 8 m, shows "current / max" under the crosshair. Read from its ZDO only.
    // Also: Valheim's stagger, piece-health and action bars get the generic skin (glass, our fonts).
    internal static class TargetHealth
    {
        internal static ConfigEntry<bool> On;
        private static TextMeshProUGUI _text; private static float _next;
        private static readonly AccessTools.FieldRef<Destructible, ZNetView> DView = AccessTools.FieldRefAccess<Destructible, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<TreeBase, ZNetView> TView = AccessTools.FieldRefAccess<TreeBase, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<TreeLog, ZNetView> LView = AccessTools.FieldRefAccess<TreeLog, ZNetView>("m_nview");

        internal static void Bind(ConfigFile c) => On = c.Bind("HUD", "TargetHealth", false, "The health of the tree, log or rock you aim at, under the crosshair.");

        internal static void Tick()
        {
            if (!On.Value || Root.Canvas == null || Player.m_localPlayer == null) { if (_text != null) _text.text = ""; return; }
            if (Time.time < _next) return; _next = Time.time + 0.1f;
            if (_text == null)
            {
                _text = Kit.Text(Root.Canvas.transform, "jatify-targethp", "", Fonts.Mono, 12f, Color.white); _text.alignment = TextAlignmentOptions.Center;
                var r = _text.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = new Vector2(0f, -34f); r.sizeDelta = new Vector2(200f, 18f);
            }
            _text.text = InventoryGui.IsVisible() || Menu.IsVisible() ? "" : Read();
        }

        private static string Read()
        {
            var cam = GameCamera.instance; var p = Player.m_localPlayer; if (cam == null) return "";
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, mask) || (hit.point - p.transform.position).sqrMagnitude > 64f) return "";
            float k = 1f + Game.m_worldLevel * Game.instance.m_worldLevelMineHPMultiplier;
            var d = hit.collider.GetComponentInParent<Destructible>(); if (d != null) return Hp(DView(d), d.m_health * k);
            var tr = hit.collider.GetComponentInParent<TreeBase>(); if (tr != null) return Hp(TView(tr), tr.m_health * k);
            var lg = hit.collider.GetComponentInParent<TreeLog>(); if (lg != null) return Hp(LView(lg), lg.m_health * k);
            return "";
        }

        private static string Hp(ZNetView nv, float max)
        {
            if (nv == null || !nv.IsValid() || max <= 0f) return "";
            float hp = nv.GetZDO().GetFloat(ZDOVars.s_health, max); if (hp < 0f) hp = max;
            return $"{Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(max)}";
        }

        internal static void Destroy() { if (_text != null) Object.Destroy(_text.gameObject); _text = null; }
    }
}
