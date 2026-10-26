using System;

namespace Jatify.UI
{
    // Load counter that survives ScriptEngine reloads (each reload is a new assembly, so statics reset;
    // the AppDomain does not). Every reload keeps the previous assembly in memory, roughly the DLL's size,
    // so the badge shows the count: restart the game when it climbs high.
    internal static class Dev
    {
        private const string Key = "jatify.ui.loads";
        internal static int Loads { get; private set; }
        // loaded by ScriptEngine (the dev profile's hot reload) rather than from plugins: cheats like map teleport only here
        internal static readonly bool Build = string.IsNullOrEmpty(typeof(Dev).Assembly.Location) || typeof(Dev).Assembly.Location.Replace('\\', '/').Contains("/scripts/");
        internal static void CountLoad()
        {
            Loads = (AppDomain.CurrentDomain.GetData(Key) as int? ?? 0) + 1;
            AppDomain.CurrentDomain.SetData(Key, Loads);
        }
    }
}
