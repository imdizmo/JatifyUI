using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // F8: a small in-game panel to fire every notice by hand (skill banner, unlock card, pickups, biome,
    // achievement banner, Valheim's own messages), to get the test kit and to teleport to your grave and back. While it is open the cursor is
    // free and the game takes no input (Typing's patches).
    internal static class TestPanel
    {
        internal static bool Open => _root != null && _root.gameObject.activeSelf;
        private static RectTransform _root;
        private static int _biome;
        private static readonly string[] Biomes = { "$biome_meadows", "$biome_blackforest", "$biome_swamp", "$biome_mountain", "$biome_plains", "$biome_mistlands", "$biome_ashlands", "$biome_deepnorth", "$biome_ocean" };
        private static readonly string[] Topics = { "$msg_newrecipe", "$msg_newmaterial", "$msg_newpiece", "$msg_newitem", "$msg_newtrophy" };
        private static readonly System.Random Rng = new System.Random();
        // real Valheim messages, cycled, to see their icons
        private static readonly string[] Centres = { "$msg_donthaveany $item_resin", "$msg_bedneedroof", "$msg_youdied", "$msg_noteleport", "$msg_pvpon", "$inventory_full", "$msg_outof $ammo_arrows", "$hud_powernotready", "$msg_nobuildzone", "$msg_needfire", "$se_rested_start", "$piece_fermenter_needroof", "$msg_cantconsume" };
        private static readonly string[] TopLefts = { "$msg_worldsaved", "$msg_pin_added", "$msg_skills_lowered", "$msg_dropped $item_wood", "$msg_removed $item_stone" };
        private static int _ce, _tl;

        internal static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.F8) && Player.m_localPlayer != null && !Typing.Active) Toggle();
            if (Open && Input.GetKeyDown(KeyCode.Escape)) _root.gameObject.SetActive(false);
        }

        internal static void Toggle() { if (_root == null) Build(); else _root.gameObject.SetActive(!_root.gameObject.activeSelf); }

        private static void Build()
        {
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-testpanel");
            var cv = _root.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 31000; _root.gameObject.AddComponent<GraphicRaycaster>();
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 0.5f); _root.anchoredPosition = new Vector2(-260f, 0f);
            var actions = new (string, Action)[]
            {
                ("Skill level-up", Skill),
                ("Unlock card", Unlock),
                ("Unlock ×4", () => { for (int i = 0; i < 4; i++) Unlock(); }),
                ("Pickup +N", () => Pickup(Rng.Next(1, 6))),
                ("Pickup burst ×6", () => { for (int i = 0; i < 6; i++) Pickup(1); }),
                ("Biome discovered", Biome),
                ("Achievement banner", Achievement),
                ("Valheim top-left message", () => MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, TopLefts[_tl++ % TopLefts.Length])),
                ("Valheim centre message", () => MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, Centres[_ce++ % Centres.Length])),
                ("Give test kit", () => DevTest.Run("ms-kit")),
                ("Teleport to my grave", () => DevTest.Run("tp-grave")),
                ("Teleport back", () => DevTest.Run("tp-back")),
                ("Loading screen", LoadingScreen),
            };
            float h = 52f + actions.Length * 40f + 30f;
            _root.sizeDelta = new Vector2(280f, h);
            var (bg, _) = Kit.Box(_root, "Bg", Theme.C(t.glass), Theme.C(t.line2), 14); bg.rectTransform.Fill(); bg.raycastTarget = true;
            var title = Kit.Text(_root, "Title", "Test notices", Fonts.Bold, 16f, Color.white); title.rectTransform.At(16, 0, 200, 48); title.alignment = TextAlignmentOptions.MidlineLeft;
            Btn("×", 236, 10, 30, 28, () => _root.gameObject.SetActive(false), t);
            var line = Kit.Img(_root, "Line", null, Theme.C(t.glassLine)); line.rectTransform.At(0, 48, 280, 1);
            for (int i = 0; i < actions.Length; i++) { var a = actions[i].Item2; Btn(actions[i].Item1, 14, 58 + i * 40, 252, 34, () => { try { a(); } catch (Exception e) { Log.Warn("test panel: " + e.Message); } }, t); }
            var hint = Kit.Text(_root, "Hint", "F8 or Esc closes", Fonts.Mono, 10.5f, Theme.C(t.mute)); hint.rectTransform.At(16, h - 26, 250, 16);
        }

        // Campfire's loading-screen preview (its art and bar, 6 s, nothing loads): in a world, with the LoadingScreen switch on
        private static void LoadingScreen()
        {
            if (!Campfire.Module.On || !Campfire.Module.LoadingScreen.Value) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Loading screen is off (Start Screen settings)"); return; }
            Campfire.LoadBar.Preview(); _root.gameObject.SetActive(false);
        }

        private static void Btn(string label, float x, float y, float w, float h, Action on, ThemeData t)
        {
            var (b, f) = Kit.Box(_root, "Btn", Theme.C(t.raised), Theme.C(t.glassLine), 8); b.rectTransform.At(x, y, w, h);
            var tx = Kit.Text(b.rectTransform, "T", label, Fonts.Medium, 13f, Theme.C(t.text)); tx.rectTransform.Fill(); tx.rectTransform.offsetMin = new Vector2(12f, 0f); tx.alignment = label == "×" ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            if (label == "×") tx.rectTransform.offsetMin = Vector2.zero;
            b.raycastTarget = true; var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = f;
            var cb = btn.colors; cb.highlightedColor = new Color(1.35f, 1.35f, 1.4f); btn.colors = cb;
            btn.onClick.AddListener(() => on());
        }

        private static ItemDrop.ItemData RandomItem(Func<ItemDrop.ItemData, bool> ok)
        {
            var all = ObjectDB.instance.m_items.Select(g => g ? g.GetComponent<ItemDrop>() : null).Where(d => d != null && d.m_itemData.m_shared.m_icons.Length > 0 && ok(d.m_itemData)).ToList();
            return all.Count == 0 ? null : all[Rng.Next(all.Count)].m_itemData;
        }

        private static void Skill()
        {
            var sk = Player.m_localPlayer.GetSkills().m_skills; var s = sk[Rng.Next(sk.Count)];
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "$msg_skillup $skill_" + s.m_skill.ToString().ToLower() + ": " + Rng.Next(2, 100), 0, s.m_icon);
        }

        private static void Unlock()
        {
            var it = RandomItem(_ => true); if (it == null) return;
            MessageHud.instance.QueueUnlockMsg(it.GetIcon(), Topics[Rng.Next(Topics.Length)], it.m_shared.m_name);
        }

        private static void Pickup(int n)
        {
            var it = RandomItem(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material); if (it == null) return;
            Player.m_localPlayer.ShowPickupMessage(it, n);
        }

        private static void Biome()
        {
            var mh = MessageHud.instance; if (mh == null) return;
            mh.ShowBiomeFoundMsg(Biomes[_biome++ % Biomes.Length], true);
        }

        private static void Achievement()
        {
            if (!Deeds.Model.Ready || Root.Ach == null) return;
            var all = Deeds.Model.All; Root.Ach.Show(all[Rng.Next(all.Count)]);
        }

        internal static void Destroy() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); _root = null; }
    }
}
