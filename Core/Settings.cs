using BepInEx.Configuration;

namespace Jatify.UI
{
    // Jatify UI's own options (shown on the Jatify UI page; saved by BepInEx in jatify.ui.cfg).
    internal static class Cfg
    {
        internal static ConfigEntry<bool> PowerPanel, GearCollapsed, BarsCollapsed, Hints, FoodAsBuffs, BuffFrames, MinimapPanel, CompassMinimal, SlotBackdrops, Tracker, TrackerCollapsed, Banners, CompassPins, CharacterView, InstantEquip, SkillBanner, PickupFloat, Messages, ConfirmDelete, CharacterIdle, CraftFromChests, BuildFromChests, JatifyCursor, Knotwork, CompUndiscovered, CompDevInfo, GraveReequip, GraveCompass, GraveTouch, UtilityTogether;
        internal enum Regear { Everything, ArmourOnly, ArmourAndWeapons }
        internal static ConfigEntry<Regear> GraveReequipWhat;
        internal static ConfigEntry<int> PinRange, StackRange, CursorSize, MapPinSize, MapNameSize, MapZoomIn;
        internal static ConfigEntry<bool> CompassNames, HideOverlapNames;
        internal enum Clock { Off, H24, H12 }
        internal enum Unlock { Grouped, Every, Off }
        internal static ConfigEntry<Unlock> UnlockNotices;
        internal static ConfigEntry<Clock> RealClock;

