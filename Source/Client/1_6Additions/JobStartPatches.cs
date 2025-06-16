// In file: JobStartPatches.cs

using HarmonyLib;
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
            if (!Multiplayer.ShouldSync) return true;

            Pawn pawn = __instance.pawn;
            bool isAIJob = jobGiver != null;

            if (isAIJob)
            {
                if (Multiplayer.LocalServer == null) return false;

                newJob.jobGiver = jobGiver;
                newJob.jobGiverThinkTree = thinkTree;
            }

            var jobParams = new JobParams(newJob);

            // Create and populate the new context object
            var context = new StartJobContext
            {
                lastJobEndConditionByte = (byte)lastJobEndCondition,
                resumeCurJobAfterwards = resumeCurJobAfterwards,
                cancelBusyStances = cancelBusyStances,
                hasTag = tag.HasValue,
                tagValueByte = (byte)tag.GetValueOrDefault(),
                fromQueue = fromQueue,
                canReturnCurJobToPool = canReturnCurJobToPool,
                hasCarryOverride = keepCarryingThingOverride.HasValue,
                carryOverrideValue = keepCarryingThingOverride.GetValueOrDefault(),
                continueSleeping = continueSleeping,
                preToilReservationsCanFail = preToilReservationsCanFail
            };

            // Call the new, simplified sync method
            SyncedActions.StartJob(pawn, jobParams, context);

            return false;
        }
    }
}
