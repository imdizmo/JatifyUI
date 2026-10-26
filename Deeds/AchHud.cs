using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Jatify.UI.Deeds
{
    // Achievements on the HUD, from the design: the tracker top-left (tracked achievements and what
    // is still missing; click the header to fold it) and the "Achievement earned" banners under the
    // compass (newest on top at full size, older ones knocked down to compact, dissolving oldest
    // first, at most four). Data: Deeds' model (Valheim's own achievements, the character's stats).
    internal sealed class AchHud
    {
        private readonly ThemeData _t;
        private readonly RectTransform _tracker, _banners;
        private readonly TextMeshProUGUI _count;
        private readonly List<Card> _cards = new List<Card>();
        private string _shownKey;
        private readonly Queue<Deed> _queue = new Queue<Deed>();
        private readonly List<Banner> _live = new List<Banner>();
        private float _nextArrival;
        private const float Gap = 0.9f, Life = 5.2f, Fade = 0.7f; private const int MaxBanners = 4;

        internal AchHud(Transform parent, ThemeData t)
        {
            _t = t;
            _tracker = Kit.Node(parent, "Tracker");
            _tracker.anchorMin = _tracker.anchorMax = _tracker.pivot = new Vector2(0f, 1f);
            _tracker.anchoredPosition = new Vector2(24f, -22f); _tracker.sizeDelta = new Vector2(300f, 400f);
            var head = Kit.Node(_tracker, "Header").At(0, 0, 300, 26);
            var hit = head.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;
            head.gameObject.AddComponent<Button>().onClick.AddListener(() => Cfg.TrackerCollapsed.Value = !Cfg.TrackerCollapsed.Value);
            var title = Kit.Text(head, "Title", "ACHIEVEMENTS", Fonts.Mono, 11f, Color.white); title.characterSpacing = 12f; title.rectTransform.At(4, 0, 160, 26); title.alignment = TextAlignmentOptions.MidlineLeft;
            var (badge, _) = Kit.Box(head, "Count", Theme.C(t.accent), Theme.C(t.accent), 5); badge.rectTransform.At(150, 5, 22, 16);
            _count = Kit.Text(badge.rectTransform, "N", "", Fonts.Mono, 10f, new Color(0.02f, 0.08f, 0.05f)); _count.rectTransform.Fill(); _count.alignment = TextAlignmentOptions.Center;
            _banners = Kit.Node(parent, "Banners");
            _banners.anchorMin = _banners.anchorMax = _banners.pivot = new Vector2(0.5f, 1f);
            _banners.anchoredPosition = new Vector2(0f, -96f); _banners.sizeDelta = new Vector2(560f, 420f);
        }

        // lowest point of the tracker on screen (design px from the top), 0 when hidden: vanilla's top-left messages go under it
        internal static float Bottom;

        internal void Show(Deed d) { if (Cfg.Banners.Value) _queue.Enqueue(d); }

        internal void Tick()
        {
            // Y folds the tracker to its header and back (hint in the bottom-left corner); not while typing or in a menu
            if (Input.GetKeyDown(KeyCode.Y) && !Typing.Active && !InventoryGui.IsVisible() && !Menu.IsVisible() && !Console.IsVisible() && !TextInput.IsVisible() && !Minimap.IsOpen()
                && (Chat.instance == null || !Chat.instance.HasFocus()))
                Cfg.TrackerCollapsed.Value = !Cfg.TrackerCollapsed.Value;
            TickTracker();
            TickBanners();
        }

        // ---- tracker ------------------------------------------------------------------------------
        private void TickTracker()
        {
            var list = Model.Ready ? Model.Tracked.Select(id => Model.All.FirstOrDefault(d => d.A.m_id == id)).Where(d => d != null && !d.IsEarned).ToList() : new List<Deed>();
            bool show = list.Count > 0 && !InventoryGui.IsVisible() && !StoreGui.IsVisible() && Cfg.Tracker.Value; // HUD only: the inventory takes that corner
            if (_tracker.gameObject.activeSelf != show) _tracker.gameObject.SetActive(show);
            if (!show) { Bottom = 0f; _shownKey = null; return; }   // rebuilt (and measured) when it shows again
            bool folded = Cfg.TrackerCollapsed.Value;
            var key = (folded ? "f" : "") + string.Join("|", list.Select(d => d.A.m_id + ":" + d.Done + ":" + d.Criteria.Sum(c => Mathf.Min(c.Have, c.Need)) + (d.A.m_icon != null && d.A.m_icon.texture != null ? "" : "?")));   // "?": its picture isn't loaded yet; drawn again once it is (user: white squares right after entering a world)
            if (key == _shownKey) return;                 // rebuilt only when progress changes
            _shownKey = key;
            _count.text = list.Count.ToString();
            foreach (var c in _cards) Object.Destroy(c.Root.gameObject);
            _cards.Clear();
            if (folded) { Bottom = 22f + 26f; return; }
            float y = 32f;
            foreach (var d in list) { var c = new Card(_tracker, d, y, _t); _cards.Add(c); y += c.Height + 6f; }
            Bottom = 22f + y;
        }

        private sealed class Card
        {
            internal readonly RectTransform Root; internal readonly float Height;
            internal Card(Transform parent, Deed d, float y, ThemeData t)
            {
                var open = d.Criteria.Where(c => !c.Met).ToList();
                bool single = d.Criteria.Count == 1 && d.Criteria[0].Need > 1f;
                int lines = single ? 0 : Mathf.Min(open.Count, 3) + (open.Count > 3 ? 1 : 0);
                Height = 9 + 22 + 9 + lines * 16 + (lines > 0 ? 6 : 0) + 9;
                var bg = Kit.Panel(parent, "Card", new Color(0.055f, 0.055f, 0.063f, 0.55f), 10);
                Root = bg.rectTransform.At(0, y, 300, Height);
                // the green edge clipped by the card's own rounded shape, so it curves round the corners (user: not a straight line)
                var clip = Kit.Img(Root, "EdgeClip", Kit.Rounded(10), Color.white); clip.type = Image.Type.Sliced; clip.rectTransform.Fill(); clip.raycastTarget = false;
                clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var edge = Kit.Img(clip.rectTransform, "Edge", null, Theme.C(t.accent)); edge.rectTransform.At(0, 0, 4, Height); edge.raycastTarget = false;
                var icon = Kit.Img(Root, "Icon", d.A.m_icon, Color.white, false); icon.rectTransform.At(10, 8, 22, 22); icon.preserveAspect = true;
                var name = Kit.Text(Root, "Name", d.Name, Fonts.Bold, 13.5f, Color.white); name.rectTransform.At(40, 8, 180, 22); name.alignment = TextAlignmentOptions.MidlineLeft; name.overflowMode = TextOverflowModes.Ellipsis;
                string prog = single ? $"{Mathf.Min(d.Criteria[0].Have, d.Criteria[0].Need):0}/{d.Criteria[0].Need:0}" : $"{d.Done}/{d.Criteria.Count}";
                var p = Kit.Text(Root, "Progress", prog, Fonts.Mono, 11f, Theme.C(t.accent)); p.rectTransform.At(220, 8, 70, 22); p.alignment = TextAlignmentOptions.MidlineRight;
                float k = single ? Mathf.Clamp01(d.Criteria[0].Have / Mathf.Max(1f, d.Criteria[0].Need)) : d.Criteria.Count > 0 ? (float)d.Done / d.Criteria.Count : 0f;
                var track = Kit.Panel(Root, "Bar", new Color(1f, 1f, 1f, 0.12f), 2); track.rectTransform.At(40, 34, 250, 3);
                if (k > 0f) { var fill = Kit.Panel(track.rectTransform, "Fill", Theme.C(t.accent), 2); var r = fill.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(k, 1f); r.offsetMin = r.offsetMax = Vector2.zero; }
                float ly = 44f;
                foreach (var c in open.Take(3))
                {
                    var dot = Kit.Img(Root, "Dot", Kit.Circle(), new Color(1f, 1f, 1f, 0.6f), false); dot.rectTransform.At(40, ly + 5, 5, 5);
                    var l = Kit.Text(Root, "Line", c.Label, Fonts.Medium, 12f, new Color(1f, 1f, 1f, 0.82f)); l.rectTransform.At(52, ly, 190, 16); l.overflowMode = TextOverflowModes.Ellipsis;
                    if (c.Need > 1f) { var n = Kit.Text(Root, "N", $"{Mathf.Min(c.Have, c.Need):0}/{c.Need:0}", Fonts.Mono, 10.5f, Theme.C(t.mute)); n.rectTransform.At(240, ly, 50, 16); n.alignment = TextAlignmentOptions.MidlineRight; }
                    ly += 16f;
                }
                if (!single && open.Count > 3) { var m = Kit.Text(Root, "More", $"+{open.Count - 3} more", Fonts.Medium, 11.5f, Theme.C(t.mute)); m.rectTransform.At(52, ly, 190, 16); }
            }
        }

        // ---- banners ------------------------------------------------------------------------------
        private void TickBanners()
        {
            float now = Time.unscaledTime;
            if (_queue.Count > 0 && now >= _nextArrival)
            {
                var b = new Banner(_banners, _queue.Dequeue(), _t, now);
                _live.Insert(0, b);
                foreach (var o in _live.Skip(1)) o.MakeCompact();
                if (_live.Count(x => !x.Leaving) > MaxBanners) _live.Last(x => !x.Leaving).Leave(now); // the oldest makes room
                _nextArrival = now + Gap;
            }
            foreach (var b in _live) if (!b.Leaving && now - b.Born > Life) b.Leave(now);
            float y = 0f;
            for (int i = 0; i < _live.Count; i++) { var b = _live[i]; b.Tick(now, y); y += b.Height + 8f; }
            for (int i = _live.Count - 1; i >= 0; i--) if (_live[i].Gone(now)) { Object.Destroy(_live[i].Root.gameObject); _live.RemoveAt(i); }
        }

        internal void Destroy() { if (_tracker != null) Object.Destroy(_tracker.gameObject); if (_banners != null) Object.Destroy(_banners.gameObject); }

        private sealed class Banner
        {
            internal readonly RectTransform Root; internal readonly float Born;
            private readonly CanvasGroup _cg; private readonly RectTransform _desc, _label, _ptsLabel, _icon, _name, _pts;
            private float _leftAt = -1f, _y = float.NaN; private bool _compact;
            internal bool Leaving => _leftAt >= 0f;
            internal float Height => _compact ? 64f : 104f;

            internal Banner(Transform parent, Deed d, ThemeData t, float now)
            {
                Born = now;
                var (line, fill) = Kit.Box(parent, "Banner", new Color(0.075f, 0.08f, 0.078f, 0.94f), new Color(0.13f, 0.84f, 0.49f, 0.45f), 16);
                Root = line.rectTransform; Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 1f); Root.sizeDelta = new Vector2(560f, 104f);
                _cg = Root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
                var (ring, _) = Kit.Box(Root, "Ring", new Color(0.13f, 0.84f, 0.49f, 0.16f), Theme.C(t.accent), 12); _icon = ring.rectTransform.At(14, 14, 76, 76);
                var ic = Kit.Img(_icon, "Icon", d.A.m_icon, Color.white, false); ic.rectTransform.Fill(12f); ic.preserveAspect = true;
                var label = Kit.Text(Root, "Label", "ACHIEVEMENT EARNED", Fonts.Mono, 10.5f, Theme.C(t.accent)); label.characterSpacing = 10f; _label = label.rectTransform.At(108, 20, 300, 14);
                var name = Kit.Text(Root, "Name", d.Name, Fonts.Bold, 22f, Color.white); _name = name.rectTransform.At(108, 36, 330, 30); name.overflowMode = TextOverflowModes.Ellipsis;
                var desc = Kit.Text(Root, "Desc", d.Desc, Fonts.Medium, 13f, Theme.C(t.dim)); _desc = desc.rectTransform.At(108, 66, 330, 18); desc.overflowMode = TextOverflowModes.Ellipsis;
                var sep = Kit.Img(Root, "Sep", null, Theme.C(t.line2)); sep.rectTransform.At(458, 26, 1, 52);
                var pts = Kit.Text(Root, "Points", "+" + d.Points, Fonts.Bold, 26f, Theme.C(t.accent)); pts.alignment = TextAlignmentOptions.Center; _pts = pts.rectTransform.At(466, 24, 80, 32);
                var pl = Kit.Text(Root, "PointsLabel", "points", Fonts.Medium, 11f, Theme.C(t.mute)); pl.alignment = TextAlignmentOptions.Center; _ptsLabel = pl.rectTransform.At(466, 56, 80, 16);
            }

            internal void MakeCompact()
            {
                if (_compact) return; _compact = true;
                Root.sizeDelta = new Vector2(492f, 64f);
                _icon.At(10, 8, 48, 48); _label.gameObject.SetActive(false); _ptsLabel.gameObject.SetActive(false);
                _name.At(72, 9, 300, 24); _name.GetComponent<TMP_Text>().fontSize = 16f;
                _desc.At(72, 33, 300, 18); _desc.GetComponent<TMP_Text>().fontSize = 12f;
                _pts.At(410, 16, 70, 32); _pts.GetComponent<TMP_Text>().fontSize = 18f;
                Root.Find("Sep").GetComponent<RectTransform>().At(402, 14, 1, 36);
            }

            internal void Leave(float now) { if (_leftAt < 0f) _leftAt = now; }
            internal bool Gone(float now) => _leftAt >= 0f && now - _leftAt > Fade;

            internal void Tick(float now, float targetY)
            {
                if (float.IsNaN(_y)) _y = targetY - 24f;                       // arrives from a little above
                _y = Mathf.Lerp(_y, targetY, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime)); // knocked down smoothly
                float alpha = Mathf.Clamp01((now - Born) / 0.35f), scale = Mathf.Lerp(0.94f, 1f, alpha);
                if (_leftAt >= 0f) { float k = Mathf.Clamp01((now - _leftAt) / Fade); alpha *= 1f - k; scale *= 1f - 0.04f * k; _y += 8f * k * Time.unscaledDeltaTime * 4f; }
                _cg.alpha = alpha; Root.localScale = new Vector3(scale, scale, 1f);
                Root.anchoredPosition = new Vector2(0f, -_y);
            }
        }
    }
}
