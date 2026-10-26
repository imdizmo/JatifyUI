using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Jatify.Campfire
{
    // The loading screen's picture: one of Iron Gate's press-kit artworks (user), a different one each time the loading
    // screen comes up. They ship as 1920x1080 JPGs in a "campfire-loading" folder beside the DLL (or anywhere under
    // plugins), and only the one on screen is in memory: the previous texture is freed when the next is picked.
    [HarmonyPatch(typeof(Hud), "UpdateBlackScreen")]
    internal static class LoadingArt
    {
        private static string[] _files;
        private static Texture2D _tex;
        private static Sprite _sprite, _vanilla;
        private static bool _up;
        private static Hud _hud;
        private static Color _vanillaColor = Color.white;
        // ImageConversion.LoadImage by reflection, as Jatify UI does: its assembly targets a newer netstandard than we can reference
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method("UnityEngine.ImageConversion:LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        private static void Postfix(Hud __instance)
        {
            if (!Module.On || !Module.LoadingScreen.Value) return;   // off: Valheim's picture (put back when switched off)
            if (Time.unscaledTime < LoadBar.PreviewUntil)   // the test panel's preview: held up like a real load (Valheim fades it out again after)
            {
                __instance.m_loadingScreen.gameObject.SetActive(true); __instance.m_loadingScreen.alpha = 1f;
                __instance.m_loadingProgress.SetActive(true); __instance.m_sleepingProgress.SetActive(false); __instance.m_teleportingProgress.SetActive(false);
            }
            if (__instance != _hud) { _hud = __instance; _up = false; }   // a new HUD each time a world loads (user: the art never changed; the flag was left from the last session)
            bool up = __instance.m_loadingScreen.gameObject.activeSelf;   // each time the screen comes up: loading, teleporting, respawning
            if (up == _up) return;
            _up = up;
            if (up) Pick(__instance.m_loadingImage);
        }

        private static void Pick(UnityEngine.UI.Image img)
        {
            if (img == null) return;
            var files = Files(); if (files.Length == 0) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "campfire-loading", wrapMode = TextureWrapMode.Clamp };
            if (Load == null || !(bool)Load.Invoke(null, new object[] { tex, File.ReadAllBytes(files[Random.Range(0, files.Length)]), true })) { Object.Destroy(tex); return; }
            if (_vanilla == null) { _vanilla = img.sprite; _vanillaColor = img.color; }
            var old = (_tex, _sprite);
            _tex = tex; _sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            img.sprite = _sprite; img.preserveAspect = false; img.color = Color.white;   // Valheim tints its own art blue
            if (old._sprite != null) Object.Destroy(old._sprite);
            if (old._tex != null) Object.Destroy(old._tex);
        }

        private static string[] Files()
        {
            if (_files != null) return _files;
            var here = Module.Dir;
            var dir = !string.IsNullOrEmpty(here) && Directory.Exists(Path.Combine(here, "campfire-loading")) ? Path.Combine(here, "campfire-loading")
                : Directory.GetDirectories(Paths.PluginPath, "campfire-loading", SearchOption.AllDirectories).FirstOrDefault();
            _files = dir != null ? Directory.GetFiles(dir, "*.jpg") : new string[0];
            if (_files.Length == 0) Log.Info("no campfire-loading folder: Valheim's own loading picture stays");
            return _files;
        }

        // unloaded (ScriptEngine): Valheim's picture back, ours freed
        internal static void Unload()
        {
            var img = Hud.instance != null ? Hud.instance.m_loadingImage : null;
            if (img != null && _vanilla != null && img.sprite == _sprite) { img.sprite = _vanilla; img.color = _vanillaColor; }
            if (_sprite != null) Object.Destroy(_sprite);
            if (_tex != null) Object.Destroy(_tex);
            _sprite = null; _tex = null; _vanilla = null; _files = null; _up = false; _hud = null;
        }
    }
}
