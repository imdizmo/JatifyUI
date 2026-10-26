namespace Jatify.UI.Deeds
{
    // Deeds' model logs through Jatify UI's log.
    internal static class Log
    {
        internal static void Info(string m) => Jatify.UI.Log.Info(m);
        internal static void Warn(string m) => Jatify.UI.Log.Warn(m);
    }
}
