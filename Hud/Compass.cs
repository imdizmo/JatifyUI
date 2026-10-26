using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Map pins within 400 m that are in view show above the line with their distance below it
    // (nearest 12). Player markers, pings and shouts are left out.
    // Compass strip, top centre, 580 x 62: a tick every 5 degrees (taller every 15), the eight
    // directions as letters (N in ember), 3.2 px per degree so about 180 degrees are in view,
    // soft fade at both ends, the facing notch in the middle. Elements are created once and moved.
    internal sealed class Compass
    {
        private const float W = 580f, PPD = 3.2f;
        private readonly RectTransform _root;
        internal RectTransform Root => _root;
        private readonly RectTransform[] _ticks = new RectTransform[72];
        private readonly RectTransform[] _cards = new RectTransform[8];
        private float _lastYaw = float.NaN;
        private const float PinRange = 400f; private const int MaxPins = 12;
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        private readonly List<(Minimap.PinData pin, float dist, float delta)> _near = new List<(Minimap.PinData, float, float)>();
        private readonly PinView[] _pins = new PinView[MaxPins];
        private readonly Dictionary<int, int> _extra = new Dictionary<int, int>();

        private sealed class PinView
        {
            internal RectTransform Root; internal Image Icon; internal TextMeshProUGUI Dist; internal int Shown = -1;
        }

        internal Compass(Transform parent, ThemeData t)
        {
            _root = Kit.Node(parent, "Compass");
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -12f); _root.sizeDelta = new Vector2(W, 62f);
            var mask = _root.gameObject.AddComponent<RectMask2D>(); mask.softness = new Vector2Int(70, 0);
            var band = Kit.Panel(_root, "Band", new Color(0.05f, 0.05f, 0.06f, 0.32f), 10); band.rectTransform.At(0, 14, W, 34); // contrast against bright sky
            var line = Kit.Img(_root, "Line", null, new Color(1f, 1f, 1f, 0.75f)); line.rectTransform.At(0, 30, W, 1);
            for (int d = 0; d < 360; d += 5)
            {
                if (d % 45 == 0) continue;
                bool major = d % 15 == 0;
                var tk = Kit.Img(_root, "Tick" + d, null, new Color(1f, 1f, 1f, major ? 0.8f : 0.5f));
                _ticks[d / 5] = tk.rectTransform.At(0, major ? 26 : 28, 1, major ? 9 : 5);
            }
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int i = 0; i < 8; i++)
            {
                bool inter = names[i].Length > 1;
                var c = Kit.Text(_root, names[i], names[i], Fonts.Bold, inter ? 12f : 15f, i == 0 ? Theme.C(t.ember) : inter ? Theme.C(t.dim) : Color.white);
                c.alignment = TextAlignmentOptions.Center;
                _cards[i] = c.rectTransform.At(0, 20, 40, 20);
            }
            for (int i = 0; i < MaxPins; i++)
            {
                var v = new PinView { Root = Kit.Node(_root, "Pin" + i).At(0, 0, 40, 58) };
                v.Icon = Kit.Img(v.Root, "Icon", null, Color.white, false); v.Icon.rectTransform.At(12, 0, 16, 16); v.Icon.preserveAspect = true;
                v.Dist = Kit.Text(v.Root, "Dist", "", Fonts.Mono, 10f, Color.white); v.Dist.alignment = TextAlignmentOptions.Top; v.Dist.rectTransform.At(-10, 40, 60, 13);
                v.Root.gameObject.SetActive(false);
                _pins[i] = v;
            }
            var notch = Kit.Img(_root, "Notch", null, Theme.C(t.accent));
            notch.rectTransform.At(W / 2f - 4f, 37f, 8f, 8f); notch.rectTransform.localEulerAngles = new Vector3(0, 0, 45f);
        }

        internal void Tick()
        {
            var cam = GameCamera.instance;
            if (cam == null) return;
            float yaw = cam.transform.eulerAngles.y;
            TickPins(yaw);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, _lastYaw)) < 0.05f) return;
            _lastYaw = yaw;
            for (int d = 0; d < 360; d += 5)
            {
                var rt = d % 45 == 0 ? _cards[d / 45] : _ticks[d / 5];
                float x = W / 2f + Mathf.DeltaAngle(yaw, d) * PPD;
                bool vis = x > -40f && x < W + 40f;
                if (rt.gameObject.activeSelf != vis) rt.gameObject.SetActive(vis);
                if (!vis) continue;
                float w = rt.sizeDelta.x;
                rt.anchoredPosition = new Vector2(x - (d % 45 == 0 ? w / 2f : 0f), rt.anchoredPosition.y);
            }
        }

        private void TickPins(float yaw)
        {
            _near.Clear();
            var map = Minimap.instance; var p = Player.m_localPlayer;
            if (map != null && p != null && (Cfg.CompassPins.Value || Cfg.GraveCompass.Value))
            {
                var me = p.transform.position;
                foreach (var pin in Pins(map))
                {
                    var t = pin.m_type;
                    if (pin.m_icon == null || t == Minimap.PinType.Player || t == Minimap.PinType.Ping || t == Minimap.PinType.Shout || t == Minimap.PinType.None) continue;
                    bool grave = t == Minimap.PinType.Death && Cfg.GraveCompass.Value;   // your grave: at any range, even with compass pins off
                    if (!grave && !Cfg.CompassPins.Value) continue;
                    var to = pin.m_pos - me; to.y = 0f;
                    float dist = to.magnitude;
                    if ((!grave && dist > Cfg.PinRange.Value) || dist < 2f) continue;
                    float delta = Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg); // 0 deg = north (+Z), clockwise
                    if (Mathf.Abs(delta) > 92f) continue;
                    _near.Add((pin, dist, delta));
                }
                // nearest first; a pin that would overlap one already shown folds into it (+n), the strip stays readable
                _near.Sort((a, b) => (a.pin.m_type == Minimap.PinType.Death) != (b.pin.m_type == Minimap.PinType.Death) ? (a.pin.m_type == Minimap.PinType.Death ? -1 : 1) : a.dist.CompareTo(b.dist));   // the grave first, then nearest
                _extra.Clear(); int kept = 0;
                for (int i = 0; i < _near.Count; i++)
                {
                    int into = -1; for (int j = 0; j < kept; j++) if (Mathf.Abs(_near[j].delta - _near[i].delta) * PPD < 58f) { into = j; break; }   // a distance label is ~55 px wide
                    if (into >= 0) { _extra[into] = (_extra.TryGetValue(into, out var n) ? n : 0) + 1; continue; }
                    if (kept == MaxPins) continue;
                    _near[kept++] = _near[i];
                }
                _near.RemoveRange(kept, _near.Count - kept);
            }
            for (int i = 0; i < MaxPins; i++)
            {
                var v = _pins[i]; bool on = i < _near.Count;
                if (v.Root.gameObject.activeSelf != on) v.Root.gameObject.SetActive(on);
                if (!on) continue;
                var (pin, dist, delta) = _near[i];
                if (v.Icon.sprite != pin.m_icon) v.Icon.sprite = pin.m_icon;
                v.Icon.color = pin.m_checked ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                int m = Mathf.RoundToInt(dist / 5f) * 5;           // 5 m steps: the text changes less often
                int more = _extra.TryGetValue(i, out var n2) ? n2 : 0;
                int key = (m * 100 + more) * (Cfg.CompassNames.Value ? -1 : 1);
                if (key != v.Shown)
                {
                    v.Shown = key;
                    string name = Cfg.CompassNames.Value && !string.IsNullOrEmpty(pin.m_name) ? Kit.Plain(Localization.instance.Localize(pin.m_name)) + " " : "";   // others' pins: literal
                    v.Dist.text = name + (more > 0 ? $"{m} m <color=#{ColorUtility.ToHtmlStringRGB(new Color(1f, 1f, 1f, 0.6f))}>+{more}</color>" : m + " m");
                }
                v.Root.anchoredPosition = new Vector2(W / 2f + delta * PPD - 20f, -3f);
            }
        }
    }
}
