using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // One overlay canvas that owns every Jatify UI object, scaled from a 1920x1080 reference.
    // Destroying it removes everything the mod drew.
    internal static class Root
    {
        internal static Canvas Canvas;
        private static CanvasScaler _scaler;
        private static ThemeData _t;
        private static Image _badge;
        private static bool _badgeOff;
        internal static void Badge(bool on) { _badgeOff = !on; if (_badge != null) _badge.gameObject.SetActive(on); }   // DevShot badge-off / badge-on: clean screenshots
        private static TextMeshProUGUI _badgeText;
        private static HudView _hud;

        internal static void Create(Theme theme)
        {
            var go = new GameObject("JatifyUI", typeof(RectTransform));
            Object.DontDestroyOnLoad(go);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 50;
            _scaler = go.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            BuildBadge();
            Restyle(theme.D);
        }

        // Dev badge, top centre: proves the reload loop works and shows the reload count.
        private static void BuildBadge()
        {
            _badge = Kit.Panel(Canvas.transform, "DevBadge", Color.black, 10);
            var rt = _badge.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -84); rt.sizeDelta = new Vector2(360, 34);
            _badge.raycastTarget = false;
            _badgeText = Kit.Text(rt, "Text", "", Fonts.Mono, 12, Color.white);
            var tr = _badgeText.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            _badgeText.alignment = TextAlignmentOptions.Center;
            _badgeText.richText = true;
        }

        internal static void Restyle(ThemeData t)
        {
            _t = t;
            if (Canvas == null) return;
            _scaler.scaleFactor = 1f;
            _scaler.referenceResolution = new Vector2(1920, 1080) / Mathf.Clamp(t.uiScale, 0.5f, 2f);
            _badge.gameObject.SetActive(t.devBadge && Dev.Build);
            _badge.sprite = Kit.Rounded(Mathf.RoundToInt(t.radius));
            _badge.color = Theme.C(t.glass);
            _badgeText.color = Theme.C(t.dim);
            var accent = ColorUtility.ToHtmlStringRGB(Theme.C(t.accent));
            _badgeText.text = $"<color=#{accent}>●</color>  Jatify UI {Plugin.Version} dev  ·  load #{Dev.Loads}  ·  {System.DateTime.Now:HH:mm:ss}";
            _hud?.Destroy();
            _hud = new HudView(Canvas.transform, t); // rebuilt on every theme change: colours live in the parts
            TooltipStyle.T = t;
            _badge.transform.SetAsLastSibling();
        }

        internal static void Tick()
        {
            _hud?.Tick();
            if (_badge != null && _t != null) { bool show = _t.devBadge && Dev.Build && !_badgeOff && !InventoryGui.IsVisible() && !Menu.IsVisible() && !(_hud != null && _hud.RaidShown) /* the raid banner sits where the badge is */; if (_badge.gameObject.activeSelf != show) _badge.gameObject.SetActive(show); }
        }
        internal static RectTransform MapHole => _hud?.MapHole;
        internal static Deeds.AchHud Ach => _hud?.Ach;

        internal static void Destroy()
        {
            _hud?.Destroy(); _hud = null;
            TooltipStyle.Unload();
            if (Canvas != null) Object.Destroy(Canvas.gameObject);
            Canvas = null; _scaler = null; _badge = null; _badgeText = null;
        }
    }
}
