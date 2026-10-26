using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Jatify.UI
{
    // Dev test kit (user: tests with Rayla in a clean world, spawning what each test needs, then hover, move, attack). Driven by
    // shot.request lines "t:<command>:<args>", so a script can set a scene up and take the shot.
    //   t:royal  t:god  t:spawn:<prefab>[:dist[:level[:tame]]]  t:saddle  t:ride  t:dismount  t:look:<name>  t:hit:<name>[:dmg]
    //   t:attack  t:hover-slot:<x>,<y>  t:hover-eq:<slot>  t:fill:<container>[:clear]  t:unhover  t:clear
    // Everything it spawns is remembered and t:clear removes it; nothing else in the world is touched.
    internal static class TestKit
    {
        private static readonly List<ZNetView> _spawned = new List<ZNetView>();
        private static readonly List<(Transform t, Vector3 face, bool big, bool objs)> _lineup = new List<(Transform, Vector3, bool, bool)>();
        private static readonly AccessTools.FieldRef<Character, Quaternion> LookYaw = AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");
        private static readonly AccessTools.FieldRef<Player, float> LookPitch = AccessTools.FieldRefAccess<Player, float>("m_lookPitch");
        private static GameObject _hovered;

        private static readonly List<Minimap.PinData> _pins = new List<Minimap.PinData>();

        private static System.Collections.IEnumerator Drops(Player p, float seconds)
        {
            string[] items = { "Wood", "Stone", "Resin", "Raspberry", "Flint", "Mushroom", "FineWood", "Feathers" };
            float end = Time.time + seconds; int i = 0;
            Log.Info($"test drops: auto pickup {AccessTools.Field(typeof(Player), "m_enableAutoPickup")?.GetValue(p)}, range {p.m_autoPickupRange}");
            while (Time.time < end && p != null)
            {
                var pf = ObjectDB.instance.GetItemPrefab(items[i++ % items.Length]);
                if (pf != null) Object.Instantiate(pf, p.transform.position + p.transform.right * 0.9f + p.transform.forward * 0.4f + Vector3.up * 1.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                yield return new WaitForSeconds(0.4f);
            }
        }

        internal static void Run(string cmd)
        {
            var p = Player.m_localPlayer; if (p == null) { Log.Info("test: no player"); return; }
            var a = cmd.Split(':'); string Arg(int i, string d = "") => a.Length > i && a[i].Length > 0 ? a[i] : d;
            Recover();
            switch (a[0])
            {
                case "royal":   // mobs leave Rayla alone: the crown's Crowned effect, as if worn
                {
                    var se = ObjectDB.instance.m_StatusEffects.FirstOrDefault(s => s != null && (s.m_name == "$se_crowned_name" || s.name.ToLowerInvariant().Contains("crown")));
                    if (se == null) { Log.Info("test royal: no Crowned effect in ObjectDB"); return; }
                    p.GetSEMan().AddStatusEffect(se.NameHash(), true); Log.Info($"test royal: {se.name} on"); return;
                }
                case "god": p.SetGodMode(true); Log.Info("test god: on"); return;
                case "coins":   // what 100 coins weigh with the current coin settings
                {
                    var c = ObjectDB.instance.GetItemPrefab("Coins").GetComponent<ItemDrop>().m_itemData.Clone(); c.m_stack = 100;
                    Log.Info($"test coins: 100 weigh {c.GetWeight():0.###} kg (one {c.GetNonStackedWeight():0.###})"); return;
                }
                case "explore": Minimap.instance?.ExploreAll(); Log.Info("test explore: the whole map revealed (test world)"); return;   // the world map screenshot: the flat test world, all of it
                case "hover-item":   // pointer over the bag slot holding <prefab> (e.g. axeblackmetal): its tooltip, compared with what you wear or hold
                {
                    var it = p.GetInventory().GetAllItems().FirstOrDefault(i => !i.m_equipped && i.m_dropPrefab != null && i.m_dropPrefab.name.ToLowerInvariant() == Arg(1));
                    if (it == null) { Log.Info($"test hover-item: no {Arg(1)} in the bag"); return; }
                    Run($"hover-slot:{it.m_gridPos.x},{it.m_gridPos.y}"); return;
                }
                case "assign":   // t:assign:<chest name>:<item prefab> keeps the nearest chest for that item (its label shows on the chest)
                {
                    var go = Named(p, Arg(1)); var c = go != null ? go.GetComponentInChildren<Container>() : null; if (c == null) { Log.Info($"test assign: no container {Arg(1)}"); return; }
                    if (Arg(2) == "none") { ChestAssign.Set(c, ""); Log.Info($"test assign: {go.name} cleared"); return; }
                    var pf = ObjectDB.instance.m_items.FirstOrDefault(i => i != null && i.name.ToLowerInvariant() == Arg(2)); if (pf == null) { Log.Info($"test assign: no item {Arg(2)}"); return; }
                    ChestAssign.Set(c, pf.name); Log.Info($"test assign: {go.name} -> {pf.name}"); return;
                }
                case "queue":   // a few recipes in the craft queue (they wait while the materials are missing)
                {
                    if (Arg(1) == "clear") { CraftQueue.Items.Clear(); Log.Info("test queue: cleared"); return; }
                    foreach (var n in new[] { "Recipe_ArrowWood", "Recipe_Club", "Recipe_Torch" })
                    { var r = ObjectDB.instance.m_recipes.FirstOrDefault(x => x != null && x.name == n); if (r != null) CraftQueue.Add(r, n == "Recipe_ArrowWood" ? 2 : 1); }
                    Log.Info($"test queue: {CraftQueue.Items.Count} queued"); return;
                }
                case "ach": Pages.TestAch(char.ToUpperInvariant(Arg(1, "statistics")[0]) + Arg(1, "statistics").Substring(1)); Log.Info($"test ach: {Arg(1)}"); return;
                case "pin":   // the nearest creature called <name> stays exactly where it is (no AI, no knockback): a training dummy for attack shots
                {
                    var go = Named(p, Arg(1)); var c = go != null ? go.GetComponentInParent<Character>() : null; if (c == null) { Log.Info($"test pin: no creature {Arg(1)}"); return; }
                    var ai = c.GetComponent<BaseAI>(); if (ai != null) ai.enabled = false;
                    var rb = c.GetComponent<Rigidbody>(); if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
                    Log.Info($"test pin: {c.name}"); return;
                }
                case "health": p.SetHealth(float.Parse(Arg(1, "5"), System.Globalization.CultureInfo.InvariantCulture)); Log.Info($"test health: {p.GetHealth():0}"); return;   // low health: the red vignette (god off first: t:clean)
                case "pins":   // README map shot: one pin of each Jatify kind around Rayla, a few of Valheim's, and a close group of four; t:pins:clear removes them
                {
                    var map = Minimap.instance; if (map == null) return;
                    foreach (var old in _pins) map.RemovePin(old); _pins.Clear();
                    if (Arg(1) == "clear") { Log.Info("test pins: cleared"); return; }
                    var at = p.transform.position; string[] names = { "Copper vein", "Burial chamber", "Troll cave", "Longship", "Loot", "Farm", "Raspberries", "Fishing spot", "Troll", "Meet here" };
                    for (int i = 0; i < PinKinds.Names.Length; i++)
                    {
                        float ang = i * Mathf.PI * 2f / PinKinds.Names.Length; var pos = at + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (90f + 25f * (i % 3));
                        var pin = map.AddPin(pos, Minimap.PinType.Icon3, names[i], true, false); PinKinds.Set(pin, i + 1); _pins.Add(pin);
                    }
                    foreach (var (t, n, off) in new[] { (Minimap.PinType.Icon0, "Camp", new Vector3(-40f, 0f, 160f)), (Minimap.PinType.Icon1, "Home", new Vector3(30f, 0f, -40f)), (Minimap.PinType.Icon4, "Eikthyr", new Vector3(170f, 0f, 60f)) })
                        _pins.Add(map.AddPin(at + off, t, n, true, false));
                    for (int i = 0; i < 4; i++) { var pin = map.AddPin(at + new Vector3(-150f + i * 4f, 0f, -120f + (i % 2) * 4f), Minimap.PinType.Icon3, "Copper " + (i + 1), true, false); PinKinds.Set(pin, 1); _pins.Add(pin); }
                    Log.Info($"test pins: {_pins.Count} added"); return;
                }
                case "drops":   // items fall just in front of Rayla for <seconds> (default 8), one every 0.4 s, for auto pickup: the pickup float
                    Plugin.Instance.StartCoroutine(Drops(p, float.Parse(Arg(1, "8"), System.Globalization.CultureInfo.InvariantCulture))); Log.Info($"test drops: {Arg(1, "8")} s"); return;
                case "step":   // Rayla moves <m> (default 40) metres the way she faces, onto open ground: shots away from the test row's hover labels
                {
                    var to = p.transform.position + p.transform.forward * float.Parse(Arg(1, "40"), System.Globalization.CultureInfo.InvariantCulture);
                    if (ZoneSystem.instance != null) to.y = ZoneSystem.instance.GetGroundHeight(to) + 0.2f;
                    p.TeleportTo(to, p.transform.rotation, true); Log.Info($"test step: {to:F0}"); return;
                }
                case "lift":   // Rayla up <m> metres (default 40) where she stands, no teleport effect: she falls back down (low-health and fall shots)
                {
                    var body = p.GetComponent<Rigidbody>(); var to = p.transform.position + Vector3.up * float.Parse(Arg(1, "40"), System.Globalization.CultureInfo.InvariantCulture);
                    p.transform.position = to; if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
                    Log.Info($"test lift: {to.y:F0}"); return;
                }
                case "mapzoom":   // the world map's zoom (smaller = closer; Valheim's large map opens around 0.1)
                {
                    var f = AccessTools.Field(typeof(Minimap), "m_largeZoom"); if (Minimap.instance == null || f == null) return;
                    f.SetValue(Minimap.instance, float.Parse(Arg(1, "0.03"), System.Globalization.CultureInfo.InvariantCulture)); Log.Info($"test mapzoom: {Arg(1, "0.03")}"); return;
                }
                case "face":   // Rayla turns to face where the camera looks, so shots show her from behind (user)
                {
                    var yaw = Quaternion.Euler(0f, LookYaw(p).eulerAngles.y, 0f); p.transform.rotation = yaw; LookPitch(p) = 6f;
                    Log.Info("test face"); return;
                }
                case "clean":   // screenshots for the README: no cheat flags (god, devcommands) and no dev badge; t:clean:off puts god and the badge back
                {
                    bool on = Arg(1) != "off"; p.SetGodMode(!on); if (on) Terminal.m_cheat = false; Root.Badge(!on);
                    Log.Info($"test clean: {on}"); return;
                }
                case "fly": Player.m_debugMode = true; Log.Info($"test fly: {AccessTools.Method(typeof(Player), "ToggleDebugFly").Invoke(p, null)} (Space up, Ctrl down)"); return;
                case "unarmed": foreach (var it in p.GetInventory().GetAllItems().Where(i => i.m_equipped && i.IsWeapon() || i.m_equipped && i.m_shared.m_buildPieces != null).ToList()) p.UnequipItem(it, false); Log.Info("test unarmed"); return;
                case "clearing":   // open, flat, dry ground nearby (user: a clear view): rings out to 90 m, no tree, rock or building within 14 m
                {
                    var zs = ZoneSystem.instance; var from = p.transform.position; int wet = 0, steep = 0, busy = 0;
                    for (float r = 20f; r <= 90f; r += 10f)   // only loaded zones have colliders to check
                        for (int k = 0; k < 16; k++)
                        {
                            float ang = k * Mathf.PI / 8f; var at = from + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                            float h = zs.GetGroundHeight(at); if (h < zs.m_waterLevel + 1f) { wet++; continue; }
                            at.y = h; bool flat = true;
                            for (int q = 0; q < 8 && flat; q++) { var e = at + new Vector3(Mathf.Cos(q * Mathf.PI / 4f), 0f, Mathf.Sin(q * Mathf.PI / 4f)) * 10f; flat = Mathf.Abs(zs.GetGroundHeight(e) - h) < 3f; }
                            if (!flat) { steep++; continue; }
                            if (Physics.OverlapSphere(at, 14f).Any(c => c.GetComponentInParent<TreeBase>() || c.GetComponentInParent<MineRock>() || c.GetComponentInParent<MineRock5>() || c.GetComponentInParent<Piece>() || c.GetComponentInParent<Location>())) { busy++; continue; }   // trees, rocks, buildings; grass and bushes are fine
                            p.TeleportTo(at + Vector3.up * 0.5f, p.transform.rotation, false); Log.Info($"test clearing: {r:0} m away"); return;
                        }
                    Log.Info($"test clearing: none found within 90 m (water {wet}, steep {steep}, trees/rocks/buildings {busy})"); return;
                }
                case "spawn":
                {
                    var prefab = ZNetScene.instance.m_prefabs.FirstOrDefault(x => x != null && x.name.Equals(Arg(1), System.StringComparison.OrdinalIgnoreCase));   // shot.request arrives lowercased if (prefab == null) { Log.Info($"test spawn: no prefab {Arg(1)}"); return; }
                    float dist = float.Parse(Arg(2, "4"), System.Globalization.CultureInfo.InvariantCulture);
                    var fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up).normalized; var pos = p.transform.position + fwd * dist + Vector3.up * 0.5f;
                    var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd));
                    var c = go.GetComponent<Character>(); if (c != null && int.TryParse(Arg(3, "1"), out var lv)) c.SetLevel(lv);
                    if (Arg(4) == "tame") Tame(go);
                    var nv = go.GetComponent<ZNetView>(); if (nv != null) { _spawned.Add(nv); Tag(nv, -1); }
                    Log.Info($"test spawn: {prefab.name} at {dist} m"); return;
                }
                case "lineup":   // every test creature in one frozen row to Rayla's right: 0, 1 and 2 stars, then Eikthyr (user: one boss);
                {   // every second one set alerted; t:goto:N stands Rayla in front of number N; t:clear removes them
                    // t:lineup:objects: a second row of stations and world objects (chest, workbench, smelter, beehive, sign, bed, portal,
                    // campfire, door, cart, an item on the ground, an ore rock, a tree)
                    bool objs = Arg(1) == "objects";
                    string[] row = objs ? new[] { "piece_chest_wood:0", "piece_workbench:0", "smelter:0", "piece_beehive:0", "sign:0", "bed:0", "portal_wood:0", "fire_pit:0", "wood_door:0", "Cart:0", "Wood:0", "MineRock_Copper:0", "Beech1:0" }
                                        : new[] { "Greydwarf:1", "Greydwarf:2", "Greydwarf:3", "Skeleton:1", "Skeleton:2", "Skeleton:3", "Troll:1", "Troll:2", "Troll:3", "Eikthyr:1" };
                    var fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up).normalized; var right = Vector3.Cross(Vector3.up, fwd);
                    var setAlert = AccessTools.Method(typeof(BaseAI), "SetAlerted"); var at = p.transform.position + fwd * (objs ? 60f : 30f);
                    foreach (var l in _lineup.Where(l => l.objs == objs && l.t != null).ToList()) { var nv0 = l.t.GetComponent<ZNetView>(); if (nv0 != null && nv0.IsValid()) ZNetScene.instance.Destroy(l.t.gameObject); }   // a new row replaces the old one
                    foreach (var nv0 in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))   // and strays of this row a reload lost track of
                        if (nv0 != null && nv0.IsValid()) { int v = nv0.GetZDO().GetInt(TagKey) - 2; if (v >= 0 && (v >= 100) == objs) ZNetScene.instance.Destroy(nv0.gameObject); }
                    _lineup.RemoveAll(l => l.objs == objs);
                    for (int k = 0; k < row.Length; k++)
                    {
                        var e = row[k].Split(':'); var prefab = ZNetScene.instance.GetPrefab(e[0]); if (prefab == null) continue;
                        bool big = e[0] == "Troll" || e[0] == "Eikthyr" || e[0].StartsWith("portal") || e[0].StartsWith("MineRock"); at += right * (big ? 9f : objs ? 6f : 4f);
                        var pos = at; pos.y = ZoneSystem.instance.GetGroundHeight(pos) + (objs ? 0f : 0.2f);   // pieces right on the ground: a floating one has no support and breaks
                        var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd));
                        var c = go.GetComponent<Character>(); if (c != null && e[1] != "0") c.SetLevel(int.Parse(e[1]));
                        var ai = go.GetComponent<BaseAI>();
                        if (ai != null) { if (k % 2 == 1) setAlert?.Invoke(ai, new object[] { true }); ai.enabled = false; }   // frozen: no AI, no walking
                        var rb = go.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                        var nv = go.GetComponent<ZNetView>(); if (nv != null) { _spawned.Add(nv); Tag(nv, (objs ? 100 : 0) + k); }
                        _lineup.Add((go.transform, -fwd, big, objs));
                    }
                    Log.Info($"test lineup{(objs ? " objects" : "")}: {string.Join(", ", _lineup.Select((l, n) => n + " " + l.t.name.Replace("(Clone)", "")))}"); return;
                }
                case "goto":   // instant: Rayla in front of lineup entry N (or the first whose name has <name>), facing it, no loading screen
                {
                    int k = int.TryParse(Arg(1, "0"), out var kn) ? kn : _lineup.FindIndex(l => l.t != null && l.t.name.ToLowerInvariant().Contains(Arg(1)));
                    if (k < 0 || k >= _lineup.Count || _lineup[k].t == null) { Log.Info($"test goto: nothing at {Arg(1)} in the lineup"); return; }
                    var (t, face, big, objs) = _lineup[k]; var pos = t.position + face * (big ? 9f : objs ? 3.2f : 4.5f); pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.1f;
                    var rb = p.GetComponent<Rigidbody>(); p.transform.position = pos; if (rb != null) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
                    var dir = (t.position + Vector3.up * (big ? 2.5f : objs ? 0.4f : 1.2f) - (pos + Vector3.up * 1.6f)).normalized;
                    LookYaw(p) = Quaternion.Euler(0f, Quaternion.LookRotation(dir).eulerAngles.y, 0f); LookPitch(p) = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;
                    p.transform.rotation = LookYaw(p);
                    Hover = objs ? t.gameObject : null;   // objects: the crosshair "is on" it, so its hover text and prompt show (t:close clears it)
                    Log.Info($"test goto: {k} {t.name}"); return;
                }
                case "find":   // what's near called <name>, and its test tag: t:find:chest
                    foreach (var nv in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                        if (nv != null && nv.name.ToLowerInvariant().Contains(Arg(1)) && Vector3.Distance(nv.transform.position, p.transform.position) < 150f)
                            Log.Info($"test find: {nv.name} valid {nv.IsValid()} tag {(nv.IsValid() ? nv.GetZDO().GetInt(TagKey) : -99)} at {Vector3.Distance(nv.transform.position, p.transform.position):0} m");
                    return;
                case "store":   // the shop of the nearest trader called <name> (Haldor, Hildir, BogWitch), as talking to them opens it
                {
                    var go = Named(p, Arg(1)); var tr = go != null ? go.GetComponentInChildren<Trader>() : null; if (tr == null || StoreGui.instance == null) { Log.Info($"test store: no trader called {Arg(1)}"); return; }
                    StoreGui.instance.Show(tr); Log.Info($"test store: {go.name}, {tr.GetAvailableItems().Count} items"); return;
                }
                case "remove":   // every networked object called <name> within 150 m, destroyed (traders, pieces: what t:kill doesn't take)
                {
                    int n = 0; foreach (var nv in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).ToList())
                        if (nv != null && nv.IsValid() && nv.name.ToLowerInvariant().Contains(Arg(1)) && Vector3.Distance(nv.transform.position, p.transform.position) < 150f) { nv.ClaimOwnership(); nv.Destroy(); n++; }
                    Log.Info($"test remove: {n} {Arg(1)}"); return;
                }
                case "kill":   // every creature called <name> within 120 m, gone at once (no drops): t:kill:eikthyr
                {
                    int n = 0; foreach (var c in Character.GetAllCharacters().ToList())
                        if (c != null && !c.IsPlayer() && c.name.ToLowerInvariant().Contains(Arg(1)) && Vector3.Distance(c.transform.position, p.transform.position) < 120f) { var nv = c.GetComponent<ZNetView>(); if (nv != null && nv.IsValid()) { ZNetScene.instance.Destroy(c.gameObject); n++; } }
                    Log.Info($"test kill: {n} {Arg(1)}"); return;
                }
                case "purge":   // every creature within N m (default 60), including ones an earlier load spawned and lost track of
                {
                    float r = float.Parse(Arg(1, "60"), System.Globalization.CultureInfo.InvariantCulture); int n = 0;
                    foreach (var c in Character.GetAllCharacters().ToList())
                        if (c != null && !c.IsPlayer() && Vector3.Distance(c.transform.position, p.transform.position) < r) { var nv = c.GetComponent<ZNetView>(); if (nv != null && nv.IsValid()) { ZNetScene.instance.Destroy(c.gameObject); n++; } }
                    foreach (var nv in _spawned) if (nv != null && nv.IsValid()) { ZNetScene.instance.Destroy(nv.gameObject); n++; }
                    _spawned.Clear(); _lineup.Clear(); Log.Info($"test purge: {n} removed"); return;
                }
                case "noon": EnvMan.instance.m_debugTimeOfDay = Arg(1) != "off"; EnvMan.instance.m_debugTime = 0.5f; EnvMan.instance.m_debugEnv = Arg(1) != "off" ? "Clear" : ""; Log.Info($"test noon: {EnvMan.instance.m_debugTimeOfDay}"); return;   // midday, clear sky
                case "saddle":
                {
                    var t = Near<Tameable>(p, 12f); if (t == null) { Log.Info("test saddle: no tameable near"); return; }
                    Tame(t.gameObject); AccessTools.Method(typeof(Tameable), "SetSaddle").Invoke(t, new object[] { true }); Log.Info($"test saddle: {t.name}"); return;
                }
                case "ride":
                {
                    var s = Near<Sadle>(p, 12f); if (s == null) { Log.Info("test ride: no saddle near"); return; }
                    Log.Info($"test ride: {s.Interact(p, false, false)}"); return;
                }
                case "dismount": AccessTools.Method(typeof(Player), "StopDoodadControl")?.Invoke(p, null); Log.Info("test dismount"); return;
                case "look":   // aim the camera at the nearest object whose name has <name>: what the crosshair hovers shows its hover text
                {
                    var go = Named(p, Arg(1)); if (go == null) { Log.Info($"test look: nothing called {Arg(1)} near"); return; }
                    var col = go.GetComponentInChildren<Collider>(); var at = col != null ? col.bounds.center : go.transform.position + Vector3.up;
                    if (go.GetComponent<TreeBase>() != null || go.GetComponent<TreeLog>() != null) at = go.transform.position + Vector3.up * 1.6f;   // the trunk at eye height, not the crown (user: camera too high)
                    var eye = GameCamera.instance != null ? GameCamera.instance.transform.position : p.GetEyePoint();
                    var dir = (at - eye).normalized;
                    LookYaw(p) = Quaternion.Euler(0f, Quaternion.LookRotation(dir).eulerAngles.y, 0f);
                    LookPitch(p) = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;
                    Log.Info($"test look: {go.name}"); return;
                }
                case "hit":   // damage the nearest damageable called <name> as Rayla: health bars, nameplates, tree health
                {
                    var go = Named(p, Arg(1)); var d = go != null ? go.GetComponentInParent<IDestructible>() : null; if (d == null) { Log.Info($"test hit: nothing to hit called {Arg(1)}"); return; }
                    var hit = new HitData { m_point = go.transform.position + Vector3.up, m_dir = (go.transform.position - p.transform.position).normalized, m_toolTier = 10 };
                    hit.m_damage.m_slash = hit.m_damage.m_chop = hit.m_damage.m_pickaxe = float.Parse(Arg(2, "20"), System.Globalization.CultureInfo.InvariantCulture);
                    hit.SetAttacker(p); d.Damage(hit); Log.Info($"test hit: {go.name}"); return;
                }
                case "attack": Log.Info($"test attack: {p.StartAttack(null, false)}"); return;
                case "hover-slot":   // pointer over a bag slot: its tooltip
                {
                    var xy = Arg(1, "0,1").Split(','); var g = InventoryGui.instance != null ? InventoryGui.instance.m_playerGrid : null; if (g == null) return;
                    var el = (InventoryElement)AccessTools.Method(typeof(InventoryGrid), "GetElement").Invoke(g, new object[] { int.Parse(xy[0]), int.Parse(xy[1]), g.GetInventory().GetWidth() }); if (el == null) { Log.Info("test hover-slot: no slot"); return; }
                    Unhover(); _hovered = el.gameObject;
                    // the game reads the mouse itself for tooltips: move the virtual mouse there (the desktop cursor stays where it is)
                    var mouse = UnityEngine.InputSystem.Mouse.current; var cv = el.GetComponentInParent<Canvas>()?.rootCanvas; var at = RectTransformUtility.WorldToScreenPoint(cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null, ((RectTransform)el.transform).TransformPoint(((RectTransform)el.transform).rect.center));   // the slot's centre on screen (the GUI canvas may render through a camera)
                    if (mouse != null) UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position, at);
                    _holdAt = at;   // and keep it there: the desktop's own mouse events move it back (the tooltip then hides)
                    ExecuteEvents.Execute(_hovered, new PointerEventData(EventSystem.current) { position = at }, ExecuteEvents.pointerEnterHandler);
                    var item = g.GetInventory().GetItemAt(int.Parse(xy[0]), int.Parse(xy[1]));   // the grid fills in the item's text only for the slot under the real mouse: fill it here
                    if (item != null && el.m_tooltip != null) AccessTools.Method(typeof(InventoryGrid), "CreateItemTooltip").Invoke(g, new object[] { item, el.m_tooltip });
                    Log.Info($"test hover-slot: {xy[0]},{xy[1]} {item?.m_shared.m_name} at {at} (screen {Screen.width}x{Screen.height}, canvas {cv?.renderMode})"); return;
                }
                case "unhover": Unhover(); return;
                case "cfg":   // t:cfg:<Key>:<value> sets one of our settings (as the settings page would)
                {
                    var en = Plugin.Instance.Config.Keys.FirstOrDefault(k => k.Key.Equals(Arg(1), System.StringComparison.OrdinalIgnoreCase));
                    if (en == null) { Log.Info($"test cfg: no {Arg(1)}"); return; }
                    Plugin.Instance.Config[en].SetSerializedValue(Arg(2)); Log.Info($"test cfg: {en.Key} = {Plugin.Instance.Config[en].BoxedValue}"); return;
                }
                case "comp": Pages.TestIndex(Arg(1) == "q" ? Arg(2) : null, Arg(1) == "q" ? null : Arg(1)); Log.Info($"test comp: {Arg(1)} {Arg(2)}"); return;   // t:comp:<entry> opens its page, t:comp:q:<text> searches
                case "fill":   // the nearest container called <name> gets the design's stacks (wood, stone, ores...); t:fill:<name>:clear empties it
                {
                    var go = Named(p, Arg(1, "chest")); var box = go != null ? go.GetComponentInChildren<Container>(true) ?? go.GetComponentInParent<Container>() : null; if (box == null) { Log.Info($"test fill: no container called {Arg(1)} ({go?.name})"); return; }
                    var inv = box.GetInventory();
                    if (Arg(2) == "clear") { inv.RemoveAll(); Log.Info($"test fill: {go.name} emptied"); return; }
                    int at = 0;   // each its own stack in the next cell (the Wood 7 and Stone 21 could merge)
                    foreach (var (n, k) in new[] { ("Wood", 50), ("Wood", 43), ("FineWood", 34), ("Stone", 50), ("Stone", 21), ("Copper", 18), ("Bronze", 9), ("Coal", 40), ("Wood", 7), ("Stone", 12) })
                    {
                        var pf = ObjectDB.instance.GetItemPrefab(n); if (pf == null || at >= inv.GetWidth() * inv.GetHeight()) continue;
                        var d = pf.GetComponent<ItemDrop>().m_itemData.Clone(); d.m_dropPrefab = pf; d.m_stack = k;
                        d.m_gridPos = new Vector2i(at % inv.GetWidth(), at / inv.GetWidth()); inv.GetAllItems().Add(d); at++;   // straight in: AddItem would top up the stack before
                    }
                    AccessTools.Method(typeof(Inventory), "Changed").Invoke(inv, new object[] { false, false });
                    Log.Info($"test fill: {go.name} holds {inv.GetAllItems().Count} stacks"); return;
                }
                case "chest-search": ChestPanel.DevSearch(Arg(1)); Log.Info($"test chest-search: '{Arg(1)}'"); return;   // as if typed in the open chest's search
                case "type": if (TextInput.instance != null && TextInput.instance.m_inputField != null) TextInput.instance.m_inputField.text = Arg(1); Log.Info($"test type: '{Arg(1)}'"); return;   // into the open text box
                case "rename":   // the text box for the nearest tamed <name>, as Shift + E does
                {
                    var tm = Named(p, Arg(1))?.GetComponent<Tameable>(); if (tm == null) { Log.Info($"test rename: no tameable {Arg(1)}"); return; }
                    AccessTools.Method(typeof(Tameable), "SetName").Invoke(tm, null); Log.Info($"test rename: {tm.name}"); return;
                }
                case "hover-eq":   // pointer over a worn-gear slot (Head, Chest, Legs, Cape, Main hand...) or the first recipe "Cell": its tooltip
                {
                    var frame = InventoryGui.instance != null ? InventoryGui.instance.transform : null; var name = Arg(1, "Head");   // any shown slot of that name: worn gear, a recipe "Cell"
                    var slot = frame != null ? (Component)frame.GetComponentsInChildren<UITooltip>().FirstOrDefault(u => u.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                        ?? frame.GetComponentsInChildren<UnityEngine.UI.Graphic>().FirstOrDefault(u => u.raycastTarget && u.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) : null;   // or any control of that name (the chest's "Sort")
                    if (slot == null) { Log.Info($"test hover-eq: no slot {name}"); return; }
                    Unhover(); _hovered = slot.gameObject;
                    var at = RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)slot.transform).TransformPoint(((RectTransform)slot.transform).rect.center));
                    var mouse = UnityEngine.InputSystem.Mouse.current; if (mouse != null) UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position, at);
                    _holdAt = at; ExecuteEvents.Execute(_hovered, new PointerEventData(EventSystem.current) { position = at }, ExecuteEvents.pointerEnterHandler);
                    var worn = p.GetInventory().GetAllItems().Where(i => i.m_equipped).Select(i => $"{i.m_shared.m_name} [{i.m_shared.m_itemType}{(i.m_shared.m_setName.Length > 0 ? " set " + i.m_shared.m_setName + "/" + i.m_shared.m_setSize : "")}]");
                    Log.Info($"test hover-eq: {slot.name} '{(slot as UITooltip)?.m_topic}'; worn: {string.Join(", ", worn)}"); return;
                }
                case "under":   // what the (virtual) mouse is over, top first, with any tooltip's text: to find a stray empty tooltip
                {   // t:under or t:under:<x>,<y> (screen pixels from the top left)
                    var es = EventSystem.current; var m = UnityEngine.InputSystem.Mouse.current; if (es == null || m == null) return;
                    if (Arg(1).Contains(",")) { var q = Arg(1).Split(','); UnityEngine.InputSystem.LowLevel.InputState.Change(m.position, new Vector2(float.Parse(q[0]), Screen.height - float.Parse(q[1]))); }
                    var hits = new List<RaycastResult>(); es.RaycastAll(new PointerEventData(es) { position = m.position.ReadValue() }, hits);
                    foreach (var h in hits.Take(8)) { var ie = h.gameObject.GetComponentInParent<InventoryElement>(); if (ie != null) { var inv = InventoryGui.instance.m_playerGrid.GetInventory(); Log.Info($"under: cell {ie.Position} holds {inv.GetItemAt(ie.Position.x, ie.Position.y)?.m_shared.m_name ?? "nothing"}, at {((RectTransform)ie.transform).position}, active {ie.gameObject.activeInHierarchy}"); }
                        var tip = h.gameObject.GetComponentInParent<UITooltip>(); Log.Info($"under: {Path(h.gameObject.transform)}{(tip != null ? $"  tooltip on {tip.name}: '{tip.m_topic}' / '{(tip.m_text ?? "").Substring(0, System.Math.Min(40, (tip.m_text ?? "").Length))}'" : "")}"); }
                    return;
                }
                case "export":   // the world's real terrain (user: to rebuild it in Fog-Bound): t:export[:size m[:metres per pixel]], centred on Rayla
                {
                    float size = float.Parse(Arg(1, "2048"), System.Globalization.CultureInfo.InvariantCulture), step = float.Parse(Arg(2, "2"), System.Globalization.CultureInfo.InvariantCulture);
                    TerrainExport.Start(Arg(3) == "world" ? Vector3.zero : p.transform.position, size, step); return;   // :world centres it on the map
                }
                case "hover":   // the crosshair "is over" the nearest <name> (camera aim is too rough for small things): its hover text
                {
                    Hover = Named(p, Arg(1)); Log.Info($"test hover: {(Hover != null ? Hover.name : "nothing called " + Arg(1))}"); return;
                }
                case "station":   // open the crafting window at the nearest station called <name>, without its roof check
                {
                    var go = Named(p, Arg(1)); var st = go != null ? go.GetComponentInChildren<CraftingStation>() : null; if (st == null) { Log.Info($"test station: none called {Arg(1)}"); return; }
                    if (!st.InUseDistance(p))
                    {   // Valheim closes the window at once out of use range (t:goto stands 3.2 m off): step in to 70 % of it, still facing it
                        var away = p.transform.position - st.transform.position; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                        var pos = st.transform.position + away.normalized * st.m_useDistance * 0.7f; pos.y = ZoneSystem.instance.GetGroundHeight(pos) + 0.1f;
                        var rb = p.GetComponent<Rigidbody>(); p.transform.position = pos; if (rb != null) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
                    }
                    p.SetCraftingStation(st); InventoryGui.instance.Show(null); Log.Info($"test station: {st.m_name} (in range {st.InUseDistance(p)})"); return;
                }
                case "use":   // walk-up use (E) on the nearest <name>: chests, workbenches, signs, beds, portals
                {
                    var go = Named(p, Arg(1)); var it = go != null ? go.GetComponentInChildren<Interactable>() : null; if (it == null) { Log.Info($"test use: nothing to use called {Arg(1)}"); return; }
                    Log.Info($"test use: {go.name} -> {it.Interact(p, false, false)}"); return;
                }
                case "card":   // our smelter card for the nearest smelter/kiln/fermenter called <name>
                {
                    var go = Named(p, Arg(1)); var st = go != null ? (Component)go.GetComponent<Smelter>() ?? go.GetComponent<Fermenter>() : null; if (st == null) { Log.Info($"test card: no station called {Arg(1)}"); return; }
                    SmelterCard.Show(st); Log.Info($"test card: {go.name}"); return;
                }
                case "raid": RandEventSystem.instance.SetRandomEventByName(Arg(1, "army_eikthyr"), p.transform.position); Log.Info($"test raid: {Arg(1, "army_eikthyr")}"); return;
                case "raid-stop": RandEventSystem.instance.ResetRandomEvent(); Log.Info("test raid stopped"); return;
                case "close":   // back to the bare HUD between tests
                    Unhover(); Hover = null; if (StoreGui.IsVisible()) StoreGui.instance.Hide(); SmelterCard.Close();
                    ConfigPage.Open?.Close(); Settings.instance?.OnBack();   // the Jatify UI / Mods page and Settings too: they sat over the next shot
                    if (TextInput.instance != null && TextInput.instance.m_panel != null && TextInput.instance.m_panel.activeSelf) TextInput.instance.Hide();   // the panel itself: a mod may patch IsVisible
                    if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
                    if (Minimap.instance != null && Minimap.IsOpen()) Minimap.instance.SetMapMode(Minimap.MapMode.Small);
                    if (Menu.IsVisible()) Menu.instance.Hide();
                    if (Hud.IsPieceSelectionVisible()) Hud.HidePieceSelection();
                    return;
                case "clear":
                {
                    int n = 0; foreach (var nv in _spawned) if (nv != null && nv.IsValid()) { ZNetScene.instance.Destroy(nv.gameObject); n++; }
                    _spawned.Clear(); Log.Info($"test clear: {n} removed"); return;
                }
            }
            Log.Info($"test: unknown command {cmd}");
        }

        internal static GameObject Hover;   // FindHoverObject answers this while set (t:hover, t:close clears it)

        private static string Path(Transform t) { var s = t.name; for (int i = 0; i < 4 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; } return s; }

        // test objects carry a tag in their ZDO, so a hot reload (which forgets _spawned and _lineup) still finds them:
        // -1 a plain spawn, 0..99 creature lineup place, 100.. object lineup place
        private const string TagKey = "jatify.test";
        private static void Tag(ZNetView nv, int place) { if (nv.IsValid()) nv.GetZDO().Set(TagKey, place + 2); }   // +2: 0 is "no tag"
        private static void Recover()
        {
            if (_lineup.Count > 0 || _spawned.Count > 0) return;
            var found = new List<(int place, ZNetView nv)>();
            foreach (var nv in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                if (nv != null && nv.IsValid()) { int v = nv.GetZDO().GetInt(TagKey); if (v > 0) found.Add((v - 2, nv)); }
            foreach (var (place, nv) in found.OrderBy(f => f.place))
            {
                _spawned.Add(nv); if (place < 0) continue;
                var name = nv.name.Replace("(Clone)", ""); bool objs = place >= 100;
                bool big = name == "Troll" || name == "Eikthyr" || name.StartsWith("portal") || name.StartsWith("MineRock");
                _lineup.Add((nv.transform, nv.transform.forward, big, objs));
            }
            if (found.Count > 0) Log.Info($"test kit: found {found.Count} test objects from an earlier load: {string.Join(", ", found.OrderBy(f => f.place).Select(f => f.place + " " + f.nv.name.Replace("(Clone)", "")))}");
        }

        private static Vector2? _holdAt;
        // every frame while a slot is hovered: the virtual mouse stays on it
        internal static void Hold()
        {
            if (_hovered == null || !_holdAt.HasValue) return;
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null && m.position.ReadValue() != _holdAt.Value) UnityEngine.InputSystem.LowLevel.InputState.Change(m.position, _holdAt.Value);
        }
        private static void Unhover()
        {
            if (_hovered != null) { ExecuteEvents.Execute(_hovered, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler); UITooltip.HideTooltip(); }   // its tooltip goes too (it outlived the hover into the next shots)
            if (_holdAt.HasValue) { var m = UnityEngine.InputSystem.Mouse.current; if (m != null) UnityEngine.InputSystem.LowLevel.InputState.Change(m.position, new Vector2(4f, Screen.height / 2f)); }   // off the slot, or the next open hovers it for real
            _holdAt = null;
            _hovered = null;
        }

        private static void Tame(GameObject go)
        {
            var t = go.GetComponent<Tameable>(); if (t == null || t.IsTamed()) return;
            AccessTools.Method(typeof(Tameable), "Tame").Invoke(t, null);
        }

        private static T Near<T>(Player p, float range) where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsSortMode.None).Where(c => Vector3.Distance(c.transform.position, p.transform.position) < range)
                  .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();

        // the nearest networked object (creature, tree, rock, piece...) whose prefab name has <name>, within 40 m
        private static GameObject Named(Player p, string name)
        {
            name = name.ToLowerInvariant();
            var own = _spawned.FirstOrDefault(v => v != null && v.name.ToLowerInvariant().Contains(name));   // what the test spawned first (an Eikthyr altar is no Eikthyr)
            if (own != null) return own.gameObject;
            return Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                .Where(v => v != null && v.gameObject != p.gameObject && v.name.ToLowerInvariant().Contains(name) && Vector3.Distance(v.transform.position, p.transform.position) < 40f)
                .OrderBy(v => Vector3.Distance(v.transform.position, p.transform.position)).Select(v => v.gameObject).FirstOrDefault();
        }
    }

    [HarmonyPatch(typeof(Player), "FindHoverObject")]
    internal static class TestKitHover
    {
        private static void Postfix(Player __instance, ref GameObject hover, ref Character hoverCreature)
        {
            var h = TestKit.Hover; if (h == null || __instance != Player.m_localPlayer) return;
            var hv = h.GetComponentInChildren<Hoverable>() as Component; hover = hv != null ? hv.gameObject : h; hoverCreature = h.GetComponent<Character>();
        }
    }
}
