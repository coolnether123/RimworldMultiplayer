// In CoreTick_Patch.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

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
        }
    }
}
