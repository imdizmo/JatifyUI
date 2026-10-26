using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // New character from the Jatify design (stNewChar): "New character" in Norse at the top, the look in
    // a glass panel on the left, the name with Cancel / Create at the bottom. Valheim's own panel stays active but
    // invisible: its PlayerCustomizaton keeps dressing the preview from its sliders, which these controls set, and
    // Create / Cancel are Valheim's own (OnNewCharacterDone / OnNewCharacterCancel).
    internal static class NewCharView
    {
        private const float PW = 340f, PI = PW - 32f;
        private static RectTransform _root, _panel, _sexBox, _beardRow;
        private static readonly List<(RectTransform rt, float h)> _rows = new List<(RectTransform, float)>();
        private static Slider _skin, _hairTone, _blond;
        private static TextMeshProUGUI _hair, _beard;
        private static TMP_InputField _name;
        private static RectTransform _doneBox;
        private static bool _beardShown = true;

        private static PlayerCustomizaton Pc => Fejd.F.m_newCharacterPanel.GetComponentInChildren<PlayerCustomizaton>(true);
        internal static bool Visible => _root != null && _root.gameObject.activeSelf;

        internal static void Show()
        {
            Party.Clear();   // the four at the fire stay up for the title screen; here only the new one stands (user: they overlapped)
            if (_root == null) Build();
            var group = Fejd.F.m_newCharacterPanel.GetComponent<CanvasGroup>() ?? Fejd.F.m_newCharacterPanel.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _name.text = "";
            var pc = Pc;
            PaintSex(pc != null && pc.m_femaleToggle != null && pc.m_femaleToggle.isOn ? 1 : 0);
            if (pc != null)
            {
                _skin.SetValueWithoutNotify(pc.m_skinHue.value);
                _hairTone.SetValueWithoutNotify(pc.m_hairTone.value);
                _blond.SetValueWithoutNotify(pc.m_hairLevel.value);
            }
            PaintDone(false);
            Focus(_name);
        }

        internal static void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            var group = Fejd.F != null ? Fejd.F.m_newCharacterPanel.GetComponent<CanvasGroup>() : null;
            if (group != null) { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
        }

        private static void Build()
        {
            _root = Fill(Node(CharScreen.Root.parent, "NewCharacter"));
            Vignette(_root);
            var head = At(Node(_root, "title"), 0.5f, 1f, 0f, -24f, 900f, 68f);
            Shadowed(head, "New character", "title", 44f, White, 0f, 0f, 900f, 68f, TextAlignmentOptions.Center);

            // the look (.cf-look): Male / Female, sliders and pickers, 16 px apart
            _panel = Glass(_root, "look").rectTransform;
            _rows.Clear();
            _sexBox = Row(30f);
            _skin = SliderRow("Skin tone", v => { if (Pc != null) Pc.m_skinHue.value = v; });
            _hair = Picker("Hair", () => Pc?.OnHairLeft(), () => Pc?.OnHairRight(), out _);
            _beard = Picker("Beard", () => Pc?.OnBeardLeft(), () => Pc?.OnBeardRight(), out _beardRow);
            _hairTone = SliderRow("Hair tone", v => { if (Pc != null) Pc.m_hairTone.value = v; });
            _blond = SliderRow("Blondness", v => { if (Pc != null) Pc.m_hairLevel.value = v; });
            Layout();

            // the name (.cf-namebox): the field, then Cancel / Create bottom right
            const float bw = 460f, bi = bw - 32f;
            var box = Glass(_root, "name").rectTransform; At(box, 0.5f, 0f, 0f, 40f, bw, 16f + 62f + 10f + 40f + 16f);
            _name = Field(box, "Enter a name", 16f, 16f + 22f, bi, 40f, null, s => PaintDone((s ?? "").Trim().Length >= 3), Done, false, 15, 15f);
            Label(box, "Name", 16f, 16f, bi);
            Secondary(box, "Cancel", 16f + bi - 140f - 8f - 110f, 16f + 62f + 10f, 110f, 40f, () => Fejd.F.OnNewCharacterCancel());
            _doneBox = Place(Node(box, "create"), 16f + bi - 140f, 16f + 62f + 10f, 140f, 40f);
            _root.gameObject.SetActive(false);
        }

        private static void PaintDone(bool can)
        {
            Clear(_doneBox);
            Primary(_doneBox, "Create", 0f, 0f, 140f, 40f, Done, can);
        }

        private static void PaintSex(int on)
        {
            Clear(_sexBox);
            Seg(_sexBox, 0f, 0f, PI, new[] { "Male", "Female" }, on, i =>
            {
                var pc = Pc; if (pc == null) return;
                pc.m_maleToggle.isOn = i == 0; pc.m_femaleToggle.isOn = i == 1; pc.SetPlayerModel(i);
                PaintSex(i);
            });
        }

        private static void Done()
        {
            var n = (_name.text ?? "").Trim();
            if (n.Length < 3) { CharScreen.Toast("The name needs 3 or more letters"); return; }
            Fejd.F.m_csNewCharacterName.text = n;
            Fejd.F.OnNewCharacterDone(false);
            if (Fejd.F.m_newCharacterError.activeSelf) CharScreen.Toast($"There is already a character called {n}");
        }

        internal static void Tick()
        {
            if (!Visible) return;
            var pc = Pc;
            if (pc == null) return;
            Set(_hair, pc.m_selectedHair.text);
            Set(_beard, pc.m_selectedBeard.text);
            bool beard = pc.m_beardPanel.gameObject.activeSelf && !pc.m_femaleToggle.isOn;
            if (beard != _beardShown) { _beardShown = beard; _beardRow.gameObject.SetActive(beard); Layout(); }
            if (Input.GetKeyDown(KeyCode.Escape) && !_name.isFocused) Fejd.F.OnNewCharacterCancel();
        }

        // rows top down, 16 px apart, the hidden beard row left out; the panel fits them
        private static void Layout()
        {
            float y = 16f;
            foreach (var (rt, h) in _rows)
            {
                if (!rt.gameObject.activeSelf) continue;
                Place(rt, 16f, y, PI, h);
                y += h + 16f;
            }
            Place(_panel, 24f, 120f, PW, y);
        }

        private static RectTransform Row(float h) { var rt = Node(_panel, "row"); _rows.Add((rt, h)); return rt; }

        private static Slider SliderRow(string label, Action<float> change)
        {
            var row = Row(18f + 6f + 20f);
            Label(row, label, 0f, 0f, PI);
            return SliderBar(row, 0f, 24f, PI, 0.5f, change);
        }

        // a ‹ name › picker (.cf-pick)
        private static TextMeshProUGUI Picker(string label, Action left, Action right, out RectTransform row)
        {
            row = Row(18f + 6f + 34f);
            Label(row, label, 0f, 0f, PI);
            Secondary(row, null, 0f, 24f, 36f, 34f, left, true, 13f, Glyphs.Triangle, 90f);
            Secondary(row, null, PI - 36f, 24f, 36f, 34f, right, true, 13f, Glyphs.Triangle, -90f);
            return Text(row, "", "title", 19f, White, 44f, 24f, PI - 88f, 34f, TextAlignmentOptions.Center);
        }
    }
}
