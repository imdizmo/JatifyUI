using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // World modifiers from the Jatify design (stDialog "mods"): preset chips, then one row per rule with
    // its choices (segmented) or a switch. Valheim's own modifier panel (ServerOptionsGUI) stays the source of truth:
    // this window reads its sliders/toggles, sets them, and saves with Valheim's own "Done" (OnServerOptionsDone), so
    // the keys written are exactly what Valheim would write.
    internal static class ModsView
    {
        private const float W = 660f;
        private static Window _win;
        private static RectTransform _list;
        private static World _world;

        private static ServerOptionsGUI Gui => Fejd.F.m_serverOptions;
        private static KeyUI[] Keys => Traverse.Create(typeof(ServerOptionsGUI)).Field("m_modifiers").GetValue<KeyUI[]>() ?? new KeyUI[0];
        private static KeyButton[] Presets => Traverse.Create(typeof(ServerOptionsGUI)).Field("m_presets").GetValue<KeyButton[]>() ?? new KeyButton[0];

        internal static void Hide() { if (_win != null) _win.Show(false); }

        internal static void Open(World w)
        {
            if (w == null) return;
            if (_win == null || _win.Root == null) Build();
            _world = w;
            Wake();
            Fejd.World = w;
            Gui.ReadKeys(w);
            _win.SetTitle("Modifiers · " + w.m_name);
            _win.Show(true);
            Paint();
        }

        private static void Build()
        {
            _win = new Window(CharScreen.Root, "mods", W, 860f, () => _win.Show(false));
            _win.Buttons("Cancel", "Save", Save);
            _list = JatifyLook.Scroll(_win.Body, "list", 0f, 0f, _win.BodyW, _win.BodyH);
        }

        // Valheim's panel only finds its sliders when it has been shown once: show it for a frame's
        // worth of Awake, then hide it again.
        private static void Wake()
        {
            if (Keys.Length > 0) return;
            bool panel = Fejd.F.m_startGamePanel.activeSelf, opts = Gui.gameObject.activeSelf;
            Fejd.F.m_startGamePanel.SetActive(true);
            Gui.gameObject.SetActive(true);
            Gui.gameObject.SetActive(opts);
            Fejd.F.m_startGamePanel.SetActive(panel);
        }

        private static void Paint()
        {
            Clear(_list);
            float w = _win.BodyW, y = 0f;

            // presets (.cf-presets): pill chips, white when it is the current one
            var presets = Presets;
            if (presets.Length > 0)
            {
                float x = 0f;
                var active = Traverse.Create(Gui).Field("m_preset").GetValue();
                foreach (var p in presets)
                {
                    var preset = p;
                    var name = Words(p.m_preset.ToString());
                    bool on = Equals(active, p.m_preset);
                    float cw = BtnW(name, 13f, 12f);
                    if (x > 0f && x + cw > w) { x = 0f; y += 36f; }
                    var chip = Box(_list, "p" + name, on ? White : C("raised"), on ? White : LineSoft, 15); Place(chip.rectTransform, x, y, cw, 30f);
                    Fill(Text(chip.transform, name, "medium", 13f, on ? SegInk : C("dim"), 0f, 0f, cw, 30f, TextAlignmentOptions.Center).rectTransform);
                    if (!on) { On(chip, () => { Gui.OnPresetButton(preset); Paint(); }); HoverLine(chip, C("line2")); }
                    x += cw + 6f;
                }
                y += 30f + 12f;
            }

            foreach (var s in Keys.OfType<KeySlider>().Where(s => (int)s.m_modifier != 0))
            {
                // Valheim's panel is hidden, so look its controls up through its own fields:
                // GetComponentInParent skips inactive objects.
                var slider = Traverse.Create(s).Field("m_slider").GetValue<Slider>();
                var labels = s.m_settings.Select(o => Loc(o.m_name)).ToArray();
                int on = slider != null ? Mathf.RoundToInt(slider.value) : -1;
                float segW = labels.Sum(l => Mathf.Max(32f, Measure(l, "medium", 12.5f) + 20f) + 2f) + 2f;
                bool below = segW > w - 24f - 190f;   // too many choices beside the name: under it
                var row = Row(Words(s.m_modifier.ToString()), Loc(s.m_toolTip), y, below ? 58f + 38f : 58f, below ? w - 24f : w - 24f - segW - 16f);
                Seg(row, below ? 12f : w - 12f - segW, below ? 58f : 14f, 0f, labels, on, i => { if (slider != null) slider.value = i; Paint(); }, 12.5f);
                y += row.sizeDelta.y + 6f;
            }
            foreach (var t in Keys.OfType<KeyToggle>())
            {
                var toggle = Traverse.Create(t).Field("m_toggle").GetValue<Toggle>();
                var row = Row(ToggleName(t), Loc(t.m_toolTip), y, 58f, w - 24f - 60f);
                new OnOff(row, w - 12f - 38f, 18f, toggle != null && toggle.isOn, v => { if (toggle != null) toggle.isOn = v; Paint(); });
                y += 58f + 6f;
            }
            _list.sizeDelta = new Vector2(0f, y);
        }

        // Valheim's own Done: clears the world's keys, writes them from its panel, saves the world
        // file, then returns to the world panel, which comes back to our screen.
        private static void Save()
        {
            _win.Show(false);
            Fejd.World = _world;
            Fejd.F.OnServerOptionsDone();
            CharScreen.Toast($"Modifiers saved for {_world.m_name}");
        }

        // a rule's row (.cf-modrow): its name, Valheim's description under it in Averia
        private static RectTransform Row(string name, string tip, float y, float h, float textW)
        {
            var row = Box(_list, "r" + name, C("raised"), LineSoft, 10); row.raycastTarget = false; Place(row.rectTransform, 0f, y, _win.BodyW, h);
            Text(row.transform, name, "bold", 14f, White, 12f, 6f, textW, 24f);
            if (!string.IsNullOrEmpty(tip)) { Text(row.transform, tip, "lore", 12.5f, C("mute"), 12f, 29f, textW, 24f); Tip(row, tip); }   // the whole text when it is cut
            return row.rectTransform;
        }

        // The menu's label key is its tooltip key without "_tooltip" ($menu_firehazards_tooltip ->
        // $menu_firehazards); when that doesn't resolve, make the key name readable.
        private static string ToggleName(KeyToggle t)
        {
            foreach (var key in new[] { t.m_toolTip?.Replace("_tooltip", ""), "$menu_" + t.m_enabledKey })
            {
                if (string.IsNullOrEmpty(key)) continue;
                var s = Loc(key);
                if (!string.IsNullOrEmpty(s) && !s.StartsWith("[") && !s.StartsWith("$")) return s;
            }
            return Words(char.ToUpperInvariant(t.m_enabledKey[0]) + t.m_enabledKey.Substring(1));
        }

        private static string Words(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])([A-Z])", m => " " + m.Value.ToLowerInvariant());

        private static string Loc(string s) => Localization.instance != null && !string.IsNullOrEmpty(s) ? Localization.instance.Localize(s) : s ?? "";
    }
}
