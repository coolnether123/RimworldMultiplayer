// Multiplayer/Client/Patches/JobPatches.cs

using HarmonyLib;
using Multiplayer.API; // Required for [SyncMethod]
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition))]
    public static class Toils_Combat_GotoCastPosition_Patch
    {
        static void Postfix(Toil __result)
        {
            var originalInit = __result.initAction;
            if (originalInit == null) return;

            __result.initAction = () =>
            {
                originalInit();

                var pawn = __result.actor;
                var job = pawn.CurJob;

                // If the original logic chose a verb, and we need to sync it...
                if (job.verbToUse != null && Multiplayer.ShouldSync)
                {
                    // Call the corrected sync method, passing the PAWN, not the JOB.
                    SyncedJobs.SetJobVerb(pawn, new JobParams(job));
                }
            };
        }
    }
}
