using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UserManagement;
using Valheim.UI;

namespace Jatify.UI
{
    // The pause menu's Players page (live design "players"), cascading right of the menu like Mods: who is
    // here (initials, name, you / host, platform, where they are when they share their position, your own
    // ping), Kick for the others (Valheim's own kick: the server only obeys its host and admins), and the
    // invite code with Copy. Steam picture, name, id and Kick / Mute / Block / Report come from Valheim's own list, opened
    // hidden. ponytail: no other players' ping (Valheim doesn't send it).
    internal static class PlayersPage
    {
        internal static bool Open => _root != null;
        private static RectTransform _root, _list;
        private static float _next;
        private static string _key;
        private static readonly Color[] Avatar = { Hex("22d67e"), Hex("5aa9ff"), Hex("f2703a"), Hex("b48cff"), Hex("e8c547"), Hex("e05c8a") };

        private static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        internal static void Show()
        {
            Close(); ConfigPage.Open?.Close(); if (Settings.instance != null) Settings.instance.OnBack();
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-players").Fill();
            var own = _root.gameObject.AddComponent<Canvas>(); own.overrideSorting = true; own.sortingOrder = 32000;
            _root.gameObject.AddComponent<GraphicRaycaster>();
            var head = Kit.Text(_root, "Head", "PLAYERS", Fonts.Mono, 12f, Theme.C(t.mute)); head.characterSpacing = 8f; head.rectTransform.At(420, 300, 260, 18);
            _list = Kit.Node(_root, "List").At(420, 330, 560, 600);
            _key = null; _next = 0f;
            Tick();
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root.gameObject); _root = null;
            var m = Menu.instance; var go = m != null ? Vanilla(m) : null; if (go != null && go.activeSelf) go.SetActive(false);   // Valheim's list goes with it (it destroys itself)
        }

        // Valheim's own list, opened hidden: its entries carry the Steam picture, gamertag, platform id
        // and the kick / mute / block / report buttons, which we press for the player
        private static readonly AccessTools.FieldRef<Menu, GameObject> Vanilla = AccessTools.FieldRefAccess<Menu, GameObject>("m_currentPlayersInstance");
        private static readonly AccessTools.FieldRef<SessionPlayerList, List<SessionPlayerListEntry>> Entries = AccessTools.FieldRefAccess<SessionPlayerList, List<SessionPlayerListEntry>>("_allPlayers");

