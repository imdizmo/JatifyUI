using System;
using System.IO;
using UnityEngine;

namespace Jatify.UI
{
    // The look as data: BepInEx/config/jatify.ui/theme.json. Saving the file restyles the UI in place,
    // no reload needed. Missing keys keep their defaults; a broken file is reported and ignored.
    [Serializable]
    internal class ThemeData
    {
        public string glass = "#262323C5";     // panel fill
        public string glassLine = "#FFFFFF26";  // panel outline
        public string line2 = "#FFFFFF33";      // stronger outline
        public string raised = "#2A2B30F2";     // slots, rows
        public string raised2 = "#2C2C32E6";    // switches, disabled buttons
        public string text = "#ECEEED";
        public string dim = "#B4B4BA";
        public string mute = "#80808A";
        public string accent = "#EC8219";
        public string accentSoft = "#EC821924";
        public string ember = "#F2703A";
        public string warnText = "#E4892D";     // softer orange for warning text in the crafting panel (user)
        public string emberSoft = "#F2703A29";
        public string health = "#E5484D";
        public string stamina = "#F5B942";
        public string eitr = "#740DFF";
        public float radius = 12f;              // panel corner radius, px at 1080p
        public float uiScale = 1f;              // on top of the 1080p reference scaling
        public float textSize = 15f;
        public bool devBadge = false;   // the dev build's "load #N" pill (user: off; theme.json devBadge true brings it back)
    }

    internal sealed class Theme : IDisposable
    {
        internal ThemeData D { get; private set; } = new ThemeData();
        internal event Action<ThemeData> Changed;
        private readonly string _file;
        private FileSystemWatcher _watch;
        private volatile bool _dirty;

        internal Theme(string configDir)
        {
            var dir = Path.Combine(configDir, "jatify.ui");
            Directory.CreateDirectory(dir);
            _file = Path.Combine(dir, "theme.json");
            if (!File.Exists(_file)) File.WriteAllText(_file, JsonUtility.ToJson(D, true));
            Read(); Migrate();
            _watch = new FileSystemWatcher(dir, "theme.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _watch.Changed += (_, __) => _dirty = true;  // watcher threads only flag; Unity work happens in Tick
            _watch.Created += (_, __) => _dirty = true;
            _watch.Renamed += (_, __) => _dirty = true;  // editors that save via rename
            _watch.EnableRaisingEvents = true;
        }

        internal void Tick()
        {
            if (!_dirty) return;
            _dirty = false;
            if (Read()) { Log.Info("theme.json applied"); Changed?.Invoke(D); }
        }

        // 0.3.0: orange replaced green as the default. Every install wrote the old defaults into its file, so a token still at its
        // old default moves to the new one; a colour the player picked stays.
        private static readonly (string token, string was)[] Old =
        {
            ("glass", "#141416EB"), ("line2", "#FFFFFF29"), ("accent", "#22D67E"), ("accentSoft", "#22D67E24"), ("warnText", "#F59B75"), ("eitr", "#5AA9FF"),
        };
        private void Migrate()
        {
            var fresh = new ThemeData(); bool moved = false;
            foreach (var (token, was) in Old)
            {
                var f = typeof(ThemeData).GetField(token);
                if (!string.Equals((string)f.GetValue(D), was, StringComparison.OrdinalIgnoreCase)) continue;
                f.SetValue(D, f.GetValue(fresh)); moved = true;
            }
            if (!moved) return;
            try { File.WriteAllText(_file, JsonUtility.ToJson(D, true)); Log.Info("theme.json: the old green defaults moved to the new orange ones"); }
            catch (Exception e) { Log.Warn("theme.json not updated: " + e.Message); }
        }

        private bool Read()
        {
            try
            {
                var d = new ThemeData();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(_file), d);
                D = d;
                return true;
            }
            catch (Exception e) { Log.Warn("theme.json not applied: " + e.Message); return false; }
        }

        internal static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        internal static Color SoftEmber(ThemeData t) => C(t.warnText);   // the crafting panel's warnings: a softer orange (user)

        public void Dispose() { if (_watch != null) { _watch.EnableRaisingEvents = false; _watch.Dispose(); _watch = null; } }
    }
}
