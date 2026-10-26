using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // dev: a sprite (often in a shared, unreadable atlas) copied through a render texture and saved as a PNG
    internal static class DevIcons
    {
        private static readonly System.Reflection.MethodInfo Encode = AccessTools.Method("UnityEngine.ImageConversion:EncodeToPNG", new[] { typeof(Texture2D) });

        internal static bool Save(Sprite s, string path)
        {
            if (s == null || s.texture == null || Encode == null) return false;
            var r = s.textureRect; int w = Mathf.Max(1, (int)r.width), h = Mathf.Max(1, (int)r.height);
            var rt = RenderTexture.GetTemporary(s.texture.width, s.texture.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(s.texture, rt); RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0); tex.Apply();
                File.WriteAllBytes(path, (byte[])Encode.Invoke(null, new object[] { tex }));
                Object.Destroy(tex); return true;
            }
            catch (System.Exception e) { Log.Warn($"icon {s.name}: {e.Message}"); return false; }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}
