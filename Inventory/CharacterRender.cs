using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Jatify.UI
{
    // The character in the equipment column: a small camera in front of the player renders only the
    // player's own layers, within a tight near/far range, into a texture shown in the inventory.
    // It renders only while the inventory is open. Unloading destroys the camera and releases the
    // texture (video memory is not kept).
    // Studio lighting: while this camera renders, every scene light is off and two lights of our own are
    // on (neutral ambient, no fog), and effect renderers on the player (wisp, potion and weather vfx) are
    // hidden. All of it is put back right after, before the game's own camera draws.
    // The figure is a stand-in, not your character (user): a player model spawned like the main menu's
    // preview (networking off), far above the world, its physics and game logic removed, wearing what you
    // wear (VisEquipment state copied twice a second). It idles, or holds still (setting CharacterIdle).
    internal sealed class CharacterRender
    {
        private static readonly System.Reflection.FieldInfo[] VisState = typeof(VisEquipment).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => (f.Name.EndsWith("Item") || f.Name.EndsWith("ItemVariant") || f.Name.EndsWith("ItemQuality") || f.Name == "m_skinColor" || f.Name == "m_hairColor" || f.Name == "m_modelIndex") && !f.Name.StartsWith("m_current") && (f.FieldType == typeof(int) || f.FieldType == typeof(string) || f.FieldType == typeof(Vector3))).ToArray();
        private static readonly Vector3 Studio = new Vector3(0f, 6000f, 0f);
        private GameObject _doll; private VisEquipment _dollVis; private Animator[] _anims = new Animator[0];
        private float _nextSync, _born;
        private readonly RawImage _view;
        private GameObject _camGo;
        private Camera _cam;
        private RenderTexture _rt;
        private Light _key, _fill;
        private Player _p;
        private readonly List<Light> _lights = new List<Light>();          // scene lights we switched off
        private readonly List<Renderer> _hidden = new List<Renderer>();    // effect renderers we switched off
        private float _nextScan;
        private Light[] _scene = new Light[0];
        private Color _ambient; private AmbientMode _ambientMode; private bool _fog, _inRender;

        internal CharacterRender(RectTransform parent, float x, float y, float w, float h)
        {
            _view = Kit.Node(parent, "jatify-character").gameObject.AddComponent<RawImage>();
            _view.rectTransform.At(x, y, w, h);
            _view.raycastTarget = false;
            _view.enabled = false;
            _rt = new RenderTexture(Mathf.RoundToInt(w * 2), Mathf.RoundToInt(h * 2), 24, RenderTextureFormat.ARGB32) { name = "jatify-character", antiAliasing = 2 };
            _view.texture = _rt;
            Camera.onPreCull += PreRender; Camera.onPostRender += PostRender;
        }

        internal void Tick(Player p, bool open)
        {
            _p = p;
            if (p == null || !Cfg.CharacterView.Value) { if (_cam != null) _cam.enabled = false; _view.enabled = false; return; }
            // the figure lives from the moment you are in the world, out of sight, and is never switched off:
            // re-enabling an Animator restarts it, and the player model starts by standing up from sitting
            if (_doll == null && !Spawn()) return;
            if (Time.unscaledTime >= _nextSync) { _nextSync = Time.unscaledTime + 0.5f; Sync(p); }
            float speed = Cfg.CharacterIdle.Value || Time.unscaledTime - _born < 3f ? 1f : 0f;   // still: hold the idle pose it settled into
            foreach (var a in _anims) if (a != null && a.speed != speed) a.speed = speed;
            if (!open || Time.unscaledTime - _born < 3f) { if (_cam != null) _cam.enabled = false; _view.enabled = false; _shown = false; return; }   // shown once it stands idle
            if (_cam == null)
            {
                _camGo = new GameObject("jatify-character-camera");
                Object.DontDestroyOnLoad(_camGo);
                _cam = _camGo.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0, 0, 0, 0);
                _cam.targetTexture = _rt; _cam.fieldOfView = 26f; _cam.nearClipPlane = 0.3f; _cam.farClipPlane = 6f;
                _cam.allowHDR = false; _cam.allowMSAA = true; _cam.depth = -50;
                _key = StudioLight("Key", new Vector3(28f, 150f, 0f), 1.3f, new Color(1f, 0.96f, 0.9f));
                _fill = StudioLight("Fill", new Vector3(10f, 230f, 0f), 0.45f, new Color(0.85f, 0.9f, 1f));
            }
            _cam.cullingMask = _mask;
            var t = _doll.transform;
            var center = t.position + Vector3.up * 0.95f;
            _camGo.transform.position = center + t.forward * 4.2f + Vector3.up * 0.15f;
            _camGo.transform.LookAt(center);
            _key.transform.rotation = t.rotation * Quaternion.Euler(28f, 150f, 0f);   // lights turn with the figure
            _fill.transform.rotation = t.rotation * Quaternion.Euler(10f, 230f, 0f);
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 1f; _scene = Object.FindObjectsByType<Light>(FindObjectsSortMode.None); } // ponytail: a scan a second while open; lights spawned in between light the doll for under a second
            _view.enabled = true;
            // idling: drawn every frame (it moves); still (CharacterIdle off, user): drawn when the inventory opens and when what you
            // wear or look like changes, nothing in between
            bool idle = Cfg.CharacterIdle.Value;
            _cam.enabled = idle;
            if (!_shown) _redraw = 3;
            if (!idle && _redraw > 0) { _cam.Render(); _redraw--; }   // a few frames: VisEquipment rebuilds the models a frame after a change
            _shown = true;
        }
        private bool _shown; private int _redraw;
        private int _mask, _sig;

        private bool Spawn()
        {
            var prefab = Game.instance != null ? Game.instance.m_playerPrefab : null; if (prefab == null) return false;
            ZNetView.m_forceDisableInit = true;
            try { _doll = Object.Instantiate(prefab, Studio, Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
            _doll.name = "jatify-character-doll";
            // a figure only: no physics, no Player / Character logic (enemies, minimap and saves never see it)
            foreach (var c in _doll.GetComponents<Component>())
                if (!(c is Transform) && !(c is VisEquipment) && !(c is ZNetView) && !(c is Animator) && !(c is LODGroup)) Object.Destroy(c);
            foreach (var col in _doll.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
            _dollVis = _doll.GetComponent<VisEquipment>();
            _anims = _doll.GetComponentsInChildren<Animator>(true);
            foreach (var a in _anims) { a.updateMode = AnimatorUpdateMode.Normal; a.keepAnimatorStateOnDisable = true; }
            _born = Time.unscaledTime; _nextSync = 0f;
            return _dollVis != null;
        }

        // what you look like and wear -> the figure (VisEquipment builds the models from this)
        private void Sync(Player p)
        {
            var src = p.GetComponent<VisEquipment>(); if (src == null || _dollVis == null) return;
            int sig = 17; foreach (var f in VisState) { var v = f.GetValue(src); sig = sig * 31 + (v != null ? v.GetHashCode() : 0); f.SetValue(_dollVis, v); }
            var zdo = src.GetComponent<ZNetView>()?.GetZDO();   // body, skin and hair live in the character's network data
            if (zdo != null)
            {
                _dollVis.SetModel(zdo.GetInt(ZDOVars.s_modelIndex));
                _dollVis.SetSkinColor(zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one));
                _dollVis.SetHairColor(zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one));
                sig = sig * 31 + zdo.GetInt(ZDOVars.s_modelIndex) + zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one).GetHashCode() * 7 + zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one).GetHashCode() * 13;
            }
            if (sig != _sig) { _sig = sig; _redraw = 3; }
            // the layers the figure is drawn on, refreshed with the outfit rather than every frame
            int mask = 0; foreach (var r in _doll.GetComponentsInChildren<Renderer>(false)) if (!IsEffect(r)) mask |= 1 << r.gameObject.layer; _mask = mask;
        }

        private Light StudioLight(string name, Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject("jatify-character-" + name); go.transform.SetParent(_camGo.transform, false);
            var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = intensity; l.color = color; l.shadows = LightShadows.None; l.enabled = false;
            return l;
        }

        private static bool IsEffect(Renderer r)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return true;
            for (var t = r.transform; t != null; t = t.parent)
            {
                var n = t.name;
                if (n.StartsWith("vfx") || n.StartsWith("fx_") || n.StartsWith("sfx") || n.Contains("demister") || n.Contains("Wisp")) return true;
                if (t.GetComponent<Player>() != null) break;
            }
            return false;
        }

        private void PreRender(Camera c)
        {
            if (c != _cam || _cam == null) return;
            _lights.Clear(); _hidden.Clear(); _inRender = true;
            foreach (var l in _scene) if (l != null && l.enabled && l != _key && l != _fill) { l.enabled = false; _lights.Add(l); }
            _key.enabled = true; _fill.enabled = true;
            _ambient = RenderSettings.ambientLight; _ambientMode = RenderSettings.ambientMode; _fog = RenderSettings.fog;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.44f); RenderSettings.fog = false;
            if (_doll != null) foreach (var r in _doll.GetComponentsInChildren<Renderer>(false)) if (r.enabled && IsEffect(r)) { r.enabled = false; _hidden.Add(r); }
        }

        private void PostRender(Camera c)
        {
            if (c != _cam || _cam == null) return;
            Restore();
        }

        private void Restore()
        {
            foreach (var l in _lights) if (l != null) l.enabled = true;
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            if (_inRender) { RenderSettings.ambientMode = _ambientMode; RenderSettings.ambientLight = _ambient; RenderSettings.fog = _fog; _inRender = false; }
            _lights.Clear(); _hidden.Clear();
            if (_key != null) { _key.enabled = false; _fill.enabled = false; }
        }

        internal void Destroy()
        {
            Camera.onPreCull -= PreRender; Camera.onPostRender -= PostRender;
            Restore();
            if (_camGo != null) Object.Destroy(_camGo);
            if (_doll != null) Object.Destroy(_doll);
            _doll = null; _dollVis = null; _anims = new Animator[0];
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            _rt = null; _cam = null; _camGo = null; _scene = new Light[0];
            if (_view != null) Object.Destroy(_view.gameObject);
        }
    }
}
