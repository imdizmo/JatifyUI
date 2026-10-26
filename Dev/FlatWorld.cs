using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Dev only: a world named "Jatify Flat" is one flat Meadows plain (user: an empty flat world for the UI tests). No trees, rocks,
    // locations besides the start temple, or random spawns, so only what the test kit spawns is there. Any other world, and any release build, is untouched.
    internal static class FlatWorld
    {
        internal const string Name = "Jatify Flat";
        private const float Ground = 31f;   // the water sits at 30
        [System.ThreadStatic] internal static bool Bypass;   // the terrain export reads the real world on its own thread
        // only while a world named Jatify Flat is running: the title screen's terrain goes through the same generator (user: the menu
        // came out flattened into a mess after leaving the world)
        internal static bool On => !Bypass && Dev.Build && ZNet.instance != null && ZNet.World != null && string.Equals(ZNet.World.m_name, Name, System.StringComparison.OrdinalIgnoreCase);

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.Initialize))]
        private static class Init { private static void Prefix(World world) { if (Dev.Build && world != null && string.Equals(world.m_name, Name, System.StringComparison.OrdinalIgnoreCase)) Log.Info("flat test world: on"); } }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), new[] { typeof(float), typeof(float), typeof(float), typeof(bool) })]
        private static class Biome { private static bool Prefix(ref Heightmap.Biome __result) { if (!On) return true; __result = Heightmap.Biome.Meadows; return false; } }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiomeHeight))]
        private static class Height { private static bool Prefix(ref float __result, out Color mask) { mask = Color.black; if (!On) return true; __result = Ground; return false; } }

        [HarmonyPatch(typeof(ZoneSystem), "PlaceVegetation")]
        private static class Vegetation { private static bool Prefix() => !On; }

        // only the start temple: new characters spawn there (without it the game waits forever), and every other dungeon and altar
        // would retry its placement thousands of times on a flat Meadows plain (user: it took ages)
        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.GenerateLocations), new System.Type[0])]
        private static class Generate
        {
            private static void Prefix(ZoneSystem __instance)
            {
                if (On) __instance.m_locations = __instance.m_locations.FindAll(l => l != null && l.m_prefabName == "StartTemple");   // this session's ZoneSystem only
            }
        }

        // the start temple doesn't take on flat ground (its placement rules fail): spawn in the middle of the map instead, once it's loaded
        [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
        private static class Spawn
        {
            private static void Postfix(ref bool __result, ref Vector3 point, ref bool usedLogoutPoint)
            {
                if (!On || __result) return;
                var at = Vector3.zero; ZNet.instance.SetReferencePosition(at);
                if (!ZNetScene.instance.IsAreaReady(at)) return;
                point = new Vector3(0f, Ground + 0.5f, 0f); usedLogoutPoint = false; __result = true;
            }
        }

        // stations work in the open here (a workbench wants a roof): the tests open their windows without building a shed
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
        private static class Usable { private static bool Prefix(ref bool __result) { if (!On) return true; __result = true; return false; } }

        [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
        private static class Spawning { private static bool Prefix() => !On; }
    }
}
