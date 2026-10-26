using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Dev tool: writes a UI object tree (names, components, rects, sprites, texts) to
    // BepInEx/cache/jatify.ui/dump-<name>.txt so layouts can be read without opening the game's assets.
    internal static class DevDump
    {
        internal static void Tree(string name, Transform root, string cacheDir)
        {
            if (root == null) return;
            var sb = new StringBuilder();
            Walk(root, 0, sb);
            var dir = Path.Combine(cacheDir, "jatify.ui"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "dump-" + name + ".txt"), sb.ToString());
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.gameObject.activeSelf ? "" : "(off) ").Append(t.name);
            if (t is RectTransform rt) sb.Append($"  [{rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##}-{rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##} pos {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0} size {rt.rect.width:0}x{rt.rect.height:0} scale {rt.localScale.x:0.##}]"); if (t is RectTransform wr) { var w = new Vector3[4]; wr.GetWorldCorners(w); sb.Append($" @screen {w[0].x:0},{w[0].y:0}-{w[2].x:0},{w[2].y:0}"); }
            foreach (var c in t.GetComponents<Component>())
            {
                if (c is Transform) continue;
                sb.Append("  ").Append(c.GetType().Name);
                if (c is Canvas cv) sb.Append($"(override {cv.overrideSorting} order {cv.sortingOrder} layer {cv.sortingLayerName})");
                if (c is CanvasGroup cg) sb.Append($"(alpha {cg.alpha:0.##})");
                if (c is Image i) sb.Append($"({(i.sprite ? i.sprite.name : "-")} {i.type} #{ColorUtility.ToHtmlStringRGBA(i.color)}{(i.enabled ? "" : " off")})");
                if (c is TMP_Text tx) sb.Append($"(\"{((tx.text ?? "").Length > 24 ? tx.text.Substring(0, 24) : tx.text ?? "").Replace('\n', ' ')}\" {(tx.font != null ? tx.font.name : "-")} {tx.fontSize:0} #{ColorUtility.ToHtmlStringRGBA(tx.color)} a{tx.alpha:0.##} mat {(tx.fontSharedMaterial != null ? tx.fontSharedMaterial.shader.name : "-")}{(tx.enabled ? "" : " off")})");
            }
            sb.Append('\n');
            if (depth < 9) foreach (Transform c in t) Walk(c, depth + 1, sb);
        }
    }
}
