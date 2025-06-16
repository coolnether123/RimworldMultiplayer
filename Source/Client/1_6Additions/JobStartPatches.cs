// In file: StartJob.cs

using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Pawn_JobTracker_StartJob_Patch
    {
        public static bool Prefix(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, ThinkNode jobGiver, bool resumeCurJobAfterwards, bool cancelBusyStances, ThinkTreeDef thinkTree, JobTag? tag, bool fromQueue, bool canReturnCurJobToPool, bool? keepCarryingThingOverride, bool continueSleeping, bool preToilReservationsCanFail)
        {
            // If not in multiplayer, or if a sync command is currently being executed, run the original logic.
            if (!Multiplayer.ShouldSync) return true;

            Pawn pawn = __instance.pawn;

            // If the job comes from the AI, it needs to be determined by the host.
            // A player-forced job (from right-click, etc.) has no jobGiver.
            bool isAIJob = jobGiver != null;

            if (isAIJob)
            {
                // Only the host generates AI jobs. Clients must wait for the command.
                if (Multiplayer.LocalServer == null) return false;

                // Set the source info on the job before creating the params
                newJob.jobGiver = jobGiver;
                newJob.jobGiverThinkTree = thinkTree;
            }

            // Package the job and its context into params and send it for syncing.
            var jobParams = new JobParams(newJob);

            // The SyncedJobs class will handle broadcasting and execution on all clients.
            SyncedActions.StartJob(pawn, jobParams, lastJobEndCondition, resumeCurJobAfterwards, cancelBusyStances, tag, fromQueue, canReturnCurJobToPool, keepCarryingThingOverride, continueSleeping, preToilReservationsCanFail);

            // Always prevent the original StartJob from running directly.
            return false;
        }
    }
}
