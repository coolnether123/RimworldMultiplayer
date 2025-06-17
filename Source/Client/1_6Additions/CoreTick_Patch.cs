// In CoreTick_Patch.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using static Verse.AI.ThingCountTracker;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    static class CoreTick_Patch
    {
        static void Postfix()
        {
            // Only run on host
            if (Multiplayer.Client == null || Multiplayer.LocalServer == null)
                return;

            Log.Message("[CoreTick_Patch] Postfix running - forcing AI tick");

            int pawnCount = 0; 

            // Force AI thinking on all pawns
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawns)
                {
                    if (pawn.needs == null || pawn.jobs == null || !pawn.mindState.Active)
                        continue;

                    pawn.jobs.JobTrackerTickInterval(1);
                }
            }
            Log.Message($"[CoreTick_Patch] Forced AI tick on {pawnCount} pawns");
        }
    }
}
