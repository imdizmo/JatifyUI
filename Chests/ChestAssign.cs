using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // A chest can be assigned to one item: only that item goes in (drag and drop, Ctrl + click, Put, quick stack; Valheim's
    // hold-E stacking only adds to stacks already there). Saved on the chest (its ZDO), so everyone sees and respects it;
    // what is already inside stays. In the world, the item's icon sits on the front of assigned chests near you, like a label.
    [HarmonyPatch]
    internal static class ChestAssign
    {
        internal const string Key = "jatify.chestonly";
        private static readonly AccessTools.FieldRef<Container, ZNetView> View = AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragInv = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");

        internal static string Of(Container c) { var nv = c != null ? View(c) : null; return nv != null && nv.IsValid() ? nv.GetZDO().GetString(Key, "") : ""; }

        internal static void Set(Container c, string prefab)
        {
            var nv = c != null ? View(c) : null; if (nv == null || !nv.IsValid()) return;
            if (!nv.IsOwner()) nv.ClaimOwnership();
            nv.GetZDO().Set(Key, prefab ?? "");
        }

        internal static bool Allows(Container c, ItemDrop.ItemData it)
        {
            var only = Of(c); if (only.Length == 0 || it == null) return true;
            return it.m_dropPrefab != null && it.m_dropPrefab.name == only;
        }

        // the assigned item's shared name ("$item_coal"), for P / Put similar: an assigned chest counts as holding it
        internal static string SharedName(Container c)
        {
            var only = Of(c); if (only.Length == 0 || ObjectDB.instance == null) return null;
            return ObjectDB.instance.GetItemPrefab(only)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
        }

        internal static string Name(string prefab)
        {
            var d = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            return d != null ? Localization.instance.Localize(d.m_itemData.m_shared.m_name) : prefab;
        }

        internal static Sprite Icon(string prefab) => ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;

        // vanilla's drop / swap / Ctrl + click into the open chest: what would end up inside must be the assigned item
        [HarmonyPrefix, HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        private static bool Guard(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            var c = ChestPanel.Open; if (c == null || Of(c).Length == 0 || grid == null) return true;
            var chest = c.GetInventory(); var drag = DragItem(__instance); var from = DragInv(__instance);
            ItemDrop.ItemData incoming = null;
            if (drag != null && grid.GetInventory() == chest && from != chest) incoming = drag;                       // dropped into the chest
            else if (drag != null && from == chest && grid.GetInventory() != chest && item != null) incoming = item;     // a swap sends this one in
            else if (drag == null && mod == InventoryGrid.Modifier.Move && grid.GetInventory() != chest && item != null) incoming = item;   // Ctrl + click
            if (incoming == null || Allows(c, incoming)) return true;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"This chest only takes {Name(Of(c))}");
            return false;
        }

        // ---- the chest panel's "Only" tile: drop an item on it to assign, right click to clear --------------------------
        private static Image _tileIcon; private static TextMeshProUGUI _tileText; private static RectTransform _tile; private static string _shown;

        internal static RectTransform BuildTile(RectTransform frame, ThemeData t)
        {
            var (b, _) = Kit.Box(frame, "Only", Theme.C(t.raised), Theme.C(t.glassLine), 8); _tile = b.rectTransform; b.raycastTarget = true;
            _tileIcon = Kit.Img(_tile, "I", null, Color.white, false); _tileIcon.rectTransform.At(6, 4, 22, 22); _tileIcon.preserveAspect = true;
            _tileText = Kit.Text(_tile, "T", "", Fonts.Medium, 12f, Theme.C(t.dim)); _tileText.alignment = TextAlignmentOptions.MidlineLeft; _tileText.overflowMode = TextOverflowModes.Ellipsis;
            var press = Press.On(b.gameObject, () =>
            {
                var c = ChestPanel.Open; var g = InventoryGui.instance; if (c == null || g == null) return;
                var drag = DragItem(g); if (drag == null || drag.m_dropPrefab == null) return;
                Set(c, drag.m_dropPrefab.name);   // the item stays in hand; it only names what the chest takes
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"This chest now only takes {Name(drag.m_dropPrefab.name)}");
            });
            press.Right = () => { var c = ChestPanel.Open; if (c != null && Of(c).Length > 0) { Set(c, ""); Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "This chest takes anything again"); } };
            _shown = null;
            return _tile;
        }

        // every frame while a chest is open: the tile shows what it takes
        internal static void TickTile(Container c, ThemeData t)
        {
            if (_tile == null) return;
            var only = c != null ? Of(c) : ""; if (only == _shown) return; _shown = only;
            bool set = only.Length > 0;
            _tileIcon.sprite = set ? Icon(only) : null; _tileIcon.enabled = set && _tileIcon.sprite != null;
            _tileText.text = set ? $"Only {Name(only)}" : "Any item · drop one here";
            _tileText.color = set ? Color.white : Theme.C(t.mute);
            _tileText.rectTransform.At(set ? 32 : 10, 0, _tile.rect.width - (set ? 38 : 16), 30);
        }

        // ---- floating icons over assigned chests near you -----------------------------------------------------------
        private static readonly Dictionary<Container, (Transform go, SpriteRenderer sr, string prefab)> _marks = new Dictionary<Container, (Transform, SpriteRenderer, string)>();

        // the middle of the chest's front face, and which way it faces: the side its open-lid model sits towards (the
        // latches, on the reinforced chest), measured on the chest's own meshes once
        private static void Front(Container c, out Vector3 pos, out Vector3 normal)
        {
            var tr = c.transform; float z = 1f;   // chests face their +z (measured on the reinforced chest: the latches)
            foreach (var t in c.GetComponentsInChildren<Transform>(true)) if (t.name.ToLowerInvariant().Contains("open")) { z = tr.InverseTransformPoint(t.position).z < -0.05f ? -1f : 1f; break; }   // its open lid's side
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
            foreach (var mf in c.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.name.ToLowerInvariant().Contains("open")) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = tr.InverseTransformPoint(mf.transform.TransformPoint(corner)); min = Vector3.Min(min, l); max = Vector3.Max(max, l);
                }
            }
            if (min.x > max.x) { min = new Vector3(-0.5f, 0f, -0.5f); max = new Vector3(0.5f, 0.8f, 0.5f); }
            var local = new Vector3((min.x + max.x) / 2f, min.y + (max.y - min.y) * 0.5f, (z < 0 ? min.z : max.z) + z * 0.06f);
            pos = tr.TransformPoint(local); normal = tr.TransformDirection(new Vector3(0f, 0f, z));
        }
        private static float _scanAt; private static Material _mat;
        private const float Reach = 30f;

        private static readonly List<Piece> _near = new List<Piece>();
        internal static void TickWorld()
        {
            var p = Player.m_localPlayer; var cam = GameCamera.instance;
            if (p == null || cam == null) { Clear(); return; }
            if (Time.time >= _scanAt)
            {   // once a second: which chests near you are assigned
                _scanAt = Time.time + 1f;
                var seen = new HashSet<Container>();
                _near.Clear(); Piece.GetAllPiecesInRadius(p.transform.position, Reach, _near);   // built pieces within reach (chests are), not a scene-wide search (~1.4 ms a frame in a big base)
                foreach (var piece in _near)
                {
                    var c = piece != null ? piece.GetComponentInChildren<Container>() : null;
                    if (c == null || c.GetComponent<TombStone>() != null || (c.transform.position - p.transform.position).sqrMagnitude > Reach * Reach) continue;
                    var only = Of(c); if (only.Length == 0) continue;
                    seen.Add(c);
                    if (_marks.TryGetValue(c, out var m) && m.prefab == only && m.go != null) continue;
                    if (m.go != null) Object.Destroy(m.go.gameObject);
                    var go = new GameObject("jatify-chestmark"); var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = Icon(only);
                    _mat = _mat ?? new Material(Shader.Find("Sprites/Default")); sr.sharedMaterial = _mat;
                    float ss = sr.sprite != null ? Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y) : 1f;
                    go.transform.localScale = Vector3.one * (0.4f / Mathf.Max(0.01f, ss));   // about 40 cm across
                    Front(c, out var fp, out var fn); go.transform.position = fp; go.transform.rotation = Quaternion.LookRotation(-fn);   // a label on the front, facing out (seen from in front)
                    _marks[c] = (go.transform, sr, only);
                }
                foreach (var c in new List<Container>(_marks.Keys)) if (c == null || !seen.Contains(c)) { var m = _marks[c]; if (m.go != null) Object.Destroy(m.go.gameObject); _marks.Remove(c); }
            }

        }

        internal static string Debug() { var sb = new System.Text.StringBuilder(); foreach (var kv in _marks) if (kv.Key != null && kv.Value.go != null) sb.Append($"{kv.Key.name} at {kv.Key.transform.position} mark {kv.Value.go.position} fwd {kv.Value.go.forward} sprite {(kv.Value.sr.sprite != null ? kv.Value.sr.sprite.name : "none")} mat {(kv.Value.sr.sharedMaterial != null ? kv.Value.sr.sharedMaterial.shader.name : "none")} layer {kv.Value.go.gameObject.layer} visible {kv.Value.sr.isVisible} enabled {kv.Value.sr.enabled} size {kv.Value.sr.bounds.size} cam {GameCamera.instance?.transform.position}; "); return sb.Length > 0 ? sb.ToString() : "no marks"; }

        internal static void Clear() { foreach (var m in _marks.Values) if (m.go != null) Object.Destroy(m.go.gameObject); _marks.Clear(); }
        internal static void Destroy() { Clear(); if (_mat != null) Object.Destroy(_mat); _mat = null; _tile = null; }
    }
}
