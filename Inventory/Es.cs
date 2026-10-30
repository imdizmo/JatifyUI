using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Jatify.UI
{
    // shudnal's ExtraSlots: like AzuEPI it keeps its slots as rows at the bottom of the player inventory and sizes the grid
    // itself every frame. Installed, it owns the grid and Jatify stands aside exactly as for AzuEPI (no rows of ours, no
    // save packing, no moving items for them): two mods reshaping one grid push worn gear out of the inventory.
    // Its cells are ordinary cells of our grid: InventoryStyle places them where our own slots sit (EsCell), and the HUD bars
    // show its quick, food and ammo slots. Soft: its public API by reflection, from its own assembly.
    internal static class Es
    {
        internal const string Guid = "shudnal.ExtraSlots";
        private static bool? _on;
        private static MethodInfo _heightPlayer; private static FieldInfo _slotsField; private static PropertyInfo _slotsProp;
        private static PropertyInfo _id, _active, _grid; private static MethodInfo _shortcut;

        internal static bool Drives
        {
            get
            {
                if (_on != null) return _on.Value;
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(Guid, out var info)) { if (Epic.Started) _on = false; return false; }   // plugins still loading: ask again
                if (info.Instance == null) return false;
                _on = true; Bind(info.Instance.GetType().Assembly);
                return true;
            }
        }

        private static void Bind(Assembly asm)
        {
            var api = asm.GetType("ExtraSlots.API"); var slots = asm.GetType("ExtraSlots.Slots"); var slot = asm.GetType("ExtraSlots.Slots+Slot");
            _heightPlayer = api != null ? AccessTools.Method(api, "GetInventoryHeightPlayer") : null;
            _slotsField = slots != null ? AccessTools.Field(slots, "slots") : null; if (_slotsField == null && slots != null) _slotsProp = AccessTools.Property(slots, "slots");
            if (slot != null) { _id = AccessTools.Property(slot, "ID"); _active = AccessTools.Property(slot, "IsActive"); _grid = AccessTools.Property(slot, "GridPosition"); _shortcut = AccessTools.Method(slot, "GetShortcutText"); }
            Log.Info($"ExtraSlots found: its slot rows own the inventory grid, Jatify's rows stand aside; API {(api != null && _heightPlayer != null ? "bound" : "missing")}, slots {(_id != null && _grid != null ? "bound" : "missing")}");
        }

        // the bag rows: what sits above its slot rows
        private static int _rowsFrame = -1, _rows = 4;
        internal static int NormalRows
        {
            get
            {
                if (_rowsFrame == UnityEngine.Time.frameCount) return _rows; _rowsFrame = UnityEngine.Time.frameCount;
                var p = Player.m_localPlayer; if (p == null) return _rows = 4;
                if (_heightPlayer != null) try { return _rows = Math.Max(1, (int)_heightPlayer.Invoke(null, null)); } catch (Exception e) { Log.Warn("ExtraSlots GetInventoryHeightPlayer: " + e.Message); _heightPlayer = null; }
                return _rows = Math.Max(1, p.GetInventory().GetHeight() - 5);   // it always adds five rows at width 8
            }
        }

        internal struct Slot { internal string Id, Key; internal Vector2i Cell; internal bool Active; }
        private static readonly List<Slot> _list = new List<Slot>(); private static float _listAt = -1f;
        private static readonly Dictionary<int, int> _byCell = new Dictionary<int, int>();

        // its slots in their order, read again once a second (progression and config add slots while you play)
        internal static List<Slot> Slots
        {
            get
            {
                if (UnityEngine.Time.unscaledTime < _listAt) return _list;
                _listAt = UnityEngine.Time.unscaledTime + 1f; _list.Clear(); _byCell.Clear();
                if (_id == null || _grid == null) return _list;
                try
                {
                    var arr = (_slotsField != null ? _slotsField.GetValue(null) : _slotsProp?.GetValue(null)) as System.Collections.IEnumerable; if (arr == null) return _list;
                    foreach (var s in arr)
                    {
                        if (s == null) continue;
                        var id = _id.GetValue(s) as string; if (string.IsNullOrEmpty(id) || id == "Empty") continue;
                        var sl = new Slot { Id = id, Cell = (Vector2i)_grid.GetValue(s), Active = _active == null || (bool)_active.GetValue(s), Key = _shortcut != null ? _shortcut.Invoke(s, null) as string ?? "" : "" };
                        _byCell[sl.Cell.y * 128 + sl.Cell.x] = _list.Count; _list.Add(sl);
                    }
                }
                catch (Exception e) { Log.Warn("ExtraSlots slots: " + e.Message); _id = null; }
                return _list;
            }
        }

        internal static bool At(int x, int y, out Slot s) { var l = Slots; s = default; if (!_byCell.TryGetValue(y * 128 + x, out var i)) return false; s = l[i]; return true; }

        // where an ExtraSlots slot goes in our panel: one of our doll cells, the strip (Tools / Food), or the row under the doll
        internal static string DollCell(string id)
        {
            switch (id)
            {
                case "Helmet": return "Head";
                case "Chest": return "Chest";
                case "Legs": return "Legs";
                case "Shoulder": return "Cape";
                case "Utility": return "Belt";
                case "Trinket": return "Trinket";
                case "ExtraUtility1": return "Wishbone";
                case "ExtraUtility2": return "Wisplight";
            }
            return null;
        }
        internal static int Quick(string id) => id.StartsWith("Quick") && int.TryParse(id.Substring(5), out var n) ? n - 1 : -1;
        internal static int Food(string id) => id.StartsWith("Food") && int.TryParse(id.Substring(4), out var n) ? n - 1 : -1;
        internal static int Ammo(string id) => id.StartsWith("Ammo") && int.TryParse(id.Substring(4), out var n) ? n - 1 : -1;
        internal static bool IsRing(string id) => id.IndexOf("Ring", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // whoever owns the player grid besides us (AzuEPI or ExtraSlots: they refuse to load together)
    internal static class SlotHost
    {
        internal static bool Other => Azu.Drives || Es.Drives;
    }
}
