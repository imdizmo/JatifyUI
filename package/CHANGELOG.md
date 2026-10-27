## 0.2.0

- AzuExtendedPlayerInventory (AzuEPI) support: AzuEPI keeps owning the inventory rows and slots, Jatify UI draws them.
  - Its equipment slots (helmet, chest, legs, cape, belt, trinket, Wishbone, Wisplight) sit on the character panel, worn or not; slots other mods add through it (Jewelcrafting's ring and necklace, its Custom Equipment Slots, ...) sit beside the ammo and key pouch under the figure.
  - Its quick slots (up to 8) fill the row under the bag, and show on the HUD tools bar with their keys; its own HUD bar is hidden. Its "Show Quick Slots on HUD" setting is respected.
  - Its Vanity, Loadouts and Stats screens open from three buttons in the inventory header, in Jatify UI's style; vanity looks show on the character figure.
  - Its favourites: Alt + click favourites (instead of also locking), and To chests, Sort, Merge and the chest buttons leave favourited items in place.
  - A character played with Jatify UI alone, loaded with AzuEPI for the first time, keeps every item and keeps its gear on; what doesn't fit waits in a hold and comes back as the bag frees up. Loaded without AzuEPI again, the Wishbone, Wisplight and rings go back into Jatify UI's slots.
  - Settings it takes over (Extra Rows, the Food / Tools / Meads keys) say so on their row.
- New setting Inventory > UtilityTogether: belt, Wishbone and Wisplight are worn together (on, as before) or one at a time as in Valheim (right-click a slot to switch). With AzuEPI it sets AzuEPI's "One Utility Item At A Time".
- EpicLoot support:
  - Two ring slots under the equipment columns (where Main / Off hand were; weapons show on the action bar). Rings worn there, and magic Wishbones and Wisplights in their slots, count as worn for EpicLoot's effects, sets and abilities. With AzuExtendedPlayerInventory installed, its own ring slots are used instead.
  - Item cards show the magic name and rarity, a card of enchantments, sockets and set bonuses, and a rarity border; EpicLoot's own tooltip box no longer shows behind ours.
  - Rarity backgrounds on the equipment slots, the HUD action bars and the upgrade list, and rarity-coloured names on items lying on the ground.
  - The enchanting table and Haldor's and Hildir's EpicLoot panels in Jatify UI's look; the trader panel sits beside the store window instead of under it. The table spends materials from nearby chests (stackable materials only, saved safely).
  - Map: Bounties and Treasure chips; bounty pins are no longer removed by the map's clean-up (vehicle pins took the same pin number).
  - EpicLoot abilities show on the HUD beside the Forsaken power, with key and cooldown.
  - EpicLoot's compendium pages (magic effects, sets, treasure and bounties, shardstones) are listed first on the Texts page.
  - EpicLoot's socket shortcut (Ctrl + E) works over worn gear on the equipment panel, and the socket grid shows in Jatify UI's container frame.
  - Set-ability keys (Shield Throw T, ...) show above the right-hand key hints while the ability can be used.
  - Locked bag cells are kept out of the Sacrifice tab; "Sell all" keeps bounty tokens and magic items; Lucky Craft's free crafts stay free when paying from chests.
- The achievement tracker's key is a setting now (Keys > Tracker), U by default: Y is EpicLoot's Spirit Animal key.
- Map: vehicle pins no longer share a pin number with other mods' pin types (BetterMap's ore pins, ...).
- Crafting from chests: with AzuCraftyBoxes installed, Jatify UI no longer also pays for crafts and builds (both mods paid, so a craft cost double; refuelling a Hip Lantern lost the lantern, reported by Leo on Discord). AzuCraftyBoxes does the paying; the crafting panel still counts the chests it uses.
- With AzuCraftyBoxes installed, the crafting and build panels count through it: its range, per-chest exclusions, item rules and Alt+O mode (all / mine / off).
- Hover texts show other mods' key hints as key rows too (e.g. AzuCraftyBoxes' "Shift + E: add coal from nearby containers").
- With AzuCraftyBoxes installed, a station's Auto card opens on Left Alt + use when AzuCraftyBoxes' "fill all" key is Left Shift (one press did both).
- Messages like "It's full" and "Inventory full" show a chest icon instead of a shirt.
- Fixed: dropping an item a reserved row doesn't take onto one of its slots could make the item vanish (a knife dropped on a cultivator in the Tools row, reported on Nexus). The Tools row now takes anything you put there; tools (including other mods' tools) land in a free Tools slot by themselves.
- Fixed: about 190 "LiberationSans SDF Font Asset was not found" warnings in the log on load (reported on Nexus).
- Ammo: the equipped stack leaves the bag like worn gear and shows on the ammo slot with its count, spare stacks of it as "+n"; bows and crossbows on the action bars show how much ammo you carry (asked for on Nexus).
- Graves: Take everything uses Valheim's own grave pickup, so what you wore goes back on (it came back unequipped); graves can't be sorted.
- The crafting panel is easier to read: text 2 px bigger, warnings in a softer orange (reported on Reddit).
- Editing theme.json while playing no longer breaks the crafting panel (Valheim's recipe list came back, page tabs went missing).
- Rebinding a key on the Jatify UI settings page works (it never finished in release builds).

## 0.1.1

- Map: crowded pin groups work again. Hovering a group shows its list, clicking it keeps the list open, and clicking a pin in the list zooms there (reported by Leo on Discord).

## 0.1.0

- First release.
