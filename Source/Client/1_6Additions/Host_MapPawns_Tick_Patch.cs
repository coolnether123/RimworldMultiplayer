// In file: Source/Client/1_6Additions/Host_MapPawns_Tick_Patch.cs

using HarmonyLib;
using Verse;
using System.Collections.Generic;
using RimWorld;

namespace Multiplayer.Client.Patches
{
    /// <summary>
    /// This patch is the core of the host-authoritative AI simulation.
    /// It ensures that the host runs the AI tick for all pawns on ALL maps, every frame.
    /// Without this, pawns on non-active maps will not have their JobTrackers ticked,
    /// preventing them from choosing new jobs and creating a desync.
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class Host_MapPawns_Tick_Patch
    {
        static void Postfix(TickManager __instance)
        {
            // This logic must ONLY run on the host.
            if (Multiplayer.LocalServer == null) return;

            // Do not run during world-only ticking (e.g., on the planet view).
            if (Find.CurrentMap == null) return;

            // The main game loop already ticks the current map, so we only need to
            // tick the pawns on all OTHER maps.
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];

                if (map != Find.CurrentMap)
                {
                    // Manually tick every pawn on this non-active map.
                    // This ensures their JobTracker and PathFollower get processed.
                    // We must use a temporary list as the AllPawnsSpawned list can be modified during the tick.
                    List<Pawn> pawnsOnMap = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
                    foreach (Pawn pawn in pawnsOnMap)
                    {
                        // Check if the pawn is still valid and spawned before ticking.
                        if (pawn != null && pawn.Spawned && pawn.Map == map)
                        {
                            pawn.Tick();
                        }
                    }
                }
            }
        }
    }
}
