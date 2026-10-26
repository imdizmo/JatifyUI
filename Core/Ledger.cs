using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Every change Jatify UI makes to a vanilla object goes through a ledger that remembers the old
    // value first; Undo() puts them back newest-first. Objects we create are listed and destroyed.
    internal sealed class Ledger
    {
        private readonly List<Action> _undo = new List<Action>();

        internal void Img(Image i, Sprite sprite, Color color, Image.Type type = Image.Type.Sliced)
        {
            if (i == null) return;
            var (s0, c0, t0) = (i.sprite, i.color, i.type);
            _undo.Add(() => { if (i != null) { i.sprite = s0; i.color = c0; i.type = t0; } });
            i.sprite = sprite; i.color = color; i.type = sprite != null ? type : Image.Type.Simple;
        }

        internal void Txt(TMP_Text x, TMP_FontAsset font, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            if (x == null) return;
            var (f0, s0, c0, st0, a0) = (x.font, x.fontSize, x.color, x.fontStyle, x.enableAutoSizing);
            _undo.Add(() => { if (x != null) { x.font = f0; x.fontSize = s0; x.color = c0; x.fontStyle = st0; x.enableAutoSizing = a0; } });
            if (font != null) x.font = font;
            x.fontSize = size; x.color = color; x.fontStyle = style; x.enableAutoSizing = false;
        }

        // Anything else: apply now, remember how to undo it.
        internal void Do(Action apply, Action undo) { _undo.Add(undo); apply(); }

        internal void Font(TMP_Text x, TMP_FontAsset font)
        {
            if (x == null || font == null) return;
            var f0 = x.font;
            _undo.Add(() => { if (x != null) x.font = f0; });
            x.font = font;
        }

        internal void Align(TMP_Text x, TextAlignmentOptions a)
        {
            if (x == null) return;
            var a0 = x.alignment;
            _undo.Add(() => { if (x != null) x.alignment = a0; });
            x.alignment = a;
        }

        internal void Off(Graphic g)
        {
            if (g == null || !g.enabled) return;
            _undo.Add(() => { if (g != null) g.enabled = true; });
            g.enabled = false;
        }

        internal void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            _undo.Add(() => { if (go != null) go.SetActive(true); });
            go.SetActive(false);
        }

        internal void Rect(RectTransform r, Action<RectTransform> change)
        {
            if (r == null) return;
            var (a, b, p, pos, size, scale) = (r.anchorMin, r.anchorMax, r.pivot, r.anchoredPosition, r.sizeDelta, r.localScale);
            _undo.Add(() => { if (r != null) { r.anchorMin = a; r.anchorMax = b; r.pivot = p; r.anchoredPosition = pos; r.sizeDelta = size; r.localScale = scale; } });
            change(r);
        }

        // Own: an object WE created; undo destroys its whole GameObject.
        internal T Own<T>(T c) where T : Component { if (c != null) _undo.Add(() => { if (c != null) UnityEngine.Object.Destroy(c.gameObject); }); return c; }

        // Added: a component we added to a VANILLA object; undo removes only that component.
        internal T Added<T>(T c) where T : Component { if (c != null) _undo.Add(() => { if (c != null) UnityEngine.Object.Destroy(c); }); return c; }

        internal void Undo()
        {
            for (int i = _undo.Count - 1; i >= 0; i--) { try { _undo[i](); } catch (Exception e) { Log.Warn("undo: " + e.Message); } }
            _undo.Clear();
        }
    }
}
