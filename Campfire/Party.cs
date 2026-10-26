using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jatify.Campfire
{
    // Up to four characters standing at the fire, built the way Valheim builds its own preview
    // (player prefab, no network, no rigidbody, profile loaded onto it).
    internal static class Party
    {
        internal sealed class Member
        {
            internal PlayerProfile Profile;
            internal GameObject Go;
            internal Vector3 Head => Go.transform.position + Vector3.up * 2.15f;
        }

        internal static readonly List<Member> Members = new List<Member>();
        private static Light _spot;

        // All characters in the player's order; the first four stand at the fire.
        internal static List<PlayerProfile> Ordered(List<PlayerProfile> all)
        {
            var names = Module.Order.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var list = names.Select(n => all.FirstOrDefault(p => p.GetFilename() == n)).Where(p => p != null).Distinct().ToList();
            list.AddRange(all.Where(p => !list.Contains(p)));
            return list;
        }

        internal static bool AtFire(PlayerProfile p, List<PlayerProfile> all) { int i = Ordered(all).IndexOf(p); return i >= 0 && i < 4; }

        // The selected character joins the fire in the first slot when it isn't one of the four.
        internal static List<PlayerProfile> Chosen(List<PlayerProfile> all, PlayerProfile selected)
        {
            var list = Ordered(all).Take(4).ToList();
            if (selected != null && !list.Contains(selected)) list.Insert(0, selected);
            return list.Take(4).ToList();
        }

        internal static void Move(PlayerProfile p, int index, List<PlayerProfile> all)
        {
            var list = Ordered(all);
            list.Remove(p);
            list.Insert(Mathf.Clamp(index, 0, list.Count), p);
            Module.Order.Value = string.Join(",", list.Select(x => x.GetFilename()));
        }

        private static Vector2[] SlotOffsets()
        {
            var r = new List<Vector2>();
            foreach (var s in Module.Slots.Value.Split(';'))
            {
                var xz = s.Split(',');
                if (xz.Length == 2 && float.TryParse(xz[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(xz[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    r.Add(new Vector2(x, z));
            }
            while (r.Count < 4) r.Add(new Vector2(-1.8f * r.Count, 1f));
            return r.ToArray();
        }

        private static List<PlayerProfile> _last = new List<PlayerProfile>();

        internal static void Show(List<PlayerProfile> who)
        {
            if (who.SequenceEqual(_last) && Members.All(m => m.Go != null)) return;
            Clear();
            _last = who.ToList();
            var point = Fejd.PreviewPoint;
            var marker = Fejd.CameraMarker != null ? Fejd.CameraMarker : point;
            var right = Vector3.ProjectOnPlane(marker.right, Vector3.up).normalized;
            var away = Vector3.ProjectOnPlane(marker.forward, Vector3.up).normalized;
            var slots = SlotOffsets();
            for (int i = 0; i < who.Count; i++)
            {
                var pos = point.position + right * slots[i].x + away * slots[i].y;
                if (Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Default", "static_solid", "terrain", "piece")))
                    pos.y = hit.point.y;
                var face = Vector3.ProjectOnPlane(marker.position - pos, Vector3.up);
                Members.Add(new Member { Profile = who[i], Go = Spawn(who[i], pos, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : -away)) });
            }
        }

        private static GameObject Spawn(PlayerProfile profile, Vector3 pos, Quaternion rot)
        {
            ZNetView.m_forceDisableInit = true;
            var go = UnityEngine.Object.Instantiate(Fejd.PlayerPrefab, pos, rot);
            ZNetView.m_forceDisableInit = false;
            UnityEngine.Object.Destroy(go.GetComponent<Rigidbody>());
            foreach (var a in go.GetComponentsInChildren<Animator>()) a.updateMode = AnimatorUpdateMode.Normal;
            try { profile.LoadPlayerData(go.GetComponent<Player>()); }
            catch (Exception e) { Log.Warn($"could not dress {profile.GetFilename()}: {e.Message}"); }
            return go;
        }

        internal static void Select(PlayerProfile p)
        {
            _focus = p;
            var m = Members.FirstOrDefault(x => x.Profile == p);
            if (m == null || m.Go == null) { if (_spot) _spot.enabled = false; return; }
            if (_spot == null)
            {
                var go = new GameObject("CampfireSpot");
                _spot = go.AddComponent<Light>();
                _spot.type = LightType.Spot;
                _spot.color = new Color(1f, 0.82f, 0.55f);
                _spot.intensity = 2.2f;
                _spot.range = 7f;
                _spot.spotAngle = 38f;
                _spot.shadows = LightShadows.None;
            }
            _spot.enabled = true;
            _spot.transform.position = m.Go.transform.position + Vector3.up * 4.5f + (Fejd.Camera.transform.position - m.Go.transform.position).normalized * 1.2f;
            _spot.transform.LookAt(m.Go.transform.position + Vector3.up * 1f);
        }

        internal static Member Focus() => Members.FirstOrDefault(m => m.Profile == _focus && m.Go != null);
        private static PlayerProfile _focus;

        internal static void Turn(PlayerProfile p, float degrees)
        {
            var m = Members.FirstOrDefault(x => x.Profile == p);
            if (m?.Go != null) m.Go.transform.Rotate(0f, degrees, 0f, Space.World);
        }

        internal static void Rebuild() { _last.Clear(); CharScreen.Repaint(); }

        internal static void Unload()
        {
            Clear();
            if (_spot != null) UnityEngine.Object.Destroy(_spot.gameObject);
            _spot = null;
        }

        internal static void Clear()
        {
            foreach (var m in Members) if (m.Go != null) UnityEngine.Object.Destroy(m.Go);
            Members.Clear();
            _last.Clear();
            if (_spot != null) _spot.enabled = false;
        }
    }
}
