using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace Jatify.Campfire
{
    // Campfire, a module of Jatify UI: replaces Valheim's character select with a WoW-style one: up to four characters
    // stand at the campfire, the list of all characters sits on the right, and worlds / servers on the left of the same
    // screen. Every save operation (start, join, create, delete) is Valheim's own code; the module only collects the
    // choices. Before Valheim deletes a character or world, the files are copied to <save dir>/campfire-backups, and
    // nothing is deleted if that copy fails. Jatify's Plugin calls Init / Tick / Unload; the switches live on Jatify's
    // settings page (Start Screen), the remembered choices in jatify.campfire.cfg (the standalone Campfire's file).
    internal static class Module
    {
        internal const string StandaloneGuid = "jatify.campfire";
        internal static bool On { get; private set; }   // Init ran: a client, and no standalone Campfire
        internal static string Dir;   // where Jatify UI's DLL sits (LoadingArt looks for campfire-loading beside it)
        internal static ConfigFile Config;
        internal static ConfigEntry<string> Order, Slots;
        internal static ConfigEntry<float> CamDistance;
        internal static ConfigEntry<string> LastTab, LastWorld, LastServer, LastCharacter;
        internal static ConfigEntry<bool> HostServer, Crossplay, Community, RememberPasswords;
        // Jatify UI's own settings (Start Screen)
        internal static ConfigEntry<bool> CharacterScreen, TitleParty, LoadingScreen;
        private static bool _reshow;
        internal static bool Screen => On && CharacterScreen.Value;   // the character screen's hooks

        internal static void Bind(ConfigFile c)
        {
            CharacterScreen = c.Bind("Start Screen", "CharacterScreen", true, "Campfire's character screen: your characters at the fire, their list on the right, worlds and servers on the left; off: Valheim's own character select (from the next time it opens).");
            TitleParty = c.Bind("Start Screen", "TitleParty", true, "Up to four of your characters stand at the fire on the title screen; off: Valheim's single character.");
            LoadingScreen = c.Bind("Start Screen", "LoadingScreen", true, "A loading bar with the world's name and stage, over one of Iron Gate's press-kit artworks; off: Valheim's own loading screen.");
            CharacterScreen.SettingChanged += (_, __) => { if (On && !CharacterScreen.Value) HandBack(); };
            TitleParty.SettingChanged += (_, __) => { if (On) Fejd.TitleNow(); };   // on or off: the next tick swaps the party and Valheim's character
            LoadingScreen.SettingChanged += (_, __) => { if (On && !LoadingScreen.Value) { LoadBar.Unload(); LoadingArt.Unload(); } };
        }

        // A standalone Campfire running too would put up a second screen over ours: then this module stays off.
        internal static bool Standalone => Chainloader.PluginInfos.TryGetValue(StandaloneGuid, out var info) && info.Instance != null;   // a hot-reloaded-away copy leaves its entry with a destroyed Instance

        internal static void Init(string dir)
        {
            Dir = dir;
            SaveBackup.DataDir = Application.persistentDataPath;
            Config = new ConfigFile(Path.Combine(Paths.ConfigPath, "jatify.campfire.cfg"), true);   // the standalone's file: its remembered choices carry over
            Order = Config.Bind("Characters", "Order", "", "Character file names in list order (comma separated); the first four stand at the fire. Set by dragging cards or their up/down arrows.");
            Slots = Config.Bind("Characters", "Slots", "0,0; -1.7,1.4; -3.8,2.1; -5.6,0.4",
                "Where the four characters stand: metres right (+) / left (-) and away from the camera (+), relative to Valheim's own character spot. Ctrl+R in the menu re-reads this file.");
            CamDistance = Config.Bind("Characters", "CameraDistance", 0.85f, "Camera distance to the selected character, as a share of Valheim's own preview distance.");
            LastTab = Config.Bind("Remembered", "Tab", "worlds", "Worlds or servers: the tab the screen opens on (the last one you entered from).");
            LastWorld = Config.Bind("Remembered", "World", "", "File name of the last world you entered.");
            LastServer = Config.Bind("Remembered", "Server", "", "Address of the last server you joined.");
            LastCharacter = Config.Bind("Remembered", "Character", "", "File name of the last character you played.");
            HostServer = Config.Bind("Worlds", "HostServer", true, "Remembered 'Host server' switch.");
            Crossplay = Config.Bind("Worlds", "Crossplay", false, "Remembered 'Crossplay' switch.");
            RememberPasswords = Config.Bind("Servers", "RememberPasswords", true, "Remember server passwords (encrypted in Valheim's prefs) and fill them in when joining.");
            Community = Config.Bind("Worlds", "Community", false, "Remembered 'Community server' switch.");
            _reshow = FejdStartup.instance != null && FejdStartup.instance.m_selectCharacterPanel.activeSelf;   // reloaded on the character screen
            On = true;
        }

        internal static void Tick()
        {
            if (!On) return;
            if (_reshow)
            {
                _reshow = false;
                var f = FejdStartup.instance;
                if (CharacterScreen.Value && f != null && f.m_selectCharacterPanel.activeSelf && SaveSystem.GetAllPlayerProfiles().Count > 0) { f.m_selectCharacterPanel.SetActive(false); CharScreen.Show(); }
            }
            Passwords.Tick();
            PasswordPrompt.Tick();
            LoadBar.Tick();
            if (NewCharView.Visible) { CharScreen.HideForeign(); NewCharView.Tick(); }
            Fejd.TitleTick();
            ServerBrowser.Tick();
            if (!CharScreen.Visible) return;
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.R))
            {
                Config.Reload();
                Party.Rebuild();
                Log.Info("config re-read, party re-placed");
            }
            CharScreen.Tick();
        }

        // Valheim gets its character screen back (switched off, or unloaded): ours and the party go, its panel and preview return.
        private static void HandBack()
        {
            var f = FejdStartup.instance;
            bool shown = CharScreen.Visible || NewCharView.Visible, making = NewCharView.Visible || (f != null && f.m_newCharacterPanel.activeSelf);
            NewCharView.Hide();
            if (making && f != null) f.OnNewCharacterCancel();   // mid New character: back to the list, not both at once (our postfix is unpatched or switched off by now)
            CharScreen.Unload();
            if (shown && f != null) f.m_selectCharacterPanel.SetActive(true);
        }

        // A reload (ScriptEngine) takes everything of ours away and gives Valheim its screens back; Jatify unpatched first.
        internal static void Unload()
        {
            if (!On) return;
            On = false;
            ServerBrowser.Restore();
            HandBack();
            PasswordPrompt.Unload();
            LoadBar.Unload();
            LoadingArt.Unload();
            CampfireUi.Unload();
            Config = null;
        }
    }
}
