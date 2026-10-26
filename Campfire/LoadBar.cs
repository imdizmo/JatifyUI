using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // A WoW-style loading bar over the loading screen (the Jatify design's stLoading: the world's name in Norse, the
    // character and the stage in Averia, one long ember-to-amber bar, the percentage). It follows Valheim's real stages and creeps
    // inside each one (approaching the stage's end, never reaching it), so it always moves while
    // frames are drawn. When Valheim freezes the main thread (drawing the map, loading the area) the
    // bar simply stalls like any loading bar.
    internal static class LoadBar
    {
        private enum Stage { Connecting, Password, Receiving, Map, Entering, Done }

        // Stage start, stage end (fraction of the bar) and a typical length in seconds (for the creep).
        private static readonly (float from, float to, float secs, string text)[] Stages =
        {
            (0.00f, 0.10f, 2f, "Connecting"),
            (0.10f, 0.12f, 1f, "Waiting for the password"),
            (0.12f, 0.45f, 6f, "Receiving the world"),
            (0.45f, 0.55f, 2f, "Drawing the map"),
            (0.55f, 0.99f, 12f, "Entering the world"),
            (1.00f, 1.00f, 0.4f, "Ready"),
        };

        private static bool _active, _hosting, _sawZNet, _builtAtStartup;
        private static float _startupAt = -10f;
        private static string _title, _who;
        private static Stage _stage;
        private static float _shown, _stageTime, _doneAt;
        private static RectTransform _root, _fill;
        private static TextMeshProUGUI _stageText, _titleText, _pctText;

        internal static void Begin(string title, bool hosting)
        {
            if (!Module.LoadingScreen.Value) return;   // off: Valheim's own loading screen
            _active = true; _hosting = hosting; _sawZNet = false; _title = title;
            _stage = hosting ? Stage.Receiving : Stage.Connecting;
            _shown = Stages[(int)_stage].from; _stageTime = 0f; _doneAt = 0f;
            _who = CharScreen.Selected?.GetName();
            // Built on the game's first loading screen, before Valheim's own fonts were there: build again.
            if (_root != null && _builtAtStartup) { UnityEngine.Object.Destroy(_root.root.gameObject); _root = null; }
        }

        // the F8 test panel's "Loading screen" button (Jatify UI; user: to see the loading screen on demand): Valheim's loading screen with a new picture and our bar
        // running through the stages for a few seconds; nothing is loaded
        internal const float PreviewSecs = 6f;
        internal static float PreviewUntil;
        internal static void Preview()
        {
            if (!Module.On || !Module.LoadingScreen.Value || _active || Hud.instance == null) return;
            PreviewUntil = Time.unscaledTime + PreviewSecs;
            if (_root != null && _builtAtStartup) { UnityEngine.Object.Destroy(_root.root.gameObject); _root = null; }
        }
        private static void TickPreview()
        {
            if (_root == null) Build(false);
            _root.gameObject.SetActive(true); _root.SetAsLastSibling();
            float k = Mathf.Clamp01(1f - (PreviewUntil - Time.unscaledTime) / PreviewSecs);
            _shown = k;
            var stage = Stages.LastOrDefault(s => s.from <= k && s.to > s.from).text ?? Stages[0].text;
            Paint(ZNet.instance != null ? ZNet.instance.GetWorldName() : "Valheim", $"{Player.m_localPlayer?.GetPlayerName()} · {stage}… (preview)");
        }

        internal static void Tick()
        {
            if (!Module.LoadingScreen.Value) { _active = false; PreviewUntil = 0f; return; }   // switched off: Unload took the bar away
            if (Time.unscaledTime < PreviewUntil) { TickPreview(); return; }
            if (!_active) { if (_root != null && Time.unscaledTime - _startupAt > 0.5f) _root.gameObject.SetActive(false); return; }
            var next = Detect();
            if (next == null) { End(); return; }
            if (next.Value != _stage)
            {
                if (next.Value > _stage) _shown = Mathf.Max(_shown, Stages[(int)next.Value].from);
                _stage = next.Value; _stageTime = 0f;
            }
            _stageTime += Mathf.Min(Time.unscaledDeltaTime, 0.25f);   // a frozen frame doesn't leap the bar
            var s = Stages[(int)_stage];
            // Creep: cover ~63% of what's left of the stage per typical stage length.
            float target = s.to - (s.to - s.from) * Mathf.Exp(-_stageTime / Mathf.Max(0.1f, s.secs));
            _shown = Mathf.Max(_shown, _stage == Stage.Done ? 1f : target);

            if (_root == null) Build(false);
            _root.gameObject.SetActive(true);
            HideMenuLoadingText();
            _root.SetAsLastSibling();
            var stage = _stage == Stage.Password ? "Waiting for the password" : s.text + (_stage == Stage.Done ? "" : "…");
            Paint(_title ?? "", string.IsNullOrEmpty(_who) ? stage : $"{_who} · {stage}");
            if (_stage == Stage.Done)
            {
                if (_doneAt == 0f) _doneAt = Time.unscaledTime;
                if (Time.unscaledTime - _doneAt > 0.5f) End();
            }
        }

        // The game's first load (the SceneLoader scene): Valheim's own progress, our bar instead of its ring.
        internal static void Startup(float progress, TMP_FontAsset vanillaFont)
        {
            if (_active) return;
            if (vanillaFont != null && Fallback == null) Fallback = vanillaFont;
            if (_root == null) Build(true);
            _startupAt = Time.unscaledTime;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _shown = Mathf.Max(_root.gameObject.activeSelf ? _shown : 0f, Mathf.Clamp01(progress));
            Paint("Valheim", "Loading the game…");
        }

        // the main menu's own "Loading..." bottom right while it fades out (user: remove it); ours says it already
        private static void HideMenuLoadingText()
        {
            var f = FejdStartup.instance; if (f == null) return;
            var go = Traverse.Create(f).Field("m_loading").GetValue<GameObject>();
            var t = go != null ? go.transform.Find("Text") : null;
            if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
        }

        private static void Paint(string title, string stage)
        {
            SetShadowed(_titleText, title);
            SetShadowed(_stageText, stage);
            Set(_pctText, $"{Mathf.RoundToInt(_shown * 100f)}%");
            _fill.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1f);
        }

        // What Valheim is doing now; null = the load is over (back in the menu, or failed).
        private static Stage? Detect()
        {
            var znet = ZNet.instance;
            if (znet == null) return _sawZNet ? (Stage?)null : _stage;   // menu still fading out
            _sawZNet = true;
            if (Player.m_localPlayer != null) return Stage.Done;
            if (!_hosting)
            {
                var status = ZNet.GetConnectionStatus();
                if (status != ZNet.ConnectionStatus.Connected && status != ZNet.ConnectionStatus.Connecting && status != ZNet.ConnectionStatus.None) return null;
                if (znet.InPasswordDialog()) return Stage.Password;
                if (ZNet.World == null) return status == ZNet.ConnectionStatus.Connected ? Stage.Receiving : Stage.Connecting;
            }
            var map = Minimap.instance;
            bool mapDone = map != null && Traverse.Create(map).Field("m_hasGenerated").GetValue<bool>();
            if (!mapDone) return ZNet.World == null ? Stage.Receiving : Stage.Map;
            return Stage.Entering;
        }

        internal static void Unload()
        {
            _active = false; PreviewUntil = 0f;
            if (_root != null) UnityEngine.Object.Destroy(_root.root.gameObject);
            _root = null;
        }

        private static void End()
        {
            _active = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private static void Build(bool startup)
        {
            _builtAtStartup = startup;
            _root = MakeCanvas("CampfireLoadBar", 31999, false);   // over loading-screen mods, under the password prompt
            // .cf-loadvig: the lower half darkens so the bar reads over any loading art
            Fill(Img(_root, Gradient("load-v", true, (0f, NoColor), (0.45f, NoColor), (1f, new Color(0.02f, 0.02f, 0.024f, 0.85f))), White, "Vignette").rectTransform);
            var box = At(Node(_root, "load"), 0.5f, 0f, 0f, 175f, 1100f, 130f);   // above Valheim's tip and its braid (bottom 52-158; user: the bar sat on the tips)
            _titleText = Shadowed(box, "", "title", 46f, White, 0f, -8f, 1100f, 70f, TextAlignmentOptions.Center);
            _stageText = Shadowed(box, "", "lore", 16f, C("dim"), 0f, 58f, 1100f, 26f, TextAlignmentOptions.Center);
            var track = Box(box, "bar", new Color(0f, 0f, 0f, 0.55f), C("line2"), 5); track.raycastTarget = false; Place(track.rectTransform, 0f, 92f, 1100f, 10f);
            var mask = Fill(Node(track.transform, "mask"), 1f); mask.gameObject.AddComponent<RectMask2D>();
            var fill = Img(mask, Gradient("load-fill", false, (0f, C("ember")), (1f, C("stamina"))), White, "fill");
            _fill = fill.rectTransform; _fill.anchorMin = Vector2.zero; _fill.anchorMax = new Vector2(0f, 1f); _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            _pctText = Text(box, "", "mono", 12f, C("mute"), 0f, 106f, 1100f, 22f, TextAlignmentOptions.Center);
        }
    }
}

namespace Jatify.Campfire
{
    // On the startup screen only: hide Valheim's round progress indicator (and the percentage other
    // mods put in it) and feed its progress to our bar. Elsewhere the indicator is left alone.
    [HarmonyLib.HarmonyPatch(typeof(LoadingIndicator), nameof(LoadingIndicator.SetProgress))]
    internal static class StartupBar
    {
        private static void Postfix(LoadingIndicator __instance, float progress)
        {
            if (!Module.On || !Module.LoadingScreen.Value) return;   // off: Valheim's own ring
            if (UnityEngine.Object.FindObjectOfType<SceneLoader>() == null) return;
            var t = HarmonyLib.Traverse.Create(__instance);
            foreach (var f in new[] { "m_spinner", "m_progressIndicator", "m_background" })
            {
                var img = t.Field(f).GetValue<UnityEngine.UI.Image>();
                if (img != null) img.enabled = false;
            }
            var text = t.Field("m_text").GetValue<TMPro.TMP_Text>();
            if (text != null) text.enabled = false;
            LoadBar.Startup(progress, text != null ? text.font : null);
        }
    }
}
