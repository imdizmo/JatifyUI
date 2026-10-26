using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Dev profile only (F8 test panel): free build is vanilla's own no-cost switch (no materials, no station in
    // range, every piece unlocked); build anywhere passes every placement rule (build zone, biome, cultivated
    // ground, more space, missing station extension, snow, dungeon...). Only a ghost that hits nothing stays red.
    internal static class DevBuild
    {
        internal static bool Anywhere;
        internal static bool Free => Player.m_localPlayer != null && Player.m_localPlayer.PlacementCostDisabled;

        internal static void ToggleFree() { var p = Player.m_localPlayer; if (p != null) p.SetNoPlacementCost(!p.PlacementCostDisabled); }

        internal static void ToggleAnywhere()
        {
            Anywhere = !Anywhere;
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Build anywhere: " + (Anywhere ? "on" : "off"));
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class DevBuildAnywhere
    {
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> Status = AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        private static void Postfix(Player __instance)
        {
            if (!Dev.Build || !DevBuild.Anywhere || __instance != Player.m_localPlayer) return;
            var g = Ghost(__instance);
            if (g == null || !g.activeSelf) return;
            var s = Status(__instance);
            if (s == Player.PlacementStatus.Valid || s == Player.PlacementStatus.NoRayHits) return;
            Status(__instance) = Player.PlacementStatus.Valid;
            g.GetComponent<Piece>()?.SetInvalidPlacementHeightlight(false);
        }
    }
}