        internal static void Bind(ConfigFile c)
        {
            JatifyCursor = c.Bind("HUD", "JatifyCursor", true, "The Jatify mouse cursor instead of the system one.");
            CursorSize = c.Bind("HUD", "CursorSize", 96, new ConfigDescription("Cursor size in pixels.", new AcceptableValueList<int>(32, 48, 64, 96)));
            FoodAsBuffs = c.Bind("HUD", "FoodTimersAsBuffs", true, "Eaten food's time left with the buffs by the minimap; off: in a row of its own above the health bar.");
            BuffFrames = c.Bind("HUD", "BuffFrames", false, "A framed panel behind each buff and food timer; off (the default): Valheim's plain icons.");
            MinimapPanel = c.Bind("HUD", "MinimapPanel", false, "The glass panel behind the minimap and its lines; off (the default): the map and its text stand on their own.");
            CompassMinimal = c.Bind("HUD", "CompassMinimal", false, "The Minimal look's compass: one faint line and the letters, no band or ticks.");
            PowerPanel = c.Bind("HUD", "PowerPanel", false, "The round glass panel behind the guardian power's icon; off (the default): the icon alone.");
            Hints = c.Bind("HUD", "KeyHints", true, "The key hints across the UI: the HUD corners, the inventory and chest key bars, the build menu's and the map's foot rows.");
            SlotBackdrops = c.Bind("HUD", "ActionBarPanels", false, "The glass panel behind the 1-8 bar, the tools bar and the food and meads bar; off (the default): the slots stand on their own.");
            Tracker = c.Bind("HUD", "AchievementTracker", true, "Tracked achievements in the top left corner.");
            TrackerCollapsed = c.Bind("HUD", "TrackerCollapsed", false, "The tracker shows only its header (Y or a click on the header).");
            Banners = c.Bind("HUD", "AchievementBanners", true, "The banner under the compass when you earn an achievement.");
            SkillBanner = c.Bind("HUD", "SkillBanner", true, "Skill level-ups as a banner in the middle of the screen, and new recipes / materials as Jatify cards, instead of Valheim's pop-ups.");
            Messages = c.Bind("HUD", "Messages", true, "Valheim's top-left and centre messages in Jatify's style.");
            PickupFloat = c.Bind("HUD", "PickupFloat", true, "Picked-up items float up softly from your character as '+1' with their icon, instead of Valheim's top-left line.");
            Knotwork = c.Bind("Panels", "Knotwork", true, "Celtic knot ornaments on Jatify's windows: the corners, section dividers and dialog titles; off: plain panels");
            GraveReequip = c.Bind("Grave", "ReequipGear", true, "Picking up your grave puts the gear you wore back on, as it was when you died; off: it goes back to the bag, as in Valheim.");
            GraveReequipWhat = c.Bind("Grave", "ReequipWhat", Regear.Everything, "What ReequipGear puts back on: Everything, ArmourOnly (armour, cape, belt, trinket) or ArmourAndWeapons (and your weapons, shield, tools).");
            GraveCompass = c.Bind("Grave", "CompassMarker", true, "Your grave on the compass with its distance, at any range and even with compass pins off, until you pick it up.");
            GraveTouch = c.Bind("Grave", "PickupOnTouch", false, "Walking into your own grave takes everything at once, without the use key.");
            CompUndiscovered = c.Bind("Compendium", "ShowUndiscovered", false, "List things you haven't found yet by name (greyed, 'not found yet'); off: they read 'Not yet discovered'.");
            CompDevInfo = c.Bind("Compendium", "DeveloperInfo", false, "A box at the end of each compendium page with its ids: prefab, name token, prefab hash, icon sprite and texture, source; click a value to copy it.");
            CompassPins = c.Bind("Compass", "ShowPins", true, "Map pins near you on the compass, with their distance.");
            MapPinSize = c.Bind("Map", "PinScale", 100, new ConfigDescription("Pin size on the world map, in percent.", new AcceptableValueRange<int>(40, 150)));
            MapNameSize = c.Bind("Map", "NameScale", 100, new ConfigDescription("Pin name size on the world map, in percent (0 hides names).", new AcceptableValueRange<int>(0, 150)));
            MapZoomIn = c.Bind("Map", "ZoomIn", 1, new ConfigDescription("How much closer the world map zooms in than Valheim allows (1 = vanilla).", new AcceptableValueRange<int>(1, 4)));
            HideOverlapNames = c.Bind("Map", "HideOverlapNames", true, "Where two pin names on the world map overlap, only the first shows (yours before shared ones).");
            CompassNames = c.Bind("Compass", "ShowNames", false, "Pin names next to their distance on the compass.");
            RealClock = c.Bind("HUD", "RealClock", Clock.Off, "Your computer's time on the minimap card: Off, 24-hour or 12-hour.");
            UnlockNotices = c.Bind("HUD", "UnlockNotices", Unlock.Grouped, "New recipes, pieces and materials: Grouped folds a burst into one card after the first two, Every shows a card each, Off shows none. The New Recipes page keeps them all.");
            PinRange = c.Bind("Compass", "PinRange", 400, new ConfigDescription("How far away a pin may be to show on the compass, in metres.", new AcceptableValueRange<int>(50, 2000)));
            StackRange = c.Bind("Inventory", "ChestRange", 10, new ConfigDescription("How far To chests and Restock reach, in metres.", new AcceptableValueRange<int>(3, 30)));
            GearCollapsed = c.Bind("Inventory", "GearCollapsed", false, "The left column (character, gear, stats, resistances, effects) folded away and the inventory panel narrower (the helmet tab on the panel's left edge).");
            BarsCollapsed = c.Bind("Inventory", "BarsCollapsed", false, "The Tools, Food and Meads rows folded away in the inventory (the axe and sword tab under the panel); they still work from the HUD.");
            CharacterView = c.Bind("Inventory", "CharacterView", true, "Your character, rendered in the equipment column.");
            CharacterIdle = c.Bind("Inventory", "CharacterIdle", true, "The character in the equipment column breathes and idles; off: a still pose.");
            ConfirmDelete = c.Bind("Inventory", "ConfirmDelete", true, "Ask before the trash or the Delete key destroys an item. Turned off by 'Don't ask again' in that prompt.");
            CraftFromChests = c.Bind("Inventory", "CraftFromChests", true, "Crafting counts and takes materials from nearby chests (Chest Range) unless a chest is switched to Excluded from crafting.");
            BuildFromChests = c.Bind("Inventory", "BuildFromChests", true, "Building counts and takes materials from nearby chests too.");
            UtilityTogether = c.Bind("Inventory", "UtilityTogether", true, "Belt, Wishbone and Wisplight are worn together, each in its own slot; off: one at a time as in Valheim (right-click a slot to wear that one). With AzuEPI installed this sets its 'One Utility Item At A Time' (a server enforcing AzuEPI's config decides).");
            UtilityTogether.SettingChanged += (_, __) => { ModSlots.Sync(Player.m_localPlayer); Azu.UtilityRule(); };   // worn / not worn at once
            InstantEquip = c.Bind("Inventory", "InstantEquip", true, "Armour goes on and off at once, without Valheim's equip delay (right click and drag alike).");
        }
    }
}
