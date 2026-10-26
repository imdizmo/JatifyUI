using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Jatify.UI
{
    // An icon for Valheim messages that come without one. Order: an item named in the message ($item_...),
    // the ammo type ($ammo_...), a door key named in an already-translated text, then the message itself
    // (bed, death, portal, map, save, PvP, rest, fire, building, inventory, food, power...).
    internal static class NoticeIcons
    {
        private static readonly Regex ItemToken = new Regex(@"\$item_[a-z0-9_]+", RegexOptions.Compiled);
        private static Dictionary<string, Sprite> _items;   // "$item_wood" -> icon, built once

        private static readonly Dictionary<string, string> ByMessage = new Dictionary<string, string>
        {
            // value: a Valheim sprite or one of our icons (Kit.Icon), "item:Prefab", "piece:Prefab" or "se:Name"
            ["$msg_bedwet"] = "mapicon_bed", ["$msg_bedtooexposed"] = "mapicon_bed", ["$msg_bedneedroof"] = "mapicon_bed", ["$msg_bedenemiesnearby"] = "mapicon_bed",
            ["$msg_bednofire"] = "mapicon_bed", ["$msg_cantsleep"] = "mapicon_bed", ["$msg_goodmorning"] = "mapicon_bed", ["$msg_spawnpointset"] = "mapicon_bed", ["$message_dream"] = "mapicon_bed",
            ["$msg_youdied"] = "mapicon_death", ["$msg_softdeath"] = "mapicon_death", ["$msg_skills_lowered"] = "mapicon_death", ["$piece_tombstone_recovered"] = "mapicon_death",
            ["$msg_noteleport"] = "mapicon_portal", ["$msg_noteleportarea"] = "mapicon_portal", ["$msg_portal_blocked"] = "mapicon_portal",
            ["$msg_mapsaved"] = "mapicon_pin", ["$msg_mapsynced"] = "mapicon_pin", ["$msg_mapnodata"] = "mapicon_pin", ["$msg_alreadysynced"] = "mapicon_pin", ["$msg_pin_exist"] = "mapicon_pin", ["$msg_pin_added"] = "mapicon_pin",
            ["$msg_worldsaved"] = "save_icon", ["$msg_worldsavewarning"] = "save_icon", ["$msg_worldsavewarningonserver"] = "save_icon", ["$msg_worldsaveblockedonserver"] = "save_icon", ["$msg_worldsavedcloudstoragefull"] = "save_icon",
            ["$msg_pvpon"] = "pvp_on", ["$msg_pvpoff"] = "pvp_off",
            ["$se_rested_start"] = "se:Rested", ["$se_resting_start"] = "se:Rested",
            ["$msg_needfire"] = "mapicon_fire", ["$msg_firenotburning"] = "mapicon_fire",
            ["$msg_nobuildzone"] = "hammer", ["$msg_privatezone"] = "hammer", ["$msg_invalidplacement"] = "hammer", ["$msg_needspace"] = "hammer", ["$msg_snapping"] = "hammer",
            ["$msg_wrongbiome"] = "hammer", ["$msg_nosnow"] = "hammer", ["$msg_snowtoodeep"] = "hammer", ["$msg_needdirt"] = "hammer", ["$msg_needcultivated"] = "hammer", ["$msg_blocked"] = "hammer",
            ["$msg_stationneedroof"] = "hammer", ["$msg_stationtooexposed"] = "hammer", ["$msg_extensionmissingstation"] = "hammer", ["$msg_missingstation"] = "hammer", ["$msg_cantremovenow"] = "hammer",
            ["$inventory_full"] = "box", ["$inventory_needspace"] = "box", ["$msg_isfull"] = "box", ["$msg_itsfull"] = "box", ["$msg_noroom"] = "box",   // box: the storage chest ("chest" is the chest-armour slot icon)
            ["$msg_stackall"] = "to-chest", ["$msg_stackall_none"] = "to-chest", ["$msg_cantpickup"] = "box", ["$msg_cantdrop"] = "box",
            ["$msg_food_done"] = "food_icon", ["$msg_cantconsume"] = "food_icon", ["$msg_nocookitems"] = "food_icon", ["$msg_nocookroom"] = "food_icon",
            ["$piece_fermenter_exposed"] = "piece:fermenter", ["$piece_fermenter_needroof"] = "piece:fermenter",
            ["$hud_eitrrequired"] = "item:MeadEitrMinor", ["$msg_noturretammo"] = "item:TurretBolt",
        };

        internal static Sprite For(string raw)
        {
            if (string.IsNullOrEmpty(raw) || ObjectDB.instance == null) return null;
            Items();
            foreach (Match m in ItemToken.Matches(raw)) if (_items.TryGetValue(m.Value, out var s)) return s;
            if (raw.Contains("$ammo_")) return Prefab(raw.Contains("$ammo_bolts") ? "BoltBone" : raw.Contains("$ammo_") ? "ArrowWood" : null);
            if (raw.StartsWith("$hud_powernotready") && Player.m_localPlayer != null) { Player.m_localPlayer.GetGuardianPowerHUD(out var se, out _); return se != null ? se.m_icon : null; }
            int sp = raw.IndexOf(' '); var key = sp > 0 ? raw.Substring(0, sp) : raw;
            if (ByMessage.TryGetValue(key, out var spec)) return Resolve(spec);
            if (!raw.StartsWith("$"))
                foreach (var kv in _keys) if (raw.Contains(kv.Key)) return kv.Value;   // "You need the Swamp key": translated before it reaches us
            return null;
        }

        private static readonly List<KeyValuePair<string, Sprite>> _keys = new List<KeyValuePair<string, Sprite>>();

        private static void Items()
        {
            if (_items != null) return;
            _items = new Dictionary<string, Sprite>();
            foreach (var go in ObjectDB.instance.m_items)
            {
                var d = go ? go.GetComponent<ItemDrop>() : null; if (d == null || d.m_itemData.m_shared.m_icons == null || d.m_itemData.m_shared.m_icons.Length == 0) continue;
                var n = d.m_itemData.m_shared.m_name; if (!_items.ContainsKey(n)) _items[n] = d.m_itemData.GetIcon();
                if (ModSlots.IsKey(d.m_itemData)) _keys.Add(new KeyValuePair<string, Sprite>(Localization.instance.Localize(n), d.m_itemData.GetIcon()));
            }
        }

        private static Sprite Prefab(string name) => name == null ? null : ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();

        private static Sprite Resolve(string spec)
        {
            if (spec.StartsWith("item:")) return Prefab(spec.Substring(5));
            if (spec.StartsWith("piece:")) return ZNetScene.instance?.GetPrefab(spec.Substring(6))?.GetComponent<Piece>()?.m_icon;
            if (spec.StartsWith("se:")) return ObjectDB.instance.GetStatusEffect(spec.Substring(3).GetStableHashCode())?.m_icon;
            return Kit.Icon(spec);
        }

        internal static void Forget() { _items = null; _keys.Clear(); }
    }
}
