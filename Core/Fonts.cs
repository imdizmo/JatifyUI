using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Jatify.UI
{
    // Space Grotesk and JetBrains Mono ship inside the DLL. TMP builds font assets from a file path, so
    // they are written once to BepInEx/cache/jatify.ui. Assets and their atlas textures are destroyed on
    // unload: a reload would otherwise keep every old atlas in video memory.
    internal static class Fonts
    {
        internal static TMP_FontAsset Medium, Bold, Mono;

        // Valheim's own lettering, borrowed from the game (live design: Norse for panel titles and big moments, Averia
        // Serif Libre for lore); Jatify's fonts until the game has loaded them
        internal static TMP_FontAsset Norse => Game("Valheim-Norsebold") ?? Bold;
        internal static TMP_FontAsset Lore => Game("Valheim-AveriaSerifLibre") ?? Medium;
        private static readonly Dictionary<string, TMP_FontAsset> _game = new Dictionary<string, TMP_FontAsset>();
        private static TMP_FontAsset Game(string name)
        {
            if (_game.TryGetValue(name, out var f) && f != null) return f;
            foreach (var a in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) if (a.name == name) return _game[name] = a;
            return null;
        }
        private static readonly List<TMP_FontAsset> _made = new List<TMP_FontAsset>();

        // Copies of a font's material with a look of ours, one per font and key, destroyed on unload: Valheim's Norse with a soft
        // shadow (nameplates, damage numbers), the boss name's red glow
        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        internal static Material Mat(TMP_FontAsset font, string key, System.Action<Material> setup)
        {
            if (font == null || font.material == null) return null;
            key = font.name + "|" + key;
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(font.material) { name = "jatify " + key };
            // our fonts' Mobile shader: Valheim's build has no underlay variant of it (the shadow never drew); the full Distance Field
            // shader (Valheim's own Norse uses it) has
            var full = Norse != null && Norse.material != null ? Norse.material.shader : null;
            if (m.shader != null && m.shader.name.Contains("Mobile") && full != null && !full.name.Contains("Mobile")) m.shader = full;
            setup(m);
            return _mats[key] = m;
        }
        // small numbers over the world (nameplate and boss HP): a tighter, darker edge than Shadowed, which thin mono digits lose
        internal static Material Edged(TMP_FontAsset font) => Mat(font, "edge", m =>
        {
            m.EnableKeyword("UNDERLAY_ON"); m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.9f));
            m.SetFloat("_UnderlayOffsetX", 0.3f); m.SetFloat("_UnderlayOffsetY", -0.8f); m.SetFloat("_UnderlayDilate", 0.6f); m.SetFloat("_UnderlaySoftness", 0.15f);
        });
        internal static Material Shadowed(TMP_FontAsset font) => Mat(font, "shadow", m =>
        {   // design .shadow: 0 1px 2px black, a faint dark halo; dilated so Norse's thin strokes keep it over bright sky and grass (user)
            m.EnableKeyword("UNDERLAY_ON"); m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.7f));
            m.SetFloat("_UnderlayOffsetX", 0f); m.SetFloat("_UnderlayOffsetY", -0.5f); m.SetFloat("_UnderlayDilate", 0.35f); m.SetFloat("_UnderlaySoftness", 0.5f);
        });

        internal static void Load(string cacheDir)
        {
            var dir = Path.Combine(cacheDir, "jatify.ui");
            Directory.CreateDirectory(dir);
            Medium = Make(dir, "SpaceGrotesk-Medium.ttf");
            Bold = Make(dir, "SpaceGrotesk-Bold.ttf");
            Mono = Make(dir, "JetBrainsMono-Medium.ttf");
            // Space Grotesk has no geometric shapes (▸ ▾ ● ✓): JetBrains Mono, which has them, draws them instead of a warning
            if (Mono != null) foreach (var fa in new[] { Medium, Bold }) if (fa != null) fa.fallbackFontAssetTable = new List<TMP_FontAsset> { Mono };
        }

        private static TMP_FontAsset Make(string dir, string name)
        {
            var path = Path.Combine(dir, name);
            using (var s = typeof(Fonts).Assembly.GetManifestResourceStream("fonts/" + name))
            {
                if (s == null) { Log.Warn("missing embedded font " + name); return null; }
                if (!File.Exists(path) || new FileInfo(path).Length != s.Length)
                    using (var f = File.Create(path)) s.CopyTo(f);
            }
            var fa = TMP_FontAsset.CreateFontAsset(path, 0, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024);
            if (fa == null) { Log.Warn("could not build font " + name); return null; }
            fa.name = "Jatify " + Path.GetFileNameWithoutExtension(name);
            var m = fa.material; // text over the game world needs a shadow to stay readable
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.75f));
            m.SetFloat("_UnderlayOffsetY", -0.55f);
            m.SetFloat("_UnderlaySoftness", 0.45f);
            _made.Add(fa);
            return fa;
        }

        internal static void Unload()
        {
            foreach (var fa in _made)
            {
                if (fa == null) continue;
                if (fa.atlasTextures != null) foreach (var t in fa.atlasTextures) if (t != null) Object.Destroy(t);
                if (fa.material != null) Object.Destroy(fa.material);
                Object.Destroy(fa);
            }
            _made.Clear();
            foreach (var m in _mats.Values) if (m != null) Object.Destroy(m);
            _mats.Clear(); _game.Clear();
            Medium = Bold = Mono = null;
        }
    }
}
