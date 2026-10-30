namespace Jatify.UI
{
    // shudnal's ExtraSlots: like AzuEPI it keeps its slots as rows at the bottom of the player inventory and sizes the grid
    // itself every frame. Installed, it owns the grid and Jatify stands aside exactly as for AzuEPI (no rows of ours, no
    // save packing, no moving items for them): two mods reshaping one grid push worn gear out of the inventory.
    // Drawing its cells in our panel comes later (extraslots-plan step 4); until then they show as plain bag rows.
    internal static class Es
    {
        internal const string Guid = "shudnal.ExtraSlots";
        private static bool? _on;
        internal static bool Drives
        {
            get
            {
                if (_on != null) return _on.Value;
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(Guid, out var info)) { if (Epic.Started) _on = false; return false; }   // plugins still loading: ask again
                if (info.Instance == null) return false;
                _on = true; Log.Info("ExtraSlots found: its slot rows own the inventory grid, Jatify's rows stand aside");
                return true;
            }
        }
    }

    // whoever owns the player grid besides us (AzuEPI or ExtraSlots: they refuse to load together)
    internal static class SlotHost
    {
        internal static bool Other => Azu.Drives || Es.Drives;
    }
}
