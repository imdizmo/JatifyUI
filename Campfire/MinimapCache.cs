using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Jatify.Campfire
{
    // Valheim caches the minimap it draws from the world generator, but only for worlds you host: on a
    // server the cache paths are never set, and the client's World lacks the version the check wants.
    // So every join redraws the whole map on the main thread (~5 s of frozen screen). This keeps a
    // client-side cache per server world (seed + generator version + world id) in
    // <save dir>/campfire-minimap/, in Valheim's own file format. It only ever reads or writes there.
    [HarmonyPatch(typeof(Minimap), "TryLoadMinimapTextureData")]
    internal static class MinimapCache
    {
        private static bool Prefix(Minimap __instance, int worldSeed, ref bool __result)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZNet.World == null) return true;   // hosting: Valheim's own cache
            var w = ZNet.World;
            var dir = Path.Combine(SaveBackup.DataDir, "campfire-minimap", $"{w.m_seed}_{w.m_worldGenVersion}_{w.m_uid}");
            var t = Traverse.Create(__instance);
            string mask = dir + "/cacheMinimapMask", biome = dir + "/cacheMinimapBiome", height = dir + "/cacheMinimapHeight", meta = dir + "/cacheMinimapMeta";
            // Point Valheim's own save-after-generate at our folder, so a miss fills the cache.
            Directory.CreateDirectory(dir);
            t.Field("m_saveDirectory").SetValue(dir);
            t.Field("m_cachedMinimapMaskTexturePath").SetValue(mask);
            t.Field("m_cachedMinimapBiomeTexturePath").SetValue(biome);
            t.Field("m_cachedMinimapHeightTexturePath").SetValue(height);
            t.Field("m_cachedMinimapMetaPath").SetValue(meta);

            __result = false;
            if (!File.Exists(mask) || !File.Exists(biome) || !File.Exists(height) || !File.Exists(meta)) return false;
            try
            {
                var m = File.ReadAllBytes(meta);
                if (m.Length < 8 || BitConverter.ToInt32(m, 0) != worldSeed || BitConverter.ToInt32(m, 4) != 1) return false;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var forest = t.Field("m_forestMaskTexture").GetValue<Texture2D>();
                var heights = t.Field("m_heightTexture").GetValue<Texture2D>();
                forest.SetPixels32(Utils.CompressedBufferToColors(File.ReadAllBytes(mask)));
                forest.Apply();
                __instance.m_mapTexture.SetPixels32(Utils.CompressedBufferToColors(File.ReadAllBytes(biome)));
                __instance.m_mapTexture.Apply();
                heights.SetPixels(Utils.CompressedHalfBufferToRedChannel(File.ReadAllBytes(height)));
                heights.Apply();
                Log.Info($"minimap loaded from cache in {sw.ElapsedMilliseconds} ms ({dir})");
                __result = true;
            }
            catch (Exception e) { Log.Warn("minimap cache unreadable, redrawing: " + e.Message); }
            return false;
        }
    }
}
