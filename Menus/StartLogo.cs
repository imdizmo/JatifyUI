using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // The start screen's "Valheim 1.0 / Deep North" logo is destroyed with the start screen, so we keep our own
    // copy (1000 px wide, about 1.5 MB) made there, for the pause menu in the world.
    // ponytail: never freed (one 1.5 MB copy per session, kept across reloads of the mod).
    [HarmonyPatch(typeof(FejdStartup), "Start")]
    internal static class StartLogo
    {
        private const string Name = "Valheim_DeepNorth_Logo";
        private const int Width = 1000;
        private const string Copy = "jatify-startlogo";
        internal static Sprite Sprite { get; private set; }

        private static void Postfix() => Grab();

        internal static void Grab()
        {
            if (Sprite != null) return;
            // a reload of the mod (dev) finds the copy the last load made: one copy for the whole session
            Sprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == Copy); if (Sprite != null) return;
            var src = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == Name); if (src == null) return;
            var r = src.textureRect; var t = src.texture;
            int w = Width, h = Mathf.RoundToInt(w * r.height / r.width);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(t, rt, new Vector2(r.width / t.width, r.height / t.height), new Vector2(r.x / t.width, r.y / t.height));
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = Copy, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(false, true);   // no longer readable: the CPU copy is freed
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            Sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f); Sprite.name = tex.name;
        }
    }
}
