using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace Jatify.UI.Deeds
{
    // Valheim's own achievements, read as data: each criterion with its target and current value from
    // the character's stats. Nothing here unlocks anything on Steam; "earned" is this mod's record
    // per character (plus Steam's own unlock, shown when it exists).
    internal sealed class Criterion
    {
        public string Label;
        public float Have, Need;
        public bool Met;
    }

    internal sealed class Deed
    {
        public Achievement A;
        public string Category, Name, Desc;
        public List<Criterion> Criteria = new List<Criterion>();
        public bool Met;            // all criteria met now
        public string Earned;       // date we recorded it, "" if not
        public string SteamEarned;  // Steam unlock date, "" if none
        public int Done => Criteria.Count(c => c.Met);
        public bool IsEarned => Met || !string.IsNullOrEmpty(Earned) || !string.IsNullOrEmpty(SteamEarned);
        public int Points => 10;
    }

    internal static class Model
    {
        internal static readonly List<Deed> All = new List<Deed>();
        internal static readonly List<string> Categories = new List<string>();
        internal static event Action<Deed> Earned;

        private static Dictionary<string, string> _record = new Dictionary<string, string>();
        private static string _recordFor;

        internal static bool Ready => All.Count > 0;

        // Valheim's one achievement list carries no categories (its asset is named "1"): the live design's,
        // by achievement id, and a guess from the id for ones added later.
        private static readonly string[] CategoryOrder = { "Bosses", "Exploration", "Creatures", "Gathering", "Crafting & food", "Building", "Deaths", "Milestones" };
        private static readonly Dictionary<string, string> CategoryById = new Dictionary<string, string> { ["AllBosses"] = "Bosses", ["AllBossesNormal"] = "Bosses", ["AllBuildPieces"] = "Building", ["AllFoodCooked"] = "Crafting & food", ["AllFoodEaten"] = "Crafting & food", ["AllItemCraft"] = "Crafting & food", ["AllMiniBosses"] = "Bosses", ["AllWeaponCraft"] = "Crafting & food", ["Arrived"] = "Exploration", ["BigFish"] = "Gathering", ["Boss1Eikthyr"] = "Bosses", ["Boss2Elder"] = "Bosses", ["Boss3Bonemass"] = "Bosses", ["Boss4Moder"] = "Bosses", ["Boss5Yagluth"] = "Bosses", ["Boss6Queen"] = "Bosses", ["Boss7Fader"] = "Bosses", ["Boss8FrozenKing"] = "Bosses", ["BuildDyrnwyn"] = "Building", ["ChildOfOdin"] = "Milestones", ["DeathByAllTypes"] = "Deaths", ["DeathByObliterator"] = "Deaths", ["DeathByTree"] = "Deaths", ["DeathByTreeAll"] = "Deaths", ["ExploreNSEW"] = "Exploration", ["FindAllTrophies"] = "Creatures", ["FirstDeath"] = "Deaths", ["GrindCook"] = "Crafting & food", ["GrindEat"] = "Crafting & food", ["GrindFish"] = "Gathering", ["GrindForager"] = "Gathering", ["GrindHarvest"] = "Gathering", ["GrindHoney"] = "Gathering", ["GrindLeviathan"] = "Creatures", ["GrindSail"] = "Exploration", ["GrindSailHelm"] = "Exploration", ["GrindTreasure"] = "Exploration", ["GrindTrees"] = "Gathering", ["KillAllCreatures"] = "Creatures", ["KingsQueens"] = "Milestones", ["MultiplayerBoss"] = "Bosses", ["SailEdge"] = "Deaths", ["SoloBoss"] = "Bosses" };
        private static string CategoryOf(string id)
        {
            if (CategoryById.TryGetValue(id ?? "", out var c)) return c;
            var s = (id ?? "").ToLowerInvariant();
            if (s.Contains("boss")) return "Bosses";
            if (s.Contains("death") || s.Contains("die")) return "Deaths";
            if (s.Contains("build") || s.Contains("piece")) return "Building";
            if (s.Contains("craft") || s.Contains("cook") || s.Contains("food") || s.Contains("eat")) return "Crafting & food";
            if (s.Contains("kill") || s.Contains("trophy") || s.Contains("creature")) return "Creatures";
            if (s.Contains("explore") || s.Contains("sail") || s.Contains("biome") || s.Contains("discover")) return "Exploration";
            if (s.Contains("grind") || s.Contains("fish") || s.Contains("harvest") || s.Contains("gather")) return "Gathering";
            return "Milestones";
        }

        internal static void Load()
        {
            All.Clear(); Categories.Clear();
            var inst = Achievements.m_instance;
            if (inst == null || Game.instance == null) return;
            foreach (var list in inst.m_achievementLists)
            {
                if (list == null) continue;
                foreach (var a in list.m_achievements)
                    if (a != null) All.Add(new Deed { A = a, Category = CategoryOf(a.m_id), Name = Loc(a.m_name), Desc = Loc(a.m_description) });
            }
            foreach (var c in CategoryOrder) if (All.Any(d => d.Category == c)) Categories.Add(c);
            LoadRecord();
            Refresh(silent: true);
        }

        // Re-evaluate every criterion; record (and announce) newly earned deeds.
        internal static void Refresh(bool silent = false)
        {
            var profile = Game.instance?.GetPlayerProfile();
            if (profile == null) return;
            if (_recordFor != profile.GetFilename()) LoadRecord();
            bool dirty = false;
            foreach (var d in All)
            {
                d.Criteria = Evaluate(d.A, profile);
                d.Met = d.A.m_unlocked || (d.Criteria.Count > 0 && d.Criteria.All(c => c.Met));
                d.Earned = _record.TryGetValue(d.A.m_id, out var when) ? when : "";
                if (d.SteamEarned == null) d.SteamEarned = SteamDate(d.A);
                if (d.Met && string.IsNullOrEmpty(d.Earned))
                {
                    d.Earned = silent ? "earlier" : DateTime.Now.ToString("yyyy-MM-dd");
                    _record[d.A.m_id] = d.Earned; dirty = true;
                    if (!silent) Earned?.Invoke(d);
                }
            }
            if (dirty) SaveRecord();
        }

        private static List<Criterion> Evaluate(Achievement a, PlayerProfile p)
        {
            var r = new List<Criterion>();
            var s = p.m_playerStats[(int)a.m_difficultyRequirement];
            foreach (var t in a.m_statTrigger)
            {
                s.m_stats.TryGetValue(t.m_stat, out var v);
                r.Add(C(Words(t.m_stat.ToString()), v, t.m_amountAboveEquals, v >= t.m_amountAboveEquals));
            }
            void Dict(List<Achievement.DictStatRequirement> ts, Dictionary<string, float> d, string verb)
            {
                foreach (var t in ts)
                {
                    float v = d != null && d.TryGetValue(t.m_stat, out var x) ? x : 0f;
                    r.Add(C(verb + Loc(t.m_stat), v, t.m_amount, Meets(t.m_operator, v, t.m_amount, d != null && d.ContainsKey(t.m_stat))));
                }
            }
            Dict(a.m_knownWorldTriggers, s.m_knownWorlds, "Visit ");
            Dict(a.m_knownWorldKeysTriggers, s.m_knownWorldKeys, "");
            Dict(a.m_knownCommandsTriggers, s.m_knownCommands, "Use ");
            Dict(a.m_itemPickupTriggers, s.m_itemPickupStats, "Find ");
            Dict(a.m_itemCraftTriggers, s.m_itemCraftStats, "Craft ");
            Dict(a.m_pickableTriggers, s.m_pickableStats, "Pick ");
            Dict(a.m_foodEatenTriggers, s.m_foodEatenStats, "Eat ");
            Dict(a.m_piecePlacedTriggers, s.m_piecesPlacedStats, "Build ");
            foreach (var t in a.m_enemyStatsTriggers)
            {
                var d = s.m_enemyStats[(int)t.m_modifier];
                float v = d != null && d.TryGetValue(t.m_stat, out var x) ? x : 0f;
                var how = t.m_modifier == KillModifiers.MixedAndTotal ? "" : " (" + t.m_modifier.ToString().ToLowerInvariant() + ")";
                r.Add(C("Kill " + Loc(t.m_stat) + how, v, t.m_amount, Meets(t.m_operator, v, t.m_amount, d != null && d.ContainsKey(t.m_stat))));
            }
            foreach (var o in a.m_otherAchievementTriggers)
                if (o != null) r.Add(C(Loc(o.m_name), o.m_unlocked ? 1 : 0, 1, o.m_unlocked || All.Any(x => x.A == o && x.IsEarned)));
            return r;
        }

        private static bool Meets(RequirementOperator op, float v, float need, bool has) => op switch
        {
            RequirementOperator.AboveEquals => has && v >= need,
            RequirementOperator.BelowEquals => has && v <= need,
            RequirementOperator.Equals => has && v == need,
            RequirementOperator.NotEquals => has && v != need,
            _ => false,
        };

        private static Criterion C(string label, float have, float need, bool met) => new Criterion { Label = label, Have = have, Need = need, Met = met };

        // "defeated_eikthyr" -> "Defeated eikthyr"; "$enemy_troll" -> "Troll"
        internal static string Loc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("$") && Localization.instance != null) { var l = Localization.instance.Localize(s); if (!l.StartsWith("[")) return l; }
            return Words(s.TrimStart('$'));
        }

        internal static string Words(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s.Replace('_', ' '), "(?<=[a-z])([A-Z])", " $1").Trim();
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        private static string SteamDate(Achievement a)
        {
            try
            {
                if (!Steamworks.SteamAPI.IsSteamRunning()) return "";
                var id = a.m_platformInternalIds.FirstOrDefault(x => (x.m_platform ?? "").IndexOf("steam", StringComparison.OrdinalIgnoreCase) >= 0).m_internalPlatformId;
                if (string.IsNullOrEmpty(id)) id = a.m_id;
                if (Steamworks.SteamUserStats.GetAchievementAndUnlockTime(id, out bool done, out uint t) && done)
                    return DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime.ToString("yyyy-MM-dd");
            }
            catch { }
            return "";
        }

        // ---- tracked list and earned record, per character -----------------------------------------
        private static string Dir => Path.Combine(Paths.ConfigPath, "jatify.deeds");
        private static string File_(string who) => Path.Combine(Dir, who + ".txt");
        internal static readonly List<string> Tracked = new List<string>();

        private static void LoadRecord()
        {
            _record = new Dictionary<string, string>(); Tracked.Clear();
            _recordFor = Game.instance?.GetPlayerProfile()?.GetFilename();
            if (_recordFor == null) return;
            var f = File_(_recordFor);
            if (!File.Exists(f)) return;
            foreach (var line in File.ReadAllLines(f))
            {
                var kv = line.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "track") Tracked.AddRange(kv[1].Split(',').Where(x => x.Length > 0));
                else _record[kv[0]] = kv[1];
            }
        }

        internal static void SaveRecord()
        {
            if (_recordFor == null) return;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(File_(_recordFor), new[] { "track=" + string.Join(",", Tracked) }.Concat(_record.Select(kv => kv.Key + "=" + kv.Value)));
            }
            catch (Exception e) { Log.Warn("saving deeds: " + e.Message); }
        }

        internal static void ToggleTrack(Deed d)
        {
            if (!Tracked.Remove(d.A.m_id)) { if (Tracked.Count >= 5) Tracked.RemoveAt(0); Tracked.Add(d.A.m_id); }
            SaveRecord();
        }
        internal static bool IsTracked(Deed d) => Tracked.Contains(d.A.m_id);
    }
}