        internal static List<SessionPlayerListEntry> VanillaEntries()
        {
            var m = Menu.instance; var go = m != null ? Vanilla(m) : null; if (go == null) return null;
            var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false; }   // ours shows instead
            var list = go.GetComponentInChildren<SessionPlayerList>(true); return list != null ? Entries(list) : null;
        }

        internal static void Tick()
        {
            if (_root == null) return;
            if (!Menu.IsVisible()) { Close(); return; }   // Continue clicked beside us
            if (Time.unscaledTime < _next) return; _next = Time.unscaledTime + 0.5f;
            var zn = ZNet.instance; if (zn == null) return;
            var entries = VanillaEntries();
            if (entries == null) { var m = Menu.instance; if (m != null) { _opening = true; m.OnCurrentPlayers(); _opening = false; } return; }   // its list closed itself (Esc): open it again
            var players = zn.GetPlayerList();
            var me = Player.m_localPlayer; var myPos = me != null ? me.transform.position : Vector3.zero;
            var key = "";
            foreach (var e in entries) if (e != null) key += e.CharacterName + e.Gamertag + (e.GamerPic != null && e.GamerPic.sprite != null) + e.HasKick + e.HasMute + e.HasBlock + e.HasReport + MuteList.Instance.Contains(e.User) + BlockList.Instance.Contains(e.User) + ";";
            foreach (var p in players) key += p.m_name + (p.m_publicPosition ? Mathf.RoundToInt(Utils.DistanceXZ(myPos, p.m_position) / 50f).ToString() : "-") + ";";
            key += Mathf.RoundToInt(Ping() / 10f) + ZPlayFabMatchmaking.JoinCode;
            if (key == _key) return; _key = key;
            Fill(entries, players, myPos, Plugin.Instance.ThemeData);
        }

        internal static bool _opening;

        private static float Ping() { var m = AccessTools.Method(typeof(ZNet), "GetServerPing"); return ZNet.instance != null && m != null ? (float)m.Invoke(ZNet.instance, null) * 1000f : 0f; }

        private static void Fill(List<SessionPlayerListEntry> entries, List<ZNet.PlayerInfo> players, Vector3 myPos, ThemeData t)
        {
            foreach (Transform c in _list) Object.Destroy(c.gameObject);
            var zn = ZNet.instance; float y = 0f; int i = 0; var mute = ColorUtility.ToHtmlStringRGB(Theme.C(t.mute));
            foreach (var e in entries)
            {
                if (e == null) continue;
                bool you = e.IsOwnPlayer; var name = Kit.Plain(e.CharacterName ?? "");
                var row = Kit.Node(_list, "Row").At(0, y, 640, 56);
                var pic = e.GamerPic != null ? e.GamerPic.sprite : null;
                if (pic != null) { var (f, _) = Kit.Box(row, "Pic", Theme.C(t.raised), Theme.C(t.line2), 8); f.rectTransform.At(0, 8, 40, 40); var im = Kit.Img(f.rectTransform, "I", pic, Color.white, false); im.rectTransform.At(2, 2, 36, 36); }
                else { var av = Kit.Img(row, "Avatar", Kit.Circle(), Avatar[i % Avatar.Length], false); av.rectTransform.At(0, 8, 40, 40); var ini = Kit.Text(av.rectTransform, "I", name.Length >= 2 ? name.Substring(0, 2) : name, Fonts.Bold, 14f, new Color(0.03f, 0.05f, 0.05f)); ini.alignment = TextAlignmentOptions.Center; ini.rectTransform.Fill(); }
                i++;
                var tags = new List<string>(); if (e.IsHost) tags.Add("host"); if (you) tags.Add("you");
                var nm = Kit.Text(row, "Name", name + (tags.Count > 0 ? $"  <size=60%><color=#{ColorUtility.ToHtmlStringRGB(Theme.C(t.accent))}>{string.Join(" · ", tags).ToUpperInvariant()}</color></size>" : ""), Fonts.Bold, 22f, Color.white);
                nm.rectTransform.At(54, 4, 380, 28);
                // the platform line (Steam name · id), then where they are
                var who = new List<string>(); if (!string.IsNullOrEmpty(e.Gamertag)) who.Add(Kit.Plain(e.Gamertag)); if (!string.IsNullOrEmpty(e.PlatformUserID)) who.Add(Kit.Plain(e.PlatformUserID));
                var info = players.Find(p => p.m_name == e.CharacterName);
                string where = you ? Biome(myPos) : info.m_name != null && info.m_publicPosition ? $"{Biome(info.m_position)} · {Km(Utils.DistanceXZ(myPos, info.m_position))}" : "position hidden";
                string ping = you && !zn.IsServer() ? $" · {Ping():0} ms" : "";
                var sub = Kit.Text(row, "Sub", string.Join("  ·  ", who) + $"  <color=#{mute}>·  {where}{ping}</color>", Fonts.Medium, 12.5f, Theme.C(t.dim)); sub.rectTransform.At(54, 32, 420, 18); sub.overflowMode = TextOverflowModes.Ellipsis;
                // Valheim's own actions for this player, pressed for you
                float bx = 640f; var entry = e;
                void Act(bool has, string label, System.Action a)
                {
                    if (!has) return;
                    var tx = Kit.Text(row, "T", label, Fonts.Medium, 12.5f, Color.white); tx.alignment = TextAlignmentOptions.Center; float w = Kit.W(tx) + 22f; bx -= w + 6f;
                    var (b, _) = Kit.Box(row, label, Theme.C(t.raised), Theme.C(t.line2), 7); b.rectTransform.At(bx, 14, w, 28); b.raycastTarget = true; tx.transform.SetParent(b.transform, false); tx.rectTransform.Fill();
                    b.gameObject.AddComponent<Button>().onClick.AddListener(() => { a(); _key = null; _next = 0f; });
                }
                bool muted = MuteList.Instance.Contains(e.User), blocked = BlockList.Instance.Contains(e.User);
                Act(e.HasKick, "Kick", entry.OnKick);
                Act(e.HasReport, "Report", entry.OnReport);
                Act(e.HasBlock, blocked ? "Unblock" : "Block", entry.OnBlock);
                Act(e.HasMute, muted ? "Unmute" : "Mute", entry.OnMute);
                y += 60f;
            }
            // invite code
            var code = ZPlayFabMatchmaking.JoinCode;
            if (!string.IsNullOrEmpty(code) && code.Trim('0').Length > 0)   // crossplay worlds only; others show Valheim's 000000 placeholder
            {
                y += 16f;
                var l = Kit.Text(_list, "CodeL", "CROSSPLAY JOIN CODE", Fonts.Mono, 10f, Theme.C(t.mute)); l.characterSpacing = 8f; l.rectTransform.At(0, y, 200, 16);
                var c = Kit.Text(_list, "Code", code, Fonts.Mono, 22f, Color.white); c.rectTransform.At(0, y + 18, 200, 30);
                var (cb, _) = Kit.Box(_list, "Copy", Theme.C(t.raised), Theme.C(t.line2), 7); cb.rectTransform.At(Kit.W(c) + 16f, y + 20, 70, 28); cb.raycastTarget = true;
                var ct = Kit.Text(cb.rectTransform, "T", "Copy", Fonts.Medium, 12.5f, Color.white); ct.alignment = TextAlignmentOptions.Center; ct.rectTransform.Fill();
                cb.gameObject.AddComponent<Button>().onClick.AddListener(() => { GUIUtility.systemCopyBuffer = code; ct.text = "Copied"; });
                y += 60f;
            }
            y += 20f;
            var back = Kit.Text(_list, "Back", "Back", Fonts.Bold, 20f, Theme.C(t.dim)); back.rectTransform.At(0, y, 80, 30); back.alignment = TextAlignmentOptions.MidlineLeft;
            back.raycastTarget = true; back.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            var esc = Kit.Text(_list, "Esc", "Esc", Fonts.Mono, 12f, Theme.C(t.mute)); esc.rectTransform.At(Kit.W(back) + 12f, y, 60, 30); esc.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static string Biome(Vector3 p) => WorldGenerator.instance != null ? Localization.instance.Localize("$biome_" + WorldGenerator.instance.GetBiome(p).ToString().ToLowerInvariant()) : "";
        private static string Km(float d) => d >= 1000f ? $"{d / 1000f:0.0} km" : $"{d:0} m";
    }

    [HarmonyPatch(typeof(Menu), nameof(Menu.OnCurrentPlayers))]
    internal static class PlayersInsteadOfVanilla
    {
        // Valheim's list opens too (hidden; ours reads it); a second press closes both
        private static bool Prefix()
        {
            if (Root.Canvas == null || PlayersPage._opening) return true;
            if (PlayersPage.Open) { PlayersPage.Close(); return false; }
            PlayersPage.Show(); return true;
        }
        private static void Postfix() { if (PlayersPage.Open) PlayersPage.VanillaEntries(); }   // hidden the moment it opens: no flash of Valheim's list
    }
}
