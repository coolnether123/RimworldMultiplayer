// In new file: SyncedActions.cs

using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace Multiplayer.Client
{
    // A central place for all our job and path related SyncMethods
    public static class SyncedActions
    {
        [SyncMethod]
        public static void StartJob(Pawn pawn, JobParams jobParams, StartJobContext context)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;

            Job job = jobParams.ToJob();

            // Reconstruct all parameters from the context object
            JobCondition lastJobEndCondition = (JobCondition)context.lastJobEndConditionByte;
            JobTag? tag = context.hasTag ? new JobTag?((JobTag)context.tagValueByte) : null;
            bool? keepCarryingThingOverride = context.hasCarryOverride ? new bool?(context.carryOverrideValue) : null;

            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, lastJobEndCondition, job.jobGiver, context.resumeCurJobAfterwards, context.cancelBusyStances, job.jobGiverThinkTree, tag, context.fromQueue, context.canReturnCurJobToPool, keepCarryingThingOverride, context.continueSleeping, context.preToilReservationsCanFail);
            }
        }

        [SyncMethod]
        public static void SetJobVerb(Pawn pawn, JobParams jobParams)
        {
            Job job = pawn?.CurJob;
            if (job == null) return;

            var reconstructedJob = jobParams.ToJob();
            job.verbToUse = reconstructedJob.verbToUse;
        }

        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, List<IntVec3> nodes, int cost, bool usedRegionHeuristics)
        {
            Log.Message($"[SYNC] {pawn?.LabelShortCap ?? "NULL PAWN"} is RECEIVING path with {nodes?.Count ?? 0} nodes from host.");

            if (pawn == null || pawn.pather == null || nodes == null) return;

            if (pawn.pather.curPath != null)
            {
                pawn.pather.curPath.ReleaseToPool();
            }

            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            path.InitializeFromNodeList(nodes, cost, usedRegionHeuristics);

            using (new Multiplayer.DontSync())
            {
                pawn.pather.DisposeAndClearCurPathRequest();
                pawn.pather.curPath = path;
                pawn.pather.ResetToCurrentPosition();
            }
        }
    }

    public static class PawnPath_Extensions
    {
        public static void InitializeFromNodeList(this PawnPath path, List<IntVec3> nodes, int cost, bool usedRegionHeuristics)
        {
            path.NodesReversed.Clear();
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                path.NodesReversed.Add(nodes[i]);
            }

            var pathTraverser = Traverse.Create(path);
            pathTraverser.Field<float>("totalCostInt").Value = cost;
            pathTraverser.Field<int>("curNodeIndex").Value = path.NodesReversed.Count - 1;
            pathTraverser.Field<bool>("usedRegionHeuristics").Value = usedRegionHeuristics;
            pathTraverser.Field<bool>("inUse").Value = true;
        }
    }
}
