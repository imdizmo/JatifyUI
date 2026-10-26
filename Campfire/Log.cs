namespace Jatify.Campfire
{
    // Campfire's lines in Jatify UI's log, marked as Campfire's
    internal static class Log
    {
        internal static void Info(string m) => UI.Log.Info("Campfire: " + m);
        internal static void Warn(string m) => UI.Log.Warn("Campfire: " + m);
        internal static void Error(string m) => UI.Log.Error("Campfire: " + m);
    }
}
