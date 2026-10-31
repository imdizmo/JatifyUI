using System;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Jatify UI: a full Valheim interface replacement. Everything this plugin creates is owned by Root
    // and torn down in OnDestroy, so ScriptEngine can unload and reload it while the game runs.
    // Rule for every new feature: whatever Awake/Start adds (patches, objects, textures, events,
    // statics) has an undo in Teardown.
    [BepInPlugin(Guid, "Jatify UI", Version)]
    // mods we work with load before us when installed (not required): their API and patches are there when we look
    [BepInDependency(Epic.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Azumatt.AzuCraftyBoxes", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aedenthorn.CraftFromContainers", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Azu.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "jatify.ui", Version = "0.3.0";
        internal static Plugin Instance;
        private Harmony _harmony;
        private Theme _theme;
        internal ThemeData ThemeData => _theme.D;
        private bool _dumped, _invStyled, _buildStyled;

        private void Awake()
        {
            Instance = this;
            Log.Bind(Logger);
            Dev.CountLoad();
            if (Headless)
            {   // a dedicated server: no UI, only the world's station list and far Auto switches for players (ServerLink)
                Cfg.Bind(Config); Smelters.Bind(Config);   // chest range and the Automation switch, for ServerAuto
                AreaPickup.Bind(Config); CoinWeight.Bind(Config); OreScout.Bind(Config); Sync.Bind(Config);   // the settings it hands players who join
                Log.Info($"Jatify UI {Version} on a dedicated server: station list for players' Automation tab, Auto stations run while nobody is near");
                return;
            }
            ExtraRows.Bind(Config);
            Cfg.Bind(Config); Knots.Bind();
            HotKeys.Bind(Config);
            SpecialRows.BindKeys(Config);
            Windows.MainMenu = Config.Bind("HUD", "TitleScreen", true, "The title screen's character and world panels in Jatify's style.");
            Campfire.Module.Bind(Config);   // Start Screen: CharacterScreen, TitleParty, LoadingScreen
            StartMenu.On = Config.Bind("Start Screen", "TitleMenu", true, "The title menu in Jatify's look: large centred entries, Mods and Jatify UI among them, the modded note in a card; off: Valheim's own.");
            AreaPickup.Bind(Config);
            Smelters.Bind(Config);
            CraftQueue.Bind(Config);
            CoinWeight.Bind(Config);
            OreScout.Bind(Config);
            Sync.Bind(Config);   // after every setting the server may hand over is bound
            EnemyBars.Bind(Config);
            AlertFlash.Bind(Config);
            TargetHealth.Bind(Config);
            DamageNumbers.Bind(Config);
            Timers.Bind(Config);
            WhatsNew.Bind(Config);
            FindEmptySlotPatch.Pickup = Config.Bind("Inventory", "PickupOrder", FindEmptySlotPatch.Order.UnderHotbar, "Where picked-up items land: UnderHotbar (bag rows first, the hotbar last) or Vanilla.");
            TooltipKeys.On = Config.Bind("Inventory", "TooltipKeys", true, "Key hints (move, use, split, lock, delete) at the bottom of item tooltips.");
            _theme = new Theme(Paths.ConfigPath);
            Fonts.Load(Paths.CachePath);
#if JATIFY_DEV
            DevShot.Init(Paths.CachePath);
#endif
            Root.Create(_theme);
            _theme.Changed += Root.Restyle;
            _theme.Changed += d => { if (InventoryGui.instance != null) { InventoryStyle.Apply(d); CraftingStyle.Apply(d); } if (Hud.instance != null) BuildStyle.Apply(d); if (Minimap.instance != null) MapStyle.Apply(d); if (Menu.instance != null) MenuStyle.Apply(d); StartMenu.Restore(); };
            Deeds.Model.Earned += OnEarned;
            _harmony = new Harmony(Guid);
            Patch(campfire: false);   // Campfire's in Start, once we know no standalone Campfire runs
            ExtraRows.Apply();   // when reloaded with a player already in the world
            StartLogo.Grab();   // reloaded: on the start screen, or the copy an earlier load made
            Log.Info($"Jatify UI {Version} loaded (load #{Dev.Loads} this session{(Dev.Build ? ", dev build" : "")})");
        }

        // each patch class on its own: one that no longer fits the game (an update, a mistake) turns off only that feature
        private void Patch(bool campfire)
        {
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0 || (type.Namespace == typeof(Campfire.Module).Namespace) != campfire) continue;
                if (type.Namespace == "ServerSync") continue;   // ServerSync patches itself, once (Sync)
                try { _harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception e) { Log.Error($"patch {type.Name} failed, that feature is off: {e.GetBaseException().Message}"); }
            }
        }

        // Campfire, built in: every plugin has run its Awake by now, so a standalone Campfire is in PluginInfos whatever the load order
        private void Start()
        {
            if (Headless || _harmony == null) return;
            Epic.Started = true;   // every plugin has run its Awake: "not installed" is final now
            Safe("epicloot", () => Epic.Hook(_harmony));
            Safe("quick-stack-store", () => Qss.Hook(_harmony));
            Safe("dead-mods", DeadMods.Start);
            if (Campfire.Module.Standalone) { Log.Warn("the standalone Campfire plugin is installed: Jatify UI's built-in Campfire stays off (remove the standalone one to use the Start Screen switches)"); return; }
            Safe("campfire-init", () => Campfire.Module.Init(string.IsNullOrEmpty(Info.Location) ? null : System.IO.Path.GetDirectoryName(Info.Location)));
            if (Campfire.Module.On) Patch(campfire: true);
        }

        internal static readonly bool Headless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        private void Update()
        {
            Safe("server-link", ServerLink.Tick);
            Safe("server-auto", ServerAuto.Tick);   // the server runs Auto stations nobody has loaded
            if (Headless) return;
            _theme?.Tick(); // applies a changed theme file on the main thread
#if JATIFY_DEV
            if (!_dumped && InventoryGui.instance != null && Fonts.Medium != null)
            {
                _dumped = true; // dev: the vanilla inventory layout, for restyling it
                var g = InventoryGui.instance;
                DevDump.Tree("inventory", g.m_player, Paths.CachePath);
                DevDump.Tree("crafting", g.m_crafting, Paths.CachePath);
                DevDump.Tree("container", g.m_container, Paths.CachePath);
                DevDump.Tree("element", g.m_playerGrid.m_elementPrefab.transform, Paths.CachePath);
                DevDump.Tree("inventorygui", g.transform, Paths.CachePath);
                Log.Info("dumped the vanilla inventory layout to BepInEx/cache/jatify.ui");
            }
#endif
            Safe("hud", Root.Tick);
            // The inventory screen exists per world: style it once a player is in, forget it when Valheim
            // destroys it (logout, character or world switch). A failure is logged, never fatal.
            if (InventoryGui.instance != null && Player.m_localPlayer != null && !_invStyled && InventoryGui.IsVisible())   // on first open: text widths read 0 while the panel is inactive
            {
                _invStyled = true;
                try { InventoryStyle.Apply(_theme.D); CraftingStyle.Apply(_theme.D); }
                catch (Exception e) { Log.Error("inventory restyle failed: " + e); }
            }
            if (InventoryGui.instance == null && _invStyled) { _invStyled = false; InventoryStyle.Restore(); CraftingStyle.Restore(); }
            Safe("inventory", InventoryStyle.Tick);
            Safe("crafting", CraftingStyle.Tick);
            if (Hud.instance != null && Player.m_localPlayer != null && !_buildStyled) { _buildStyled = true; Safe("build-apply", () => BuildStyle.Apply(_theme.D)); }
            if (Hud.instance == null && _buildStyled) { _buildStyled = false; BuildStyle.Restore(); }
            Safe("build", BuildStyle.Tick);
            Safe("place-card", PlaceCard.Tick);
            Safe("chest-marks", ChestAssign.TickWorld);
            Safe("store", StoreWindow.Tick);
            Safe("build-menu", BuildMenu.Tick);
            if (Minimap.instance != null && Player.m_localPlayer != null && !MapStyle.AppliedTo(Minimap.instance)) Safe("map-apply", () => MapStyle.Apply(_theme.D));
            if (Menu.instance != null && !MenuStyle.AppliedTo(Menu.instance)) Safe("menu-apply", () => MenuStyle.Apply(_theme.D));
            Safe("windows", () => Windows.Tick(_theme.D));
            Safe("text-entry", () => TextEntry.Tick(_theme.D));   // signs, portal tags, names: every frame while open (the count, the preview)
            Safe("start-menu", () => StartMenu.Tick(_theme.D));   // Valheim's title menu
            Safe("campfire", Campfire.Module.Tick);   // the character screen, the title's party, the loading bar
            Safe("deeds", TickDeeds);
            Safe("quick-keys", SpecialRows.TickKeys);
            Safe("eq-row", EqRow.Tick);
            Safe("menu", MenuStyle.Tick);
            Safe("settings", SettingsStyle.Tick);
            Safe("map", MapStyle.Tick);
            Safe("players", PlayersPage.Tick);
            Safe("hotkeys", HotKeys.Tick);
            Safe("smelters", Smelters.Tick);
            Safe("craft-queue", CraftQueue.Tick);
            Safe("ore-scout", OreScout.Tick);
            Safe("nameplates", EnemyBars.Tick);
            Safe("nameplates-pvp", EnemyBars.PvpTick);
            Safe("alert-flash", AlertFlash.Tick);
            Safe("target-health", TargetHealth.Tick);
            Safe("map-marks", () => MapMarks.Tick(Minimap.instance));
            Safe("mod-slot-lift", ModSlots.TickStaged);
            Safe("screen-check", ScreenCheck.Tick);
            Safe("qss-hide", Qss.Tick);
            Safe("dead-mods-tick", () => { DeadMods.Tick(); DeadMods.TickLate(); });
            Safe("deprecated-mods", DeprecatedMods.Tick);
#if JATIFY_DEV
            if (Dev.Build) Safe("test-panel", TestPanel.Tick);   // F8 test kit and shot.request: dev builds only
            if (Dev.Build) Safe("devshot", DevShot.Tick);
#endif
            Safe("config-page", () => ConfigPage.Open?.Tick());
            Safe("whats-new", WhatsNew.Tick);   // release builds too (key capture, closing with the menu)
        }

        private void LateUpdate() { if (Headless) return; Safe("vanilla-hud", VanillaHud.Tick); Safe("minimap", () => VanillaMinimap.Tick(Root.MapHole)); Safe("layout", InventoryStyle.Layout); Safe("notices", Notices.Tick); Safe("save-badge", SaveBadge.Tick); Safe("chest-peek", ChestPeek.Tick); Safe("graves", Graves.Tick); Safe("cursor", CursorSkin.Tick); Safe("hover-card", HoverCard.Tick); Safe("item-tip", ItemTip.Tick); Safe("epic-skin", EpicSkin.Tick); Safe("azu-hold", AzuHold.Tick); } // after the game's own HUD update and animators

        private static void DevTeardown()
        {
#if JATIFY_DEV
            TestPanel.Destroy();
#endif
        }

        private void OnDestroy() { if (!Headless) Teardown(); }

        // Achievements: load the model per character, re-evaluate every 3 s, announce what is earned.
        private string _deedsFor; private float _deedsNext;
        private void TickDeeds()
        {
            if (Player.m_localPlayer == null || Game.instance == null || Achievements.m_instance == null) { _deedsFor = null; return; }
            var who = Game.instance.GetPlayerProfile().GetFilename();
            if (who != _deedsFor) { _deedsFor = who; Deeds.Model.Load(); }
            if (Time.unscaledTime < _deedsNext) return;
            _deedsNext = Time.unscaledTime + 3f;
            Deeds.Model.Refresh();
        }
        private void OnEarned(Deeds.Deed d) { Root.Ach?.Show(d); Deeds.Model.Tracked.Remove(d.A.m_id); Deeds.Model.SaveRecord(); }

        // One failing part must not stop the others; each failure is reported once per part.
        private readonly System.Collections.Generic.HashSet<string> _reported = new System.Collections.Generic.HashSet<string>();
        private void Safe(string part, Action a)
        {
#if JATIFY_DEV
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            try { a(); }
            catch (Exception e) { if (_reported.Add(part)) Log.Error($"{part} failed (reported once): {e}"); }
#if JATIFY_DEV
            Cost(part, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
#endif
        }

        // a part inside a part, timed on its own in dev builds (release: just runs it)
        internal static void Timed(string part, Action a)
        {
#if JATIFY_DEV
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); a(); Cost(part, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
#else
            a();
#endif
        }

#if JATIFY_DEV
        // dev builds: what each part costs per frame, the 8 dearest logged every 5 s with the frame rate (lag hunts)
        private static readonly System.Collections.Generic.Dictionary<string, long> _cost = new System.Collections.Generic.Dictionary<string, long>();
        private static int _costFrame, _costFrames; private static float _costAt;
        private static void Cost(string part, long ticks)
        {
            _cost.TryGetValue(part, out var c); _cost[part] = c + ticks;
            if (Time.frameCount != _costFrame) { _costFrame = Time.frameCount; _costFrames++; }
            if (Time.unscaledTime < _costAt) return;
            if (_costAt > 0f && _costFrames > 0)
            {
                double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency / _costFrames; long all = 0; foreach (var v in _cost.Values) all += v;
                Log.Info($"cost: {_costFrames / 5f:0} fps, Jatify UI {all * ms:0.00} ms/frame; " + string.Join(", ", _cost.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} {kv.Value * ms:0.00}")) + $" (inventory {(InventoryGui.IsVisible() ? "open" : "closed")})");
            }
            _cost.Clear(); _costFrames = 0; _costAt = Time.unscaledTime + 5f;
        }
#endif

        private void Teardown()
        {
            Safe("epicloot-unhook", Epic.Unhook); Safe("azu-hold", AzuHold.Remove); Safe("azu-panels", InventoryStyle.AzuRestore); Safe("epic-skin-restore", EpicSkin.Restore);
            try { ModSlots.Remove(); } catch (Exception e) { Log.Error("mod slots: " + e); }   // into the save data before anything else
            ExtraRows.Unbind();
            try { ExtraRows.Remove(); } catch (Exception e) { Log.Error("extra rows: " + e); } // before unpatching: pack, shrink, nothing dropped
            try { _harmony?.UnpatchSelf(); } catch (Exception e) { Log.Warn("unpatch: " + e.Message); }
            Safe("qss-unload", Qss.Unload);
            Safe("dead-mods-stop", DeadMods.Stop);
            Safe("deprecated-unload", DeprecatedMods.Destroy);   // its trash can back: it never spawns it twice
            Safe("sync-unload", Sync.Unload);
            Safe("campfire-unload", Campfire.Module.Unload);   // after unpatching (its postfixes stay out of the hand-back), before Kit and Fonts go
            if (_theme != null) { _theme.Changed -= Root.Restyle; _theme.Dispose(); }
            Deeds.Model.Earned -= OnEarned;
            Notices.Restore(); DevTeardown(); SaveBadge.Destroy(); ChestPeek.Destroy(); CursorSkin.Restore(); HoverCard.Destroy(); ItemTip.Destroy();
            SettingsStyle.Restore();
            VanillaHud.Restore();
            VanillaMinimap.Restore();
            InventoryStyle.Restore();
            CraftingStyle.Restore();
            Safe("texts-embed", Pages.CloseEmbedded); BuildMenu.Destroy(); PlayersPage.Close(); Smelters.Clear(); OreScout.Clear(); EnemyBars.Restore(); DamageNumbers.Clear(); AlertFlash.Destroy(); TargetHealth.Destroy();
            BuildStyle.Restore();
            PlaceCard.Destroy(); ChestAssign.Destroy(); StoreWindow.Destroy();
            MapStyle.Restore();
            MenuStyle.Restore();
            Windows.Restore();
            StartMenu.Restore();
            ConfigPage.Open?.Close(); WhatsNew.Destroy();
            Root.Destroy();
            Fonts.Unload();
            Knots.Unload();
            Kit.Unload();
            Instance = null;
            Log.Info("Jatify UI unloaded");
        }
    }
}
