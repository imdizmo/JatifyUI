using UnityEngine;

namespace Jatify.Campfire
{
    // Small pictures the fonts may not have (Valheim's fonts lack ▲ ‹ ↺) and Jatify's icon set may not be there:
    // drawn once, white, tinted where used.
    internal static class Glyphs
    {
        private static Sprite _cloud, _triangle;

        internal static Sprite Cloud
        {
            get
            {
                if (_cloud != null) return _cloud;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var circles = new[] { new Vector3(22f, 26f, 12f), new Vector3(36f, 33f, 15f), new Vector3(47f, 25f, 11f) };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = float.MaxValue;
                        foreach (var c in circles) d = Mathf.Min(d, Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) - c.z);
                        if (y >= 14 && y <= 26 && x >= 22 && x <= 47) d = Mathf.Min(d, -1f);   // flat base
                        if (y < 14) d = Mathf.Max(d, 14 - y);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
                    }
                tex.Apply();
                return _cloud = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            }
        }

        // A filled triangle pointing up; turn its Image 90 / 180 / 270 degrees for left, down, right.
        internal static Sprite Triangle
        {
            get
            {
                if (_triangle != null) return _triangle;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var a = new Vector2(16f, 27f); var b = new Vector2(4f, 6f); var c = new Vector2(28f, 6f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Mathf.Max(Edge(p, a, b), Mathf.Max(Edge(p, b, c), Edge(p, c, a)));   // signed distance to the outside
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
                    }
                tex.Apply();
                return _triangle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            }
        }

        internal static void Unload()
        {
            foreach (var s in new[] { _cloud, _triangle }) if (s != null) { Object.Destroy(s.texture); Object.Destroy(s); }
            _cloud = _triangle = null;
        }

        private static float Edge(Vector2 p, Vector2 from, Vector2 to)
        {
            var e = (to - from).normalized; var normal = new Vector2(e.y, -e.x);   // counter-clockwise corners: outward normal
            return Vector2.Dot(p - from, normal);
        }
    }
}
