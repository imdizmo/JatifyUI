using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // Steam achievements for this account, read-only: only SteamUserStats getters are called here,
    // never SetAchievement / ClearAchievement / StoreStats.
    internal static class AchView
    {
        private sealed class Ach
        {
            public string Id, Name, Desc;
            public bool Hidden, Done;
            public DateTime When;
            public float Percent = -1f;
            public Sprite Icon;
        }

        private static Window _win;
        private static RectTransform _head, _filter, _list;
        private static int _mode;   // 0 all, 1 unlocked, 2 locked
        private static List<Ach> _all = new List<Ach>();
        private static CallResult<GlobalAchievementPercentagesReady_t> _percents;
        private static Callback<UserAchievementIconFetched_t> _iconFetched;
        private static bool _percentsAsked;

        internal static bool Shown => _win != null && _win.Shown;
        internal static void Hide() { if (_win != null) _win.Show(false); }

        internal static void Open()
        {
            if (_win == null || _win.Root == null) Build();
            _win.Show(true);
            Load();
            Paint();
        }

        // the head (count, bar, the character's line), the All / Unlocked / Locked filter, the cards
        private static void Build()
        {
            _win = new Window(CharScreen.Root, "achievements", 780f, 860f, () => _win.Show(false));
            _win.SetTitle("Achievements");
            _win.Buttons("Close");
            _head = Place(Node(_win.Body, "head"), 0f, 0f, _win.BodyW, 62f);
            _filter = Place(Node(_win.Body, "filter"), 0f, 70f, _win.BodyW, 30f);
            _list = JatifyLook.Scroll(_win.Body, "grid", 0f, 112f, _win.BodyW, _win.BodyH - 112f);
        }

        private static bool SteamReady()
        {
            try { return SteamAPI.IsSteamRunning() && SteamUser.BLoggedOn(); } catch { return false; }
        }

        private static void Load()
        {
            _all.Clear();
            if (!SteamReady()) return;
            if (_iconFetched == null) _iconFetched = Callback<UserAchievementIconFetched_t>.Create(_ => { foreach (var a in _all) if (a.Icon == null) a.Icon = Icon(a.Id); if (Shown) Paint(); });
            uint n = SteamUserStats.GetNumAchievements();
            for (uint i = 0; i < n; i++)
            {
                var id = SteamUserStats.GetAchievementName(i);
                SteamUserStats.GetAchievementAndUnlockTime(id, out bool done, out uint t);
                _all.Add(new Ach
                {
                    Id = id, Done = done,
                    When = done ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime : DateTime.MinValue,
                    Name = SteamUserStats.GetAchievementDisplayAttribute(id, "name"),
                    Desc = SteamUserStats.GetAchievementDisplayAttribute(id, "desc"),
                    Hidden = SteamUserStats.GetAchievementDisplayAttribute(id, "hidden") == "1",
                    Icon = Icon(id),
                });
            }
            if (!_percentsAsked)
            {
                _percentsAsked = true;
                _percents = CallResult<GlobalAchievementPercentagesReady_t>.Create((r, fail) =>
                {
                    if (fail) return;
                    foreach (var a in _all) if (SteamUserStats.GetAchievementAchievedPercent(a.Id, out float pct)) a.Percent = pct;
                    if (Shown) Paint();
                });
                _percents.Set(SteamUserStats.RequestGlobalAchievementPercentages());
            }
            else foreach (var a in _all) if (SteamUserStats.GetAchievementAchievedPercent(a.Id, out float pct)) a.Percent = pct;
        }

        // Steam hands icons over as raw RGBA, top row first; Unity textures start at the bottom.
        private static Sprite Icon(string id)
        {
            int handle = SteamUserStats.GetAchievementIcon(id);
            if (handle == 0 || !SteamUtils.GetImageSize(handle, out uint w, out uint h) || w == 0) return null;
            var buf = new byte[w * h * 4];
            if (!SteamUtils.GetImageRGBA(handle, buf, buf.Length)) return null;
            var flipped = new byte[buf.Length];
            int row = (int)w * 4;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(buf, y * row, flipped, ((int)h - 1 - y) * row, row);
            var tex = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(flipped);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static void Paint()
        {
            Clear(_head);
            Clear(_filter);
            Clear(_list);
            float w = _win.BodyW;
            if (!SteamReady())
            {
                Para(_head, "Achievements need Steam: start Valheim through Steam while signed in.", "lore", 15f, C("dim"), 0f, 0f, w, 60f);
                return;
            }
            Seg(_filter, 0f, 0f, 0f, new[] { "All", "Unlocked", "Locked" }, _mode, i => { _mode = i; Paint(); });
            int done = _all.Count(a => a.Done);
            Text(_head, $"{done} / {_all.Count} unlocked", "title", 22f, White, 0f, 0f, 260f, 30f);
            JatifyLook.Bar(_head, 270f, 11f, w - 270f, 8f, _all.Count > 0 ? (float)done / _all.Count : 0f, C("accent"));
            CharacterLine(w);

            var list = _all.Where(a => _mode == 0 || (_mode == 1) == a.Done)
                .OrderByDescending(a => a.Done).ThenByDescending(a => a.When).ThenByDescending(a => a.Percent).ToList();
            float colW = (w - 10f) / 2f, y = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                bool rare = a.Done && a.Percent >= 0f && a.Percent < 10f;
                // One look for every unlocked card; rare ones (under 10% of players) get an amber edge and a tag.
                var card = Box(_list, a.Id, C("raised"), rare ? C("stamina") : LineSoft, 10); card.raycastTarget = false;
                Place(card.rectTransform, (i % 2) * (colW + 10f), y, colW, 96f);
                if (rare)
                {
                    var tag = Box(card.transform, "rare", C("raised2"), NoColor, 4); tag.raycastTarget = false; Place(tag.rectTransform, colW - 10f - 44f, 8f, 44f, 18f);
                    Fill(Text(tag.transform, "RARE", "mono", 10f, C("stamina"), 0f, 0f, 44f, 18f, TextAlignmentOptions.Center).rectTransform);
                }
                var icon = Img(card.transform, a.Icon, a.Icon != null ? (a.Done ? White : new Color(0.45f, 0.45f, 0.45f, 0.8f)) : NoColor, "i");
                icon.preserveAspect = true; Place(icon.rectTransform, 12f, 19f, 58f, 58f);
                bool secret = a.Hidden && !a.Done;
                Text(card.transform, secret ? "Hidden achievement" : a.Name, "title", 17f, a.Done ? White : C("dim"), 82f, 4f, colW - (rare ? 146f : 92f), 30f);
                var desc = Para(card.transform, secret ? "Keep playing to find out." : a.Desc, "lore", 13f, C("mute"), 82f, 32f, colW - 92f, 36f);
                desc.maxVisibleLines = 2; desc.overflowMode = TextOverflowModes.Ellipsis;
                var foot = a.Done ? $"Unlocked {a.When:yyyy-MM-dd}" : "Locked";
                if (a.Percent > 0f) foot += $" · {a.Percent:0.#}% of players";
                Text(card.transform, foot, "mono", 11f, a.Done ? C("dim") : C("mute"), 82f, 70f, colW - 92f, 20f);
                if (i % 2 == 1 || i == list.Count - 1) y += 106f;
            }
            _list.sizeDelta = new Vector2(0f, y);
        }

        // Valheim stops unlocks for a character it has flagged for cheats; say so for the selected one.
        private static void CharacterLine(float w)
        {
            var p = CharScreen.Selected;
            if (p == null) return;
            bool flagged = p.m_usedCheats;
            Text(_head, flagged ? $"Achievements are off for {p.GetName()}: Valheim flagged this character for cheats."
                                : $"{p.GetName()} can earn achievements.", "medium", 14f, flagged ? C("health") : C("accent"), 0f, 34f, w, 24f);
        }
    }
}
