// In new file: Source/Client/1_6Additions/Host_JobGiver_Patch.cs

using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches
{
    /// <summary>
    /// This is the definitive patch to enable Host-Authoritative AI.
    /// Problem: The vanilla Pawn_JobTracker will not try to find a new job if the pawn is on an inactive map.
    /// Solution: This Postfix patch runs *only on the host* after the original method.
    /// If the pawn is on an inactive map, we manually trigger the job search logic that was skipped.
    /// This ensures all pawns on all maps are always thinking, without breaking the client or double-ticking.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTickInterval))]
    static class Host_JobGiver_Patch
    {
        static void Postfix(Pawn_JobTracker __instance)
        {
            // This patch must only ever run on the host.
            if (Multiplayer.LocalServer == null) return;

            Pawn pawn = __instance.pawn;

            // The vanilla game ticks the current map. We only need to force-tick non-active maps.
            if (pawn.Map == Find.CurrentMap) return;

            // If the pawn has no job and its mind is active, it should be looking for one.
            // The vanilla code skips this for inactive maps, so we do it here.
            if (__instance.curJob == null && pawn.mindState.Active)
            {
                // Using TryFindAndStartJob() is safer and more comprehensive than CheckForJobOverride().
                __instance.TryFindAndStartJob();
            }
        }
    }
}
