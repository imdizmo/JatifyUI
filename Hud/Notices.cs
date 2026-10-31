using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Valheim's notices around our HUD:
    //  * Top-left messages ("Picked up", "New recipe" pop-ups) go under the achievement tracker, which
    //    sits in that corner. They keep their vanilla look; only their position moves, and it is put back
    //    on unload.
    //  * Skill level-ups become a banner in the middle of the screen: the skill's icon in a ring, the text
    //    under it, on a band that fades out towards the sides. Vanilla's text for it is swallowed.
    internal static class Notices
    {
        // notices draw above Valheim's open panels (the inventory, crafting): their own canvas, sorted high
        internal static void OnTop(RectTransform rt) { var cv = rt.gameObject.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = 30500; }

        private static readonly AccessTools.FieldRef<MessageHud, List<GameObject>> Unlocks = AccessTools.FieldRefAccess<MessageHud, List<GameObject>>("m_unlockMessages");
        private static readonly Dictionary<RectTransform, Vector2> _home = new Dictionary<RectTransform, Vector2>();   // moved -> its vanilla position
        private static RectTransform _topLeft;

        internal static void Tick()
        {
            var mh = MessageHud.instance; var canvas = Root.Canvas;
            if (mh == null || canvas == null) return;
            float down = Deeds.AchHud.Bottom > 0f ? Deeds.AchHud.Bottom + 10f : 0f;
            if (_topLeft == null && mh.m_messageText != null)
            {   // IngameGui/TopLeftMessage/root: holds the message text and icon
                var t = mh.m_messageText.transform;
                while (t.parent != null && t.parent.name != "TopLeftMessage") t = t.parent;
                _topLeft = t.parent != null ? (RectTransform)t : null;   // its "root" child: TopLeftMessage itself is canvas-driven
            }
            Shift(_topLeft, (down > 0f ? down : 0f) + UnlockCards.Height, canvas);   // under the tracker and any unlock cards
            var list = Unlocks(mh);
            if (list != null) foreach (var go in list) if (go != null) Shift((RectTransform)go.transform, down, canvas);
            if (_home.Count > 8) { var dead = new List<RectTransform>(); foreach (var k in _home.Keys) if (k == null) dead.Add(k); foreach (var k in dead) _home.Remove(k); }
            SkillBanner.Tick();
            PickupFloat.Tick();
            UnlockCards.Tick(down > 0f ? down : 22f);
            BiomeBanner.Tick();
            CenterPill.Tick();
            ActionToast.Tick();
        }

        private static void Shift(RectTransform rt, float downDesign, Canvas canvas)
        {
            if (rt == null || rt.parent == null) return;
            if (!_home.TryGetValue(rt, out var home)) { home = rt.anchoredPosition; _home[rt] = home; }
            float k = canvas.transform.lossyScale.y / rt.parent.lossyScale.y;      // design px -> this transform's units
            var want = home - new Vector2(0f, downDesign * k);
            if ((rt.anchoredPosition - want).sqrMagnitude > 0.01f) rt.anchoredPosition = want;
        }

        internal static void Restore()
        {
            foreach (var kv in _home) if (kv.Key != null) kv.Key.anchoredPosition = kv.Value;
            _home.Clear(); _topLeft = null;
            SkillBanner.Destroy();
            PickupFloat.Destroy();
            BiomeBanner.Destroy();
            CenterPill.Destroy();
            ActionToast.Destroy();
            UnlockCards.Destroy();
        }
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    internal static class SkillUpMessagePatch
    {
        private static readonly System.Reflection.MethodInfo AddLog = AccessTools.Method(typeof(MessageHud), "AddLog");

        internal static string PowerName() { var p = Player.m_localPlayer; if (p == null) return "Power"; p.GetGuardianPowerHUD(out var se, out _); return se != null ? Localization.instance.Localize(se.m_name) : "Power"; }

        private static bool Prefix(MessageHud __instance, MessageHud.MessageType type, string text, int amount, Sprite icon, bool showDespiteHiddenHUD, bool log)
        {
            if (text == null || Root.Canvas == null) return true;
            if (text.StartsWith("$msg_worldsaved") && !text.StartsWith("$msg_worldsavedcloud")) return !SaveBadge.Saved();   // the save banner says it
            if (text == "$hud_powernotready" && Player.m_localPlayer != null) { ActionToast.Show($"{PowerName()} is recharging", false); return false; }   // design: a toast
            if (!text.StartsWith("$msg_skillup") || !Cfg.SkillBanner.Value)
            {   // everything else: our cards (top-left) and pill (centre), still in Valheim's message log
                if (!Cfg.Messages.Value) return true;
                if (Hud.IsUserHidden() && !showDespiteHiddenHUD) return false;
                if (icon == null) icon = NoticeIcons.For(text);   // a fitting icon when Valheim sends none
                var msg = Localization.instance.Localize(text);
                if (log) AddLog?.Invoke(__instance, new object[] { msg });
                if (type == MessageHud.MessageType.TopLeft) UnlockCards.ShowLine(icon, msg, amount); else CenterPill.Show(icon, msg);
                return false;
            }
            // "$msg_skillup $skill_farming: 23" with the skill's icon -> our banner
            int colon = text.LastIndexOf(':'); int sp = text.IndexOf(' ');
            if (colon < 0 || sp < 0) return true;
            string skill = Localization.instance.Localize(text.Substring(sp + 1, colon - sp - 1).Trim());
            SkillBanner.Show(icon, skill, text.Substring(colon + 1).Trim());
            return false;
        }
    }

    internal static class SkillBanner
    {
        private const float In = 0.3f, Hold = 3f, Out = 0.7f;
        private static readonly Queue<(Sprite, string, string)> _queue = new Queue<(Sprite, string, string)>();
        private static RectTransform _root, _ring; private static CanvasGroup _cg; private static float _born = -1f;
        private static Texture2D _fadeTex; private static Sprite _fade;

        internal static void Show(Sprite icon, string skill, string level)
        {
            if (_queue.Count < 6) _queue.Enqueue((icon, skill, level));   // ponytail: at most 6 waiting; a burst of level-ups past that is dropped
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (_root != null)
            {
                float hold = _queue.Count > 0 ? 1.4f : Hold;           // others waiting: make room sooner
                float age = now - _born;
                _cg.alpha = age < In ? age / In : age < In + hold ? 1f : 1f - (age - In - hold) / Out;
                float pop = age < In ? Mathf.Lerp(1.25f, 1f, age / In) : 1f;
                _ring.localScale = new Vector3(pop, pop, 1f);
                if (age > In + hold + Out) { Object.Destroy(_root.gameObject); _root = null; }
                return;
            }
            if (_queue.Count > 0) { var (icon, skill, level) = _queue.Dequeue(); Build(icon, skill, level); _born = now; }
        }

        // a horizontal band: opaque in the middle, fading to nothing at both ends
        internal static Texture2D FadeTex { get { Fade(); return _fadeTex; } }
        internal static Sprite Fade()
        {
            if (_fade != null) return _fade;
            _fadeTex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { name = "jatify-fade", wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 256; x++) { float d = Mathf.Abs(x - 127.5f) / 127.5f; float a = 1f - Mathf.SmoothStep(0.25f, 1f, d); _fadeTex.SetPixel(x, 0, new Color(1f, 1f, 1f, a)); }
            _fadeTex.Apply();
            _fade = Sprite.Create(_fadeTex, new Rect(0, 0, 256, 1), new Vector2(0.5f, 0.5f), 100f);
            return _fade;
        }

        private static void Build(Sprite icon, string skill, string level)
        {
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-skillbanner"); Notices.OnTop(_root);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f); _root.anchoredPosition = new Vector2(0f, -140f); _root.sizeDelta = new Vector2(760f, 174f);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
            var band = Kit.Img(_root, "Band", Fade(), new Color(0.06f, 0.065f, 0.07f, 0.92f), false); band.rectTransform.At(0, 90, 760, 84);   // under the ring, not behind it
            var top = Kit.Img(_root, "Top", Fade(), new Color(0.13f, 0.84f, 0.49f, 0.7f), false); top.rectTransform.At(0, 90, 760, 1);
            var bottom = Kit.Img(_root, "Bottom", Fade(), new Color(0.13f, 0.84f, 0.49f, 0.35f), false); bottom.rectTransform.At(0, 173, 760, 1);
            // the icon in a ring, centred, sitting on the band's top edge
            var (ring, _) = Kit.Box(_root, "Ring", new Color(0.06f, 0.065f, 0.07f, 0.96f), Theme.C(t.accent), 38); _ring = ring.rectTransform.At(342, 6, 76, 76);
            _ring.pivot = new Vector2(0.5f, 0.5f); _ring.anchoredPosition = new Vector2(380f, -44f);
            if (icon != null) { var ic = Kit.Img(_ring, "Icon", icon, Color.white, false); ic.rectTransform.Fill(16f); ic.preserveAspect = true; }
            var label = Kit.Text(_root, "Label", "SKILL IMPROVED", Fonts.Mono, 10.5f, Theme.C(t.accent)); label.characterSpacing = 10f; label.alignment = TextAlignmentOptions.Center; label.rectTransform.At(0, 100, 760, 14);
            var name = Kit.Text(_root, "Name", skill, Fonts.Bold, 24f, Color.white); name.alignment = TextAlignmentOptions.Center; name.rectTransform.At(0, 114, 760, 30);
            var lv = Kit.Text(_root, "Level", "Level " + level, Fonts.Mono, 12.5f, Theme.C(t.dim)); lv.alignment = TextAlignmentOptions.Center; lv.rectTransform.At(0, 146, 760, 16);
        }

        internal static void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null; _queue.Clear();
            if (_fade != null) Object.Destroy(_fade); if (_fadeTex != null) Object.Destroy(_fadeTex);
            _fade = null; _fadeTex = null;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ShowPickupMessage))]
    internal static class PickupMessagePatch
    {
        private static bool Prefix(Character __instance, ItemDrop.ItemData item, int amount)
        {
            if (__instance != Player.m_localPlayer || item == null || !Cfg.PickupFloat.Value || Root.Canvas == null) return true;
            PickupFloat.Show(item.GetIcon(), amount);
            return false;
        }
    }

    // "+1" and the item's icon, rising from the character on a soft arc, left and right in turn, then gone.
    internal static class PickupFloat
    {
        private const float Life = 1.6f, Max = 0.85f; private const int MaxLive = 12;
        private sealed class Float { public RectTransform Rt; public CanvasGroup Cg; public float Born; public float Side; }
        private static readonly List<Float> _live = new List<Float>();
        private static float _side = 1f;

        internal static void Show(Sprite icon, int amount)
        {
            if (_live.Count >= MaxLive) { Object.Destroy(_live[0].Rt.gameObject); _live.RemoveAt(0); }   // oldest makes room
            var rt = Kit.Node(Root.Canvas.transform, "jatify-pickup"); Notices.OnTop(rt);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(90f, 32f);
            var cg = rt.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.alpha = 0f;
            var tx = Kit.Text(rt, "N", "+" + amount, Fonts.Bold, 17f, Color.white); tx.alignment = TextAlignmentOptions.MidlineRight; tx.rectTransform.At(0, 0, 42, 32);
            tx.fontSharedMaterial = tx.font.material; tx.outlineWidth = 0.18f; tx.outlineColor = new Color32(0, 0, 0, 160);   // readable on bright ground
            if (icon != null) { var ic = Kit.Img(rt, "I", icon, Color.white, false); ic.rectTransform.At(46, 1, 30, 30); ic.preserveAspect = true; }
            _side = -_side;
            _live.Add(new Float { Rt = rt, Cg = cg, Born = Time.unscaledTime, Side = _side });
        }

        internal static void Tick()
        {
            if (_live.Count == 0) return;
            var p = Player.m_localPlayer; var cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            var canvas = (RectTransform)Root.Canvas.transform;
            Vector2 anchor = Vector2.zero; bool seen = false;
            if (p != null && cam != null)
            {
                var sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 1.5f);
                seen = sp.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, sp, null, out anchor);
            }
            float now = Time.unscaledTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var f = _live[i]; float t = (now - f.Born) / Life;
                if (t >= 1f || f.Rt == null) { if (f.Rt != null) Object.Destroy(f.Rt.gameObject); _live.RemoveAt(i); continue; }
                // a soft arc: out to the side, up, and a little down again at the end
                float x = f.Side * (40f + 80f * t), y = 30f + 90f * Mathf.Sin(t * Mathf.PI * 0.75f);
                f.Rt.anchoredPosition = anchor + new Vector2(x, y);
                f.Cg.alpha = !seen ? 0f : Max * Mathf.Min(1f, t / 0.12f) * (t > 0.55f ? 1f - (t - 0.55f) / 0.45f : 1f);
                float s = 0.9f + 0.1f * Mathf.Min(1f, t / 0.12f); f.Rt.localScale = new Vector3(s, s, 1f);
            }
        }

        internal static void Destroy()
        {
            foreach (var f in _live) if (f.Rt != null) Object.Destroy(f.Rt.gameObject);
            _live.Clear();
        }
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.QueueUnlockMsg))]
    internal static class UnlockMessagePatch
    {
        private static bool Prefix(Sprite icon, string topic, string description)
        {
            if (Cfg.UnlockNotices.Value == Cfg.Unlock.Off) return false;   // none at all (the Unlocks page still records them)
            if (Root.Canvas == null || !Cfg.SkillBanner.Value) return true;
            UnlockCards.Show(icon, Localization.instance.Localize(topic ?? ""), Localization.instance.Localize(description ?? ""));
            return false;
        }
    }

    // "New recipe" / "New material" cards in the top-left corner, under the achievement tracker:
    // icon tile, the topic in green mono caps, the name; they slide in, stay a few seconds, fade.
    internal static class UnlockCards
    {
        private const float Life = 4.5f, In = 0.25f, Out = 0.6f, H = 58f, Gap = 8f; private const int MaxShown = 4;
        private sealed class Card { public RectTransform Rt; public CanvasGroup Cg; public float Born = -1f; public float Y = float.NaN; public float H; }
        private static readonly Queue<(Sprite, string, string)> _queue = new Queue<(Sprite, string, string)>();   // topic null = a plain message line
        private static readonly List<Card> _live = new List<Card>();
        private static float _next;

        internal static float Height { get { if (_live.Count == 0 || InventoryGui.IsVisible()) return 0f; float h = 0f; foreach (var c in _live) h += c.H + Gap; return h + (Deeds.AchHud.Bottom > 0f ? 0f : 22f); } }

        // Valheim's top-left messages: a slim card, icon, text and the amount
        internal static void ShowLine(Sprite icon, string text, int amount) { if (_queue.Count < 30) _queue.Enqueue((icon, null, amount > 1 ? $"{text}  <color=#{ColorUtility.ToHtmlStringRGB(Theme.C(Plugin.Instance.ThemeData.accent))}>×{amount}</color>" : text)); }

        // unlocks arrive in bursts (one pickup can unlock several recipes): collected until 0.6 s pass without a new one
        internal static void Show(Sprite icon, string topic, string name) { if (_burst.Count < 200) _burst.Add((icon, topic, name)); _burstAt = Time.unscaledTime; }
        private static readonly List<(Sprite icon, string topic, string name)> _burst = new List<(Sprite, string, string)>();
        private static float _burstAt;

        private static void Flush()
        {
            if (_burst.Count == 0 || Time.unscaledTime - _burstAt < 0.6f) return;
            bool fold = Cfg.UnlockNotices.Value == Cfg.Unlock.Grouped && _burst.Count > 3;
            int cards = fold ? 2 : _burst.Count;
            for (int i = 0; i < cards && _queue.Count < 30; i++) _queue.Enqueue(_burst[i]);   // ponytail: 30 waiting at most
            if (fold) { var icons = new List<Sprite>(); for (int i = 2; i < _burst.Count && icons.Count < 9; i++) icons.Add(_burst[i].icon); _more.Enqueue(icons); _queue.Enqueue((null, MoreTopic, $"+{_burst.Count - 2} more")); }
            _burst.Clear();
        }
        private const string MoreTopic = "jatify-more";
        private static readonly Queue<List<Sprite>> _more = new Queue<List<Sprite>>();   // one icon strip per "+N more" card waiting

        internal static void Tick(float top)
        {
            float now = Time.unscaledTime;
            Flush();
            bool paused = InventoryGui.IsVisible();   // the inventory takes this corner: hold the cards until it closes
            if (!paused && _queue.Count > 0 && _live.Count < MaxShown && now >= _next) { var (i, t, n) = _queue.Dequeue(); _live.Add(Build(i, t, n, now)); _next = now + 0.35f; }
            float y = top;
            for (int i = 0; i < _live.Count; i++)
            {
                var c = _live[i]; if (paused) c.Born += Time.unscaledDeltaTime; float age = now - c.Born;
                float life = _queue.Count > 0 ? Life * 0.6f : Life;     // more waiting: move along sooner
                if (age > life + Out || c.Rt == null) { if (c.Rt != null) Object.Destroy(c.Rt.gameObject); _live.RemoveAt(i--); continue; }
                c.Y = float.IsNaN(c.Y) ? y : Mathf.Lerp(c.Y, y, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));   // slide up as older cards leave
                float slide = age < In ? (1f - age / In) * -40f : 0f;
                c.Rt.anchoredPosition = new Vector2(24f + slide, -c.Y);
                c.Cg.alpha = age < In ? age / In : age > life ? 1f - (age - life) / Out : 1f;
                if (InventoryGui.IsVisible()) c.Cg.alpha = 0f;   // the inventory panel takes that corner
                y += c.H + Gap;
            }
        }

        private static Card Build(Sprite icon, string topic, string name, float now)
        {
            var t = Plugin.Instance.ThemeData;
            if (topic == null)
            {   // feed line: a dark band fading out to the right, a green tick, the icon, the text (no box)
                var srt = Kit.Node(Root.Canvas.transform, "jatify-msg"); srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
                var scg = srt.gameObject.AddComponent<CanvasGroup>(); scg.blocksRaycasts = false; scg.alpha = 0f;
                float x0 = 14f;
                if (icon != null) { var sic = Kit.Img(srt, "I", icon, Color.white, false); sic.rectTransform.At(12, 3, 26, 26); sic.preserveAspect = true; x0 = 46f; }
                var st = Kit.Text(srt, "T", name, Fonts.Medium, 14f, Color.white); st.richText = true; st.overflowMode = TextOverflowModes.Ellipsis;
                st.fontSharedMaterial = st.font.material; st.outlineWidth = 0.1f; st.outlineColor = new Color32(0, 0, 0, 120);
                float w = Mathf.Min(Kit.W(st) + x0 + 120f, 520f);                    // the band runs on past the text before it fades
                st.rectTransform.At(x0, 0, Mathf.Min(Kit.W(st) + 4f, w - x0 - 20f), 32); st.alignment = TextAlignmentOptions.MidlineLeft;
                var band = Kit.Node(srt, "Band").gameObject.AddComponent<RawImage>(); band.texture = SkillBanner.FadeTex; band.uvRect = new Rect(0.5f, 0f, 0.5f, 1f);
                band.color = new Color(0.04f, 0.045f, 0.05f, 0.78f); band.raycastTarget = false; band.rectTransform.At(0, 0, w, 32); band.transform.SetAsFirstSibling();
                var tick = Kit.Panel(srt, "Tick", Theme.C(t.accent), 1); tick.rectTransform.At(0, 7, 2, 18);
                srt.sizeDelta = new Vector2(w, 32f);
                return new Card { Rt = srt, Cg = scg, Born = now, H = 32f };
            }
            if (topic == MoreTopic)
            {   // the rest of a burst: "+N more" and a strip of their icons (the Unlocks page lists them)
                var (ml, _) = Kit.Box(Root.Canvas.transform, "jatify-unlock", Theme.C(t.glass), Theme.C(t.glassLine), 12);
                var mrt = ml.rectTransform; mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0f, 1f); mrt.sizeDelta = new Vector2(300f, H);
                var mcg = mrt.gameObject.AddComponent<CanvasGroup>(); mcg.blocksRaycasts = false; mcg.alpha = 0f;
                var mt = Kit.Text(mrt, "Topic", "ALSO UNLOCKED", Fonts.Mono, 10f, Theme.C(t.accent)); mt.characterSpacing = 8f; mt.rectTransform.At(14, 9, 180, 14);
                var mn = Kit.Text(mrt, "Name", name, Fonts.Bold, 15f, Color.white); mn.alignment = TextAlignmentOptions.TopRight; mn.rectTransform.At(200, 6, 86, 20);
                var icons = _more.Count > 0 ? _more.Dequeue() : new List<Sprite>();
                for (int i = 0; i < icons.Count; i++) { if (icons[i] == null) continue; var mi = Kit.Img(mrt, "I", icons[i], Color.white, false); mi.rectTransform.At(14 + i * 30, 27, 24, 24); mi.preserveAspect = true; }
                return new Card { Rt = mrt, Cg = mcg, Born = now, H = H };
            }
            var (line, _) = Kit.Box(Root.Canvas.transform, "jatify-unlock", Theme.C(t.glass), Theme.C(t.glassLine), 12);
            var rt = line.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); rt.sizeDelta = new Vector2(300f, H);
            var cg = rt.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.alpha = 0f;
            var (tile, _) = Kit.Box(rt, "Tile", Theme.C(t.raised), Theme.C(t.glassLine), 9); tile.rectTransform.At(8, 8, 42, 42);
            if (icon != null) { var ic = Kit.Img(tile.rectTransform, "I", icon, Color.white, false); ic.rectTransform.Fill(5f); ic.preserveAspect = true; }
            var tp = Kit.Text(rt, "Topic", topic.ToUpperInvariant(), Fonts.Mono, 10f, Theme.C(t.accent)); tp.characterSpacing = 8f; tp.rectTransform.At(60, 11, 230, 14); tp.overflowMode = TextOverflowModes.Ellipsis;
            var nm = Kit.Text(rt, "Name", name, Fonts.Bold, 15f, Color.white); nm.rectTransform.At(60, 27, 230, 22); nm.overflowMode = TextOverflowModes.Ellipsis;
            return new Card { Rt = rt, Cg = cg, Born = now, H = H };
        }

        internal static void Destroy()
        {
            foreach (var c in _live) if (c.Rt != null) Object.Destroy(c.Rt.gameObject);
            _live.Clear(); _queue.Clear(); _burst.Clear(); _more.Clear();
        }
    }

    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowBiomeFoundMsg))]
    internal static class BiomeMessagePatch
    {
        private static bool Prefix(MessageHud __instance, string text, bool playStinger)
        {
            if (Root.Canvas == null || !Cfg.SkillBanner.Value) return true;
            BiomeBanner.Show(Localization.instance.Localize(text ?? ""), playStinger ? __instance.m_biomeFoundStinger : null);
            return false;
        }
    }

    // Biome discovered: a title in the upper middle, "BIOME DISCOVERED" between two hairlines that fade
    // outwards, the name large with its letter spacing settling as it fades in, the day under it.
    // Vanilla's sting still plays.
    internal static class BiomeBanner
    {
        private const float In = 0.9f, Hold = 3.6f, Out = 1.2f;
        private static readonly Queue<(string, GameObject)> _queue = new Queue<(string, GameObject)>();
        private static RectTransform _root; private static CanvasGroup _cg; private static TextMeshProUGUI _name; private static float _born;

        internal static void Show(string biome, GameObject stinger) { if (_queue.Count < 3) _queue.Enqueue((biome, stinger)); }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (_root != null)
            {
                float age = now - _born, hold = _queue.Count > 0 ? 1.5f : Hold;
                float k = age < In ? Mathf.SmoothStep(0f, 1f, age / In) : 1f;
                _cg.alpha = age < In ? k : age < In + hold ? 1f : 1f - (age - In - hold) / Out;
                _name.characterSpacing = Mathf.Lerp(40f, 12f, k);
                if (age > In + hold + Out) { Object.Destroy(_root.gameObject); _root = null; }
                return;
            }
            if (_queue.Count == 0) return;
            var (biome, stinger) = _queue.Dequeue();
            if (stinger != null) Object.Instantiate(stinger);   // as vanilla does
            Build(biome); _born = now;
        }

        private static void Build(string biome)
        {
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-biome"); Notices.OnTop(_root);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 1f); _root.anchoredPosition = new Vector2(0f, -210f); _root.sizeDelta = new Vector2(900f, 130f);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
            var glow = Kit.Img(_root, "Glow", SkillBanner.Fade(), new Color(0.03f, 0.035f, 0.04f, 0.55f), false); glow.rectTransform.At(0, 10, 900, 110);   // a soft dark band behind for contrast
            var label = Kit.Text(_root, "Label", "BIOME DISCOVERED", Fonts.Mono, 11f, Theme.C(t.accent)); label.characterSpacing = 16f; label.alignment = TextAlignmentOptions.Center; label.rectTransform.At(0, 18, 900, 16);
            float lw = Kit.W(label) / 2f + 18f;
            foreach (var (x, uv) in new[] { (450f - lw - 200f, new Rect(0f, 0f, 0.5f, 1f)), (450f + lw, new Rect(0.5f, 0f, 0.5f, 1f)) })
            {   // hairlines fading away from the label
                var ln = Kit.Node(_root, "Line").gameObject.AddComponent<RawImage>(); ln.texture = SkillBanner.FadeTex; ln.uvRect = uv; ln.color = new Color(0.13f, 0.84f, 0.49f, 0.8f); ln.raycastTarget = false;
                ln.rectTransform.At(x, 25.5f, 200, 1);
            }
            _name = Kit.Text(_root, "Name", biome.ToUpperInvariant(), Fonts.Bold, 46f, Color.white); _name.alignment = TextAlignmentOptions.Center; _name.rectTransform.At(0, 36, 900, 60);
            _name.fontSharedMaterial = _name.font.material; _name.outlineWidth = 0.12f; _name.outlineColor = new Color32(0, 0, 0, 140);
            var env = EnvMan.instance;
            var day = Kit.Text(_root, "Day", env != null ? $"Day {env.GetDay()} · {(int)(env.GetDayFraction() * 24f):00}:{(int)(env.GetDayFraction() * 1440f) % 60:00}" : "", Fonts.Mono, 12f, Theme.C(t.dim)); day.alignment = TextAlignmentOptions.Center; day.rectTransform.At(0, 100, 900, 16);
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; _queue.Clear(); }
    }

    // Valheim's centre messages ("You need a key", "Not enough space"...): a fading band where vanilla's text
    // was, a little above the middle; a new one replaces the old, as in vanilla.
    // design "toast": a small glass pill under the compass for something you just did (ate, drank, used the power)
    internal static class ActionToast
    {
        private const float Hold = 2.2f, Fade = 0.3f;
        private static RectTransform _root; private static CanvasGroup _cg; private static float _born;

        internal static void Show(string text, bool ok = true)
        {
            if (Root.Canvas == null || !Cfg.Messages.Value) return;
            Destroy();
            var t = Plugin.Instance.ThemeData;
            _root = Kit.Node(Root.Canvas.transform, "jatify-toast"); _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f); _root.pivot = new Vector2(0.5f, 1f);
            Notices.OnTop(_root);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
            var (bg, _) = Kit.Box(_root, "Bg", Theme.C(t.glass), Theme.C(t.glassLine), 10); bg.rectTransform.Fill();
            var ic = Kit.IconImg(_root, ok ? "check" : "rune", Theme.C(ok ? t.accent : t.ember), 18f); ic.rectTransform.At(14, 8, 18, 18);
            var tx = Kit.Text(_root, "T", text, Fonts.Medium, 13.5f, Color.white); tx.alignment = TextAlignmentOptions.MidlineLeft;
            float w = Kit.W(tx); tx.rectTransform.At(42, 0, w + 4, 34);
            _root.sizeDelta = new Vector2(42 + w + 16, 34); _root.anchoredPosition = new Vector2(0f, -86f);
            _born = Time.unscaledTime;
        }

        internal static void Tick()
        {
            if (_root == null) return;
            float age = Time.unscaledTime - _born;
            _cg.alpha = age < Fade ? age / Fade : age < Fade + Hold ? 1f : 1f - (age - Fade - Hold) / Fade;
            if (age > Hold + 2 * Fade) Destroy();
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; }
    }

    [HarmonyPatch(typeof(Player))]
    internal static class ActionToastPatches
    {
        [HarmonyPostfix, HarmonyPatch(nameof(Player.ConsumeItem))]
        private static void Ate(Player __instance, Inventory inventory, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer || item == null) return;
            int left = inventory != null ? inventory.CountItems(item.m_shared.m_name) : 0;
            var name = Localization.instance.Localize(item.m_shared.m_name);
            ActionToast.Show(item.m_shared.m_food > 0f ? $"Ate {name} \u00b7 {left} left" : $"{name} \u00b7 {left} left");
        }

        [HarmonyPostfix, HarmonyPatch(nameof(Player.StartGuardianPower))]
        private static void Power(Player __instance, bool __result) { if (__result && __instance == Player.m_localPlayer) ActionToast.Show($"{SkillUpMessagePatch.PowerName()} activated"); }
    }

    internal static class CenterPill
    {
        private const float In = 0.25f, Hold = 3.2f, Out = 0.6f;
        private const float CentreY = 335f;   // centre at y 205 of 1080: the gap between the action bar and the inventory rows (user)
        private static RectTransform _root; private static CanvasGroup _cg; private static float _born;

        internal static void Show(Sprite icon, string text)
        {
            Destroy();
            var t = Plugin.Instance.ThemeData;
            // a band fading out to both sides, faint hairlines above and below, the message centred (the skill banner's language)
            _root = Kit.Node(Root.Canvas.transform, "jatify-centremsg"); _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = new Vector2(0f, CentreY); _root.sizeDelta = new Vector2(820f, 56f);
            Notices.OnTop(_root);
            _cg = _root.gameObject.AddComponent<CanvasGroup>(); _cg.blocksRaycasts = false; _cg.alpha = 0f;
            var band = Kit.Img(_root, "Band", SkillBanner.Fade(), new Color(0.04f, 0.045f, 0.05f, 0.85f), false); band.rectTransform.At(0, 0, 820, 56);
            foreach (var (y, a) in new[] { (0f, 0.7f), (55f, 0.35f) }) { var hl = Kit.Img(_root, "Line", SkillBanner.Fade(), new Color(0.13f, 0.84f, 0.49f, a), false); hl.rectTransform.At(60, y, 700, 1); }   // our green, like the skill banner
            var tx = Kit.Text(_root, "T", text, Fonts.Medium, 18f, Color.white); tx.richText = true; tx.alignment = TextAlignmentOptions.Center; tx.overflowMode = TextOverflowModes.Ellipsis;
            tx.fontSharedMaterial = tx.font.material; tx.outlineWidth = 0.1f; tx.outlineColor = new Color32(0, 0, 0, 120);
            float tw = Mathf.Min(Kit.W(tx), 640f);
            if (icon != null)
            {   // icon and text centred together
                var ic = Kit.Img(_root, "I", icon, Color.white, false); ic.preserveAspect = true;
                float gx = 410f - (tw + 38f) / 2f; ic.rectTransform.At(gx, 13, 30, 30); tx.rectTransform.At(gx + 38f, 0, tw + 4f, 56);
            }
            else tx.rectTransform.At(90, 0, 640, 56);
            _born = Time.unscaledTime;
        }

        internal static void Tick()
        {
            if (_root == null) return;
            float age = Time.unscaledTime - _born;
            _cg.alpha = age < In ? age / In : age < In + Hold ? 1f : 1f - (age - In - Hold) / Out;
            _root.anchoredPosition = new Vector2(0f, CentreY - (age < In ? (1f - age / In) * 8f : 0f));   // drifts up into place
            if (age > In + Hold + Out) Destroy();
        }

        internal static void Destroy() { if (_root != null) Object.Destroy(_root.gameObject); _root = null; }
    }
}
