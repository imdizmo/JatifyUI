using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // EpicLoot's rarity on one of our item cells, as on its own grid cells: its knotwork background in the rarity colour
    // (clipped to our round corners) under the icon, and the cell's border in that colour. Nothing without EpicLoot.
    internal sealed class RarityBg
    {
        private readonly Image _bg, _line; private readonly Color _plain;

        // cell: the box the icon sits in; icon: drawn above the background; line: the border to tint (may be null)
        internal RarityBg(RectTransform cell, Image icon, Image line, int radius)
        {
            _line = line; _plain = line != null ? line.color : Color.clear;
            var clip = Kit.Panel(cell, "RarityClip", Color.white, radius); clip.rectTransform.Fill(1f); clip.raycastTarget = false;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            if (icon != null && icon.transform.parent == cell) clip.transform.SetSiblingIndex(icon.transform.GetSiblingIndex());
            _bg = Kit.Img(clip.rectTransform, "Rarity", null, Color.clear, false); _bg.rectTransform.Fill(); _bg.raycastTarget = false; _bg.enabled = false;
        }

        internal void Set(ItemDrop.ItemData it)
        {
            var hex = it != null && Epic.On ? Epic.Color(Epic.TipData(it, it.m_quality)) : null;
            Color rc = default; bool tint = hex != null && ColorUtility.TryParseHtmlString(hex, out rc);
            _bg.enabled = tint;
            if (tint)
            {
                var sp = Epic.MagicBg();
                _bg.sprite = sp != null ? sp : Kit.Rounded(8); _bg.type = sp != null ? Image.Type.Simple : Image.Type.Sliced;
                _bg.color = sp != null ? rc : new Color(rc.r, rc.g, rc.b, 0.3f);
            }
            if (_line != null) _line.color = tint ? new Color(rc.r, rc.g, rc.b, 0.85f) : _plain;
        }
    }
}
