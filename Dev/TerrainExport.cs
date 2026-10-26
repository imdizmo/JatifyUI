using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Jatify.UI
{
    // Dev: Valheim's terrain around a point, from its world generator (terrain isn't saved; the seed rebuilds it), for Fog-Bound.
    // Writes to BepInEx/cache/jatify.ui/terrain/<world>-<x>_<z>-<size>m/: height.raw (16-bit little-endian, n x n with n = 2^k + 1,
    // Unity's terrain import format), biome.ppm (colours in meta.json), meta.json (world, seed, centre, size, metres per pixel,
    // height min/max in metres; raw 0..65535 maps onto that range). Runs on its own thread, one row at a time, so the game keeps going.
    internal static class TerrainExport
    {
        private static volatile bool _busy;

        internal static void Start(Vector3 centre, float size, float step)
        {
            if (_busy) { Log.Info("terrain export: one is already running"); return; }
            var wg = WorldGenerator.instance; var w = ZNet.World; if (wg == null || w == null) { Log.Info("terrain export: no world"); return; }
            int n = 1; while (n < size / step) n *= 2; n += 1;   // 2^k + 1 samples a side
            float half = (n - 1) * step / 2f;
            var dir = Path.Combine(BepInEx.Paths.CachePath, "jatify.ui", "terrain", $"{Safe(w.m_name)}-{Mathf.RoundToInt(centre.x)}_{Mathf.RoundToInt(centre.z)}-{Mathf.RoundToInt((n - 1) * step)}m");
            Directory.CreateDirectory(dir); _busy = true;
            string name = w.m_name, seed = w.m_seedName; float cx = centre.x, cz = centre.z;
            Log.Info($"terrain export: {n} x {n} at {step} m, {(n - 1) * step} m a side, to {dir}");
            new Thread(() =>
            {
                FlatWorld.Bypass = true;
                try
                {
                    var h = new float[n * n]; var b = new byte[n * n * 3]; float lo = float.MaxValue, hi = float.MinValue;
                    for (int j = 0; j < n; j++)
                        for (int i = 0; i < n; i++)
                        {
                            float x = cx - half + i * step, z = cz - half + j * step;
                            var biome = wg.GetBiome(x, z); float y = wg.GetBiomeHeight(biome, x, z, out _);
                            int k = j * n + i; h[k] = y; if (y < lo) lo = y; if (y > hi) hi = y;
                            var c = Colour(biome); b[k * 3] = c.r; b[k * 3 + 1] = c.g; b[k * 3 + 2] = c.b;
                        }
                    var raw = new byte[n * n * 2]; float span = Mathf.Max(0.001f, hi - lo);
                    for (int j = 0; j < n; j++)   // Unity reads row 0 as the terrain's south edge (z = min): rows go south to north as sampled
                        for (int i = 0; i < n; i++) { ushort v = (ushort)Mathf.RoundToInt((h[j * n + i] - lo) / span * 65535f); int k = (j * n + i) * 2; raw[k] = (byte)(v & 0xff); raw[k + 1] = (byte)(v >> 8); }
                    File.WriteAllBytes(Path.Combine(dir, "height.raw"), raw);
                    using (var f = File.Create(Path.Combine(dir, "biome.ppm")))
                    {   // PPM rows run top (north) to bottom: flip so the image reads like the map
                        var head = Encoding.ASCII.GetBytes($"P6\n{n} {n}\n255\n"); f.Write(head, 0, head.Length);
                        for (int j = n - 1; j >= 0; j--) f.Write(b, j * n * 3, n * 3);
                    }
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    File.WriteAllText(Path.Combine(dir, "meta.json"),
                        "{\n" + $"  \"world\": \"{name}\", \"seed\": \"{seed}\",\n  \"centre\": [{cx.ToString(inv)}, {cz.ToString(inv)}], \"samples\": {n}, \"metresPerSample\": {step.ToString(inv)}, \"sizeMetres\": {((n - 1) * step).ToString(inv)},\n" +
                        $"  \"heightMin\": {lo.ToString("0.###", inv)}, \"heightMax\": {hi.ToString("0.###", inv)}, \"waterLevel\": 30,\n" +
                        "  \"heightRaw\": \"16-bit little-endian, row 0 = south edge (min z), column 0 = west edge (min x); value 0..65535 = heightMin..heightMax metres\",\n" +
                        "  \"biomeColours\": { \"Meadows\": \"#7fb04a\", \"BlackForest\": \"#2f4a2a\", \"Swamp\": \"#5a5a3a\", \"Mountain\": \"#e8e8f0\", \"Plains\": \"#d8c770\", \"Ocean\": \"#2a4a7a\", \"Mistlands\": \"#6a5a7a\", \"AshLands\": \"#8a2a1a\", \"DeepNorth\": \"#bcd8f0\" }\n}\n");
                    Log.Info($"terrain export done: {dir} (heights {lo:0.#}..{hi:0.#} m)");
                }
                catch (System.Exception e) { Log.Warn("terrain export failed: " + e.Message); }
                finally { FlatWorld.Bypass = false; _busy = false; }
            }) { IsBackground = true, Name = "jatify-terrain-export" }.Start();
        }

        private static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }

        private static Color32 Colour(Heightmap.Biome b)
        {
            switch (b)
            {
                case Heightmap.Biome.Meadows: return new Color32(0x7f, 0xb0, 0x4a, 255);
                case Heightmap.Biome.BlackForest: return new Color32(0x2f, 0x4a, 0x2a, 255);
                case Heightmap.Biome.Swamp: return new Color32(0x5a, 0x5a, 0x3a, 255);
                case Heightmap.Biome.Mountain: return new Color32(0xe8, 0xe8, 0xf0, 255);
                case Heightmap.Biome.Plains: return new Color32(0xd8, 0xc7, 0x70, 255);
                case Heightmap.Biome.Ocean: return new Color32(0x2a, 0x4a, 0x7a, 255);
                case Heightmap.Biome.Mistlands: return new Color32(0x6a, 0x5a, 0x7a, 255);
                case Heightmap.Biome.AshLands: return new Color32(0x8a, 0x2a, 0x1a, 255);
                case Heightmap.Biome.DeepNorth: return new Color32(0xbc, 0xd8, 0xf0, 255);
                default: return new Color32(0, 0, 0, 255);
            }
        }
    }
}
