using BepInEx.Logging;

namespace Jatify.UI
{
    internal static class Log
    {
        private static ManualLogSource _s;
        internal static void Bind(ManualLogSource s) { _s = s; } // rebound on every (re)load
        internal static void Info(string m) { _s?.LogInfo(m); }
        internal static void Warn(string m) { _s?.LogWarning(m); }
        internal static void Error(string m) { _s?.LogError(m); }
    }
}
