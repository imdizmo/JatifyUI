using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace Jatify.UI
{
    // The server's gameplay settings for everyone on it (automation, chests, coins, ore detection), through ServerSync (blaxxun,
    // MIT-0, built in from ThirdParty/ServerSync): a server running Jatify (dedicated or hosting) hands them to players who join, and
    // their own come back when they leave. Looks and keys stay each player's own. Server.LockConfiguration (on): only the server's
    // admins change them in game. A server without Jatify changes nothing, and nobody is kept out for not having it.
    internal static class Sync
    {
        private static ServerSync.ConfigSync _sync;

        // what the server decides: gameplay, not looks (ExtraRows stays out: rows changing mid-session would move items)
        private static readonly (string section, string key)[] Synced =
        {
            ("Automation", "Smelters"), ("Automation", "AreaPickup"), ("Automation", "AreaRadius"), ("Automation", "AreaSameOnly"),
            ("Inventory", "ChestRange"), ("Inventory", "CraftFromChests"), ("Inventory", "BuildFromChests"),
            ("Inventory", "LighterCoins"), ("Inventory", "CoinWeight"),
            ("Map", "OreDetect"), ("Map", "DungeonDetect"), ("Map", "OreRadius"),
        };

        // after every setting it hands over is bound
        internal static void Bind(ConfigFile c)
        {
            _sync = new ServerSync.ConfigSync(Plugin.Guid) { DisplayName = "Jatify UI", CurrentVersion = Plugin.Version };   // no minimum version: nobody is turned away
            var add = AccessTools.Method(typeof(ServerSync.ConfigSync), nameof(ServerSync.ConfigSync.AddConfigEntry));
            foreach (var (s, k) in Synced)
            {
                var d = new ConfigDefinition(s, k); if (!c.ContainsKey(d)) continue;
                var e = c[d]; add.MakeGenericMethod(e.SettingType).Invoke(_sync, new object[] { e });
            }
            _sync.AddLockingConfigEntry(c.Bind("Server", "LockConfiguration", true,
                "When this game is the server: on, players who join use its Automation, chest, coin and ore detection settings and only its admins can change them; off, anyone on it can."));
        }

        // set by the server right now: the settings page shows the value instead of an editor
        internal static bool ReadOnly(ConfigEntryBase e) => e.Description?.Tags?.OfType<ServerSync.ConfigurationManagerAttributes>().Any(a => a.ReadOnly == true) == true;

        // a reload: ServerSync's patches (its own Harmony id, shared with other mods' copies) from this load only
        internal static void Unload()
        {
            var h = new Harmony("org.bepinex.helpers.ServerSync"); var mine = typeof(Sync).Assembly;
            foreach (var m in Harmony.GetAllPatchedMethods().ToList())
            {
                var info = Harmony.GetPatchInfo(m); if (info == null) continue;
                foreach (var p in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                    if (p.owner == h.Id && p.PatchMethod.DeclaringType?.Assembly == mine) h.Unpatch(m, p.PatchMethod);
            }
        }
    }
}
