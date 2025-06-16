// In file: JobPatches.cs

using HarmonyLib;
using Multiplayer.API;
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

                if (job.verbToUse != null && Multiplayer.ShouldSync)
                {
                    // Sync the verb that was chosen by the toil's logic.
                    SyncedJobs.SetJobVerb(job, new JobParams(job));
                }
            };
        }
    }

    // Add the 'partial' keyword here so it can merge with the other SyncedJobs definition.
    public static partial class SyncedJobs
    {
        [SyncMethod]
        public static void SetJobVerb(Job job, JobParams jobParams)
        {
            if (job == null) return;

            var reconstructedJob = jobParams.ToJob();
            job.verbToUse = reconstructedJob.verbToUse;
        }
    }
}
