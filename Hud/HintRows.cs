using System.Collections.Generic;
using UnityEngine;

namespace Jatify.UI
{
    // KeyHints off (user): the "key + what it does" rows across the UI go: the HUD corners, the inventory and chest key bars,
    // the build menu's and the map's foot rows. Hover cards keep theirs (they are how you use the thing). Switched live.
    internal static class HintRows
    {
        internal static bool On => Cfg.Hints == null || Cfg.Hints.Value;
        private static readonly List<GameObject> _rows = new List<GameObject>();
        private static bool _shown = true;

        internal static void Register(GameObject row) { _rows.RemoveAll(g => g == null); _rows.Add(row); row.SetActive(On); }

        internal static void Tick()
        {
            bool on = On; if (on == _shown) return; _shown = on;
            _rows.RemoveAll(g => g == null);
            foreach (var g in _rows) g.SetActive(on);
        }
    }
}
