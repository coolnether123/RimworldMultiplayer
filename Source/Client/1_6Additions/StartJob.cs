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
        static bool Prefix(Pawn_JobTracker __instance, Job newJob, ThinkNode jobGiver, ThinkTreeDef thinkTree)
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
            SyncedJobs.StartJob(
                pawn,
                jobParams,
                __instance.curJob?.def == newJob.def ? JobCondition.Succeeded : JobCondition.InterruptForced
            );

            // Always prevent the original StartJob from running directly.
            return false;
        }
    }
}
