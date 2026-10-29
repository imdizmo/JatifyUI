using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jatify.Campfire
{
    // The only place that touches Valheim's start menu (FejdStartup). Everything that reads or writes
    // saves goes through Valheim's own methods here; the screen only calls these.
    internal static class Fejd
    {
        internal static FejdStartup F => FejdStartup.instance;
        private static Traverse T => Traverse.Create(F);

        internal static List<PlayerProfile> Profiles
        {
            get
            {
                var list = T.Field("m_profiles").GetValue<List<PlayerProfile>>();
                if (list == null) { list = SaveSystem.GetAllPlayerProfiles(); T.Field("m_profiles").SetValue(list); }
                return ShowOnly == null ? list : list.Where(pr => ShowOnly.Contains(pr.GetName().ToLowerInvariant())).ToList();
            }
        }

        internal static int ProfileIndex
        {
            get => T.Field("m_profileIndex").GetValue<int>();
            set => T.Field("m_profileIndex").SetValue(value);
        }

        internal static List<World> Worlds => ShowOnly == null ? SaveSystem.GetWorldList() ?? new List<World>() : (SaveSystem.GetWorldList() ?? new List<World>()).Where(w => ShowOnly.Contains(w.m_name.ToLowerInvariant())).ToList();

        // dev screenshots (DevShot showonly:<names>): only these characters and worlds are listed; the saves themselves are untouched.
        // Not for playing while set: the list indices no longer match Valheim's own.
        internal static HashSet<string> ShowOnly;
        // dev screenshots (DevShot serveralias:<name>): every server is shown under this name; joining and passwords use the real one
        internal static string ServerAlias;
        internal static string Shown(string host) => ServerAlias ?? host;

        internal static World World
        {
            get => T.Field("m_world").GetValue<World>();
            set => T.Field("m_world").SetValue(value);
        }

        internal static Transform PreviewPoint => F.m_characterPreviewPoint;
        internal static GameObject PlayerPrefab => F.m_playerPrefab;
        internal static Camera Camera => F.m_mainCamera != null ? F.m_mainCamera.GetComponentInChildren<Camera>() : Camera.main;
        internal static Transform CameraMarker => F.m_cameraMarkerCharacter;

        internal static void HideVanillaPreview()
        {
            var go = T.Field("m_playerInstance").GetValue<GameObject>();
            if (go != null) go.SetActive(false);
        }
        // The title screen: the four at the fire stand there too (user), as on the character screen, the one picked here
        // among them; Valheim's own single character stays hidden. Twice a second is plenty: Show does nothing unchanged.
        // TitleParty off: Valheim's own character instead, also when coming back from our screen with its party.
        private static float _titleAt;
        internal static void TitleNow() => _titleAt = 0f;
        internal static void TitleTick()
        {
            if (Time.unscaledTime < _titleAt) return;
            _titleAt = Time.unscaledTime + 0.5f;
            bool title = F != null && F.m_mainMenu != null && (F.m_mainMenu.activeInHierarchy || UI.ConfigPage.Open != null);   // Jatify's settings page on the title screen hides the menu: the switch still shows live
            if (!title || CharScreen.Visible || NewCharView.Visible || PreviewPoint == null) return;
            var all = Profiles; if (all == null || all.Count == 0) return;
            if (!Module.TitleParty.Value) { if (Party.Members.Count > 0) { Party.Clear(); ShowVanillaPreview(all.ElementAtOrDefault(ProfileIndex)); } return; }
            Party.Show(Party.Chosen(all, all.ElementAtOrDefault(ProfileIndex)));
            Party.Select(null);
            HideVanillaPreview();
        }

        // without Campfire (unloaded): Valheim's own character by the fire again, the one picked here
        internal static void ShowVanillaPreview(PlayerProfile picked)
        {
            if (F == null) return;
            if (picked != null && Profiles.Contains(picked)) { ProfileIndex = Profiles.IndexOf(picked); T.Method("SetupCharacterPreview", new[] { typeof(PlayerProfile) }).GetValue(picked); }
            var go = T.Field("m_playerInstance").GetValue<GameObject>();
            if (go != null) go.SetActive(true);
        }

        // ---- actions, all Valheim's own code ---------------------------------------------------

        private static void Select(PlayerProfile p)
        {
            ProfileIndex = Profiles.IndexOf(p);
            T.Method("SelectCharacter", new[] { typeof(string), typeof(FileHelpers.FileSource) }).GetValue(p.GetFilename(), p.m_fileSource);
        }

        // Returns an error to show, or null when Valheim took over.
        internal static string StartWorld(PlayerProfile p, World w, bool host, bool community, bool crossplay, string password)
        {
            if (p == null) return "Pick a character first";
            if (w == null) return "Pick a world first";
            F.m_openServerToggle.isOn = host;
            F.m_publicServerToggle.isOn = host && community;
            if (F.m_crossplayServerToggle.IsInteractable()) F.m_crossplayServerToggle.isOn = host && crossplay;
            F.m_serverPassword.text = password ?? "";
            World = w;
            if (T.Method("NeedPassword").GetValue<bool>() && !T.Method("IsPublicPasswordValid", new[] { typeof(string), typeof(World) }).GetValue<bool>(password ?? "", w))
                return $"A public server needs a password of {F.m_minimumPasswordLength}+ characters that isn't part of the world name or seed";
            Select(p);
            Passwords.Reset();
            CharScreen.Hide();
            LoadBar.Begin(w.m_name, true);
            F.OnWorldStart();
            return null;
        }

        internal static string JoinServer(PlayerProfile p, ServerJoinData server)
        {
            if (p == null) return "Pick a character first";
            if (!server.IsValid) return "Pick a server first";
            Select(p);
            CharScreen.Hide();
            F.m_characterSelectScreen.SetActive(false);
            F.SetServerToJoin(server);
            Passwords.BeforeJoin(server.ToString());
            LoadBar.Begin(server.ToString(), false);
            F.JoinServer();
            return null;
        }

        internal static void NewCharacter() { CharScreen.Hide(); F.OnCharacterNew(); NewCharView.Show(); }

        // true when the save is gone (a failed backup refuses the delete)
        internal static bool DeleteCharacter(PlayerProfile p)
        {
            T.Field("m_tempRemoveCharacterName").SetValue(p.GetFilename());
            T.Field("m_tempRemoveCharacterSource").SetValue(p.m_fileSource);
            T.Field("m_tempRemoveCharacterIndex").SetValue(Profiles.IndexOf(p));
            GuardDelete.Refused = false;
            F.OnButtonRemoveCharacterYes();   // backed up first by GuardDelete
            if (!PlayerProfile.HaveProfile(p.GetFilename())) return true;
            T.Field("m_profiles").SetValue(SaveSystem.GetAllPlayerProfiles());   // Valheim dropped it from its list anyway
            return false;
        }

        internal static void NewWorld(string name, string seed)
        {
            F.m_newWorldName.text = name;
            F.m_newWorldSeed.text = string.IsNullOrEmpty(seed) ? World.GenerateSeed() : seed;
            F.OnNewWorldDone(false);         // ends in ShowStartGame, which comes back to our screen
        }

        internal static bool DeleteWorld(World w)
        {
            World = w; GuardDelete.Refused = false;
            F.OnButtonRemoveWorldYes();       // backed up first by GuardDelete
            return !(SaveSystem.TryGetSaveByName(w.m_worldName, SaveDataType.World, out var save) && !save.IsDeleted);
        }

        // Valheim's modifier window lives on its world panel: open that, come back on Done/Cancel.
        internal static void Modifiers(World w)
        {
            World = w;
            CharScreen.Hide();
            F.m_characterSelectScreen.SetActive(false);
            F.m_startGamePanel.SetActive(true);
            F.OnServerOptions();
        }

        internal static void ManageSaves(bool worlds) { CharScreen.Hide(); F.OnManageSaves(worlds ? 0 : 1); }

        // Opens Valheim's own server browser (add / remove favourites); its Back returns here.
        internal static void ServerBrowser()
        {
            CharScreen.Hide();
            F.m_characterSelectScreen.SetActive(false);
            F.m_startGamePanel.SetActive(true);
            F.OnServerListTab();
        }

        internal static void Back() { CharScreen.Hide(); CharScreen.RestoreForeign(); F.OnSelelectCharacterBack(); }

        internal static void Reload()
        {
            T.Field("m_profiles").SetValue(SaveSystem.GetAllPlayerProfiles());
            SaveSystem.ClearWorldListCache(reload: true);
        }

        // Adds host:port to Valheim's favourites once its address resolves (as Valheim's own Add server does).
        internal static void AddFavorite(string text, Action<string> done)
        {
            ServerJoinDataUtils.GetAddressAndPortFromString(text, out var ip, out _);
            if (string.IsNullOrEmpty(ip)) { done("Use host:port, e.g. valheim.example.com:2456"); return; }
            var entry = new ServerJoinDataDedicated(text);
            MultiBackendMatchmaking.GetServerIPAsync(entry, (ok, address) =>
            {
                if (!ok || !address.HasValue) { done($"Could not find {text}"); return; }
                EditFavorites(list => { var j = new ServerJoinData(entry); if (!list.Contains(j)) list.Add(j); });
                done(null);
            });
        }

        internal static void RemoveFavorite(ServerJoinData server) => EditFavorites(list => list.Remove(server));

        private static void EditFavorites(Action<LocalServerList> edit)
        {
            var fav = new LocalServerList("favorite", ServerListGui.GetServerListLocations("favorite"));
            edit(fav);
            Log.Info($"favourite servers saved: {fav.Save(true)}");
            fav.Dispose();
        }

        // a favourite's name; its address or crossplay ID (a string of letters, reported on Nexus) only if Valheim has no name for it
        private static readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        internal static string Name(ServerJoinData s) => _names.TryGetValue(s.ToString(), out var n) ? n : s.ToString();

        internal static List<ServerJoinData> FavoriteServers()
        {
            var list = new List<ServerJoinData>();
            try
            {
                // Read-only: a fresh view of Valheim's favourites file, never saved back.
                var fav = new LocalServerList("favorite", ServerListGui.GetServerListLocations("favorite"));
                _names.Clear();
                for (int i = 0; i < fav.Count; i++)
                {   // its name as Valheim's list shows it (saved in the favourites file, or from matchmaking); read while the list is open
                    list.Add(fav[i]);
                    if (MultiBackendMatchmaking.TryGetServerName(fav[i], out string n) && !string.IsNullOrEmpty(n)) _names[fav[i].ToString()] = n;
                }
                fav.Dispose();
            }
            catch (Exception e) { Log.Warn("favourite servers: " + e.Message); }
            return list;
        }
    }

    // ---- hooks ---------------------------------------------------------------------------------
    // The screen's hooks run only while the CharacterScreen switch is on (Module.Screen); off, Valheim's flow is untouched.

    [HarmonyPatch(typeof(FejdStartup), "ShowCharacterSelection")]
    internal static class ShowOurs
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (!Module.Screen) { if (Module.On) { Party.Clear(); Fejd.ShowVanillaPreview(null); } return; }   // Valheim's own screen: its single preview, not the title's party around it
            if (SaveSystem.GetAllPlayerProfiles().Count == 0) return;   // first run: Valheim's new-character flow
            __instance.m_selectCharacterPanel.SetActive(false);
            CharScreen.Show();
        }
    }

    // Valheim's new-character Done / Cancel switch its own select panel back on.
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnNewCharacterDone))]
    internal static class AfterNewCharacter
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (!Module.Screen || !__instance.m_selectCharacterPanel.activeSelf) return;   // name taken: still on the new-character panel
            NewCharView.Hide();
            __instance.m_selectCharacterPanel.SetActive(false);
            CharScreen.Show();
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnNewCharacterCancel))]
    internal static class AfterNewCharacterCancel
    {
        private static void Postfix(FejdStartup __instance) { if (!Module.Screen) return; NewCharView.Hide(); __instance.m_selectCharacterPanel.SetActive(false); CharScreen.Show(); }
    }

    // Everything that would land on Valheim's world panel (new world, modifiers Done/Cancel, manage
    // saves Back, server browser Back) comes back to our screen instead, unless a world is starting.
    [HarmonyPatch(typeof(FejdStartup), "ShowStartGame")]
    internal static class StartGameComesHome
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (!Module.Screen || Traverse.Create(__instance).Field("m_startingWorld").GetValue<bool>()) return;
            __instance.m_startGamePanel.SetActive(false);
            Traverse.Create(__instance).Method("ShowCharacterSelection").GetValue();
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnStartGameBack))]
    internal static class StartGameBack
    {
        private static bool Prefix(FejdStartup __instance)
        {
            if (!Module.Screen) return true;
            __instance.m_startGamePanel.SetActive(false);
            Traverse.Create(__instance).Method("ShowCharacterSelection").GetValue();
            return false;
        }
    }

    // While our screen is up, Valheim's single preview model stays hidden; the party stands instead.
    [HarmonyPatch(typeof(FejdStartup), "SetupCharacterPreview")]
    internal static class HideVanillaPreview
    {
        private static void Postfix() { if (CharScreen.Visible) Fejd.HideVanillaPreview(); }
    }

    // The camera glides to whoever is selected (WoW-style) instead of Valheim's fixed character spot.
    [HarmonyPatch(typeof(FejdStartup), "UpdateCamera")]
    internal static class CameraToSelected
    {
        private static Vector3 _vel, _lookVel, _look;

        private static bool Prefix(FejdStartup __instance, float dt)
        {
            var target = CharScreen.Visible ? Party.Focus() : null;
            if (target == null || __instance.m_mainCamera == null) { _look = Vector3.zero; return true; }
            var cam = __instance.m_mainCamera.transform;
            var feet = target.Go.transform.position;
            var marker = __instance.m_cameraMarkerCharacter;
            // Same height and distance Valheim uses for its own preview, seen from the camera's side.
            var dir = Vector3.ProjectOnPlane(marker.position - __instance.m_characterPreviewPoint.position, Vector3.up).normalized;
            float dist = Vector3.ProjectOnPlane(marker.position - __instance.m_characterPreviewPoint.position, Vector3.up).magnitude;
            float height = marker.position.y - __instance.m_characterPreviewPoint.position.y;
            var pos = feet + dir * Mathf.Clamp(dist * Module.CamDistance.Value, 2f, 12f) + Vector3.up * height;
            var look = feet + Vector3.up * 1.0f;
            if (_look == Vector3.zero) _look = cam.position + cam.forward * 5f;
            cam.position = Vector3.SmoothDamp(cam.position, pos, ref _vel, 0.55f, 1000f, dt);
            _look = Vector3.SmoothDamp(_look, look, ref _lookVel, 0.45f, 1000f, dt);
            cam.rotation = Quaternion.LookRotation(_look - cam.position);
            return false;
        }
    }

    // ---- safety: copy the files away before Valheim deletes, moves or overwrites a save ----------
    // Every delete in Valheim (characters, worlds, backups, from any menu) ends in SaveSystem.Delete;
    // moving between cloud and local and restoring a backup also remove or overwrite files. Each is
    // copied to <save dir>/campfire-backups first, and the operation is refused if nothing was copied.

    internal static class SaveBackup
    {
        // Read on the main thread at startup: these guards also run on Valheim's background workers.
        internal static string DataDir;
        internal static string Root => Path.Combine(DataDir, "campfire-backups");

        internal static bool Copy(string why, SaveFile file)
        {
            if (file == null) return true;
            try
            {
                var dest = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{why}-{Safe(file.Name)}");
                int n = 0;
                var paths = file.IsChunked ? new[] { file.ChunkedDirectory } : file.AllPaths;
                foreach (var p in paths)
                {
                    var found = Resolve(p);
                    if (found == null && file.m_source == FileHelpers.FileSource.Cloud && FromCloud(p, file.IsChunked, dest)) { n++; continue; }
                    if (found == null) { Log.Warn($"backup: can't find {p} ({file.m_source})"); continue; }
                    Directory.CreateDirectory(dest);
                    var target = Path.Combine(dest, Path.GetFileName(found.TrimEnd('/', '\\')));
                    if (Directory.Exists(found)) CopyDir(found, target); else File.Copy(found, target, true);
                    n++;
                }
                Log.Info($"backup before {why}: {n}/{paths.Length} of '{file.Name}' -> {dest}");
                return n > 0 && n == paths.Length;
            }
            catch (Exception e) { Log.Warn($"backup before {why} of '{file.Name}' failed: {e}"); return false; }
        }

        // A cloud save with no copy on disk (Steam installed away from its usual folder, say): read it through Valheim's own
        // cloud storage, as moving a save out of the cloud does. A folder (a chunked world) is copied file by file.
        private static bool FromCloud(string path, bool folder, string dest)
        {
            try
            {
                var name = Path.GetFileName(path.TrimEnd('/', '\\'));
                if (folder)
                {   // Valheim's folder path has no trailing slash (and backslashes on Windows): the folder copy needs one
                    path = path.Replace('\\', '/').TrimEnd('/') + "/";
                    FileHelpers.FileCopyOutFromCloud(new List<string> { path }, new List<string> { Path.Combine(dest, name) + "/" }, false);
                }
                else { Directory.CreateDirectory(dest); FileHelpers.FileCopyOutFromCloud(path, Path.Combine(dest, name), false); }
                return File.Exists(Path.Combine(dest, name)) || Directory.Exists(Path.Combine(dest, name));
            }
            catch (Exception e) { Log.Warn($"backup: can't read {path} from the cloud: {e.Message}"); return false; }
        }

        private static string Safe(string s) => string.Join("_", (s ?? "save").Split(Path.GetInvalidFileNameChars()));

        // The path as Valheim names it, or relative to the save dir, or inside Steam's local cloud mirror.
        private static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var rel = path.Replace('\\', '/').TrimStart('/');
            var tries = new List<string> { path, Path.Combine(DataDir, rel) };
            tries.AddRange(SteamUserdata().Select(r => Path.Combine(r, rel)));
            return tries.FirstOrDefault(t => File.Exists(t) || Directory.Exists(t.TrimEnd('/')))?.TrimEnd('/');
        }

        private static IEnumerable<string> SteamUserdata()
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? "";
            foreach (var steam in new[] { Path.Combine(home, ".local/share/Steam/userdata"), Path.Combine(home, ".steam/steam/userdata"), @"C:\Program Files (x86)\Steam\userdata" })
            {
                if (!Directory.Exists(steam)) continue;
                foreach (var user in Directory.GetDirectories(steam))
                {
                    var remote = Path.Combine(user, "892970", "remote");
                    if (Directory.Exists(remote)) yield return remote;
                }
            }
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.Delete))]
    internal static class GuardDelete
    {
        internal static bool Refused;   // the last delete was refused here (its toast says why)
        private static bool Prefix(SaveFile file, ref bool __result)
        {
            if (SaveBackup.Copy("delete", file)) return true;
            Refused = true;
            CharScreen.Toast($"Could not back up {file?.Name}: NOT deleted");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.MoveSource))]
    internal static class GuardMove
    {
        private static bool Prefix(SaveFile file, ref bool __result, ref bool cloudQuotaExceeded)
        {
            if (SaveBackup.Copy("move", file)) return true;
            CharScreen.Toast($"Could not back up {file?.Name}: NOT moved");
            cloudQuotaExceeded = false;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.RestoreBackup))]
    internal static class GuardRestore
    {
        // Restoring overwrites the current save with the backup: keep a copy of the current one.
        private static bool Prefix(SaveFile backup, ref SaveSystem.RestoreBackupResult __result)
        {
            var current = backup?.ParentSaveWithBackups;
            if (current == null || current.IsDeleted || SaveBackup.Copy("restore", current.PrimaryFile)) return true;
            CharScreen.Toast($"Could not back up {current.Name}: NOT restored");
            __result = SaveSystem.RestoreBackupResult.UnknownError;
            return false;
        }
    }
}
