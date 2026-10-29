using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // The character screen from the Jatify design (Start screen: Campfire): worlds/servers in a glass
    // panel on the left, characters on the right, the party at the fire in between, the picked character's name and
    // Enter world at the bottom, Achievements | Stats in a strip at the top. Sizes are the design's at 1920 x 1080.
    // ponytail: every change repaints the lists from scratch; there are a few dozen rows at most.
    internal static class CharScreen
    {
        private const float Pad = 12f;
        private const float LW = 320f, LH = RefH - 24f - 82f, LI = LW - 2 * Pad;
        private const float RW = 320f, RH = LH, RI = RW - 2 * Pad;
        private const float ListTop = 100f, RowH = 44f, WorldOpts = 152f, ServerOpts = 122f;

        private static RectTransform _left, _right, _rightActs, _root, _plates, _tabs, _worldsOpts, _serversOpts, _rot, _enterBox, _toastBox;
        private static RectTransform _chars, _worlds, _statsList;
        private static TMP_InputField _charSearch, _worldSearch, _password;
        private static OnOff _host, _cross, _community, _remember;
        private static TextMeshProUGUI _selName, _selWorld, _toast;
        private static Window _dialog, _statsDialog;
        private static Action _dialogAction;
        private static float _toastUntil, _foreignAt;
        private static bool _builtOn;

        private static PlayerProfile _sel;
        private static World _world;
        private static ServerJoinData _server = ServerJoinData.None;
        private static bool _serversTab, _restored;
        private static List<ServerJoinData> _servers = new List<ServerJoinData>();
        private static readonly List<(PlayerProfile p, RectTransform rt)> _plateList = new List<(PlayerProfile, RectTransform)>();
        private static readonly List<(PlayerProfile p, float mid)> _cardMids = new List<(PlayerProfile, float)>();
        private static readonly List<GameObject> _hiddenForeign = new List<GameObject>();

        internal static RectTransform Root => _root;
        internal static PlayerProfile Selected => _sel;
        internal static World SelectedWorld => _world;
        internal static bool Visible => _root != null && _root.gameObject.activeSelf;

        internal static void Show()
        {
            // Jatify UI came or went since we were built: build again in the look we have now.
            if (_root != null && _builtOn != JatifyLook.On) { UnityEngine.Object.Destroy(_root.root.gameObject); _root = null; }
            if (_root == null) Build();
            Passwords.Reset();
            Fejd.Reload();
            var all = Fejd.Profiles;
            // First time this run: open where you last left from (tab, world or server, character),
            // so after a server night "Start, Join server" is two clicks.
            if (!_restored)
            {
                _serversTab = Module.LastTab.Value == "servers";
                _sel = all.FirstOrDefault(p => p.GetFilename() == Module.LastCharacter.Value);
            }
            if (_sel == null || !all.Contains(_sel)) _sel = all.ElementAtOrDefault(Mathf.Clamp(Fejd.ProfileIndex, 0, all.Count - 1));
            var worlds = Fejd.Worlds;
            _world = _world == null ? null : worlds.FirstOrDefault(w => w.m_worldName == _world.m_worldName);
            if (_world == null && !_restored) _world = worlds.FirstOrDefault(w => w.m_worldName == Module.LastWorld.Value);
            if (_world == null) _world = worlds.FirstOrDefault(w => w.m_worldName == PlatformPrefs.GetString("world", "")) ?? worlds.FirstOrDefault();
            _servers = Fejd.FavoriteServers();
            if (!_restored) _server = _servers.FirstOrDefault(s => s.ToString() == Module.LastServer.Value);
            if (!_servers.Contains(_server)) _server = _servers.FirstOrDefault();
            _restored = true;
            _root.gameObject.SetActive(true);
            _foreignAt = 0f;
            Fejd.HideVanillaPreview();
            Repaint();
        }

        internal static void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            if (_dialog != null) { _dialog.Show(false); _statsDialog.Show(false); AchView.Hide(); SavesView.Hide(); ModsView.Hide(); }
            // the title screen's party has it (user: not the last played); on quitting the game Valheim's save system is already gone
            // (its profile list throws), and there is no title screen to tell
            try { if (_sel != null && Fejd.Profiles.Contains(_sel)) Fejd.ProfileIndex = Fejd.Profiles.IndexOf(_sel); }
            catch (System.NullReferenceException) { }
        }

        internal static void Unload()
        {
            Hide();
            Party.Unload();
            Fejd.ShowVanillaPreview(_sel);
            RestoreForeign();
            if (_root != null) UnityEngine.Object.Destroy(_root.root.gameObject);
            _root = null;
        }

        // Other mods' extras on Valheim's character screen (ReportCard's stats panel and button) sit on
        // top of ours; our Stats window replaces them. They can appear a moment after the screen opens,
        // so this runs twice a second while we're up, and puts them back when we leave.
        private static readonly HashSet<string> Foreign = new HashSet<string> { "StatsPanel", "PlayerStatsPanel", "StatsButton" };

        // Back on Valheim's main menu: give other mods their buttons back.
        internal static void RestoreForeign()
        {
            foreach (var go in _hiddenForeign) if (go != null) go.SetActive(true);
            _hiddenForeign.Clear();
        }

        internal static void HideForeign()
        {
            if (Time.unscaledTime < _foreignAt) return;
            _foreignAt = Time.unscaledTime + 0.5f;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<RectTransform>())
            {
                if (!Foreign.Contains(t.name) || (_root != null && t.IsChildOf(_root.root))) continue;
                t.gameObject.SetActive(false);
                if (!_hiddenForeign.Contains(t.gameObject)) { _hiddenForeign.Add(t.gameObject); Log.Info($"hid {t.name} ({t.root.name})"); }
            }
        }

        // ---- building -------------------------------------------------------------------------

        private static void Build()
        {
            _builtOn = JatifyLook.On;
            _root = MakeCanvas("CampfireCanvas", 5);
            _root.name = "Campfire";
            Vignette(_root);
            _plates = Fill(Node(_root, "plates"));
            BuildTopStrip();
            BuildLeft();
            BuildRight();
            BuildBottom();

            _toastBox = Glass(_root, "toast").rectTransform; _toastBox.GetComponent<Image>().raycastTarget = false;
            At(_toastBox, 0.5f, 1f, 0f, -76f, 400f, 38f);
            _toast = Text(_toastBox, "", "medium", 14f, White, 0f, 0f, 400f, 38f, TextAlignmentOptions.Center);
            Fill(_toast.rectTransform);
            _toastBox.gameObject.SetActive(false);

            _dialog = new Window(_root, "dialog", 480f, 270f, () => _dialog.Show(false));
            _statsDialog = new Window(_root, "stats", 540f, 760f, () => _statsDialog.Show(false));
            _statsDialog.Buttons("Close");
            _statsList = JatifyLook.Scroll(_statsDialog.Body, "list", 0f, 0f, _statsDialog.BodyW, _statsDialog.BodyH);
        }

        // The design's .cf-strip: a small glass bar at the top centre with Achievements | Stats.
        private static void BuildTopStrip()
        {
            var entries = new (string label, Action a)[] { ("Achievements", AchView.Open), ("Stats", OpenStats) };
            var strip = Glass(_root, "topbar").rectTransform;
            float x = 4f;
            for (int i = 0; i < entries.Length; i++)
            {
                if (i > 0) { var sep = Img(strip, null, C("line2"), "sep"); Place(sep.rectTransform, x, 12f, 1f, 18f); x += 5f; }
                var e = entries[i];
                float w = BtnW(e.label, 14f, 18f);
                var b = Box(strip, e.label, NoColor, NoColor, 8); Place(b.rectTransform, x, 4f, w, 34f);
                var t = Text(b.transform, e.label, "medium", 14f, C("dim"), 0f, 0f, w, 34f, TextAlignmentOptions.Center);
                On(b, () => e.a());
                OnHover(b, () => { t.color = White; b.color = C("raised"); }, () => { t.color = C("dim"); b.color = NoColor; });
                x += w + 4f;
            }
            At(strip, 0.5f, 1f, 0f, -22f, x, 42f);
        }

        private static void OpenStats() { _statsDialog.Show(true); PaintStats(); }

        private static void BuildLeft()
        {
            var left = _left = Glass(_root, "left").rectTransform; Place(left, 24f, 24f, LW, LH);
            _tabs = Place(Node(left, "tabs"), Pad, Pad, LI, 30f);
            _worldSearch = Field(left, "Search", Pad, 54f, LI, 36f, "search", _ => PaintLeftList());
            _worlds = JatifyLook.Scroll(left, "list", Pad, ListTop, LI, LH - Pad - WorldOpts - 10f - ListTop);

            // Worlds: switches in two columns, the server password, the world's actions
            _worldsOpts = Place(Node(left, "opts"), Pad, LH - Pad - WorldOpts, LI, WorldOpts);
            Rule(_worldsOpts, 0f, 0f, LI);
            _host = SwitchRow(_worldsOpts, "Host server", 0f, 10f, Module.HostServer.Value, on => { Module.HostServer.Value = on; Repaint(); });
            _cross = SwitchRow(_worldsOpts, "Crossplay", LI / 2f + 4f, 10f, Module.Crossplay.Value, on => Module.Crossplay.Value = on);
            _community = SwitchRow(_worldsOpts, "Community", 0f, 40f, Module.Community.Value, on => Module.Community.Value = on);
            _password = Field(_worldsOpts, "Server password", 0f, 72f, LI, 36f, "lock", password: true);
            Acts(_worldsOpts, 118f, LI, true,
                ("+ New world", NewWorldDialog, true),
                ("Modifiers", () => ModsView.Open(_world), true),
                ("Saves", () => SavesView.Open(true), true),
                ("Delete", DeleteWorldDialog, true));

            // Servers: the warning, Remember passwords, the list's actions
            _serversOpts = Place(Node(left, "sopts"), Pad, LH - Pad - ServerOpts, LI, ServerOpts);
            Rule(_serversOpts, 0f, 0f, LI);
            Para(_serversOpts, "Your favourite servers. Find servers searches community ones. Joining an online server puts your character at risk.", "medium", 12.5f, C("mute"), 0f, 10f, LI, 36f);
            _remember = SwitchRow(_serversOpts, "Remember passwords", 0f, 56f, Module.RememberPasswords.Value, on => Module.RememberPasswords.Value = on, 220f);
            _remember.SetEnabled(Passwords.Available);
            Acts(_serversOpts, 88f, LI, true,
                ("Find servers", Fejd.ServerBrowser, true),   // Valheim's own browser: community servers, search, crossplay (#20)
                ("+ Add", AddServerDialog, true),
                ("Refresh", () => { _servers = Fejd.FavoriteServers(); Repaint(); Toast($"{_servers.Count} favourite servers"); }, true),
                ("Remove", RemoveServerDialog, true));

            float bw = BtnW("Back", 13f, 16f) + 16f;
            Secondary(_root, "Back", 24f, RefH - 24f - 40f, bw, 40f, Fejd.Back, true, 13f, Glyphs.Triangle, 90f);
        }

        private static void BuildRight()
        {
            var right = _right = Glass(_root, "right").rectTransform; At(right, 1f, 1f, -24f, -24f, RW, RH);
            _charSearch = Field(right, "Search characters", Pad, Pad, RI, 36f, "search", _ => PaintChars());
            _chars = JatifyLook.Scroll(right, "list", Pad, 60f, RI, RH - Pad - 34f - 10f - 60f);
            var acts = _rightActs = Place(Node(right, "actions"), Pad, RH - Pad - 34f, RI, 34f);
            Acts(acts, 0f, RI, true,
                ("+ New character", Fejd.NewCharacter, true),
                ("Saves", () => SavesView.Open(false), true),
                ("Delete", DeleteCharacterDialog, true));
        }

        // The design's .cf-bottom: where (Averia), the name (Norse 44), Enter world; and the turn buttons (.cf-turn).
        private static void BuildBottom()
        {
            var bottom = At(Node(_root, "bottom"), 0.5f, 0f, 0f, 30f, 800f, 150f);
            _selWorld = Shadowed(bottom, "", "lore", 15f, C("dim"), 0f, 4f, 800f, 26f, TextAlignmentOptions.Center);
            _selName = Shadowed(bottom, "", "title", 44f, White, 0f, 28f, 800f, 68f, TextAlignmentOptions.Center);
            _enterBox = Place(Node(bottom, "enter"), 270f, 100f, 260f, 50f);

            _rot = Node(_root, "rotate");
            _rot.sizeDelta = new Vector2(78f, 36f);
            var turn = JatifyLook.Icon("undo");
            var l = Secondary(_rot, null, 0f, 0f, 36f, 36f, () => Party.Turn(_sel, -30f), true, 13f, turn ?? Glyphs.Triangle, turn != null ? 0f : 90f);
            var r = Secondary(_rot, null, 42f, 0f, 36f, 36f, () => Party.Turn(_sel, 30f), true, 13f, turn ?? Glyphs.Triangle, turn != null ? 0f : -90f);
            Tip(l, "Turn"); Tip(r, "Turn");
            if (turn != null) r.transform.Find("Icon").localScale = new Vector3(-1f, 1f, 1f);
            if (turn != null) l.transform.Find("Icon").GetComponent<RectTransform>().sizeDelta = r.transform.Find("Icon").GetComponent<RectTransform>().sizeDelta = new Vector2(18f, 18f);
        }

        // ---- painting -------------------------------------------------------------------------

        // dev screenshots: DevShot cs-open:ach|stats|newchar|modifiers|saves-worlds|saves-chars|none
        internal static void DevOpen(string what)
        {
            if (_dialog == null) return;
            _dialog.Show(false); _statsDialog.Show(false); AchView.Hide(); SavesView.Hide(); ModsView.Hide(); NewCharView.Hide();
            switch (what)
            {
                case "ach": AchView.Open(); break;
                case "stats": OpenStats(); break;
                case "newchar": NewCharView.Show(); break;
                case "modifiers": if (_world != null) ModsView.Open(_world); break;
                case "saves-worlds": SavesView.Open(true); break;
                case "saves-chars": SavesView.Open(false); break;
            }
        }

        internal static void DevTab(bool servers) { _serversTab = servers; if (_worldSearch != null) _worldSearch.text = ""; Repaint(); }   // dev screenshots: DevShot cs-tab:worlds|servers

        internal static void Repaint()
        {
            if (!Visible) return;
            var all = Fejd.Profiles;
            var party = Party.Chosen(all, _sel);
            Party.Show(party);
            Party.Select(_sel);
            Fejd.HideVanillaPreview();
            PaintPlates(party);
            PaintChars();
            Clear(_tabs);
            Seg(_tabs, 0f, 0f, LI, new[] { "Worlds", "Servers" }, _serversTab ? 1 : 0, i => { _serversTab = i == 1; _worldSearch.text = ""; Repaint(); });
            _worldsOpts.gameObject.SetActive(!_serversTab);
            _serversOpts.gameObject.SetActive(_serversTab);
            _host.Set(Module.HostServer.Value);
            _cross.Set(Module.Crossplay.Value);
            _community.Set(Module.Community.Value);
            _remember.Set(Module.RememberPasswords.Value && Passwords.Available);
            PaintLeftList();
            SetShadowed(_selName, _sel != null ? _sel.GetName() : "");
            SetShadowed(_selWorld, _serversTab ? (_server.IsValid ? Fejd.Shown(Fejd.Name(_server)) : "")
                : _world != null ? _world.m_name + (Module.HostServer.Value ? " · hosted for friends" : " · just you") : "");
            Clear(_enterBox);
            Primary(_enterBox, _serversTab ? "Join server" : "Enter world", 0f, 0f, 260f, 50f, Enter, _sel != null && (_serversTab ? _server.IsValid : _world != null), false, 16f);
            PaintStats();
        }

        // a row of the lists (.cf-row), flat (user: the boxed Norse rows were too much): no box until hovered; picked: a
        // soft green fill and the name green (no bar: user)
        internal static Image Row(Transform parent, string name, float y, float w, float h, bool on, Action click)
        {
            var row = Box(parent, name, on ? C("accentSoft") : NoColor, NoColor, 8); Place(row.rectTransform, 0f, y, w, h);
            On(row, click);
            if (!on) OnHover(row, () => row.color = C("raised"), () => row.color = NoColor);
            return row;
        }
        // the name, and under it the line, wrapped (user: not cut); the row grows to fit, its height returned
        private static float RowText(Transform row, string name, string sub, float w, bool on)
        {
            Text(row, name, "bold", 14.5f, on ? C("accent") : White, 10f, 4f, w - 20f, 20f);
            var t = Text(row, sub, "medium", 12f, C("mute"), 10f, 23f, w - 20f, 18f, TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Overflow;
            float sh = Mathf.Max(16f, Mathf.Ceil(t.GetPreferredValues(t.text, w - 20f, 0f).y));
            t.rectTransform.sizeDelta = new Vector2(w - 20f, sh + 2f);
            var rt = (RectTransform)row; float h = Mathf.Max(RowH, 23f + sh + 6f); rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
            return h;
        }

        // the panels are only as tall as their lists (up to the full height), the options and buttons right under them
        private static void Fit()
        {
            float lo = _serversTab ? ServerOpts : WorldOpts, lmax = LH - Pad - lo - 10f - ListTop;
            float lh = Mathf.Min(Mathf.Max(_worlds.sizeDelta.y, RowH), lmax);
            ((RectTransform)_worlds.parent).sizeDelta = new Vector2(LI, lh);
            _worldsOpts.anchoredPosition = new Vector2(Pad, -(ListTop + lh + 10f)); _serversOpts.anchoredPosition = _worldsOpts.anchoredPosition;
            _left.sizeDelta = new Vector2(LW, ListTop + lh + 10f + lo + Pad);
            float rmax = RH - Pad - 34f - 10f - 60f, rh = Mathf.Min(Mathf.Max(_chars.sizeDelta.y, RowH), rmax);
            ((RectTransform)_chars.parent).sizeDelta = new Vector2(RI, rh);
            _rightActs.anchoredPosition = new Vector2(Pad, -(60f + rh + 10f));
            _right.sizeDelta = new Vector2(RW, 60f + rh + 10f + 34f + Pad);
        }

        private static void PaintChars()
        {
            Clear(_chars);
            _cardMids.Clear();
            var ordered = Party.Ordered(Fejd.Profiles);
            var q = (_charSearch.text ?? "").Trim().ToLowerInvariant();
            float y = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i == 0 || i == 4)
                {
                    Label(_chars, i == 0 ? "At the fire" : "Others", 2f, y + 6f, RI);
                    y += 28f;
                }
                var p = ordered[i];
                if (q.Length > 0 && !p.GetName().ToLowerInvariant().Contains(q)) continue;
                int index = i;
                var row = Row(_chars, p.GetFilename(), y, RI, RowH, p == _sel, () => { _sel = p; Repaint(); });
                float rh = RowText(row.transform, p.GetName(), Describe(p), RI - 30f, p == _sel);
                // Hover shows up/down arrows; dragging a card anywhere in the list moves it too.
                var moves = Place(Node(row.transform, "moves"), RI - 30f, 3f, 22f, 38f);
                Move(moves, 0f, 0f, index == 4 ? "Move to the fire" : "Move up", () => { Party.Move(p, index - 1, Fejd.Profiles); Party.Rebuild(); });
                Move(moves, 20f, 180f, index == 3 ? "Leave the fire" : "Move down", () => { Party.Move(p, index + 1, Fejd.Profiles); Party.Rebuild(); });
                moves.gameObject.SetActive(false);
                OnHover(row, () => moves.gameObject.SetActive(true), () => moves.gameObject.SetActive(false));
                row.gameObject.AddComponent<CardDrag>().Init(p, row.rectTransform);
                _cardMids.Add((p, y + rh / 2f));
                y += rh + 1f;
            }
            _chars.sizeDelta = new Vector2(0f, y);
            Fit();
        }

        // a small up / down chip (.cf-moves em)
        private static void Move(Transform parent, float y, float turn, string tip, Action a)
        {
            var b = Box(parent, "move", C("raised2"), NoColor, 5); Place(b.rectTransform, 0f, y, 22f, 18f);
            var g = Img(b.transform, Glyphs.Triangle, C("dim"), "g"); At(g.rectTransform, 0.5f, 0.5f, 0f, 0f, 9f, 9f); g.rectTransform.localEulerAngles = new Vector3(0f, 0f, turn);
            On(b, a);
            OnHover(b, () => g.color = White, () => g.color = C("dim"));
            Tip(b, tip);
        }

        // Called by a dragged card on release: drop it where the pointer is in the list.
        internal static void Dropped(PlayerProfile p, float contentY)
        {
            int index = _cardMids.Count;
            for (int i = 0; i < _cardMids.Count; i++)
                if (contentY < _cardMids[i].mid) { index = i; break; }
            var ordered = Party.Ordered(Fejd.Profiles);
            int from = ordered.IndexOf(p);
            var target = index < _cardMids.Count ? ordered.IndexOf(_cardMids[index].p) : ordered.Count;
            if (from < target) target--;
            Party.Move(p, target, Fejd.Profiles);
            Party.Rebuild();
        }

        internal static RectTransform CharContent => _chars;

        // "Cloud · most played Jatify": the world this character spent the most time in.
        private static string Describe(PlayerProfile p)
        {
            var source = (p.m_fileSource == FileHelpers.FileSource.Cloud ? "Cloud" : "Local") + (p.m_usedCheats ? " · no achievements" : "");
            try
            {
                var names = new HashSet<string>(Fejd.Worlds.Select(w => w.m_name));
                var worlds = p.m_playerStats?[0]?.m_knownWorlds;
                var top = worlds?.Where(kv => names.Contains(kv.Key)).OrderByDescending(kv => kv.Value).FirstOrDefault();
                if (top?.Key != null) return $"{source} · most played {top.Value.Key}";
            }
            catch { }
            return source + " save";
        }

        private static void PaintLeftList()
        {
            Clear(_worlds);
            var q = (_worldSearch.text ?? "").Trim().ToLowerInvariant();
            float y = 0f;
            if (!_serversTab)
            {
                foreach (var w in Fejd.Worlds.Where(w => q.Length == 0 || w.m_name.ToLowerInvariant().Contains(q)))
                {
                    bool on = _world != null && w.m_worldName == _world.m_worldName;
                    var row = Row(_worlds, "w" + w.m_worldName, y, LI, RowH, on, () => { _world = w; Repaint(); });
                    var mods = w.m_startingGlobalKeys != null && w.m_startingGlobalKeys.Count > 0 ? "Custom · " : "";
                    float rh = RowText(row.transform, w.m_name,
                        $"{mods}{(w.m_worldName != w.m_name ? w.m_worldName + " · " : "")}{w.m_seedName} · {(w.m_fileSource == FileHelpers.FileSource.Cloud ? "cloud" : "local")}", LI, on);
                    y += rh + 1f;
                }
                if (y == 0f) None(Fejd.Worlds.Count == 0 ? "No worlds yet. Make one below." : "Nothing matches.");
            }
            else
            {
                foreach (var s in _servers.Where(s => q.Length == 0 || s.ToString().ToLowerInvariant().Contains(q) || Fejd.Name(s).ToLowerInvariant().Contains(q)))
                {
                    bool on = s.Equals(_server), saved = Passwords.Has(s.ToString());
                    var row = Row(_worlds, "s" + s, y, LI, RowH, on, () => { _server = s; Repaint(); });
                    float rh = RowText(row.transform, Fejd.Shown(Fejd.Name(s)), saved ? "Favourite · password saved" : "Favourite", on && saved ? LI - 80f : LI, on);
                    if (on && saved)
                        Tip(Secondary(row.transform, "Forget", LI - 10f - 64f, 8f, 64f, 28f, () => { Passwords.Forget(s.ToString()); Repaint(); Toast("Password forgotten"); }, true, 12f),
                            "Forget this server's saved password");
                    y += rh + 1f;
                }
                if (y == 0f) None(_servers.Count == 0 ? "No favourite servers yet. Add one below." : "Nothing matches.");
            }
            _worlds.sizeDelta = new Vector2(0f, y);
            Fit();
        }

        private static void None(string text) => Para(_worlds, text, "medium", 13f, C("mute"), 2f, 6f, LI - 4f, 40f);

        // The party's names over their heads: the picked one larger, white, underlined green.
        private static void PaintPlates(List<PlayerProfile> party)
        {
            Clear(_plates);
            _plateList.Clear();
            foreach (var p in party)
            {
                var rt = Node(_plates, "plate");
                rt.sizeDelta = new Vector2(260f, 40f);
                bool on = p == _sel;
                Shadowed(rt, p.GetName(), "title", on ? 24f : 19f, on ? White : C("dim"), 0f, 0f, 260f, 40f, TextAlignmentOptions.Bottom);
                if (on) { var a = C("accent"); var line = Img(rt, Gradient("plate-line", false, (0f, new Color(a.r, a.g, a.b, 0f)), (0.5f, a), (1f, new Color(a.r, a.g, a.b, 0f))), White, "line"); Place(line.rectTransform, 50f, 44f, 160f, 2f); }   // fades at both ends (user)
                var hit = Img(rt, null, NoColor, "hit"); Fill(hit.rectTransform);
                On(hit, () => { _sel = p; Repaint(); });
                _plateList.Add((p, rt));
            }
        }

        private static void PaintStats()
        {
            if (_statsDialog == null || !_statsDialog.Shown || _sel == null) return;
            _statsDialog.SetTitle(_sel.GetName() + " · Stats");
            Clear(_statsList);
            float y = 0f, w = _statsDialog.BodyW;
            var stats = _sel.m_playerStats?[0]?.m_stats;
            int n = 0;
            if (stats != null)
                foreach (var kv in stats.Where(kv => kv.Value > 0f).OrderBy(kv => kv.Key.ToString()))
                {
                    var row = Place(Node(_statsList, kv.Key.ToString()), 0f, y, w, 30f);
                    if (n++ % 2 == 0) { var bg = Box(row, "bg", C("raised"), NoColor, 6); bg.raycastTarget = false; Fill(bg.rectTransform); }
                    Text(row, System.Text.RegularExpressions.Regex.Replace(kv.Key.ToString(), "(?<=[a-z])(?=[A-Z])", " "), "medium", 13f, C("dim"), 12f, 0f, w - 150f, 30f);
                    Text(row, kv.Value >= 1000f ? kv.Value.ToString("#,0") : kv.Value.ToString("0.##"), "mono", 13f, White, w - 140f, 0f, 128f, 30f, TextAlignmentOptions.MidlineRight);
                    y += 32f;
                }
            if (n == 0) Para(_statsList, "Nothing counted yet for this character.", "lore", 14f, C("mute"), 2f, 4f, w - 4f, 30f);
            _statsList.sizeDelta = new Vector2(0f, y);
        }

        // ---- per frame ----------------------------------------------------------------------------

        internal static void Tick()
        {
            if (Fejd.F == null) { _root = null; return; }
            HideForeign();
            ShowPendingToast();
            if (_toastBox.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toastBox.gameObject.SetActive(false);

            // Name plates and the picked name would show through a window's dimmed backdrop.
            bool modal = Window.AnyOpen;
            _plates.gameObject.SetActive(!modal);
            _selName.transform.parent.gameObject.SetActive(!modal);
            var cam = Fejd.Camera;
            _rot.gameObject.SetActive(false);
            if (modal) return;
            foreach (var (p, rt) in _plateList)
            {
                var m = Party.Members.FirstOrDefault(x => x.Profile == p);
                if (m?.Go == null || cam == null) { rt.gameObject.SetActive(false); continue; }
                var sp = cam.WorldToScreenPoint(m.Head);
                rt.gameObject.SetActive(sp.z > 0f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out var local);
                rt.anchoredPosition = local + new Vector2(0f, 10f);
                if (p != _sel) continue;
                var feet = cam.WorldToScreenPoint(m.Go.transform.position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, feet, null, out var fl);
                _rot.anchoredPosition = fl + new Vector2(0f, -26f);
                _rot.gameObject.SetActive(feet.z > 0f);
            }

            if (EnterFresh || _charSearch.isFocused || _worldSearch.isFocused || _password.isFocused) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Enter();
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                var all = Party.Ordered(Fejd.Profiles);
                if (all.Count > 0)
                {
                    int i = all.IndexOf(_sel) + (Input.GetKeyDown(KeyCode.DownArrow) ? 1 : -1);
                    _sel = all[(i + all.Count) % all.Count];
                    Repaint();
                }
            }
        }

        // ---- actions --------------------------------------------------------------------------

        private static void Enter()
        {
            Module.LastTab.Value = _serversTab ? "servers" : "worlds";
            if (_serversTab && _server.IsValid) Module.LastServer.Value = _server.ToString();
            if (!_serversTab && _world != null) Module.LastWorld.Value = _world.m_worldName;
            if (_sel != null) Module.LastCharacter.Value = _sel.GetFilename();
            var err = _serversTab ? Fejd.JoinServer(_sel, _server)
                                  : Fejd.StartWorld(_sel, _world, Module.HostServer.Value, Module.Community.Value, Module.Crossplay.Value, _password.text);
            if (err != null) Toast(err);
        }

        // "Type the name to confirm" for anything that deletes.
        internal static void ConfirmByName(string title, string what, string name, Action action)
        {
            var box = OpenDialog(title, "Delete", true, 148f);
            Para(box, $"{what} A copy goes to campfire-backups first; nothing is deleted if that copy fails. Type the name to confirm:", "medium", 13f, C("mute"), 0f, 0f, box.sizeDelta.x, 60f);
            var input = LabelledField(box, "Name", name, 80f, box.sizeDelta.x, null, () => _dialogAction?.Invoke());
            Focus(input);
            _dialogAction = () =>
            {
                if (!string.Equals(input.text.Trim(), name, StringComparison.OrdinalIgnoreCase)) { Toast("Name does not match"); return; }
                _dialog.Show(false);
                action();
            };
        }

        private static void DeleteCharacterDialog()
        {
            if (_sel == null) return;
            var p = _sel;
            ConfirmByName("Delete character", $"{p.GetName()} will be deleted.", p.GetName(), () =>
            {
                if (!Fejd.DeleteCharacter(p)) { Show(); return; }   // GuardDelete's toast says why
                _sel = null;
                Show();
                Toast($"{p.GetName()} deleted. A copy is kept in campfire-backups");
            });
        }

        private static void DeleteWorldDialog()
        {
            if (_world == null) return;
            var w = _world;
            ConfirmByName("Delete world", $"{w.m_name} will be deleted.", w.m_name, () =>
            {
                if (!Fejd.DeleteWorld(w)) { Show(); return; }
                _world = null;
                Show();
                Toast($"{w.m_name} deleted. A copy is kept in campfire-backups");
            });
        }

        private static void NewWorldDialog()
        {
            var box = OpenDialog("New world", "Create", false, 170f);
            float w = box.sizeDelta.x;
            var name = LabelledField(box, "Name", "Name your world", 0f, w, null, () => _dialogAction?.Invoke());
            var seed = LabelledField(box, "Seed", "Empty: a random one", 74f, w, null, () => _dialogAction?.Invoke());
            Para(box, "The world is made the first time you enter it.", "medium", 13f, C("mute"), 0f, 148f, w, 22f);
            Focus(name);
            _dialogAction = () =>
            {
                var n = name.text.Trim();
                if (n.Length < 3) { Toast("The name needs 3 or more characters"); return; }
                _dialog.Show(false);
                Fejd.NewWorld(n, seed.text.Trim());
                _world = Fejd.Worlds.FirstOrDefault(x => x.m_worldName == n) ?? _world;
                Show();
            };
        }

        private static void AddServerDialog()
        {
            var box = OpenDialog("Add server", "Add", false, 96f);
            float w = box.sizeDelta.x;
            var addr = LabelledField(box, "Address", "host:port, e.g. valheim.example.com:2456", 0f, w, null, () => _dialogAction?.Invoke());
            Para(box, "Added to your favourite servers.", "medium", 13f, C("mute"), 0f, 74f, w, 22f);
            Focus(addr);
            _dialogAction = () =>
            {
                var text = addr.text.Trim();
                Toast($"Looking up {text}…");
                Fejd.AddFavorite(text, err =>
                {
                    if (err != null) { Toast(err); return; }
                    _dialog.Show(false);
                    _servers = Fejd.FavoriteServers();
                    _server = _servers.LastOrDefault();
                    Repaint();
                    Toast($"{text} added to your favourites");
                });
            };
        }

        // A plain yes/no in the shared dialog.
        internal static void Confirm(string title, string text, string ok, Action action, bool danger = false)
        {
            var box = OpenDialog(title, ok, danger, 60f);
            Para(box, text, "medium", 13f, C("mute"), 0f, 0f, box.sizeDelta.x, 60f);
            _dialogAction = () => { _dialog.Show(false); action(); };
        }

        private static void RemoveServerDialog()
        {
            if (!_server.IsValid) return;
            var s = _server;
            var box = OpenDialog("Remove server", "Remove", true, 40f);
            Para(box, $"Remove {s} from your favourite servers?", "medium", 13f, C("mute"), 0f, 0f, box.sizeDelta.x, 40f);
            _dialogAction = () =>
            {
                _dialog.Show(false);
                Fejd.RemoveFavorite(s);
                Passwords.Forget(s.ToString());
                _servers = Fejd.FavoriteServers();
                _server = _servers.FirstOrDefault();
                Repaint();
                Toast($"{s} removed");
            };
        }

        private static RectTransform OpenDialog(string title, string ok, bool danger, float bodyH)
        {
            _dialogAction = null;
            _dialog.SetSize(480f, Window.Top + bodyH + Window.Bottom);
            _dialog.SetTitle(title);
            _dialog.Buttons("Cancel", ok, () => _dialogAction?.Invoke(), danger);
            Clear(_dialog.Body);
            _dialog.Show(true);   // above any other window that asked
            return _dialog.Body;
        }

        // Safe from any thread: the save guards call this from Valheim's background workers.
        private static volatile string _pendingToast;
        internal static void Toast(string text) { _pendingToast = text; Log.Info(text); }

        private static void ShowPendingToast()
        {
            var text = _pendingToast;
            if (text == null || _toast == null) return;
            _pendingToast = null;
            Set(_toast, text);
            _toastBox.sizeDelta = new Vector2(Mathf.Min(900f, Width(_toast) + 40f), 38f);
            _toastBox.gameObject.SetActive(true);
            _toastBox.SetAsLastSibling();
            _toastUntil = Time.unscaledTime + 3f;
        }
    }

    // Drag a character card up or down the list; dropping above "Others" puts it at the fire.
    internal sealed class CardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private PlayerProfile _p;
        private RectTransform _rt;
        private float _grab;

        internal void Init(PlayerProfile p, RectTransform rt) { _p = p; _rt = rt; }

        public void OnBeginDrag(PointerEventData e)
        {
            _grab = LocalY(e) - _rt.anchoredPosition.y;
            _rt.SetAsLastSibling();
            var g = gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0.85f;
            g.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData e) => _rt.anchoredPosition = new Vector2(_rt.anchoredPosition.x, LocalY(e) - _grab);

        public void OnEndDrag(PointerEventData e) => CharScreen.Dropped(_p, -LocalY(e));

        private float LocalY(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(CharScreen.CharContent, e.position, null, out var local);
            return local.y;
        }
    }
}
