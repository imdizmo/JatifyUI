using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jatify.UI
{
    // Dev screenshots without touching the desktop: drop a file named "shot.request" in
    // BepInEx/cache/jatify.ui. Its text says what to show first ("inventory", "map", "hud" = as is).
    // "dump" writes the live inventory screen tree instead. The mod opens that screen, waits a few frames, captures to shots/shot-<n>.png, deletes the
    // request and keeps only the newest 5 shots.
    internal static class DevShot
    {
        private static string _dir, _req;
        private static int _wait = -1, _dumpSettings;
        private static float _nextCheck;

        internal static void Init(string cacheDir) { _dir = Path.Combine(cacheDir, "jatify.ui"); _req = Path.Combine(_dir, "shot.request"); }

        // relog: back on the title screen, wait 3 s, start the same world with the same character
        private static string _relogWho, _relogWorld; private static float _relogAt;
        private static void Relog()
        {
            if (_relogWho == null || FejdStartup.instance == null) return;
            if (_relogAt < 0f) { _relogAt = Time.unscaledTime + 3f; return; }
            if (Time.unscaledTime < _relogAt) return;
            var p = Campfire.Fejd.Profiles.FirstOrDefault(x => x.GetName() == _relogWho); var w = Campfire.Fejd.Worlds.FirstOrDefault(x => x.m_name == _relogWorld);
            _relogWho = null;
            Log.Info($"relog: {Campfire.Fejd.StartWorld(p, w, false, false, false, "") ?? "starting"}");
        }

        private static int _focusCheck, _gearCheck, _skillShot, _cycle;
        internal static void Tick()
        {
            TestKit.Hold();
            if (_skillShot > 0 && --_skillShot == 0) File.WriteAllText(_req, "asis");   // a shot once the banner has faded in
            if (_gearCheck > 0 && --_gearCheck == 0) Log.Info($"gear page: text input {TextInput.IsVisible()}, player input blocked {Chat.instance?.HasFocus()}, cursor {Cursor.lockState}");
            if (_focusCheck > 0 && --_focusCheck == 0)
            {
                var sel = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
                Log.Info($"focus: selected {(sel ? sel.name : "none")}, focused {sel?.GetComponent<TMPro.TMP_InputField>()?.isFocused}, typing {Typing.Active}, chat focus {Chat.instance?.HasFocus()}");
            }
            if (_cycle > 0 && Time.frameCount % 20 == 0 && InventoryGui.instance != null)
            {   // cycle-inv: open and close the inventory every 20 frames (the wisp came off now and then)
                if (InventoryGui.IsVisible()) InventoryGui.instance.Hide(); else InventoryGui.instance.Show(null);
                if (--_cycle == 0) Log.Info($"cycle-inv done: wisp/utility {(Player.m_localPlayer?.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility).Select(i => i.m_shared.m_name + (i.m_equipped ? " on" : " off")).DefaultIfEmpty("none").Aggregate((a, b) => a + ", " + b))}");
            }
            Relog();
            if (_dir == null) return;
            if (_dumpSettings > 0 && --_dumpSettings == 0 && Settings.instance != null) { DevDump.Tree("settings", Settings.instance.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-settings.txt"); }
            DevTest.Tick();
            if (_wait > 0) { _wait--; return; }
            if (_wait == 0) { _wait = -1; Capture(); return; }
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;                     // polling a file twice a second is cheap
            if (!File.Exists(_req)) return;
            var what = File.ReadAllText(_req).Trim().ToLowerInvariant();
            File.Delete(_req);
            if (DevTest.Run(what)) return;
            if (what.StartsWith("equip:") && Player.m_localPlayer != null) { var pl = Player.m_localPlayer; var it = pl.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(what.Substring(6), System.StringComparison.OrdinalIgnoreCase)); Log.Info($"equip {what.Substring(6)}: {(it == null ? "not in the bag" : pl.EquipItem(it) ? "on" : "refused")}"); return; }
            if (what.StartsWith("take:") && Player.m_localPlayer != null) { var inv = Player.m_localPlayer.GetInventory(); var it = inv.GetAllItems().FirstOrDefault(i => !i.m_equipped && i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(what.Substring(5), System.StringComparison.OrdinalIgnoreCase)); Log.Info($"take {what.Substring(5)}: {(it != null && inv.RemoveItem(it) ? "removed" : "none in the bag")}"); return; }   // undoes a give: (only an unequipped one in the bag)
            if (what.StartsWith("t:")) { TestKit.Run(what.Substring(2)); return; }   // the test kit (TestKit.cs)
            if (what == "loading-preview") { Campfire.LoadBar.Preview(); return; }
            if (what == "cs-show") { Campfire.CharScreen.Hide(); Campfire.CharScreen.Show(); Log.Info("cs-show"); return; }   // dev: the character screen again (after showonly / serveralias)
            if (what.StartsWith("cs-open:")) { Campfire.CharScreen.DevOpen(what.Substring(8)); Log.Info($"cs-open: {what.Substring(8)}"); return; }   // dev: a start-screen window
            if (what.StartsWith("cs-tab:")) { Campfire.CharScreen.DevTab(what == "cs-tab:servers"); Log.Info($"cs-tab: {what.Substring(7)}"); return; }   // dev: the character screen's Worlds / Servers tab
            if (what.StartsWith("serveralias:")) { var v = what.Substring(12); Campfire.Fejd.ServerAlias = v == "off" ? null : v; Log.Info($"serveralias: {v}"); return; }   // dev: a made-up server name on the start screen
            if (what.StartsWith("showonly:")) { var v = what.Substring(9); Campfire.Fejd.ShowOnly = v == "off" ? null : new HashSet<string>(v.Split(',').Select(x => x.Trim())); Log.Info($"showonly: {(Campfire.Fejd.ShowOnly == null ? "off" : string.Join(", ", Campfire.Fejd.ShowOnly))}"); return; }   // dev: only these characters and worlds on the start screen
            if (what == "badge-off" || what == "badge-on") { Root.Badge(what == "badge-on"); return; }
            if (what == "cycle-inv") { _cycle = 40; Log.Info("cycle-inv: 20 opens and closes"); return; }
            if (what == "ui-pvp") { var g = InventoryGui.instance; if (g.m_pvp.interactable) g.m_pvp.isOn = !g.m_pvp.isOn; what = "inventory"; }   // dev: same path as the PvP switch
            if (what == "ui-trophies") { InventoryGui.instance.OnOpenTrophies(); what = "inventory"; }
            if (what == "ui-crafting") { var g = InventoryGui.instance; g.OnCloseTrophies(); g.OnCloseAchievements(); what = "inventory"; }
            if (what == "ui-keys") { InventoryGui.instance.m_player.Find("jatify-frame/jatify-equipment/Doll/Keys")?.GetComponent<Press>()?.Fire(); what = "asis"; }
            if (what.StartsWith("auto-open:")) { AutomationPage._open.Add(what.Substring(10)); what = "ui-auto"; }
            if (what == "ui-auto" || what == "ui-recipes" || what == "ui-index" || what == "ui-ach" || what == "ui-trophies2" || what == "ui-texts" || what == "ui-craftpage") { Pages.Show(what == "ui-craftpage" ? null : what == "ui-trophies2" ? "trophies" : what.Substring(3)); what = "inventory"; }
            if (what == "ul-seed") { var pl = Player.m_localPlayer; int k = 0; foreach (var go in ObjectDB.instance.m_items) { var d = go.GetComponent<ItemDrop>(); if (d == null || d.m_itemData.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Material || pl.IsMaterialKnown(d.m_itemData.m_shared.m_name)) continue; pl.AddKnownItem(d.m_itemData); if (++k == 8) break; } what = "asis"; }
            if (what == "es-info") { var es = UnityEngine.EventSystems.EventSystem.current; var ty = System.AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("UnityExplorer.UI.UIManager")).FirstOrDefault(x => x != null); Log.Info($"es-info: {(es ? es.name : "none")} module {es?.currentInputModule?.GetType().Name}, UE menu {ty?.GetProperty("ShowMenu")?.GetValue(null)}"); return; }
            if (what == "lift-key") { var pl = Player.m_localPlayer; var st = ModSlots.For(pl); var k = st.GetAllItems().FirstOrDefault(i => i.m_gridPos.y >= ModSlots.KeyRow) ?? pl.GetInventory().GetAllItems().FirstOrDefault(ModSlots.IsKey); if (k != null && pl.GetInventory().ContainsItem(k)) ModSlots.Put(pl, k, 0, ModSlots.KeyRow); Log.Info($"lift: picked {ModSlots.PickUp(pl, k.m_gridPos.x, k.m_gridPos.y)}"); return; }
            if (what == "lift-check") { var g = InventoryGui.instance; Log.Info($"lift: carrying {(HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_dragItem").GetValue(g) as ItemDrop.ItemData)?.m_shared.m_name ?? "nothing"}"); return; }
            if (what == "lift-cancel") { HarmonyLib.AccessTools.Method(typeof(InventoryGui), "SetupDragItem").Invoke(InventoryGui.instance, new object[] { null, null, 1 }); return; }
            if (what == "scroll-info") { foreach (var sr in Resources.FindObjectsOfTypeAll<UnityEngine.UI.ScrollRect>()) Log.Info($"scroll: {sr.name} sens {sr.scrollSensitivity} under {(sr.transform.root ? sr.transform.root.name : "")}"); return; }
            if (what == "ul-open") { int idx = Unlocks.All.FindLastIndex(e => e.Kind == Unlocks.Recipe); var up = HarmonyLib.Traverse.Create(typeof(Pages)).Field("_unlocks"); up.Field("_open").SetValue(idx); up.Field("_key").SetValue(null); Pages.Show("recipes"); what = "inventory"; }
            if (what == "tab-mats") { var info = InventoryGui.instance.m_info; foreach (var n in new[] { "Texts/Icon", "Trophies/Image", "Achievements/Image", "Skills/Icon" }) { var im = info.Find(n)?.GetComponent<UnityEngine.UI.Image>(); Log.Info($"tabmat {n}: {(im ? im.sprite?.name : "none")} mat {(im && im.material ? im.material.name + " / " + im.material.shader.name : "none")} color {(im ? im.color.ToString() : "")}"); } return; }
            if (what == "lit-swap") { var info = InventoryGui.instance.m_info; var v = info.Find("Trophies/Image").GetComponent<UnityEngine.UI.Image>(); var o = info.Find("Recipes-tab/Icon").GetComponent<UnityEngine.UI.Image>(); var t = v.sprite; v.sprite = o.sprite; o.sprite = t; what = "asis"; }
            if (what == "btn-info") { foreach (var n in new[] { "Trophies", "Texts", "Recipes-tab", "Crafting-tab" }) { var b = InventoryGui.instance.m_info.Find(n)?.GetComponent<UnityEngine.UI.Button>(); Log.Info($"btn {n}: target {(b && b.targetGraphic ? b.targetGraphic.name : "none")} transition {b?.transition} normal {b?.colors.normalColor} mult {b?.colors.colorMultiplier} icon crColor {b?.transform.Find(n == "Trophies" ? "Image" : n == "Texts" ? "Icon" : "Icon")?.GetComponent<CanvasRenderer>()?.GetColor()}"); } return; }
            if (what == "lit-ours") { foreach (var n in new[] { "Crafting-tab/Icon", "Recipes-tab/Icon", "Trophies/Image" }) { var im = InventoryGui.instance.m_info.Find(n)?.GetComponent<UnityEngine.UI.Image>(); Log.Info($"litours {n}: mat {(im && im.material ? im.material.name : "null")} matForRender {(im ? im.materialForRendering?.name : "")} shader {(im ? im.materialForRendering?.shader.name : "")} canvas {(im ? im.canvas?.name : "")} additional {(im && im.canvas ? im.canvas.additionalShaderChannels.ToString() : "")} color {(im ? im.color.ToString() : "")}"); } return; }
            if (what == "lit-info") { var im = InventoryGui.instance.m_info.Find("Trophies/Image").GetComponent<UnityEngine.UI.Image>(); var m = im.material; var sh = m.shader; var sb = new System.Text.StringBuilder(); for (int i = 0; i < sh.GetPropertyCount(); i++) { var n = sh.GetPropertyName(i); var ty = sh.GetPropertyType(i); sb.Append($"{n}:{ty}="); if (ty == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append(m.GetColor(n)); else if (ty == UnityEngine.Rendering.ShaderPropertyType.Float || ty == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append(m.GetFloat(n)); else if (ty == UnityEngine.Rendering.ShaderPropertyType.Texture) sb.Append(m.GetTexture(n)?.name); sb.Append("; "); } Log.Info($"lit: sprite tex {im.sprite.texture.name} {im.sprite.texture.format} {im.sprite.texture.width}x{im.sprite.texture.height}; secondary {im.sprite.GetSecondaryTextureCount()}; keywords {string.Join(",", m.shaderKeywords)}; props {sb}"); var tmp = new UnityEngine.SecondarySpriteTexture[4]; int c = im.sprite.GetSecondaryTextures(tmp); for (int i = 0; i < c; i++) Log.Info($"lit: secondary {tmp[i].name} = {tmp[i].texture?.name}"); return; }
            if (what == "ue-hide") { var ty = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityExplorer.UI.UIManager")).FirstOrDefault(x => x != null); var pr = ty?.GetProperty("ShowMenu"); Log.Info($"UE menu was {pr?.GetValue(null)}"); pr?.SetValue(null, false); return; }
            if (what.StartsWith("sprites:")) { var keys = what.Substring(8).Split(','); var names = Resources.FindObjectsOfTypeAll<Sprite>().Select(sp => sp.name).Distinct().Where(n => keys.Any(k => n.ToLowerInvariant().Contains(k))).OrderBy(n => n); File.WriteAllText(Path.Combine(_dir, "sprites.txt"), string.Join("\n", names)); Log.Info("sprites -> sprites.txt"); return; }
            if (what == "craft-focus") { var es = UnityEngine.EventSystems.EventSystem.current; CraftFilter.FocusSearch(); Log.Info($"focus now: field {CraftFilter.FieldInfo()} es {(es ? es.name : "null")} module {es?.currentInputModule?.GetType().Name} sel {es?.currentSelectedGameObject?.name} all {Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length}"); _focusCheck = 3; what = "asis"; }
            if (what == "ui-gear") { InventoryGui.instance.Hide(); ConfigPage.Show(Root.Canvas.transform, Plugin.Instance.ThemeData, false); _gearCheck = 30; what = "asis"; }
            if (what == "inv-skills") { Equipment.ForceSkills = true; what = "inventory"; }
            if (what.StartsWith("craft-")) // dev: craft-grid, craft-list, craft-q:<text>, craft-cat:<n>
            {
                if (what == "craft-avail") CraftFilter.ByAvail = !CraftFilter.ByAvail; else if (what == "craft-fav") { var sel = HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe").GetValue(InventoryGui.instance); if (sel != null) RecipeView.ToggleFav(HarmonyLib.Traverse.Create(sel).Property<Recipe>("Recipe").Value); } else if (what == "craft-grid") CraftFilter.Grid = true; else if (what == "craft-list") CraftFilter.Grid = false;
                else if (what.StartsWith("craft-q:")) CraftFilter.Query = what.Substring(8);
                else if (what.StartsWith("craft-cat:")) CraftFilter.Cat = int.Parse(what.Substring(10));
                CraftFilter.Poke(); what = "inventory";
            }
            if (what == "dump-build" && Hud.instance != null) { DevDump.Tree("build", Hud.instance.m_buildHud.transform, Path.GetDirectoryName(_dir)); DevDump.Tree("buildwindow", Hud.instance.m_pieceSelectionWindow.transform, Path.GetDirectoryName(_dir)); DevDump.Tree("pieceicon", Hud.instance.m_pieceIconPrefab.transform, Path.GetDirectoryName(_dir)); if (Hud.instance.m_buildUi != null) DevDump.Tree("buildui", Hud.instance.m_buildUi.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-build.txt"); return; }
            if (what == "dump-msg" && MessageHud.instance != null) { DevDump.Tree("msg", MessageHud.instance.transform, Path.GetDirectoryName(_dir)); DevDump.Tree("unlockmsg", MessageHud.instance.m_unlockMsgPrefab.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-msg.txt"); return; }
            if (what == "msg-test" && MessageHud.instance != null) { var mh = MessageHud.instance; var t = mh.m_messageText.transform; string path = ""; for (var x = t; x != null; x = x.parent) path = x.name + "/" + path; Log.Info($"msg text at {path} pos {((RectTransform)t).anchoredPosition} anchors {((RectTransform)t).anchorMin} size {((RectTransform)t).sizeDelta}"); mh.ShowMessage(MessageHud.MessageType.TopLeft, "Test pickup message"); mh.QueueUnlockMsg(null, "Test unlock", "Something new"); what = "asis"; }
            if (what == "skill-test" && MessageHud.instance != null) { var sk = Player.m_localPlayer.GetSkills().m_skills[0]; MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "$msg_skillup $skill_" + sk.m_skill.ToString().ToLower() + ": 23", 0, sk.m_icon); _skillShot = 25; return; }
            if (what == "pickup-test") { var it = ObjectDB.instance.GetItemPrefab("Wood").GetComponent<ItemDrop>().m_itemData; for (int i = 0; i < 3; i++) Player.m_localPlayer.ShowPickupMessage(it, i + 1); _skillShot = 20; return; }
            if (what == "biome-test" && MessageHud.instance != null) { MessageHud.instance.ShowBiomeFoundMsg("$biome_blackforest", false); _skillShot = 90; return; }
            if (what == "test-panel") { TestPanel.Toggle(); what = "asis"; }
            if (what == "raid-test") { RaidBanner.FakeUntil = Time.unscaledTime + 8f; _skillShot = 40; return; }   // the raid banner for 8 s, a shot when it has faded in
            if (what.StartsWith("sprite-png:")) { foreach (var nm in what.Substring(11).Split(',')) { var sp = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(x => x.name.ToLowerInvariant() == nm); if (sp == null) { Log.Info($"sprite {nm}: none"); continue; } var r = sp.textureRect; var rt = RenderTexture.GetTemporary(sp.texture.width, sp.texture.height); Graphics.Blit(sp.texture, rt); var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0); tex.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); var px = tex.GetPixels32(); var raw = new byte[px.Length * 4]; for (int i = 0; i < px.Length; i++) { raw[i * 4] = px[i].r; raw[i * 4 + 1] = px[i].g; raw[i * 4 + 2] = px[i].b; raw[i * 4 + 3] = px[i].a; } File.WriteAllBytes(Path.Combine(_dir, $"sprite-{nm}-{tex.width}x{tex.height}.rgba"), raw); Object.Destroy(tex); Log.Info($"sprite {nm} -> {tex.width}x{tex.height}"); } return; }
            if (what == "start-logo")   // start screen: dump its tree, save every image under a "logo" object as raw RGBA
            {
                var fs = Object.FindFirstObjectByType<FejdStartup>(); if (fs == null) { Log.Info("start-logo: not on the start screen"); return; }
                DevDump.Tree("start", fs.transform, Path.GetDirectoryName(_dir));
                foreach (var g in fs.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                {
                    var path = string.Join("/", g.GetComponentsInParent<Transform>(true).Reverse().Select(t => t.name));
                    Texture src = g is UnityEngine.UI.Image im && im.sprite ? im.sprite.texture : g is UnityEngine.UI.RawImage ri ? ri.texture : null;
                    if (src == null || !path.ToLowerInvariant().Contains("logo")) continue;
                    var r = g is UnityEngine.UI.Image im2 && im2.sprite ? im2.sprite.textureRect : new Rect(0, 0, src.width, src.height);
                    var rt = RenderTexture.GetTemporary(src.width, src.height); Graphics.Blit(src, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false); tex.ReadPixels(r, 0, 0); tex.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    var px = tex.GetPixels32(); var raw = new byte[px.Length * 4]; for (int i = 0; i < px.Length; i++) { raw[i * 4] = px[i].r; raw[i * 4 + 1] = px[i].g; raw[i * 4 + 2] = px[i].b; raw[i * 4 + 3] = px[i].a; }
                    File.WriteAllBytes(Path.Combine(_dir, $"start-{g.name}-{src.name}-{tex.width}x{tex.height}.rgba"), raw); Object.Destroy(tex);
                    Log.Info($"start-logo: {path} = {src.name} {tex.width}x{tex.height} active {g.isActiveAndEnabled}");
                }
                return;
            }
            if (what.StartsWith("tex-png:")) { foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>().Where(x => what.Substring(8).Split(',').Any(k => x.name.ToLowerInvariant().Contains(k))).GroupBy(x => x.name).Select(g => g.First()).Take(40)) { if (t.width > 4096 || t.height > 4096) continue; var rt = RenderTexture.GetTemporary(t.width, t.height); Graphics.Blit(t, rt); var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); tex.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); var px = tex.GetPixels32(); var raw = new byte[px.Length * 4]; for (int i = 0; i < px.Length; i++) { raw[i * 4] = px[i].r; raw[i * 4 + 1] = px[i].g; raw[i * 4 + 2] = px[i].b; raw[i * 4 + 3] = px[i].a; } File.WriteAllBytes(Path.Combine(_dir, $"tex-{t.name}-{t.width}x{t.height}.rgba"), raw); Object.Destroy(tex); Log.Info($"tex {t.name} -> {t.width}x{t.height}"); } return; }   // textures by name part, as raw RGBA (at most 40)
            if (what.StartsWith("icon-png:")) { var sp = ObjectDB.instance.m_items.FirstOrDefault(g => g && g.name.ToLowerInvariant() == what.Substring(9))?.GetComponent<ItemDrop>()?.m_itemData.GetIcon(); Log.Info($"icon-png {what.Substring(9)}: {(sp ? sp.name : "none")}"); if (sp != null) { var r = sp.textureRect; var rt = RenderTexture.GetTemporary(sp.texture.width, sp.texture.height); Graphics.Blit(sp.texture, rt); var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0); tex.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); var px = tex.GetPixels32(); var raw = new byte[px.Length * 4]; for (int i = 0; i < px.Length; i++) { raw[i * 4] = px[i].r; raw[i * 4 + 1] = px[i].g; raw[i * 4 + 2] = px[i].b; raw[i * 4 + 3] = px[i].a; } File.WriteAllBytes(Path.Combine(_dir, $"icon-{tex.width}x{tex.height}.rgba"), raw); Object.Destroy(tex); Log.Info($"icon -> icon-{tex.width}x{tex.height}.rgba (bottom-up)"); } return; }
            if (what == "centre-test" && MessageHud.instance != null) { MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "You need a key"); var sk = Player.m_localPlayer.GetSkills().m_skills[0]; MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "$msg_skillup $skill_" + sk.m_skill.ToString().ToLower() + ": 24", 0, sk.m_icon); _skillShot = 25; return; }
            if (what == "feed-test" && MessageHud.instance != null) { var mh = MessageHud.instance; var wood = ObjectDB.instance.GetItemPrefab("Wood").GetComponent<ItemDrop>().m_itemData; mh.ShowMessage(MessageHud.MessageType.TopLeft, "Your inventory is full"); mh.ShowMessage(MessageHud.MessageType.TopLeft, "$item_wood", 12, wood.GetIcon()); mh.ShowMessage(MessageHud.MessageType.TopLeft, "Wishbone guides you to buried treasure"); mh.ShowMessage(MessageHud.MessageType.Center, "You need a key", 0, ObjectDB.instance.GetItemPrefab("CryptKey").GetComponent<ItemDrop>().m_itemData.GetIcon()); _skillShot = 120; return; }
            if (what == "icons-test") { foreach (var m in new[] { "$msg_donthaveany $item_resin", "$msg_bedneedroof", "$msg_youdied", "$msg_noteleport", "$msg_pvpon", "$inventory_full", "$msg_outof $ammo_arrows", "$hud_powernotready", "$msg_nobuildzone", "$msg_needfire", "$se_rested_start", "$piece_fermenter_needroof", "$msg_cantconsume", "$msg_worldsaved", "$msg_pin_added", "$msg_dropped $item_wood", Localization.instance.Localize("$msg_door_needkey", "$item_cryptkey"), "$msg_noturretammo", "$hud_eitrrequired" }) { var ic = NoticeIcons.For(m); Log.Info($"icon for {m}: {(ic ? ic.name : "NONE")}"); } MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "$msg_bedneedroof"); _skillShot = 30; return; }
            if (what == "do-craft" && InventoryGui.instance != null) { var gi = InventoryGui.instance; HarmonyLib.AccessTools.Method(typeof(InventoryGui), "OnCraftPressed").Invoke(gi, null); Log.Info($"do-craft: timer {HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_craftTimer").GetValue(gi)} recipe {HarmonyLib.AccessTools.Field(typeof(InventoryGui), "m_craftRecipe").GetValue(gi)}"); _skillShot = 12; return; }
            if (what == "trash-dialog") { if (Menu.IsVisible()) { Settings.instance?.OnBack(); Menu.instance?.Hide(); } if (InventoryGui.instance != null && !InventoryGui.IsVisible()) InventoryGui.instance.Show(null); InventoryStyle.TrashSlot?.DevShow(true); what = "asis"; }   // over the open inventory, never the pause menu
            if (what == "trash-close") { InventoryStyle.TrashSlot?.DevShow(false); return; }
            if (what == "dump-save" && Hud.instance != null) { DevDump.Tree("save", Hud.instance.m_saveIcon.transform.parent, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-save.txt"); return; }
            if (what == "export-hudicons" && EnemyHud.instance != null)
            {   // Valheim's own enemy HUD sprites (Aware, Alerted, level stars...) as PNGs, for the design preview
                var outDir = Path.Combine(_dir, "hudicons"); Directory.CreateDirectory(outDir); int n = 0;
                foreach (var root in new[] { EnemyHud.instance.m_baseHud, EnemyHud.instance.m_baseHudBoss })
                    if (root != null) foreach (var im in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                        if (im.sprite != null && DevIcons.Save(im.sprite, Path.Combine(outDir, $"{im.name}-{im.sprite.name}.png"))) n++;
                Log.Info($"export-hudicons: {n} sprites -> {outDir}"); return;
            }
            if (what == "dump-enemyhud" && EnemyHud.instance != null) { DevDump.Tree("enemyhud", EnemyHud.instance.m_baseHud.transform, Path.GetDirectoryName(_dir)); DevDump.Tree("enemyhud-boss", EnemyHud.instance.m_baseHudBoss.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-enemyhud.txt"); return; }
            if (what == "dump-loading" && Hud.instance != null) { DevDump.Tree("loading", Hud.instance.m_loadingScreen.transform, Path.GetDirectoryName(_dir)); Log.Info($"dev dump -> dump-loading.txt (image {Hud.instance.m_loadingImage?.name}, sprite {Hud.instance.m_loadingImage?.sprite?.name})"); return; }
            if (what == "ach-icons") { foreach (var a in (Achievements.m_instance?.m_achievementLists ?? new System.Collections.Generic.List<AchievementList>()).Where(l => l != null).SelectMany(l => l.m_achievements).Where(a => a != null).Take(6)) Log.Info($"ach {a.m_id}: icon {(a.m_icon == null ? (ReferenceEquals(a.m_icon, null) ? "null" : "DESTROYED") : a.m_icon.name + " tex " + (a.m_icon.texture ? a.m_icon.texture.name : "none"))}"); foreach (var im in Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsSortMode.None).Where(i => i.name == "Icon" && i.transform.parent != null && i.transform.parent.name.Contains("Deed")).Take(4)) Log.Info($"tracker img {im.transform.parent.name}: sprite {(im.sprite ? im.sprite.name : "none")} mat {(im.material ? im.material.name : "-")} color {im.color}"); return; }
            if (what == "save-now") { Game.instance.SavePlayerProfile(false); what = "asis"; }
            if (what == "dump-tooltip" && TooltipStyle.Tip != null) { DevDump.Tree("tooltip", TooltipStyle.Tip.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-tooltip.txt"); return; }
            if (what == "relog" && Game.instance != null && ZNet.instance != null) { _relogWho = Game.instance.GetPlayerProfile()?.GetName(); _relogWorld = ZNet.instance.GetWorldName(); _relogAt = -1f; Log.Info($"relog: {_relogWho} into {_relogWorld}"); Game.instance.Logout(true); return; }   // a fresh inventory screen
            if (what == "dump-itemtip" && Root.Canvas != null) { DevDump.Tree("itemtip", Root.Canvas.transform.Find("jatify-itemtip"), Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-itemtip.txt"); return; }
            if (what == "dump-inv" && InventoryGui.instance != null) { DevDump.Tree("inv-now", InventoryGui.instance.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-inv-now.txt"); return; }   // the live inventory screen (the load-time dump is of a closed one)
            if (what == "dump-map" && Minimap.instance != null) { DevDump.Tree("map", Minimap.instance.m_largeRoot.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-map.txt"); return; }
            if (what == "settings-warn" && Settings.instance != null)   // scroll down, hover the first visible quality warning (its tooltip)
            {
                foreach (var v in Settings.instance.GetComponentsInChildren<UnityEngine.UI.ScrollRect>()) if (v.name == "jatify-view") v.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
            }
            if (what == "settings-warn" && Settings.instance != null)
                foreach (var w in Settings.instance.GetComponentsInChildren<UnityEngine.UI.Image>()) if (w.name == "Warning") { UnityEngine.EventSystems.ExecuteEvents.Execute(w.gameObject, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler); what = "asis"; break; }
            if (what.StartsWith("settings-q") && Settings.instance != null) { var f = Settings.instance.transform.Find("Panel/jatify-search")?.GetComponentInChildren<TMPro.TMP_InputField>(); if (f != null) f.text = what.Substring(10); what = "asis"; }
            if (what == "settings-flip" && Settings.instance != null) { var tg = Settings.instance.GetComponentInChildren<UnityEngine.UI.Toggle>(); if (tg != null) tg.isOn = !tg.isOn; what = "asis"; }   // Back afterwards drops it
            if (what == "settings-jui" && Settings.instance != null && Menu.instance != null)   // click "Jatify UI" in the pause menu while Settings is open
            {
                var e = Menu.instance.m_root.Find("Menu/MenuEntries/jatify-Jatify UI");
                if (e != null) UnityEngine.EventSystems.ExecuteEvents.Execute(e.gameObject, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left }, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                what = "asis";
            }
            if (what.StartsWith("settings-tab") && Settings.instance != null) { Settings.instance.GetComponentInChildren<TabHandler>().SetActiveTab(int.Parse(what.Substring(12))); what = "asis"; }
            if (what == "dump-selinfo" && Hud.instance != null) { DevDump.Tree("selinfo", Hud.instance.m_buildHud.transform.Find("SelectedInfo"), Path.GetDirectoryName(_dir)); return; }
            if (what == "arm-dropdown") { DropdownOnTop.Arm = true; Log.Info("dev: the next dropdown list that opens gets dumped"); return; }
            if (what == "dump-dropdown") { int n = 0; foreach (var dd in Object.FindObjectsByType<TMPro.TMP_Dropdown>(FindObjectsSortMode.None)) { DevDump.Tree("dropdown" + n, dd.transform, Path.GetDirectoryName(_dir)); var open = dd.transform.Find("Dropdown List"); if (open != null) DevDump.Tree("dropdownlist" + n, open, Path.GetDirectoryName(_dir)); n++; } Log.Info($"dev dump -> {n} dropdowns"); return; }
            if (what == "dump-quit" && Menu.instance != null) { DevDump.Tree("quit", Menu.instance.m_quitDialog, Path.GetDirectoryName(_dir)); return; }
            if (what == "dump-mini" && Minimap.instance != null) { DevDump.Tree("mini", Minimap.instance.m_smallMarker.parent, Path.GetDirectoryName(_dir)); return; }
            if (what == "dump-textinput" && TextInput.instance != null) { DevDump.Tree("textinput", TextInput.instance.m_panel.transform, Path.GetDirectoryName(_dir)); return; }
            if (what == "chest-marks") { Log.Info("chest-marks: " + ChestAssign.Debug()); return; }
            if (what.StartsWith("chest-only:") && Player.m_localPlayer != null)   // assign the nearest chest (chest-only: clears it)
            {
                var me = Player.m_localPlayer.transform.position;
                var c = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(x => x.GetComponent<TombStone>() == null).OrderBy(x => (x.transform.position - me).sqrMagnitude).FirstOrDefault();
                var pf = what.Substring(11); var go = pf.Length > 0 ? ObjectDB.instance.m_items.FirstOrDefault(g => g != null && g.name.ToLowerInvariant() == pf) : null;
                if (c != null) ChestAssign.Set(c, go != null ? go.name : ""); Log.Info($"chest-only: {(c != null ? c.name : "no chest")} -> {(go != null ? go.name : "anything")}"); return;
            }
            if (what == "dump-chest" && Player.m_localPlayer != null)
            {
                var me = Player.m_localPlayer.transform.position;
                var c = Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(x => x.GetComponent<TombStone>() == null).OrderBy(x => (x.transform.position - me).sqrMagnitude).FirstOrDefault();
                if (c == null) return;
                var sb = new System.Text.StringBuilder($"{c.name} pos {c.transform.position} fwd {c.transform.forward} right {c.transform.right}\n");
                foreach (var tr in c.GetComponentsInChildren<Transform>(true)) sb.Append($"{tr.name} local {c.transform.InverseTransformPoint(tr.position)} {(tr.GetComponent<Renderer>() is Renderer r ? "bounds " + r.bounds.center + " " + r.bounds.size : "")}\n");
                File.WriteAllText(Path.Combine(_dir, "chest.txt"), sb.ToString()); return;
            }
            if (what == "dump-chat") { if (Chat.instance != null) DevDump.Tree("chat", Chat.instance.m_chatWindow, Path.GetDirectoryName(_dir)); if (Console.instance != null) DevDump.Tree("console", Console.instance.m_chatWindow, Path.GetDirectoryName(_dir)); Log.Info("dump-chat done"); return; }
            if (what == "dump-store" && StoreGui.instance != null) { DevDump.Tree("store", StoreGui.instance.m_rootPanel.transform, Path.GetDirectoryName(_dir)); return; }
            if (what == "settings-close") { Settings.instance?.OnBack(); Menu.instance?.Hide(); return; }
            if (what.StartsWith("settings-tab:") && Settings.instance != null) { var th = Settings.instance.GetComponentInChildren<TabHandler>(); if (th != null && int.TryParse(what.Substring(13), out var ti)) th.SetActiveTab(ti); return; }
            if (what == "dd-open" && Settings.instance != null) { var dd = Settings.instance.GetComponentsInChildren<TMPro.TMP_Dropdown>(true).FirstOrDefault(d => d.name == "Resolution" && d.gameObject.activeInHierarchy); dd?.Show(); _wait = 10; return; }
            if (what == "dump-dropdown" && Settings.instance != null) { var dd = Settings.instance.GetComponentsInChildren<TMPro.TMP_Dropdown>(true).FirstOrDefault(); if (dd != null) DevDump.Tree("dropdown", dd.template, Path.GetDirectoryName(_dir)); Log.Info($"dump-dropdown: {(dd != null ? dd.name + " item text " + dd.itemText?.name + " font " + dd.itemText?.font?.name + " mat " + dd.itemText?.fontSharedMaterial?.shader?.name + " color " + dd.itemText?.color : "none")}"); return; }
            if (what == "dump-settings" && Menu.instance != null) { if (!Menu.IsVisible()) Menu.instance.Show(); if (Settings.instance == null) Menu.instance.OnSettings(); _dumpSettings = 3; return; }
            if (what == "dump-menu" && Menu.instance != null) { DevDump.Tree("menu", Menu.instance.m_root, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-menu.txt"); return; }
            if (what == "dump-settings") { var st = Object.FindFirstObjectByType<Settings>(); if (st != null) DevDump.Tree("settings", st.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-settings.txt"); return; }
            if (what.StartsWith("cfg:") && Plugin.Instance != null)
            {   // cfg:<section>.<key>=<value>: a Jatify setting, as the settings page would set it (requests arrive lower-cased)
                var kv = what.Substring(4).Split('='); var cfg = Plugin.Instance.Config;
                var def = kv.Length == 2 ? cfg.Keys.FirstOrDefault(d => (d.Section + "." + d.Key).ToLowerInvariant() == kv[0]) : null;
                if (def != null) { var e = cfg[def]; e.BoxedValue = BepInEx.Configuration.TomlTypeConverter.ConvertToValue(kv[1], e.SettingType); }
                Log.Info($"dev cfg {kv[0]}: {(def != null ? cfg[def].BoxedValue : "no such setting")}"); return;
            }
            if (what.StartsWith("remove:") && Player.m_localPlayer != null && ZNetScene.instance != null)
            {   // remove:<name>: the nearest networked object within 15 m whose name has it (a test mount, without t:clear taking the test rows too)
                var me = Player.m_localPlayer.transform.position; var key = what.Substring(7);
                var nv = Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => v != null && v.IsValid() && v.gameObject != Player.m_localPlayer.gameObject && v.name.ToLowerInvariant().Contains(key) && Vector3.Distance(v.transform.position, me) < 15f).OrderBy(v => Vector3.Distance(v.transform.position, me)).FirstOrDefault();
                if (nv != null) ZNetScene.instance.Destroy(nv.gameObject);
                Log.Info($"dev remove {key}: {(nv != null ? nv.name : "none")}"); return;
            }
            if (what.StartsWith("eat:") && Player.m_localPlayer != null)
            {   // eat:<item>[:<share left>]: a food from the game's item list, not the bag (the HUD's food timers and fills); e.g. eat:Raspberry:0.05
                var a = what.Split(':'); var pl = Player.m_localPlayer; var go = ObjectDB.instance?.m_items.Find(x => x != null && x.name.ToLowerInvariant() == a[1]); var it = go != null ? go.GetComponent<ItemDrop>()?.m_itemData.Clone() : null;   // requests arrive lower-cased
                if (it != null) it.m_dropPrefab = go;   // a clone has none; Valheim names the food by it
                bool ok = it != null && pl.EatFood(it);
                if (ok && a.Length > 2 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var k)) foreach (var f in pl.GetFoods()) if (f.m_item.m_shared.m_name == it.m_shared.m_name) f.m_time = f.m_item.m_shared.m_foodBurnTime * k;
                Log.Info($"dev eat {a[1]}: {ok}"); return;
            }
            if (what == "dump-hud" && Root.Canvas != null) { DevDump.Tree("hud", Root.Canvas.transform, Path.GetDirectoryName(_dir)); Log.Info("dev dump -> dump-hud.txt"); return; }
            if (what.StartsWith("dump") && InventoryGui.instance != null)
            {   // "dump": the inventory screen's live object tree, as it is now (after our restyle)
                DevDump.Tree("live", InventoryGui.instance.transform, Path.GetDirectoryName(_dir));
                Log.Info("dev dump -> dump-live.txt"); return;
            }
            if (what == "inventory" && InventoryGui.instance != null && !InventoryGui.IsVisible()) InventoryGui.instance.Show(null);
            if (what == "chest" && InventoryGui.instance != null && Player.m_localPlayer != null)
            {   // the nearest chest within 15 m, opened as if you used it
                var me = Player.m_localPlayer.transform.position; Container best = null; float bd = 15f;
                foreach (var c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(me, c.transform.position);
                    if (d < bd && c.GetInventory() != null && c.m_checkGuardStone == false || d < bd && c.GetInventory() != null) { bd = d; best = c; }
                }
                if (best != null) InventoryGui.instance.Show(best); else Log.Info("dev shot: no chest within 15 m");
            }
            if (what.StartsWith("map-hover:") && Minimap.instance != null) what = "map-click:" + what.Substring(10) + "|hover";   // pointer enter instead of a click
            if (what.StartsWith("map-click:") && Minimap.instance != null)   // click a child of the big map by path, then shoot
            {
                bool hov = what.EndsWith("|hover"); if (hov) what = what.Substring(0, what.Length - 6);
                Transform go = Minimap.instance.m_largeRoot.transform;   // the request comes lowercased: match names without case
                foreach (var part in what.Substring(10).Split('/')) { var nm = part.Split('#'); int nth = nm.Length > 1 ? int.Parse(nm[1]) : 0; Transform hit = null; if (go != null) foreach (Transform ch in go) if (ch.name.ToLowerInvariant() == nm[0] && nth-- == 0) { hit = ch; break; } go = hit; }   // name#n: the n-th of that name
                var ev = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
                if (go != null && hov) UnityEngine.EventSystems.ExecuteEvents.Execute(go.gameObject, ev, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                else if (go != null) UnityEngine.EventSystems.ExecuteEvents.Execute(go.gameObject, ev, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                else Log.Warn("map-click: no " + what.Substring(10));
                what = "asis";
            }
            if (what == "map-addpin" && Minimap.instance != null && Player.m_localPlayer != null)   // vanilla's "double-click": a pin and its name field, at you
            { HarmonyLib.AccessTools.Method(typeof(Minimap), "ShowPinNameInput").Invoke(Minimap.instance, new object[] { Player.m_localPlayer.transform.position + new Vector3(30f, 0f, 30f) }); what = "asis"; }
            if (what == "map" && Minimap.instance != null) Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            if (what == "build" && Player.m_localPlayer != null && Hud.instance != null)
            {   // test characters only: make sure a hammer is in hand, then open the build menu
                if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
                var p = Player.m_localPlayer; var inv = p.GetInventory();
                var hammer = inv.GetAllItems().Find(i => i.m_shared.m_buildPieces != null);
                if (hammer == null) { inv.AddItem("Hammer", 1, 1, 0, 0L, "", false); hammer = inv.GetAllItems().Find(i => i.m_shared.m_buildPieces != null); }
                if (hammer != null && !hammer.m_equipped) p.EquipItem(hammer);
                if (!Hud.IsPieceSelectionVisible()) Hud.instance.TogglePieceSelection();
            }
            if (what == "menu" && Menu.instance != null && !Menu.IsVisible()) { if (Minimap.instance != null) Minimap.instance.SetMapMode(Minimap.MapMode.Small); Menu.instance.Show(); }
            if (what == "settings" && Menu.instance != null) { if (!Menu.IsVisible()) Menu.instance.Show(); Menu.instance.OnSettings(); }
            if (what == "skills" && InventoryGui.instance != null) { if (!InventoryGui.IsVisible()) InventoryGui.instance.Show(null); InventoryGui.instance.OnOpenSkills(); }
            if (what == "track3" && Deeds.Model.Ready)
            {   // dev: track the three unearned achievements closest to done
                foreach (var d in Deeds.Model.All.Where(x => !x.IsEarned && x.Criteria.Count > 0).OrderByDescending(x => (float)x.Done / x.Criteria.Count).Take(3))
                    if (!Deeds.Model.IsTracked(d)) Deeds.Model.ToggleTrack(d);
            }
            if (what == "banners" && Deeds.Model.Ready && Root.Ach != null) foreach (var d in Deeds.Model.All.Take(5)) Root.Ach.Show(d);
            if (what == "page-jui" || what == "page-mods") ConfigPage.Show(Root.Canvas.transform, Plugin.Instance.ThemeData, what == "page-mods");
            if (what == "hud") { ConfigPage.Open?.Close(); Hud.HidePieceSelection(); var st = Object.FindFirstObjectByType<Settings>(); if (st != null) st.OnBack(); if (Menu.IsVisible()) Menu.instance.Hide(); if (InventoryGui.IsVisible()) InventoryGui.instance.Hide(); if (Minimap.instance != null) Minimap.instance.SetMapMode(Minimap.MapMode.Small); }
            _wait = 6; // let layout and fades settle
        }

        private static void Capture()
        {
            var shots = Path.Combine(_dir, "shots"); Directory.CreateDirectory(shots);
            var n = Directory.GetFiles(shots, "shot-*.png").Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f).Substring(5), out var k) ? k : 0).DefaultIfEmpty(0).Max() + 1;
            var path = Path.Combine(shots, $"shot-{n}.png");
            ScreenCapture.CaptureScreenshot(path);
            foreach (var old in Directory.GetFiles(shots, "shot-*.png").OrderByDescending(File.GetLastWriteTimeUtc).Skip(5)) File.Delete(old);
            Log.Info("dev shot -> " + path);
        }
    }
}
