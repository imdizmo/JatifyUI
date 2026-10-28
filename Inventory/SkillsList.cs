using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The Skills tab of the equipment column (design): every skill with icon, name, level and a bar for
    // progress to the next level; a button cycles the order: game order, highest first, lowest first, A to Z.
    // Names and progress as Valheim keeps them ($skill_<type>, GetLevelPercentage).
    internal sealed class SkillsList
    {
        private readonly RectTransform _root, _content;
        private readonly ThemeData _t;
        private readonly TextMeshProUGUI _sortT;
        private int _sort; private string _key;
        private static readonly string[] SortNames = { "Game order", "Highest", "Lowest", "A to Z" };

        internal SkillsList(RectTransform parent, ThemeData t)
        {
            _t = t;
            _root = Kit.Node(parent, "Skills").At(0, 76, 240, 344);
            var (sb, sf) = Kit.Box(parent, "SkillSort", Theme.C(t.raised), Theme.C(t.glassLine), 8); sb.rectTransform.At(150, 44, 90, 26);
            _sortT = Kit.Text(sb.rectTransform, "T", SortNames[0], Fonts.Medium, 11.5f, Theme.C(t.dim)); _sortT.alignment = TextAlignmentOptions.Center; _sortT.rectTransform.Fill();
            sb.raycastTarget = true; sb.gameObject.AddComponent<Button>().onClick.AddListener(() => { _sort = (_sort + 1) % SortNames.Length; _sortT.text = SortNames[_sort]; _key = null; });
            _sortBtn = sb.gameObject;
            var hit = _root.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
            _root.gameObject.AddComponent<RectMask2D>();
            _content = Kit.Node(_root, "Content");
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f); _content.anchoredPosition = Vector2.zero;
            var sr = _root.gameObject.AddComponent<ScrollRect>(); sr.content = _content; sr.horizontal = false; sr.scrollSensitivity = Kit.ScrollSpeed; sr.movementType = ScrollRect.MovementType.Clamped;
            _root.gameObject.SetActive(false); _sortBtn.SetActive(false);
        }

        private readonly GameObject _sortBtn;

        internal void Show(bool on, Player p)
        {
            if (_root.gameObject.activeSelf != on) { _root.gameObject.SetActive(on); _sortBtn.SetActive(on); }
            if (!on || p == null) return;
            var list = p.GetSkills().GetSkillList();
            IEnumerable<Skills.Skill> order = _sort == 1 ? list.OrderByDescending(s => s.m_level) : _sort == 2 ? list.OrderBy(s => s.m_level)
                : _sort == 3 ? list.OrderBy(s => Localization.instance.Localize("$skill_" + s.m_info.m_skill.ToString().ToLower()), System.StringComparer.CurrentCultureIgnoreCase)   // by the names shown, in the game's language
                : (IEnumerable<Skills.Skill>)list;
            var rows = order.ToList();
            var key = _sort + "|" + string.Join(",", rows.Select(s => s.m_info.m_skill + ":" + Mathf.FloorToInt(s.m_level) + ":" + Mathf.RoundToInt(s.GetLevelPercentage() * 100f)));
            if (key == _key) return;
            _key = key;
            foreach (Transform c in _content) Object.Destroy(c.gameObject);
            float y = 0f;
            foreach (var s in rows)
            {
                var icon = Kit.Img(_content, "Icon", s.m_info.m_icon, Color.white, false); icon.rectTransform.At(4, y + 6, 24, 24); icon.preserveAspect = true;
                var n = Kit.Text(_content, "Name", Localization.instance.Localize("$skill_" + s.m_info.m_skill.ToString().ToLower()), Fonts.Medium, 13f, Theme.C(_t.text));
                n.rectTransform.At(36, y + 3, 140, 18); n.overflowMode = TextOverflowModes.Ellipsis;
                var lv = Kit.Text(_content, "Level", Mathf.FloorToInt(s.m_level).ToString(), Fonts.Mono, 13f, Color.white); lv.alignment = TextAlignmentOptions.MidlineRight; lv.rectTransform.At(180, y + 3, 54, 18);
                var track = Kit.Panel(_content, "Bar", new Color(1f, 1f, 1f, 0.1f), 2); track.rectTransform.At(36, y + 25, 198, 4);
                float k = Mathf.Clamp01(s.m_level / 100f);
                if (k > 0f) { var f = Kit.Panel(track.rectTransform, "Fill", Theme.C(_t.accent), 2); var r = f.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(k, 1f); r.offsetMin = r.offsetMax = Vector2.zero; }
                y += 36f;
            }
            _content.sizeDelta = new Vector2(0f, y);
        }
    }
}
