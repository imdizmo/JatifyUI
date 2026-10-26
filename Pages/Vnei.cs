using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // VNEI's catalogue for the Compendium (user: integrate VNEI). Reflection only: VNEI is never required,
    // and without it (or before it has indexed) the Compendium reads Valheim's own recipes (Lore.FromValheim).
    internal static class Vnei
    {
        internal const string Guid = "com.maxsch.valheim.vnei";

        private static bool _bound, _ok;
        private static MethodInfo HasIndexed, GetActive, GetIcon, GetName, GetMod, UpdateFav, SaveFav;
        private static FieldInfo FInternal, FToken, FType, FDropType, FLocDesc, FPrefab, FFav, PItem, PAmount, PQuality, AMin, AMax, AChance;
        private static PropertyInfo RAll, RIngredients, RResults, RStations, RBlack;

        internal static bool Installed => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(Guid);

        // VNEI is there and has finished indexing
        internal static bool Ready
        {
            get
            {
                if (!Installed || !Bind()) return false;
                try { return (bool)HasIndexed.Invoke(null, null); } catch { return false; }
            }
        }

        private static bool Bind()
        {
            if (_bound) return _ok; _bound = true;
            try
            {
                Type idx = AccessTools.TypeByName("VNEI.Logic.Indexing"), item = AccessTools.TypeByName("VNEI.Logic.Item"), part = AccessTools.TypeByName("VNEI.Logic.Part"),
                     amount = AccessTools.TypeByName("VNEI.Logic.Amount"), recipe = AccessTools.TypeByName("VNEI.Logic.RecipeInfo"), fav = AccessTools.TypeByName("VNEI.Logic.FavouritesSave");
                HasIndexed = AccessTools.Method(idx, "HasIndexed"); GetActive = AccessTools.Method(idx, "GetActiveItems");
                GetIcon = AccessTools.Method(item, "GetIcon"); GetName = AccessTools.Method(item, "GetPrimaryName"); GetMod = AccessTools.Method(item, "GetModName"); UpdateFav = AccessTools.Method(item, "UpdateFavorite", new[] { typeof(bool) });
                FInternal = AccessTools.Field(item, "internalName"); FToken = AccessTools.Field(item, "preLocalizeName"); FType = AccessTools.Field(item, "itemType"); FDropType = AccessTools.Field(item, "itemDropType");
                FLocDesc = AccessTools.Field(item, "localizedDescription"); FPrefab = AccessTools.Field(item, "prefab"); FFav = AccessTools.Field(item, "isFavorite");
                PItem = AccessTools.Field(part, "item"); PAmount = AccessTools.Field(part, "amount"); PQuality = AccessTools.Field(part, "quality");
                AMin = AccessTools.Field(amount, "min"); AMax = AccessTools.Field(amount, "max"); AChance = AccessTools.Field(amount, "chance");
                RAll = AccessTools.Property(recipe, "Recipes"); RIngredients = AccessTools.Property(recipe, "Ingredients"); RResults = AccessTools.Property(recipe, "Results"); RStations = AccessTools.Property(recipe, "Stations"); RBlack = AccessTools.Property(recipe, "IsOnBlacklist");
                SaveFav = fav != null ? AccessTools.Method(fav, "Save") : null;
                _ok = HasIndexed != null && GetActive != null && GetIcon != null && GetName != null && FInternal != null && FType != null && RAll != null
                      && PItem != null && PAmount != null && AMin != null && AMax != null && AChance != null && RIngredients != null && RResults != null && RStations != null;
                if (!_ok) Log.Warn("VNEI is installed but its catalogue doesn't look as expected (a newer VNEI?): the Compendium reads Valheim's own recipes");
            }
            catch (Exception e) { _ok = false; Log.Warn("VNEI: " + e.GetBaseException().Message); }
            return _ok;
        }

        // VNEI's active entries and every recipe between them, into the Compendium's model; false until VNEI is ready.
        // ponytail: a compact copy of VNEI's catalogue (a few thousand small objects), made once per world and dropped with the page
        internal static bool Load(Lore b)
        {
            if (!Ready) return false;
            var byRaw = new Dictionary<object, Lore.Entry>();
            foreach (var kv in (IEnumerable)GetActive.Invoke(null, null))
            {
                var raw = kv.GetType().GetProperty("Value")?.GetValue(kv); if (raw == null) continue;
                var e = new Lore.Entry
                {
                    Raw = raw, Id = FInternal.GetValue(raw) as string, Token = FToken?.GetValue(raw) as string ?? "", Name = GetName.Invoke(raw, null) as string,
                    Mod = GetMod?.Invoke(raw, null) as string ?? "Valheim", Icon = GetIcon.Invoke(raw, null) as Sprite, Desc = FLocDesc?.GetValue(raw) as string ?? "",
                    Prefab = FPrefab?.GetValue(raw) as GameObject, Fav = FFav != null && (bool)FFav.GetValue(raw)
                };
                int type = Convert.ToInt32(FType.GetValue(raw));   // VNEI.Logic.ItemType: 0 Undefined, 1 Creature, 2 Piece, 3 Item, 4 Food, 5 Armor, 6 Weapon
                var drop = FDropType != null ? (ItemDrop.ItemData.ItemType)Convert.ToInt32(FDropType.GetValue(raw)) : ItemDrop.ItemData.ItemType.None;
                e.Chapter = type == 1 ? Lore.Bestiary : type == 2 ? Lore.Building : type == 4 ? Lore.Provisions : type == 5 || type == 6 ? Lore.Arms
                          : type == 3 && drop == ItemDrop.ItemData.ItemType.Material ? Lore.Materials : Lore.Other;
                e.Kind = type == 1 ? "Creature" : type == 2 ? "Piece" : type == 3 ? Lore.KindOf(drop) : type == 4 ? "Food" : type == 5 ? "Armour" : type == 6 ? "Weapon" : "Other";
                if (string.IsNullOrEmpty(e.Name) || e.Id == null) continue;
                byRaw[raw] = e; b.Add(e);
            }
            foreach (var r in (IEnumerable)RAll.GetValue(null))
            {
                if (RBlack != null && (bool)RBlack.GetValue(r)) continue;
                var rec = new Lore.Recipe();
                foreach (var p in (IEnumerable)RStations.GetValue(r)) { var s = ToPart(p, byRaw); if (s.E != null) rec.Stations.Add(s); }
                Groups(RIngredients.GetValue(r), rec.Ins, byRaw); Groups(RResults.GetValue(r), rec.Outs, byRaw);
                b.Link(rec);
            }
            return true;
        }

        // RecipeInfo's Ingredients / Results: Dictionary<Amount, List<Part>> (the key is a group's own amount; the parts carry theirs)
        private static void Groups(object dict, List<Lore.Part> into, Dictionary<object, Lore.Entry> byRaw)
        {
            if (!(dict is IDictionary d)) return;
            foreach (DictionaryEntry g in d)
            {
                if (Convert.ToInt32(AMax.GetValue(g.Key)) == 0 || !(g.Value is IEnumerable parts)) continue;   // VNEI's own rule: a group of max 0 never drops
                foreach (var p in parts) { var x = ToPart(p, byRaw); if (x.E != null) into.Add(x); }
            }
        }

        private static Lore.Part ToPart(object p, Dictionary<object, Lore.Entry> byRaw)
        {
            var raw = PItem.GetValue(p); var amt = PAmount.GetValue(p);
            Lore.Entry e = null; if (raw != null) byRaw.TryGetValue(raw, out e);
            return new Lore.Part { E = e, Min = (int)AMin.GetValue(amt), Max = (int)AMax.GetValue(amt), Chance = (float)AChance.GetValue(amt), Level = PQuality != null ? (int)PQuality.GetValue(p) : 1 };
        }

        // marking writes back through VNEI: favourites stay shared with VNEI's own window
        internal static void SetFavourite(object raw, bool on)
        {
            if (raw == null || UpdateFav == null) return;
            try { UpdateFav.Invoke(raw, new object[] { on }); SaveFav?.Invoke(null, null); } catch (Exception e) { Log.Warn("VNEI favourite: " + e.GetBaseException().Message); }
        }

        // VNEI's own tab next to Craft / Upgrade: the Compendium tab replaces it (user); VNEI makes it again now and then, so this runs each tick
        internal static void HideTab()
        {
            if (!Installed) return;
            if (_tab == null) { var g = InventoryGui.instance; if (g == null || g.m_crafting == null) return; _tab = g.m_crafting.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "VNEI")?.gameObject; if (_tab == null) return; }
            if (_tab.activeSelf) _tab.SetActive(false);
        }
        private static GameObject _tab;

        internal static void Forget() { _bound = false; _ok = false; _tab = null; }
    }
}
