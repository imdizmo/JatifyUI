using UnityEngine;

namespace Jatify.UI
{
    // Fullscreen-window at a size that isn't the screen's (a saved window size: 1920 x 1014 on a 1080 screen) shows black bars, and
    // Unity on Linux reads the mouse as if they weren't there: every click lands below the cursor. Not ours to change (the player's
    // setting): said once a session, a few seconds into the world, and in the log.
    internal static class ScreenCheck
    {
        private static bool _done; private static float _at = -1f;
        internal static void Tick()
        {
            if (_done || Player.m_localPlayer == null || MessageHud.instance == null) { if (Player.m_localPlayer == null) _at = -1f; return; }
            if (_at < 0f) { _at = Time.unscaledTime + 5f; return; }
            if (Time.unscaledTime < _at) return;
            _done = true;
            var d = Screen.mainWindowDisplayInfo; var say = Problem(Screen.fullScreenMode, Screen.width, Screen.height, d.width, d.height); if (say == null) return;
            Log.Warn(say);
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, say);
        }


        // what to tell the player, or null: only fullscreen-window of another shape than the screen
        internal static string Problem(FullScreenMode mode, int w, int h, int dw, int dh)
        {
            if (mode != FullScreenMode.FullScreenWindow || dw <= 0 || dh <= 0 || w <= 0 || h <= 0) return null;
            if (Mathf.Abs((float)w / h - (float)dw / dh) < 0.01f) return null;   // same shape: scaled, no bars
            return $"The game runs at {w}x{h} on a {dw}x{dh} screen, so clicks can land off the cursor. Set the resolution to {dw}x{dh} in Settings > Graphics.";
        }
    }
}
