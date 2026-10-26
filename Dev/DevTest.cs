using System.Linq;
using UnityEngine;

namespace Jatify.UI
{
    // Dev tests for the extra rows, run on a throwaway character through shot.request:
    //   xr-state  log rows and where every item is
    //   xr-fill   put five stacks into the extra rows
    //   xr-nomod  pack as a save would, load the result into a vanilla-sized inventory, check every
    //             item is there and inside the vanilla rows, then unpack and check they are back
    //   xr-save   a real character save; items must be back in their slots afterwards
    //   xr-die    kill the character and check the grave holds every unequipped item
    internal static class DevTest
    {
        // Valheim's teleport stops at the first floor under the target height and drops you from there (a fall
        // from too high kills): the target is the world's own ground height, plus a little, never under water
        internal static Vector3 Ground(float x, float z) => new Vector3(x, Mathf.Max(WorldGenerator.instance.GetHeight(x, z), ZoneSystem.instance.m_waterLevel) + 1.5f, z);

        private static Vector3? _back;   // where the last tp- command left from (tp-back)

        internal static bool Run(string what)
        {
            if (what == "xr-respawn" && Game.instance != null) { Game.instance.RequestRespawn(1f, true); Log.Info("XR respawn requested"); return true; }
            var p = Player.m_localPlayer; if (p == null || !(what.StartsWith("xr-") || what.StartsWith("sr-") || what.StartsWith("inv-") || what.StartsWith("ms-") || what.StartsWith("tp-") || what.StartsWith("give:") || what == "layers")) return false;
            var inv = p.GetInventory();
            if (what.StartsWith("tp-at:"))   // tp-at:x,z
            {
                var xz = what.Substring(6).Split(','); if (xz.Length != 2 || !float.TryParse(xz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) || !float.TryParse(xz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) { Log.Info("tp-at: want tp-at:x,z"); return true; }
                _back = p.transform.position; p.TeleportTo(Ground(x, z), p.transform.rotation, true); Log.Info($"tp-at: {x:F0}, {z:F0}"); return true;
            }
            if (what.StartsWith("give:"))   // give:prefab[,prefab...] one of each into the bag
            {
                foreach (var n in what.Substring(5).Split(','))
                {
                    var go = ObjectDB.instance.m_items.FirstOrDefault(g => g != null && g.name.ToLowerInvariant() == n.Trim());
                    bool ok = go != null && p.GetInventory().AddItem(go.name, 1, 1, 0, p.GetPlayerID(), p.GetPlayerName(), false) != null;
                    Log.Info($"give {n}: {(ok ? "added" : go == null ? "no such item" : "no room")}");
                }
                return true;
            }
            if (what.StartsWith("ms-piece:")) { var pc = ZNetScene.instance?.GetPrefab(what.Substring(9))?.GetComponent<Piece>() ?? ZNetScene.instance?.m_prefabs.FirstOrDefault(g => g.name.ToLowerInvariant() == what.Substring(9))?.GetComponent<Piece>(); if (pc != null && p.InPlaceMode()) p.SetSelectedPiece(pc); Log.Info($"ms-piece (place mode {p.InPlaceMode()}): {(pc != null ? pc.name : "none")}"); return true; }
            switch (what)
            {
                case "xr-respawn":   // recovery: finish a death that vanilla did not (respawn request)
                    Game.instance.RequestRespawn(1f, true);
                    Log.Info("XR respawn requested");
                    return true;
                case "inv-equipdrag":   // pick up an unequipped weapon/tool, click Main hand, expect it equipped and the drag ended
                {
                    var g = InventoryGui.instance;
                    var it = inv.GetAllItems().Find(i => !p.IsItemEquiped(i) && (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon));
                    var slot = g.m_player.Find("jatify-frame/jatify-equipment/Doll/Main hand")?.GetComponent<Press>();
                    if (it == null || slot == null) { Log.Info($"EQDRAG: no item ({it == null}) or slot ({slot == null}). FAIL"); return true; }
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "SetupDragItem").Invoke(g, new object[] { it, inv, 1 });
                    slot.Fire();
                    var left = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_dragItem").GetValue(g);
                    Log.Info($"EQDRAG: {it.m_shared.m_name} equipped {p.IsItemEquiped(it)}, drag cleared {left == null}. " + (p.IsItemEquiped(it) && left == null ? "PASS" : "FAIL"));
                    return true;
                }
                case "ms-quit":   // the pause menu's quit confirm (screenshot it, then "ms-noquit")
                    Menu.instance?.Show(); Menu.instance?.m_root.Find("ExitConfirm")?.gameObject.SetActive(true);
                    return true;
                case "tp-back": if (_back.HasValue) { var b = _back.Value; _back = p.transform.position; p.TeleportTo(b, p.transform.rotation, true); } else Log.Info("tp-back: no earlier teleport this load"); return true;
                case "tp-grave": case "tp-grave-on":   // beside where you last died (the spot is real ground: no height guess); -on: right on it (PickupOnTouch tests)
                {
                    var prof = Game.instance?.GetPlayerProfile(); if (prof == null || !prof.HaveDeathPoint()) { Log.Info("tp-grave: no death point"); return true; }
                    _back = p.transform.position; p.TeleportTo(prof.GetDeathPoint() + (what == "tp-grave-on" ? new Vector3(0.6f, 1f, 0f) : new Vector3(2f, 1f, 0f)), p.transform.rotation, true);
                    Log.Info($"tp-grave: {prof.GetDeathPoint():F0}"); return true;
                }
                case "tp-crypt":   // the nearest Black Forest burial chamber (copper / tin around), 40 m off; ore and dungeon pins on
                {
                    var zs = ZoneSystem.instance; if (zs == null) return true;
                    var me = p.transform.position;
                    var near = zs.GetLocationList().Where(l => l.m_location?.m_prefabName != null && l.m_location.m_prefabName.StartsWith("Crypt"))
                                 .OrderBy(l => Utils.DistanceXZ(l.m_position, me)).FirstOrDefault();
                    if (near.m_location == null) { Log.Info("tp-crypt: no burial chamber known (on a server the client may not have the list)"); return true; }
                    OreScout.On.Value = true; OreScout.Dungeons.Value = true;
                    var to = Ground(near.m_position.x + 40f, near.m_position.z);
                    _back = me; p.TeleportTo(to, p.transform.rotation, true);
                    Log.Info($"tp-crypt: {near.m_location.m_prefabName} at {near.m_position:F0}, {Utils.DistanceXZ(near.m_position, me):F0} m away");
                    return true;
                }
                case "ms-unlocks":   // a burst of 6 unlock notices (Grouped: two cards and "+4 more")
                    foreach (var go in ObjectDB.instance.m_items.Where(g => g != null && g.GetComponent<ItemDrop>() != null).Take(6)) { var d = go.GetComponent<ItemDrop>().m_itemData; MessageHud.instance.QueueUnlockMsg(d.GetIcon(), "$msg_newrecipe", d.m_shared.m_name); }
                    return true;
                case "ms-recipes":   // every recipe and every piece known, quietly (no unlock pop-ups); throwaway characters only
                {
                    var known = HarmonyLib.AccessTools.FieldRefAccess<Player, System.Collections.Generic.HashSet<string>>("m_knownRecipes")(p); int n0 = known.Count;
                    foreach (var r in ObjectDB.instance.m_recipes) if (r != null && r.m_item != null && r.m_enabled) known.Add(r.m_item.m_itemData.m_shared.m_name);
                    foreach (var go in ObjectDB.instance.m_items)
                    {
                        var tbl = go != null ? go.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null; if (tbl == null) continue;
                        foreach (var pg in tbl.m_pieces) { var pc = pg != null ? pg.GetComponent<Piece>() : null; if (pc != null && pc.m_enabled) known.Add(pc.m_name); }
                    }
                    HarmonyLib.AccessTools.Method(typeof(Player), "UpdateKnownRecipesList").Invoke(p, null);
                    Log.Info($"ms-recipes: {known.Count - n0} new, {known.Count} known"); return true;
                }
                case "ms-build":   // hammer in hand and the build menu open (screenshot it); again: closes it
                {
                    var hm = inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_buildPieces != null);
                    if (hm != null && !p.IsItemEquiped(hm)) p.EquipItem(hm);
                    Hud.instance?.TogglePieceSelection(); return true;
                }
                case "ms-hammer":   // hammer in hand (screenshot the build HUD); again: put it away
                {
                    var hm = inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_buildPieces != null);
                    if (hm == null) { Log.Info("ms-hammer: no hammer"); return true; }
                    if (p.IsItemEquiped(hm)) p.UnequipItem(hm); else p.EquipItem(hm);
                    return true;
                }
 Menu.instance?.Show(); Menu.instance?.OnCurrentPlayers(); return true;   // the Players page (screenshot it)
                case "ms-toast": ActionToast.Show("Ate Cooked meat \u00b7 4 left"); return true;
                case "ms-lift":   // hover "Settings" in the pause menu (screenshot it: it slides forward)
                {
                    Menu.instance?.Show(); var e = Menu.instance?.m_root.Find("Menu/MenuEntries/Settings");
                    if (e != null) UnityEngine.EventSystems.ExecuteEvents.Execute(e.gameObject, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                    return true;
                }
                case "ms-fold": Cfg.Tracker.Value = true; Cfg.TrackerCollapsed.Value = !Cfg.TrackerCollapsed.Value; return true;   // as Y does (tracker on: the old Y switched it off)
                case "ms-noquit": Menu.instance?.m_root.Find("ExitConfirm")?.gameObject.SetActive(false); return true;
                case "ms-kit":   // test items for the user: armour, cape, belt, wishbone, wisplight, keys, a quality-3 weapon, food, meads
                {
                    foreach (var (n, c, q) in new[] { ("HelmetBronze", 1, 2), ("ArmorBronzeChest", 1, 1), ("ArmorBronzeLegs", 1, 1), ("CapeDeerHide", 1, 1), ("BeltStrength", 1, 1),
                        ("Wishbone", 1, 1), ("Demister", 1, 1), ("CryptKey", 1, 1), ("SwordBronze", 1, 3), ("ShieldBronzeBuckler", 1, 2), ("KnifeFlint", 1, 4), ("Bow", 1, 1), ("ArrowFlint", 40, 1),
                        ("CookedMeat", 5, 1), ("QueensJam", 5, 1), ("MeadHealthMinor", 3, 1), ("MeadStaminaMinor", 3, 1), ("Torch", 1, 1), ("PickaxeAntler", 1, 1) })
                        inv.AddItem(n, c, q, 0, 0L, "", false);
                    Log.Info("MS kit: given");
                    return true;
                }
                case "ms-dragout":   // pick the pouch key up like a player, then drop it on an empty bag cell through vanilla's own click handler
                {
                    var g = InventoryGui.instance; var store = ModSlots.For(p);
                    var key = store.GetAllItems().FirstOrDefault(i => i.m_gridPos.y >= ModSlots.KeyRow);
                    var cell = g.m_player.Find("jatify-frame/jatify-equipment/Doll/KeyPouch/Key")?.GetComponent<Press>();
                    if (key == null || cell == null) { Log.Info($"MS dragout: no key ({key == null}) or cell ({cell == null}). FAIL"); return true; }
                    key.m_gridPos = new Vector2i(0, ModSlots.KeyRow);   // the first pouch cell
                    cell.Fire();
                    bool picked = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_dragInventory").GetValue(g) == store;
                    var free = Enumerable.Range(0, inv.GetWidth() * 4).Select(k => new Vector2i(k % inv.GetWidth(), k / inv.GetWidth())).First(v => inv.GetItemAt(v.x, v.y) == null);
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "OnSelectedItem").Invoke(g, new object[] { g.m_playerGrid, null, free, InventoryGrid.Modifier.Select });
                    bool inBag = inv.GetItemAt(free.x, free.y)?.m_shared.m_name == key.m_shared.m_name, gone = !store.ContainsItem(key);
                    Log.Info($"MS dragout: picked up from pouch {picked}, in bag at {free.x},{free.y} {inBag}, left the pouch {gone}. " + (picked && inBag && gone ? "PASS" : "FAIL"));
                    return true;
                }
                case "inv-eqdragoff":   // press the worn helmet (lift it), drop it on a free bag cell: it must come off and sit there; then back on
                {
                    var g = InventoryGui.instance; if (!InventoryGui.IsVisible()) g.Show(null);
                    var helm = p.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet && p.IsItemEquiped(i));
                    var slot = g.m_player.Find("jatify-frame/jatify-equipment/Doll/Head")?.GetComponent<Press>();
                    if (helm == null || slot == null) { Log.Info($"EQ dragoff: helmet worn {helm != null}, slot {slot != null}. FAIL"); return true; }
                    slot.Fire();
                    var free = Enumerable.Range(0, inv.GetWidth() * SpecialRows.Start).Select(k => new Vector2i(k % inv.GetWidth(), k / inv.GetWidth())).Last(v => inv.GetItemAt(v.x, v.y) == null && !InvActions.Locked(v.x, v.y));
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "OnSelectedItem").Invoke(g, new object[] { g.m_playerGrid, null, free, InventoryGrid.Modifier.Select });
                    var at = inv.GetItemAt(free.x, free.y);   // a move inside the bag leaves a copy there, not the same object
                    bool there = at != null && at.m_shared == helm.m_shared, off = there && !p.IsItemEquiped(at);
                    if (there) p.EquipItem(at, false);
                    Log.Info($"EQ dragoff: came off {off}, in the cell dropped on {there}. " + (off && there ? "PASS" : "FAIL"));
                    return true;
                }
                case "inv-srvauto":   // a smelter and a chest of ours 3 km away (nobody near), the smelter 10 min behind: one server pass
                {
                    if (!ZNet.instance.IsServer()) { Log.Info("SRVAUTO: needs the host's game. FAIL"); return true; }
                    var at = p.transform.position + new Vector3(3000f, 0f, 0f); long me = p.GetPlayerID();
                    var sz = ZDOMan.instance.CreateNewZDO(at, "smelter".GetStableHashCode()); var cz = ZDOMan.instance.CreateNewZDO(at + new Vector3(3f, 0f, 0f), "piece_chest_wood".GetStableHashCode());
                    sz.SetPrefab("smelter".GetStableHashCode()); cz.SetPrefab("piece_chest_wood".GetStableHashCode());
                    var box = ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Container>(); var cinv = new Inventory("t", null, box.m_width, box.m_height);
                    cinv.AddItem("Coal", 30, 1, 0, 0L, "", false); cinv.AddItem("CopperOre", 6, 1, 0, 0L, "", false);
                    var pk = new ZPackage(); cinv.Save(pk); cz.Set(ZDOVars.s_items, pk.GetArray()); cz.Set(ZDOVars.s_creator, me);
                    sz.Set(ServerAuto.SrvKey, me + "|Coal,CopperOre,TinOre"); sz.Set(Smelters.AutoKey, Smelters.ForcedOn); sz.Set(Smelters.ByKey, me);
                    var now = ZNet.instance.GetTime(); sz.Set(ZDOVars.s_startTime, now.AddSeconds(-600).Ticks);
                    sz.SetOwner(0L); cz.SetOwner(0L);
                    ServerAuto.RunOne(sz, now);
                    var held = ServerAuto.Held(sz); held.TryGetValue("CopperOre", out int bars);
                    var after = new Inventory("t", null, box.m_width, box.m_height); after.Load(new ZPackage(cz.GetByteArray(ZDOVars.s_items)));
                    int coal = after.CountItems("$item_coal"), ore = after.CountItems("$item_copperore");
                    float fuel = sz.GetFloat(ZDOVars.s_fuel); int queued = sz.GetInt(ZDOVars.s_queued);
                    var sm = ZNetScene.instance.GetPrefab("smelter").GetComponent<Smelter>();
                    Log.Info($"SRVAUTO: smelter {sm.m_secPerProduct}s per bar, {sm.m_fuelPerProduct} coal each, max fuel {sm.m_maxFuel}; after 600 s: {bars} copper held, queue {queued}, fuel {fuel:F1}; chest coal 30->{coal}, ore 6->{ore}. "
                        + (bars == 6 && ore == 0 && queued == 0 && coal + Mathf.RoundToInt(fuel) + bars * sm.m_fuelPerProduct == 30 ? "PASS" : "FAIL"));
                    // a fermenter whose batch finished, two bases in the chest: tapped onto the station, the next one started
                    var fe = ZNetScene.instance.GetPrefab("fermenter").GetComponent<Fermenter>(); var cv = fe.m_conversion[0]; string b = cv.m_from.gameObject.name;
                    var fz = ZDOMan.instance.CreateNewZDO(at + new Vector3(0f, 0f, 3f), "fermenter".GetStableHashCode()); fz.SetPrefab("fermenter".GetStableHashCode());
                    var finv = new Inventory("t", null, box.m_width, box.m_height); finv.AddItem(b, 2, 1, 0, 0L, "", false); var fp = new ZPackage(); finv.Save(fp); cz.Set(ZDOVars.s_items, fp.GetArray());
                    fz.Set(ServerAuto.SrvKey, me + "|" + b); fz.Set(ZDOVars.s_content, b.GetStableHashCode()); fz.Set(ZDOVars.s_startTime, now.AddSeconds(-fe.m_fermentationDuration - 5).Ticks); fz.SetOwner(0L);
                    ServerAuto.RunOne(fz, now);
                    ServerAuto.Held(fz).TryGetValue(b, out int batches); var fafter = new Inventory("t", null, box.m_width, box.m_height); fafter.Load(new ZPackage(cz.GetByteArray(ZDOVars.s_items)));
                    bool restarted = fz.GetInt(ZDOVars.s_content) == b.GetStableHashCode() && fz.GetLong(ZDOVars.s_startTime) == now.Ticks;
                    Log.Info($"SRVAUTO fermenter: {b} batch done -> {batches} held, next started {restarted}, chest bases 2->{fafter.CountItems(cv.m_from.m_itemData.m_shared.m_name)}. " + (batches == 1 && restarted && fafter.CountItems(cv.m_from.m_itemData.m_shared.m_name) == 1 ? "PASS" : "FAIL"));
                    foreach (var z in new[] { sz, cz, fz }) { z.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(z); }
                    return true;
                }
                case "inv-woodchest":   // a wooden chest of ours 2 m in front, every cell a full stack of wood
                {
                    var go = Object.Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), p.transform.position + p.transform.forward * 2f, Quaternion.LookRotation(-p.transform.forward));
                    go.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, p.GetPlayerID());
                    HarmonyLib.AccessTools.TypeByName("AzuCraftyBoxes.API")?.GetMethod("AddContainer")?.Invoke(null, new object[] { go.GetComponent<Container>() });   // a craft-from-chests mod's own registration, where there is one
                    var ci = go.GetComponent<Container>().GetInventory(); int n = 0;
                    while (ci.AddItem("Wood", 50, 1, 0, 0L, "", false) != null && ++n < 100) { }
                    Log.Info($"WOODCHEST: {n} stacks of wood");
                    return true;
                }
                case "inv-srvlist":   // what the server sees: stations carrying a copy, loaded or not, their queue, fuel and held output
                {
                    var all = HarmonyLib.AccessTools.FieldRefAccess<ZDOMan, System.Collections.Generic.Dictionary<ZDOID, ZDO>>("m_objectsByID")(ZDOMan.instance).Values;
                    var kinds = AutomationPage.Kinds();
                    foreach (var z in all)
                    {
                        if (z == null || kinds == null || !kinds.TryGetValue(z.GetPrefab(), out var name)) continue;
                        if (z.GetInt(Smelters.AutoKey, 0) == 0 && z.GetString(ServerAuto.SrvKey, "").Length == 0) continue;
                        Log.Info($"SRVLIST: {name} at {z.GetPosition():F0} ({Vector3.Distance(z.GetPosition(), p.transform.position):F0} m) auto {z.GetInt(Smelters.AutoKey, 0)} by {z.GetLong(Smelters.ByKey, 0L)} owner {z.GetOwner()} copy '{z.GetString(ServerAuto.SrvKey, "")}' queue {z.GetInt(ZDOVars.s_queued)} fuel {z.GetFloat(ZDOVars.s_fuel):F1} held '{z.GetString(ServerAuto.HeldKey, "")}' start {new System.DateTime(z.GetLong(ZDOVars.s_startTime, 0L)):HH:mm:ss} now {ZNet.instance.GetTime():HH:mm:ss}");
                    }
                    Log.Info($"SRVLIST: me {p.GetPlayerID()} session {ZDOMan.GetSessionID()} server {ZNet.instance.IsServer()} smelters on {Smelters.On.Value}");
                    return true;
                }
                case "inv-srvskip":   // 30 minutes pass for every unloaded station carrying a copy: backdate it, then one server pass
                {
                    var all = HarmonyLib.AccessTools.FieldRefAccess<ZDOMan, System.Collections.Generic.Dictionary<ZDOID, ZDO>>("m_objectsByID")(ZDOMan.instance).Values.Where(z => z != null && z.GetString(ServerAuto.SrvKey, "").Length > 0).ToList();
                    var now = ZNet.instance.GetTime(); int ran = 0;
                    foreach (var z in all)
                    {
                        if (ServerAuto.Loaded(z)) { Log.Info($"SRVSKIP: {z.GetPosition():F0} still loaded (owner {z.GetOwner()}), skipped"); continue; }
                        long start = z.GetLong(ZDOVars.s_startTime, now.Ticks); z.Set(ZDOVars.s_startTime, System.Math.Min(start, now.Ticks) - System.TimeSpan.FromMinutes(30).Ticks);
                        if (z.GetInt(ZDOVars.s_content) != 0) z.Set(ZDOVars.s_startTime, z.GetLong(ZDOVars.s_startTime) - System.TimeSpan.FromHours(1).Ticks);   // a fermenter's batch takes 40 min
                        ServerAuto.RunOne(z, now); ran++;
                        Log.Info($"SRVSKIP: {z.GetPosition():F0} queue {z.GetInt(ZDOVars.s_queued)} fuel {z.GetFloat(ZDOVars.s_fuel):F1} held '{z.GetString(ServerAuto.HeldKey, "")}'");
                    }
                    Log.Info($"SRVSKIP: {ran} station(s) moved 30 minutes on");
                    return true;
                }
                case "inv-armour":   // where every armour piece is, and what the player wears
                {
                    var worn = HarmonyLib.AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem")(p);
                    Log.Info("ARMOUR: " + string.Join("; ", inv.GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder).Select(i => $"{i.m_shared.m_name}@{i.m_gridPos} eq {i.m_equipped}")) + $"; helmet slot {worn?.m_shared.m_name ?? "empty"} in bag {(worn != null && inv.ContainsItem(worn))}");
                    return true;
                }
                case "inv-eqrow":   // worn armour in the hidden row; off -> a bag cell; on -> back
                {
                    EqRow.Park(p);
                    int er = SpecialRows.EqRow;
                    var worn = inv.GetAllItems().Where(i => i.m_gridPos.y == er).Select(i => $"{i.m_shared.m_name}@{i.m_gridPos.x}").ToList();
                    var helm = inv.GetAllItems().FirstOrDefault(i => i.m_gridPos.y == er && i.m_gridPos.x == 0);
                    if (helm == null) { Log.Info($"EQROW: worn {string.Join(", ", worn)}; no helmet worn. FAIL"); return true; }
                    p.UnequipItem(helm, false); var off = helm.m_gridPos;
                    p.EquipItem(helm, false); var on = helm.m_gridPos;
                    bool ok = off.y >= 0 && off.y < SpecialRows.Start && on.x == 0 && on.y == er && p.IsItemEquiped(helm);
                    Log.Info($"EQROW: worn {string.Join(", ", worn)}; helmet off -> {off.x},{off.y}, on -> {on.x},{on.y}. " + (ok ? "PASS" : "FAIL"));
                    return true;
                }
                case "inv-lantern": case "inv-lantern-eq": case "inv-lantern-data": case "inv-lantern-eq-data":   // Hip Lantern's refuel (lantern + surtling core -> lantern) through vanilla's DoCrafting, as the Craft button does; -eq: lantern worn; -data: it carries saved data, as a used one does (with Epic Loot, Hip Lantern then drops the new one and picks it up again) (bug report 2026-10-26: it vanished)
                {
                    var rec = ObjectDB.instance.m_recipes.FirstOrDefault(r => r.m_item != null && r.m_item.name == "HipLantern" && r.m_resources.Any(q => q.m_resItem != null && q.m_resItem.name == "HipLantern"));
                    if (rec == null) { Log.Info("LANTERN: Hip Lantern not installed or no refuel recipe. SKIP"); return true; }
                    System.Func<string> lanterns = () => string.Join("; ", inv.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "HipLantern").Select(i => $"@{i.m_gridPos.x},{i.m_gridPos.y} fuel {i.m_durability:0}/{i.GetMaxDurability():0} worn {p.IsItemEquiped(i)} data {i.m_customData.Count}"));
                    System.Func<int> count = () => inv.GetAllItems().Count(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "HipLantern");
                    foreach (var old in inv.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "HipLantern").ToList()) { p.UnequipItem(old, false); inv.RemoveItem(old); }   // exactly one lantern: the bug is about which one the craft takes
                    var lamp = inv.AddItem("HipLantern", 1, 1, 0, p.GetPlayerID(), p.GetPlayerName(), new Vector2i(-1, -1), false);
                    inv.AddItem("SurtlingCore", 2, 1, 0, 0L, "", new Vector2i(-1, -1), false);
                    if (lamp == null) { Log.Info("LANTERN: no room for a lantern. SKIP"); return true; }
                    lamp.m_durability = lamp.GetMaxDurability() * 0.25f;
                    if (what.EndsWith("-data")) lamp.m_customData["jatify.test"] = "used";
                    if (what.StartsWith("inv-lantern-eq")) p.EquipItem(lamp);
                    var nearBefore = Physics.OverlapSphere(p.transform.position, 6f).Select(c => c.GetComponentInParent<ItemDrop>()).Where(d => d != null && d.name.StartsWith("HipLantern")).Distinct().Count();
                    int before = count(), cores = inv.CountItems("$item_surtlingcore");
                    Log.Info($"LANTERN {what}: before [{lanterns()}] cores {cores}");
                    var gui = InventoryGui.instance; if (!InventoryGui.IsVisible()) gui.Show(null);
                    var tr = HarmonyLib.Traverse.Create(gui);
                    tr.Field("m_craftRecipe").SetValue(rec); tr.Field("m_craftUpgradeItem").SetValue(null); tr.Field("m_multiCrafting").SetValue(false); tr.Field("m_craftVariant").SetValue(0);
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "DoCrafting").Invoke(gui, new object[] { p });
                    int after = count(), coresAfter = inv.CountItems("$item_surtlingcore");
                    bool full = inv.GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "HipLantern" && i.m_durability >= i.GetMaxDurability() - 0.5f);
                    var near = Physics.OverlapSphere(p.transform.position, 6f).Select(c => c.GetComponentInParent<ItemDrop>()).Where(d => d != null && d.name.StartsWith("HipLantern")).Distinct().Count() - nearBefore;
                    Log.Info($"LANTERN {what}: after [{lanterns()}] cores {coresAfter}, lanterns dropped near you {near}. " + (after == before && full && coresAfter == cores - 1 ? "PASS" : $"FAIL (lanterns {before} -> {after}, a full one {full}, cores {cores} -> {coresAfter})"));
                    return true;
                }
                case "inv-pay:chest": case "inv-pay:split": case "inv-pay:upgrade": case "inv-pay:queue": case "inv-pay:hands":
                case "inv-pay:chest-ui": case "inv-pay:split-ui": case "inv-pay:upgrade-ui": case "inv-pay:queue-ui": case "inv-pay:hands-ui":
                {   // a craft is paid exactly once, whoever pays (us, or another craft-from-chests mod). Spawns its own chest (yours) and a
                    // workbench it opens like a player (interact), then crafts 1.5 s later. chest: materials only in the chest; split: one
                    // of each in the bag, the rest in the chest; upgrade: quality 1 -> 2 (workbench + chopping block); queue: three in a
                    // row; hands: inventory crafting, no station (Club)
                    string mode = what.Substring(8); bool ui = mode.EndsWith("-ui"); if (ui) mode = mode.Substring(0, mode.Length - 3);   // -ui: through the crafting list and the Craft button (queue: Jatify's Queue), with the craft timer
                    var spawned = new System.Collections.Generic.List<GameObject>();
                    var cgo = Object.Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), p.transform.position + p.transform.forward * 2f, Quaternion.LookRotation(-p.transform.forward)); spawned.Add(cgo);
                    cgo.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, p.GetPlayerID());
                    var chest = cgo.GetComponent<Container>().GetInventory();
                    HarmonyLib.AccessTools.TypeByName("AzuCraftyBoxes.API")?.GetMethod("AddContainer")?.Invoke(null, new object[] { cgo.GetComponent<Container>() });   // a craft-from-chests mod's own registration, where there is one (it skips chests without an owner on spawn)
                    Recipe rec; ItemDrop.ItemData up = null; int q = 1;
                    CraftingStation bench = null;
                    if (mode == "hands") rec = ObjectDB.instance.m_recipes.FirstOrDefault(r => r.name == "Recipe_Club");
                    else
                    {   // a workbench recipe (level 1, quality 2 costs something, so the same one serves the upgrade)
                        if (mode == "upgrade") q = 2;
                        rec = ObjectDB.instance.m_recipes.FirstOrDefault(r => r.m_item != null && r.m_craftingStation != null && r.m_craftingStation.m_name == "$piece_workbench" && r.m_minStationLevel <= 1 && r.m_item.m_itemData.m_shared.m_maxQuality > 1 && r.m_resources.Any(x => !x.m_upgraderResource && x.GetAmount(2) > 0) && r.m_resources.All(x => x.m_resItem != null));
                        var wb = Object.Instantiate(ZNetScene.instance.GetPrefab("piece_workbench"), p.transform.position + p.transform.right * 1.5f, Quaternion.LookRotation(-p.transform.right)); spawned.Add(wb);
                        wb.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, p.GetPlayerID());
                        bench = wb.GetComponent<CraftingStation>();
                        bench.m_craftRequireRoof = false;   // this test bench only: a roof has nothing to do with paying (a bench opens within 2 m, under a roof)
                        var ext = q == 2 ? ZNetScene.instance.GetPrefab("piece_workbench_ext1") : null;   // chopping block: workbench level 2, what quality 2 needs
                        if (ext != null) spawned.Add(Object.Instantiate(ext, wb.transform.position + p.transform.right * 1.5f, Quaternion.identity));
                    }
                    if (rec == null) { Log.Info("PAY: no recipe to test. SKIP"); return true; }
                    var mats = rec.m_resources.Where(r => r.m_resItem != null && !r.m_upgraderResource && r.GetAmount(q) > 0).ToList();   // upgrader items are only spent at the upgrade station (Valheim 1.0)
                    string itemName = rec.m_item.m_itemData.m_shared.m_name;
                    // set aside what you carry of these (given back afterwards), so the counts start from zero
                    var setAside = inv.GetAllItems().Where(i => i.m_shared.m_name == itemName || mats.Any(m => m.m_resItem.m_itemData.m_shared.m_name == i.m_shared.m_name)).ToList();
                    var wasWorn = setAside.Where(i => p.IsItemEquiped(i)).ToList();
                    foreach (var w in setAside) { p.UnequipItem(w, false); inv.RemoveItem(w); }
                    System.Action restore = () => {   // the test leaves nothing behind: its crafts and leftovers go, your things come back
                        foreach (var x in inv.GetAllItems().Where(i => i.m_shared.m_name == itemName || mats.Any(m => m.m_resItem.m_itemData.m_shared.m_name == i.m_shared.m_name)).ToList()) { p.UnequipItem(x, false); inv.RemoveItem(x); }
                        foreach (var w in setAside) { if (!inv.AddItem(w)) ItemDrop.DropItem(w, w.m_stack, p.transform.position + Vector3.up, Quaternion.identity); else if (wasWorn.Contains(w)) p.EquipItem(w, false); }
                    };
                    foreach (var m in mats)
                    {
                        int need = m.GetAmount(q) * 3; string pf = m.m_resItem.name;
                        if (mode == "split") { inv.AddItem(pf, 1, 1, 0, 0L, "", new Vector2i(-1, -1), false); need -= 1; }
                        chest.AddItem(pf, need + 5, 1, 0, 0L, "", false);
                    }
                    if (mode == "upgrade") up = inv.AddItem(rec.m_item.name, 1, 1, 0, p.GetPlayerID(), p.GetPlayerName(), new Vector2i(-1, -1), false);
                    Later(1.5f, () => {   // another craft-from-chests mod sees a new chest on its next refresh
                    System.Func<string> counts = () => string.Join(", ", mats.Select(m => $"{m.m_resItem.m_itemData.m_shared.m_name} bag {inv.CountItems(m.m_resItem.m_itemData.m_shared.m_name)} chest {chest.CountItems(m.m_resItem.m_itemData.m_shared.m_name)}"));
                    System.Func<int> total = () => mats.Sum(m => inv.CountItems(m.m_resItem.m_itemData.m_shared.m_name) + chest.CountItems(m.m_resItem.m_itemData.m_shared.m_name));
                    System.Func<int> made = () => inv.GetAllItems().Count(i => i.m_shared.m_name == itemName);
                    int t0 = total(), k0 = made(), runs = mode == "queue" ? 3 : 1, cost = mats.Sum(m => m.GetAmount(q));
                    Log.Info($"PAY {mode} {rec.name} q{q} (other chest mod {Stock.Other}, free build {p.NoCostCheat()}): before {counts()}");
                    var gui = InventoryGui.instance;
                    if (bench != null) { if (InventoryGui.IsVisible()) gui.Hide(); bench.Interact(p, false, false); }   // open the workbench like a player
                    else { if (InventoryGui.IsVisible()) gui.Hide(); gui.Show(null, 3); }   // the crafting tab, no station
                    var tr = HarmonyLib.Traverse.Create(gui); var craft = HarmonyLib.AccessTools.Method(typeof(InventoryGui), "DoCrafting");
                    Log.Info($"PAY {mode}: crafting at {(p.GetCurrentCraftingStation() ? p.GetCurrentCraftingStation().m_name + " level " + p.GetCurrentCraftingStation().GetLevel() : "hands (inventory)")}");
                    System.Action check = () => {
                    int paid = t0 - total(), got = made() - k0;
                    int ql = up != null ? (inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == itemName)?.m_quality ?? 0) : 0;
                    bool ok = up != null ? ql == 2 && paid == cost && got == 0 : got == runs && paid == cost * runs;
                    string why = "";
                    if (!ok) { var st0 = p.GetCurrentCraftingStation(); why = $" [requirements met {p.HaveRequirements(rec, false, q)}, station {(st0 ? st0.m_name : "none")}, craft tab {gui.InCraftTab()}, upgrade tab {gui.InUpradeTab()}, queue {CraftQueue.Items.Count}, queue on {CraftQueue.On.Value}, craft timer {HarmonyLib.AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer")(gui):0.0}, bag has room {inv.CanAddItem(rec.m_item.gameObject, 1)}]"; }
                    if (up != null && ql != 2) { var st = p.GetCurrentCraftingStation(); why = $" [station {(st ? st.m_name : "none")} level {(st ? st.GetLevel() : 0)}, upgrader station {(st ? st.m_upgrader : false)}, requirements met {p.HaveRequirements(rec, false, 2)}, max quality {rec.m_item.m_itemData.m_shared.m_maxQuality}]"; }
                    Log.Info($"PAY {mode}{(ui ? "-ui" : "")} {rec.name}: after {counts()}; paid {paid} (expected {cost * runs}), made {got}" + (up != null ? $", quality {ql}" : "") + ". " + (ok ? "PASS" : "FAIL") + why);
                    foreach (var go in spawned) ZNetScene.instance.Destroy(go);
                    restore();
                    };
                    if (!ui)
                    {
                        for (int r = 0; r < runs; r++)
                        {
                            tr.Field("m_craftRecipe").SetValue(rec); tr.Field("m_craftUpgradeItem").SetValue(up); tr.Field("m_multiCrafting").SetValue(false); tr.Field("m_craftVariant").SetValue(0);
                            craft.Invoke(gui, new object[] { p });
                        }
                        check(); return;
                    }
                    // the player's way: pick it in the list, press Craft (or Queue x3), let the craft timer run
                    if (up != null) gui.OnTabUpgradePressed(); else gui.OnTabCraftPressed();   // upgrades live on the Upgrade tab, everything else on Craft
                    Later(0.5f, () => {   // the list refills on the next frame
                    var list = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_availableRecipes").GetValue(gui) as System.Collections.IList;
                    int index = -1;
                    for (int i = 0; list != null && i < list.Count; i++)
                    {
                        var t = HarmonyLib.Traverse.Create(list[i]);
                        if (t.Property<Recipe>("Recipe").Value == rec && t.Property<ItemDrop.ItemData>("ItemData").Value == up) { index = i; break; }
                    }
                    if (index < 0) { Log.Info($"PAY {mode}-ui {rec.name}: not in the crafting list ({list?.Count ?? 0} entries). FAIL"); check(); return; }
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "SetRecipe").Invoke(gui, new object[] { index, true });
                    gui.m_multiCraftAmount = 1;   // one at a time (the detail card's stepper may hold another amount)
                    if (mode == "queue") CraftQueue.Add(rec, 3);
                    else HarmonyLib.AccessTools.Method(typeof(InventoryGui), "OnCraftPressed").Invoke(gui, null);
                    Later(mode == "queue" ? 9f : 4f, check);   // the craft timer (~2 s each; the queue crafts one after another)
                    });
                    });
                    return true;
                }
                case "inv-smeltkit":   // a smelter 2.5 m to the right and a chest of ours in front: 50 copper ore, 50 coal (Shift + add pulls from chests with some mods)
                {
                    var sm = Object.Instantiate(ZNetScene.instance.GetPrefab("smelter"), p.transform.position + p.transform.right * 2.5f, Quaternion.LookRotation(-p.transform.right));
                    sm.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, p.GetPlayerID());
                    var go = Object.Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), p.transform.position + p.transform.forward * 2f, Quaternion.LookRotation(-p.transform.forward));
                    go.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, p.GetPlayerID());
                    HarmonyLib.AccessTools.TypeByName("AzuCraftyBoxes.API")?.GetMethod("AddContainer")?.Invoke(null, new object[] { go.GetComponent<Container>() });
                    var ci = go.GetComponent<Container>().GetInventory();
                    ci.AddItem("CopperOre", 50, 1, 0, 0L, "", false); ci.AddItem("Coal", 50, 1, 0, 0L, "", false);
                    Log.Info($"SMELTKIT: smelter + chest with {ci.CountItems("$item_copperore")} copper ore, {ci.CountItems("$item_coal")} coal; bag has {inv.CountItems("$item_copperore")} ore, {inv.CountItems("$item_coal")} coal");
                    return true;
                }
                case "inv-smeltcount":   // what the smelter holds and what the chests near you still have
                {
                    var sm = Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                    var nv = sm != null ? sm.GetComponent<ZNetView>() : null;
                    int ore = nv != null ? nv.GetZDO().GetInt(ZDOVars.s_queued) : -1; float fuel = nv != null ? nv.GetZDO().GetFloat(ZDOVars.s_fuel) : -1;
                    int chestOre = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c => c.GetInventory() != null && Vector3.Distance(c.transform.position, p.transform.position) < 20f).Sum(c => c.GetInventory().CountItems("$item_copperore"));
                    int chestCoal = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c => c.GetInventory() != null && Vector3.Distance(c.transform.position, p.transform.position) < 20f).Sum(c => c.GetInventory().CountItems("$item_coal"));
                    Log.Info($"SMELTCOUNT: smelter ore queued {ore}, fuel {fuel:0.#}; chests near you: {chestOre} ore, {chestCoal} coal; bag {inv.CountItems("$item_copperore")} ore, {inv.CountItems("$item_coal")} coal");
                    return true;
                }
                case "inv-pay-tidy":   // what the payment tests left in the bag before they cleaned up after themselves
                {
                    var testMade = new[] { "Club", "ArmorRagsChest", "LeatherScraps" };   // prefab names
                    var left = inv.GetAllItems().Where(i => i.m_dropPrefab != null && testMade.Contains(i.m_dropPrefab.name)).ToList();
                    foreach (var x in left) { p.UnequipItem(x, false); inv.RemoveItem(x); }
                    Log.Info($"PAY tidy: removed {left.Count} stack(s): " + string.Join(", ", left.GroupBy(i => i.m_shared.m_name).Select(g => $"{g.Key} x{g.Sum(i => i.m_stack)}")));
                    return true;
                }
                case "inv-canadd":   // room check: what Valheim's own count says vs ours
                {
                    var wood = ObjectDB.instance.GetItemPrefab("Wood"); var club = ObjectDB.instance.GetItemPrefab("Club");
                    int all = inv.GetWidth() * inv.GetHeight() - inv.GetAllItems().Count;
                    Log.Info($"CANADD: empty cells counting reserved {all}, normal empty {inv.GetEmptySlots()}; club fits {inv.CanAddItem(club, 1)}; 1000 wood fits {inv.CanAddItem(wood, 1000)}");
                    return true;
                }
                case "ms-keyslot":   // a key in the pouch; drag from the key slot -> in hand; drop on a free bag cell
                {
                    var g = InventoryGui.instance; var store = ModSlots.For(p);
                    var key = inv.GetAllItems().FirstOrDefault(ModSlots.IsKey) ?? store.GetAllItems().FirstOrDefault(i => i.m_gridPos.y >= ModSlots.KeyRow);
                    if (key == null) { Log.Info("MS keyslot: no key. FAIL"); return true; }
                    if (inv.ContainsItem(key)) ModSlots.Put(p, key, 0, ModSlots.KeyRow);
                    var slot = g.m_player.Find("jatify-frame/jatify-equipment/Doll/Keys")?.GetComponent<Press>();
                    slot.DragStart();
                    bool inHand = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_dragItem").GetValue(g) == key;
                    var free = Enumerable.Range(0, inv.GetWidth() * SpecialRows.Start).Select(k => new Vector2i(k % inv.GetWidth(), k / inv.GetWidth())).FirstOrDefault(v => inv.GetItemAt(v.x, v.y) == null && !InvActions.Locked(v.x, v.y));
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "OnSelectedItem").Invoke(g, new object[] { g.m_playerGrid, null, free, InventoryGrid.Modifier.Select });
                    bool inBag = inv.ContainsItem(key) || inv.GetItemAt(free.x, free.y)?.m_shared.m_name == key.m_shared.m_name;
                    Log.Info($"MS keyslot: in hand after drag {inHand}, dropped in bag at {free.x},{free.y} {inBag}. " + (inHand && inBag ? "PASS" : "FAIL"));
                    return true;
                }
                case "inv-chest":   // round trips on the open chest: counts of every item must not change
                {
                    var c = ChestPanel.Open; if (c == null) { Log.Info("CHEST: no chest open. FAIL"); return true; }
                    var ch = c.GetInventory();
                    System.Func<string> snap = () => string.Join(",", inv.GetAllItems().Concat(ch.GetAllItems()).GroupBy(i => i.m_shared.m_name).OrderBy(g => g.Key).Select(g => g.Key + g.Sum(i => i.m_stack)));
                    var before = snap(); var res = new System.Collections.Generic.List<string>();
                    foreach (var k in new[] { 0, 1, 2 }) { ChestPanel.Sort = k; res.Add(ChestPanel.DoSort()); }
                    res.Add(ChestPanel.Take(false)); res.Add(ChestPanel.Put(true)); res.Add(ChestPanel.Put(false)); res.Add(ChestPanel.Take(true));
                    ChestPanel.Sort = 0; ChestPanel.DoSort();
                    var after = snap();
                    Log.Info($"CHEST: {string.Join(" | ", res)}; totals same {before == after}. " + (before == after ? "PASS" : "FAIL\n" + before + "\n" + after));
                    return true;
                }
                case "inv-stock":   // bag vs bag + chests for what the chests near you hold; can the torch / club be made
                {
                    var stockNames = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c => (c.transform.position - p.transform.position).magnitude < Cfg.StackRange.Value && c.GetInventory() != null).SelectMany(c => c.GetInventory().GetAllItems()).Select(i => i.m_shared.m_name).Distinct().Take(8);
                    Log.Info("STOCK: " + string.Join(", ", stockNames.Select(n => $"{n} bag {inv.CountItems(n)} all {Stock.Count(p, n)}")));
                    foreach (var rn in new[] { "Recipe_Torch", "Recipe_Club", "Recipe_AxeStone" }) { var r = ObjectDB.instance.m_recipes.FirstOrDefault(x => x.name == rn); if (r != null) Log.Info($"STOCK: {rn} craftable {p.HaveRequirements(r, false, 1)}"); }
                    return true;
                }
                case "inv-stockcount":
                    Log.Info($"STOCKC: wood bag {inv.CountItems("$item_wood")} all {Stock.Count(p, "$item_wood")}, resin bag {inv.CountItems("$item_resin")} all {Stock.Count(p, "$item_resin")}, torches {inv.CountItems("$item_torch")}");
                    return true;
                case "inv-takebar":   // Take Everything must fill bag rows before the action bar; then put it all back
                {
                    if (ChestPanel.Open == null) { Log.Info("TAKEBAR: no chest open. FAIL"); return true; }
                    var barBefore = new System.Collections.Generic.HashSet<ItemDrop.ItemData>(inv.GetAllItems().Where(i => i.m_gridPos.y == 0));
                    var r = ChestPanel.Take(false);
                    var onBar = inv.GetAllItems().Where(i => i.m_gridPos.y == 0 && !barBefore.Contains(i)).Select(i => i.m_shared.m_name).ToList();
                    int free = Enumerable.Range(0, inv.GetWidth() * SpecialRows.Start).Count(k => k / inv.GetWidth() >= 1 && inv.GetItemAt(k % inv.GetWidth(), k / inv.GetWidth()) == null && !InvActions.Locked(k % inv.GetWidth(), k / inv.GetWidth()));
                    var back = ChestPanel.Put(false);
                    Log.Info($"TAKEBAR: {r}; new on the action bar: [{string.Join(", ", onBar)}] while {free} bag cells were still free; back: {back}. " + (onBar.Count == 0 || free == 0 ? "PASS" : "FAIL"));
                    return true;
                }
                case "inv-all":
                    Log.Info($"ALL: weight {inv.GetTotalWeight():0} height {inv.GetHeight()} start {SpecialRows.Start}: " + string.Join(", ", inv.GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).Select(i => $"{i.m_shared.m_name}x{i.m_stack}@{i.m_gridPos.x},{i.m_gridPos.y}")) + " | store: " + string.Join(", ", ModSlots.For(p).GetAllItems().Select(i => i.m_shared.m_name)));
                    return true;
                case "inv-layout":
                    Log.Info($"LAYOUT: height {inv.GetHeight()} base {ExtraRows.Base} extra {ExtraRows.Extra.Value} start {SpecialRows.Start} eqrow {SpecialRows.EqRow}; outside: " + string.Join(", ", inv.GetAllItems().Where(i => i.m_gridPos.y >= inv.GetHeight()).Select(i => $"{i.m_shared.m_name}@{i.m_gridPos.x},{i.m_gridPos.y}")) + " | reserved: " + string.Join(", ", inv.GetAllItems().Where(i => i.m_gridPos.y >= SpecialRows.Start && i.m_gridPos.y < inv.GetHeight()).Select(i => $"{i.m_shared.m_name}@{i.m_gridPos.x},{i.m_gridPos.y}")));
                    return true;
                case "ms-dump":
                    Log.Info("MS store: " + string.Join(", ", ModSlots.For(p).GetAllItems().Select(i => $"{i.m_shared.m_name}@{i.m_gridPos.x},{i.m_gridPos.y}")) + " | bag keys: " + string.Join(", ", inv.GetAllItems().Where(ModSlots.IsKey).Select(i => $"{i.m_shared.m_name}@{i.m_gridPos.x},{i.m_gridPos.y}")));
                    return true;
                case "ms-dup":   // count CryptKeys through give -> put -> unload/reload -> save
                {
                    var store = ModSlots.For(p);
                    int Count() => inv.GetAllItems().Count(i => i.m_shared.m_name == "$item_cryptkey") + ModSlots.For(p).GetAllItems().Count(i => i.m_shared.m_name == "$item_cryptkey");
                    foreach (var k in inv.GetAllItems().Where(i => i.m_shared.m_name == "$item_cryptkey").ToList()) inv.RemoveItem(k);
                    foreach (var k in store.GetAllItems().Where(i => i.m_shared.m_name == "$item_cryptkey").ToList()) store.RemoveItem(k);
                    var steps = new System.Collections.Generic.List<string> { "clean " + Count() };
                    var key = inv.AddItem("CryptKey", 1, 1, 0, 0L, "", false); steps.Add("give " + Count());
                    ModSlots.Put(p, key, 0, ModSlots.KeyRow); steps.Add("put " + Count());
                    ModSlots.Remove(); steps.Add("unload " + Count());
                    Game.instance.SavePlayerProfile(false); steps.Add("save " + Count());
                    Log.Info("MS dup: " + string.Join(", ", steps) + (Count() == 1 ? " PASS" : " FAIL"));
                    return true;
                }
                case "ms-after":   // after a death + respawn: the store must be empty and not in the save data
                {
                    int left = ModSlots.For(p)?.NrOfItems() ?? -1; bool inSave = p.m_customData.ContainsKey(ModSlots.Key);
                    foreach (var tg in Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None)) Log.Info("grave: " + string.Join(", ", tg.GetComponent<Container>().GetInventory().GetAllItems().Select(i => i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name)));
                    Log.Info($"MS after respawn: {left} item(s) in the mod slots, in save data {inSave}. " + (left == 0 && !inSave ? "PASS" : "FAIL"));
                    return true;
                }
                case "ms-test":   // wishbone + demister in their slots, a key in the pouch; effects, doors, save/reload
                {
                    var msNames = new[] { "Wishbone", "Demister", "CryptKey" };
                    foreach (var n in msNames) if (!inv.HaveItem(ObjectDB.instance.GetItemPrefab(n).GetComponent<ItemDrop>().m_itemData.m_shared.m_name) && ModSlots.For(p).GetAllItems().All(i => i.m_dropPrefab == null || i.m_dropPrefab.name != n)) inv.AddItem(ObjectDB.instance.GetItemPrefab(n), 1);
                    ItemDrop.ItemData Bag(string n) => inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == n);
                    var wb = Bag("Wishbone"); var dm = Bag("Demister"); var key = Bag("CryptKey");
                    bool put = (wb == null || ModSlots.Put(p, wb, 0, 0)) & (dm == null || ModSlots.Put(p, dm, 1, 0)) & (key == null || ModSlots.Put(p, key, 0, ModSlots.KeyRow));
                    var store = ModSlots.For(p);
                    var seWb = store.GetItemAt(0, 0)?.m_shared.m_equipStatusEffect; var seDm = store.GetItemAt(1, 0)?.m_shared.m_equipStatusEffect;
                    var seman = p.GetSEMan();
                    bool fx = seWb != null && seDm != null && seman.HaveStatusEffect(seWb.NameHash()) && seman.HaveStatusEffect(seDm.NameHash());
                    HarmonyLib.AccessTools.Method(typeof(Humanoid), "UpdateEquipmentStatusEffects").Invoke(p, null);   // vanilla's rebuild
                    bool kept = fx && seman.HaveStatusEffect(seWb.NameHash()) && seman.HaveStatusEffect(seDm.NameHash());
                    var door = ZNetScene.instance.m_prefabs.Select(g => g.GetComponentInChildren<Door>(true)).FirstOrDefault(d => d != null && d.m_keyItem != null && d.m_keyItem.name == "CryptKey");
                    bool inBag = inv.GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "CryptKey");
                    bool opens = door != null && (bool)HarmonyLib.AccessTools.Method(typeof(Door), "HaveKey").Invoke(door, new object[] { p, true });
                    int before = store.NrOfItems();
                    Game.instance.SavePlayerProfile(false);
                    bool saved = p.m_customData.ContainsKey(ModSlots.Key);
                    ModSlots.Remove();                                  // as an unload does
                    int after = ModSlots.For(p)?.NrOfItems() ?? -1;     // as the next load does
                    bool fx2 = seman.HaveStatusEffect(seWb.NameHash());
                    Log.Info($"MS test: put {put}, effects on {fx}, kept after vanilla rebuild {kept}, key in bag {inBag}, door opens with pouch key {opens}, saved {saved}, items {before} -> {after} after reload, effects after reload {fx2}. "
                        + (put && fx && kept && !inBag && opens && saved && before == 3 && after == 3 && fx2 ? "PASS" : "FAIL"));
                    return true;
                }
                case "layers":
                {
                    var layers = p.GetComponentsInChildren<Renderer>(true).Select(r => r.gameObject.layer).Distinct().Select(l => l + ":" + LayerMask.LayerToName(l));
                    Log.Info("player render layers: " + string.Join(", ", layers) + $"; camera {GameCamera.instance?.GetComponent<Camera>()?.cullingMask:X}");
                    return true;
                }
                case "sr-test":
                {
                    int sr = SpecialRows.Start;
                    var food = inv.AddItem("Raspberry", 5, 1, 0, 0L, "", new Vector2i(0, sr), false);
                    var wood = inv.AddItem("Wood", 3, 1, 0, 0L, "", false);           // like a pickup: auto-placed
                    bool woodOk = wood == null || !SpecialRows.Is(inv, wood.m_gridPos.y);
                    var stone = inv.AddItem("Stone", 2, 1, 0, 0L, "", false);
                    bool refused = stone == null || !inv.MoveItemToThis(inv, stone, 2, 1, sr);  // stone into the Food row
                    Log.Info($"SR test: height {inv.GetHeight()}, special rows from {sr}; food at {food?.m_gridPos.x},{food?.m_gridPos.y}; pickup landed at {wood?.m_gridPos.x},{wood?.m_gridPos.y} (outside reserved: {woodOk}); stone refused by Food row: {refused}. " + (food != null && food.m_gridPos.y == sr && woodOk && refused ? "PASS" : "FAIL"));
                    return true;
                }
                case "inv-test":
                {
                    int Total() => inv.GetAllItems().Sum(i => i.m_stack);
                    inv.AddItem("Wood", 7, 1, 0, 0L, "", new Vector2i(3, 2), false);
                    inv.AddItem("Wood", 9, 1, 0, 0L, "", new Vector2i(6, 3), false);
                    inv.AddItem("SurtlingCore", 4, 1, 0, 0L, "", new Vector2i(7, 1), false);   // not carried yet: lands exactly there
                    if (!InvActions.Locked(7, 1)) InvActions.ToggleLock(7, 1);           // flint stays put
                    int before = Total();
                    bool merged = InvActions.Merge(p);
                    InvActions.Sort(p);
                    int after = Total();
                    var flint = inv.GetItemAt(7, 1);
                    bool noneReserved = inv.GetAllItems().All(i => i.m_gridPos.y < SpecialRows.Start || SpecialRows.Accepts(i.m_gridPos.y - SpecialRows.Start, i));
                    Log.Info($"INV test: total {before} -> {after}, merged {merged}, locked core still at 7,1: {flint != null && flint.m_dropPrefab != null && flint.m_dropPrefab.name == "SurtlingCore"}, rows ok: {noneReserved}; layout: " +
                             string.Join(" ", inv.GetAllItems().OrderBy(i => i.m_gridPos.y * 8 + i.m_gridPos.x).Select(i => $"{i.m_dropPrefab?.name}x{i.m_stack}@{i.m_gridPos.x},{i.m_gridPos.y}")) +
                             (before == after && flint != null && flint.m_dropPrefab != null && flint.m_dropPrefab.name == "SurtlingCore" && noneReserved ? " PASS" : " FAIL"));
                    return true;
                }
                case "inv-trash1":   // drag 2 stone onto the trash: the confirm dialog shows
                {
                    var stone = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Stone");
                    _stoneBefore = stone?.m_stack ?? -1;
                    HarmonyLib.AccessTools.Method(typeof(InventoryGui), "SetupDragItem").Invoke(InventoryGui.instance, new object[] { stone, inv, 2 });
                    InventoryStyle.TrashSlot?.OnClick();
                    Log.Info($"TRASH step 1: stone {_stoneBefore}, dialog asked");
                    return true;
                }
                case "inv-trash2":   // confirm
                {
                    InventoryStyle.TrashSlot?.Confirm();
                    var stone = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Stone");
                    int after = stone?.m_stack ?? 0;
                    Log.Info($"TRASH: stone {_stoneBefore} -> {after}. " + (after == _stoneBefore - 2 ? "PASS" : "FAIL"));
                    return true;
                }
                case "xr-state":
                    Log.Info($"XR state: base {ExtraRows.Base}, height {inv.GetHeight()}, items {inv.GetAllItems().Count}, in extra rows {ExtraRows.InOurRows(inv)}: " +
                             string.Join(" ", inv.GetAllItems().Where(i => i.m_gridPos.y >= ExtraRows.Base).Select(i => $"{i.m_dropPrefab?.name}x{i.m_stack}@{i.m_gridPos.x},{i.m_gridPos.y}")));
                    return true;
                case "xr-fill":
                    string[] names = { "Wood", "Stone", "Resin", "Flint", "LeatherScraps" };
                    for (int i = 0; i < names.Length; i++)
                        if (inv.GetItemAt(i, ExtraRows.Base) == null) inv.AddItem(names[i], 10, 1, 0, 0L, "", new Vector2i(i, ExtraRows.Base), false);
                    Log.Info($"XR fill: in extra rows now {ExtraRows.InOurRows(inv)}");
                    return true;
                case "xr-nomod":
                {
                    var before = inv.GetAllItems().Select(i => (i, i.m_gridPos)).ToList();
                    int total = inv.GetAllItems().Count;
                    int left = ExtraRows.Pack(inv);
                    var pkg = new ZPackage(); inv.Save(pkg);
                    ExtraRows.Unpack(inv);
                    var vanilla = new Inventory("no-mod test", null, inv.GetWidth(), ExtraRows.Base);
                    vanilla.Load(new ZPackage(pkg.GetArray()));
                    int seen = vanilla.GetAllItems().Count, outside = vanilla.GetAllItems().Count(i => i.m_gridPos.y >= ExtraRows.Base);
                    bool restored = before.All(b => b.i.m_gridPos == b.m_gridPos);
                    Log.Info($"XR no-mod: {total} items, without the mod {seen} load and {outside} sit outside the vanilla rows (would drop), {left} could not be packed; after unpack every item in its slot: {restored}. " +
                             (seen == total && outside == 0 && restored ? "PASS" : "FAIL"));
                    return true;
                }
                case "xr-save":
                {
                    var before = inv.GetAllItems().Select(i => (i, i.m_gridPos)).ToList();
                    Game.instance.SavePlayerProfile(false);
                    bool same = before.All(b => b.i.m_gridPos == b.m_gridPos) && inv.GetAllItems().All(i => !i.m_customData.ContainsKey(ExtraRows.Tag));
                    Log.Info($"XR save: items back in their slots and untagged after a real save: {same}. " + (same ? "PASS" : "FAIL"));
                    return true;
                }
                case "xr-die":
                {
                    int unequipped = inv.GetAllItems().Count(i => !i.m_shared.m_questItem) + (ModSlots.For(p)?.NrOfItems() ?? 0);   // mod-slot items go to the grave too
                    _expectGrave = unequipped; _dieAt = Time.unscaledTime;
                    _oldGraves = new System.Collections.Generic.HashSet<int>(Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None).Select(t => t.GetInstanceID()));
                    Log.Info($"XR die: {unequipped} unequipped item(s), {ExtraRows.InOurRows(inv)} of them in extra rows; killing the test character");
                    // a real hit: vanilla's death code reads the last hit (SetHealth(0) alone leaves it null and the death fails)
                    var hit = new HitData { m_point = p.transform.position, m_dir = Vector3.down };
                    hit.m_damage.m_damage = 100000f;
                    p.Damage(hit);
                    return true;
                }
            }
            return false;
        }

        private static int _stoneBefore = -1;
        private static int _expectGrave = -1; private static float _dieAt;
        private static System.Collections.Generic.HashSet<int> _oldGraves = new System.Collections.Generic.HashSet<int>();

        // After xr-die: find the newest grave near the death spot and compare.
        // a test step that has to wait for the world (another mod's refresh, a spawned piece settling)
        private static System.Action _later; private static float _laterAt;
        private static void Later(float seconds, System.Action a) { _later = a; _laterAt = Time.unscaledTime + seconds; }

        internal static void Tick()
        {
            if (_later != null && Time.unscaledTime >= _laterAt) { var a = _later; _later = null; try { a(); } catch (System.Exception e) { Log.Warn("dev test: " + e); } }
            if (_expectGrave < 0 || Time.unscaledTime - _dieAt < 1f) return;
            var g = Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None).FirstOrDefault(t => !_oldGraves.Contains(t.GetInstanceID())); // the grave this death made
            if (g == null) { if (Time.unscaledTime - _dieAt > 20f) { _expectGrave = -1; Log.Info("XR die: no new grave within 20 s. FAIL"); } return; }
            int expect = _expectGrave; _expectGrave = -1;
            var ginv = g.GetComponent<Container>().GetInventory();
            int n = ginv.GetAllItems().Count, outside = ginv.GetAllItems().Count(i => i.m_gridPos.y >= ginv.GetHeight() || i.m_gridPos.x >= ginv.GetWidth());
            Log.Info($"XR die: grave {ginv.GetWidth()}x{ginv.GetHeight()} holds {n} item(s), expected {expect}, {outside} outside its grid. " + (n == expect && outside == 0 ? "PASS" : "FAIL"));
            Log.Info("XR die: grave has " + string.Join(", ", ginv.GetAllItems().Select(i => i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name)));
        }
    }
}
