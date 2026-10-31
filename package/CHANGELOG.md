## 0.3.0

### Look

- Orange is the new default colour. Colours you picked yourself in `theme.json` stay as they are; the old green defaults move to orange on their own.
- A new icon: the knot in orange.
- What's new: a window with the changes, shown at each world load after an update until you tick Don't show again. Close unlocks once you have scrolled to the end. It is always there afterwards: Esc, then What's new! on the world card (and on the title screen).
- The title screen: Jatify's Discord and What's new! above the bottom-left links.

### Inventory

- Tools, Food and Meads sit in one strip along the bottom of the panel. With a short bag they sit under it beside the character column, and the panel is shorter.
- Food and Meads have 3 slots each. Anything that was in slots 4 to 6 moves into your bag, or drops at your feet if the bag is full.
- Tabs on the panel's edges: the helmet shows or hides the character column, the crossed axe and sword folds the Tools, Food and Meads rows away (both remembered), and the key pouch, the trash and each worn bag (Backpacks, AdventureBackpacks, RustyBags) have tabs of their own.
- A new Stats tab beside Character and Skills: your stats, resistances and effects.
- The character column no longer has hand slots: what you hold is on the action bar and on the figure.
- PvP moved to the top panel, next to the settings button.
- A new character has the extra rows and the Tools, Food and Meads rows from its first spawn (they only appeared after a relog).
- The trash works: drop an item on it, let go of it over it, or click it while holding one. Click it again within 15 seconds to bring the item back (#15).
- Grey labels and values get a dark edge, so they read over the see-through panel.

### HUD

- Food and Meads have their own bar right of the action bar; the guardian power sits right of the health bars, bigger (#31).
- Buffs and food timers are Valheim's plain icons, a little bigger. Ailments move under the buffs when there are many.
- The minimap has no panel behind it; its text and wind arrow have a dark edge so they read over snow. Its clock no longer runs under the wind (#22).
- The compass is one faint line with the letters (Compass minimal, on by default).
- Nameplates and the boss bar are finer: the name between two small red knots and a slim bar in the health bar's red. Nameplate numbers and the boss icon are settings, off at first. Nameplates no longer show through the inventory, menus, the map or text boxes.
- The cheat flags sit in a tab at the top left.
- An effect with no timer shows its own text (#25). Seasonality's time left shows under the season.

### Crafting and building

- Weapons and Armour get a second row of kinds (Swords, Axes, Helmets...) (#38).
- Recipes you can't craft yet are as readable as the rest, and the list has a scroll bar (#16, #35).
- The recipe search starts empty at each station. Long stat lines wrap, and the details scroll when they run long.
- The build menu has an Everything category, shows the piece's name by the cursor, fits its region filters and no longer stalls with every piece unlocked (#17, #18, #34).
- The cultivator and hoe sort their pieces into Crops, Saplings and Terrain.

### Mod support

- ExtraSlots: its slots sit in Jatify's panel and HUD in place of ours (armour and utilities on the doll, quick slots and food under Tools and Food, ammo in a bar of its own bottom left). Slots other mods add (backpacks, jewellery, tomes, lanterns) get a Mod slots tab on the left edge (#29).
- Backpacks, AdventureBackpacks, RustyBags: a tab for each bag you wear opens it.
- Quick Stack Store: To chests, Restock, Sort and the trash use its actions, its own buttons are hidden, and its sort and quick stack leave locked slots, worn gear and slot rows alone.
- Valheim Enchantment System: the Enchant button can be clicked again.
- RustyBags: its chest buttons in Jatify's look.
- Warnings: a mod made for an older Valheim that fails is named once, in the log and on screen; a known broken mod (Sleepover) gets a warning to remove it; so does a screen shape that makes clicks miss the cursor.

### Elsewhere

- Map: the Add pin name box is easy to see (#33).
- Find servers opens the community server list in Jatify's look (#20).
- Deleting a cloud character or world works (#19). The Mods settings list scrolls (#21). A mod's page in Texts keeps its Back button working.
- A horse's saddlebag no longer offers Rename (#26). Take all and Put all have the right icons.
- No missing-character font warnings (#23).

## 0.2.1

Tested with Valheim Enchantment System 1.9.18 (sighsorry) and on a Valheim dedicated server.

### Mod support

#### Valheim Enchantment System

- Its enchant button sits beside Repair in the crafting window, in Jatify UI's look (in Repair's place without a station); the wooden tile it left behind Craft / Upgrade is gone.
- Item tooltips and the crafting details show the scrolls that enchant an item, normal and blessed, with their icons and how many you have, instead of the "Can be enchanted with" line.
- Hovering the scroll in its enchant window shows the scroll's tooltip.
- Its info list scrolls at the same speed as Jatify UI's lists.

Reported by Lucare on Discord ([#5](https://github.com/imdizmo/JatifyUI/issues/5)).

### Servers

- A server running Jatify UI (dedicated, or you hosting) now hands its gameplay settings to the players who join: Automation (stations on Auto, area pickup), chest range, crafting and building from chests, coin weight, and ore and dungeon detection. Each player's own settings come back when they leave. Looks and keys stay each player's own ([#4](https://github.com/imdizmo/JatifyUI/issues/4)).
- The new Server > LockConfiguration (on by default) lets only the server's admins change those settings in game; off, anyone on the server can. On the settings page a synced setting shows the server's value instead of its control.
- Nobody is turned away for not having Jatify UI, and a server without it changes nothing.

### Improvements

- Skills tab: an A to Z order on the sort button, by the names as shown in your language (suggested by omegabattle on Discord, [#12](https://github.com/imdizmo/JatifyUI/issues/12)).

### Bug fixes

- Character screen: favourite crossplay servers show their names instead of their join ID (reported on Nexus).
- Quitting the game no longer logs an error from the character screen.

## 0.2.0

Tested with EpicLoot 0.15.2, AzuExtendedPlayerInventory 2.6.4 and AzuCraftyBoxes 1.8.28.

### Mod support

#### EpicLoot

- Two ring slots under the equipment columns (where Main / Off hand were; weapons show on the action bar). Rings worn there, and magic Wishbones and Wisplights in their slots, count as worn for EpicLoot's effects, sets and abilities. With AzuExtendedPlayerInventory installed, its own ring slots are used instead.
- Item cards show the magic name and rarity, a card of enchantments, sockets and set bonuses, and a rarity border; EpicLoot's own tooltip box no longer shows behind ours.
- Rarity backgrounds on the equipment slots, the HUD action bars and the upgrade list, and rarity-coloured names on items lying on the ground.
- The enchanting table and Haldor's and Hildir's EpicLoot panels in Jatify UI's look; the trader panel sits beside the store window instead of under it. The table spends materials from nearby chests (stackable materials only, saved safely).
- EpicLoot abilities show on the HUD beside the Forsaken power, with key and cooldown; set-ability keys (Shield Throw T, ...) show above the right-hand key hints while the ability can be used.
- Map: Bounties and Treasure chips.
- EpicLoot's compendium pages (magic effects, sets, treasure and bounties, shardstones) are listed first on the Texts page.
- EpicLoot's socket shortcut (Ctrl + E) works over worn gear on the equipment panel, and the socket grid shows in Jatify UI's container frame.
- Locked bag cells are kept out of the Sacrifice tab; "Sell all" keeps bounty tokens and magic items; Lucky Craft's free crafts stay free when paying from chests.

#### AzuExtendedPlayerInventory (AzuEPI)

AzuEPI keeps owning the inventory rows and slots; Jatify UI draws them.

- Its equipment slots (helmet, chest, legs, cape, belt, trinket, Wishbone, Wisplight) sit on the character panel, worn or not; slots other mods add through it (Jewelcrafting's ring and necklace, its Custom Equipment Slots, ...) sit beside the ammo and key pouch under the figure.
- Its quick slots (up to 8) fill the row under the bag, and show on the HUD tools bar with their keys; its own HUD bar is hidden. Its "Show Quick Slots on HUD" setting is respected.
- Its Vanity, Loadouts and Stats screens open from three buttons in the inventory header, in Jatify UI's style; vanity looks show on the character figure.
- Its favourites: Alt + click favourites (instead of also locking), and To chests, Sort, Merge and the chest buttons leave favourited items in place.
- A character played with Jatify UI alone, loaded with AzuEPI for the first time, keeps every item and keeps its gear on; what doesn't fit waits in a hold and comes back as the bag frees up. Loaded without AzuEPI again, the Wishbone, Wisplight and rings go back into Jatify UI's slots.
- Settings it takes over (Extra Rows, the Food / Tools / Meads keys) say so on their row.
- The UtilityTogether setting (below) sets its "One Utility Item At A Time".

#### AzuCraftyBoxes

- AzuCraftyBoxes pays for crafts and builds from chests; Jatify UI no longer also pays (see the fix below). The crafting and build panels count through it: its range, per-chest exclusions, item rules and Alt + O mode (all / mine / off).
- A station's Auto card opens on Left Alt + use when AzuCraftyBoxes' "fill all" key is Left Shift (one press did both).
- Hover texts show other mods' key hints as key rows too (e.g. AzuCraftyBoxes' "Shift + E: add coal from nearby containers").

### Improvements

- Build menu: favourites. Click the star on a piece (it shows when you point at it, and by the piece's name) to add it to the Favourites tab at the top of the categories; saved per character (asked for on Nexus). Right-click on a piece picks it, as in Valheim.
- Ammo: the equipped stack leaves the bag like worn gear and shows on the ammo slot with its count, spare stacks of it as "+n"; bows and crossbows on the action bars show how much ammo you carry (asked for on Nexus).
- The Tools row takes anything you put there (a knife, a circlet, ...); tools, including other mods', land in a free Tools slot by themselves.
- New setting Inventory > UtilityTogether: belt, Wishbone and Wisplight are worn together (on, as before) or one at a time as in Valheim (right-click a slot to switch).
- The crafting panel is easier to read: text 2 px bigger, warnings in a softer orange (reported on Reddit).
- The achievement tracker's key is a setting now (Keys > Tracker), U by default: Y is EpicLoot's Spirit Animal key.
- Messages like "It's full" and "Inventory full" show a chest icon instead of a shirt.

### Performance

Measured in a big base with AzuCraftyBoxes and EpicLoot installed.

- Inventory open: from about 10 to about 30 fps (27 chests nearby). The crafting panel no longer recounts chests every frame.
- Build menu and placement card: from about 12 to about 40 fps (108 chests nearby). Jatify UI's own cost with the menu open went from about 21 ms to 0.3 ms a frame.
- Stations, chest labels and dialogs no longer search the whole world on a timer.
- Without EpicLoot installed, two lookups for its parts are no longer retried every frame.

### Bug fixes

- With AzuEPI, the equipment slots around the character disappeared while a chest was open.
- Meads and food on the HUD action bars and quick slots showed 0 instead of their stack.
- The enchanting table's Upgrade / Sacrifice button flickered between green and orange.
- Repairing a Hip Lantern (or any craft) could lose the item with AzuCraftyBoxes installed: both mods paid for the craft ([#3](https://github.com/imdizmo/JatifyUI/issues/3), reported by Leo on Discord).
- Dropping an item a reserved row doesn't take onto one of its slots could make the item vanish (a knife dropped on a cultivator in the Tools row, reported on Nexus).
- Picking up your grave with Take everything left your gear unequipped; it now goes back on, and graves can't be sorted.
- About 190 "LiberationSans SDF Font Asset was not found" warnings in the log on load (reported on Nexus).
- Unlocking an achievement showed Valheim's own pop-up next to Jatify UI's banner; with AchievementBanners on, only ours shows.
- Map: bounty pins were removed by the map's clean-up, and vehicle pins shared a pin number with other mods' pin types (BetterMap's ore pins, ...).
- Editing theme.json while playing broke the crafting panel (Valheim's recipe list came back, page tabs went missing).
- Rebinding a key on the Jatify UI settings page never finished in release builds.

## 0.1.1

- Map: crowded pin groups work again. Hovering a group shows its list, clicking it keeps the list open, and clicking a pin in the list zooms there (reported by Leo on Discord).

## 0.1.0

- First release.
