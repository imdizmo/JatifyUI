using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Screen alerts: a soft red vignette pulses at the screen edges while health is low,
    // and flashes once when eitr runs out (not stamina: user). Never over menus or the inventory.
    internal static class AlertFlash
    {
        internal static ConfigEntry<bool> On;
        private static Image _img; private static Sprite _vignette;
        private static float _flash; private static bool _eiWasOut;

        internal static void Bind(ConfigFile c) => On = c.Bind("HUD", "AlertFlash", true, "Red vignette pulse at low health; a flash when eitr runs out.");

        internal static void Tick()
        {
            var p = Player.m_localPlayer;
            if (!On.Value || p == null || Root.Canvas == null || p.IsDead()) { Hide(); return; }
            if (_img == null)
            {
                _img = Kit.Img(Root.Canvas.transform, "jatify-alert", Vignette(), new Color(0.85f, 0.08f, 0.06f, 0f), false);
                _img.rectTransform.Fill(); _img.raycastTarget = false; _img.transform.SetAsFirstSibling();   // under every panel
            }
            bool eiOut = p.GetMaxEitr() > 0f && p.GetEitr() < 1f;
            if (eiOut && !_eiWasOut) _flash = 1f;
            _eiWasOut = eiOut;
            float hp = p.GetHealthPercentage(), a = 0f;
            if (hp < 0.25f) { float k = 1f - hp / 0.25f; a = (0.18f + 0.22f * k) * (0.6f + 0.4f * Mathf.Sin(Time.time * (3f + 4f * k))); }   // faster and stronger the lower it gets
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 3f);
            a = Mathf.Max(a, 0.35f * _flash);
            if (InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen()) a = 0f;
            var c = _img.color; if (Mathf.Abs(c.a - a) > 0.002f) { c.a = a; _img.color = c; }
        }

        private static void Hide() { if (_img != null && _img.color.a > 0f) { var c = _img.color; c.a = 0f; _img.color = c; } }

        // a 128 px vignette: clear centre, opaque edges (made once, kept)
        private static Sprite Vignette()
        {
            if (_vignette != null) return _vignette;
            const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "jatify-vignette" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, d = Mathf.Sqrt(dx * dx * 0.9f + dy * dy * 1.1f);
                float a = Mathf.Clamp01((d - 0.55f) / 0.6f); a *= a;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return _vignette = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        internal static void Destroy() { if (_img != null) Object.Destroy(_img.gameObject); _img = null; }
    }
}
