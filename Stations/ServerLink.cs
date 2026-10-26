using System.Collections.Generic;
using UnityEngine;

namespace Jatify.UI
{
    // Jatify on a dedicated server (headless: no UI, only this): it answers a player's Automation tab with every station of theirs in
    // the world (built or run by them) and switches Auto on stations nobody has loaded. A server without Jatify never answers: the tab then
    // uses what the player's own game knows (the stations loaded around them). When you host, your game is the server.
    internal static class ServerLink
    {
        private const string Req = "Jatify_StationsReq", List = "Jatify_Stations", Set = "Jatify_SetAuto";
        private const int Version = 2;
        private static ZRoutedRpc _for;
        private static float _askedAt = -99f;

        internal struct Station { public ZDOID Id; public string Prefab; public Vector3 Pos; public bool Auto; public long By, Creator; }
        internal static List<Station> Remote;   // the server's last answer (null: none yet, or it doesn't run Jatify)
        internal static float RemoteAt;

        // every frame: the RPCs belong to the current ZRoutedRpc (a new one per session)
        internal static void Tick()
        {
            var r = ZRoutedRpc.instance; if (r == null || r == _for) return;
            _for = r; Remote = null;
            var fns = HarmonyLib.AccessTools.FieldRefAccess<ZRoutedRpc, System.Collections.IDictionary>("m_functions")(r);
            foreach (var n in new[] { Req, List, Set }) fns.Remove(n.GetStableHashCode());   // a reload of the mod: the old load's handlers go
            r.Register<ZPackage>(Req, OnRequest);
            r.Register<ZPackage>(List, OnList);
            r.Register<ZPackage>(Set, OnSet);
        }

        // ---- server --------------------------------------------------------------------------------------------
        private static void OnRequest(long sender, ZPackage _)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            var kinds = AutomationPage.Kinds(); if (kinds == null) return;
            long player = PlayerOf(sender); if (player == 0L) return;
            // only the asker's own stations (built by them or on Auto by them): the list doesn't show where everyone else's bases are
            var rows = new List<ZDO>();
            foreach (var z in AutomationPage.AllZdos())
                if (z != null && kinds.ContainsKey(z.GetPrefab()) && (z.GetLong(Smelters.ByKey, 0L) == player || z.GetLong(ZDOVars.s_creator, 0L) == player)) rows.Add(z);
            var pkg = new ZPackage(); pkg.Write(Version); pkg.Write(rows.Count);
            foreach (var z in rows)
            {
                pkg.Write(z.m_uid.UserID); pkg.Write(z.m_uid.ID); pkg.Write(z.GetPrefab()); pkg.Write(z.GetPosition());
                pkg.Write(z.GetInt(Smelters.AutoKey, 0) == 1); pkg.Write(z.GetLong(Smelters.ByKey, 0L)); pkg.Write(z.GetLong(ZDOVars.s_creator, 0L));
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, List, pkg);
        }

        // a player switches Auto on a station far from them: one they already have on Auto, or one they built (wards can't be
        // checked on areas nobody has loaded, so nobody switches someone else's station from afar)
        private static void OnSet(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            var id = new ZDOID(pkg.ReadLong(), pkg.ReadUInt()); bool on = pkg.ReadBool();
            long player = PlayerOf(sender); if (player == 0L) return;
            var z = ZDOMan.instance.GetZDO(id); if (z == null || AutomationPage.Kinds() == null || !AutomationPage.Kinds().ContainsKey(z.GetPrefab())) return;
            long by = z.GetLong(Smelters.ByKey, 0L);
            if (!(by == player || (by == 0L && z.GetLong(ZDOVars.s_creator, 0L) == player))) return;
            z.Set(Smelters.AutoKey, on ? 1 : 0); z.Set(Smelters.ByKey, on ? player : 0L);
        }

        // who asks comes from the connection, not the message: the sender's character on this server
        private static long PlayerOf(long sender)
        {
            var peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null; var pz = peer != null && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            // that character must be a player's, and owned by this very connection (not someone else's character it names)
            if (pz == null || pz.GetOwner() != sender || pz.GetPrefab() != "Player".GetStableHashCode()) return 0L;
            return pz.GetLong(ZDOVars.s_playerID, 0L);
            // ponytail: the id itself is written by the player's game, as Valheim's wards and creators are; keying Auto
            // ownership to the platform id (Steam / Xbox) would close that if it ever matters
        }

        // ---- player --------------------------------------------------------------------------------------------
        internal static void Ask()
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.IsServer() || Time.unscaledTime - _askedAt < 3f) return;
            _askedAt = Time.unscaledTime; ZRoutedRpc.instance.InvokeRoutedRPC(Req, new ZPackage());
        }

        private static void OnList(long sender, ZPackage pkg)
        {
            var server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (ZNet.instance == null || ZNet.instance.IsServer() || server == null || sender != server.m_uid || pkg.ReadInt() != Version) return;   // only the server's answer counts
            var kinds = AutomationPage.Kinds(); int n = pkg.ReadInt(); var list = new List<Station>(n);
            for (int i = 0; i < n; i++)
            {
                var id = new ZDOID(pkg.ReadLong(), pkg.ReadUInt()); int hash = pkg.ReadInt(); var pos = pkg.ReadVector3(); bool auto = pkg.ReadBool(); long by = pkg.ReadLong(), creator = pkg.ReadLong();
                if (kinds != null && kinds.TryGetValue(hash, out var prefab)) list.Add(new Station { Id = id, Prefab = prefab, Pos = pos, Auto = auto, By = by, Creator = creator });
            }
            Remote = list; RemoteAt = Time.unscaledTime;
        }

        // shown at once; the server's next answer confirms it
        internal static void Flip(ZDOID id, bool on)
        {
            if (Remote == null) return; var me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            for (int i = 0; i < Remote.Count; i++) if (Remote[i].Id == id) { var st = Remote[i]; st.Auto = on; st.By = on ? me : 0L; Remote[i] = st; }
        }

        internal static void SetAuto(ZDOID id, bool on)
        {
            var p = Player.m_localPlayer; if (ZRoutedRpc.instance == null || p == null) return;
            var pkg = new ZPackage(); pkg.Write(id.UserID); pkg.Write(id.ID); pkg.Write(on);   // the server knows who we are
            ZRoutedRpc.instance.InvokeRoutedRPC(Set, pkg);
        }
    }
}
