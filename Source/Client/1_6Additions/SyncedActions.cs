// In new file: SyncedActions.cs

using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Runtime.CompilerServices;
using Multiplayer.Common;

namespace Multiplayer.Client
{
    // A central place for all our job and path related SyncMethods
    public static class SyncedActions
    {

        // This holds the surrogate between the custom reader and the SyncMethod execution.
        public static PawnPathSurrogate tempPathSurrogate;

        // Temporary storage for data deserialized by our custom SyncWorker.
        public static (List<IntVec3> nodes, int cost, bool usedHeuristics) tempPathData;

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
        // The signature takes a PawnPath, but the SyncWorker will actually pass null
        // and populate our tempPathSurrogate field instead.
        public static void SetPawnPath(Pawn pawn, PawnPath newPath)
        {
            if (pawn == null || pawn.pather == null) return;

            // Use the surrogate from our temp field to create the real path.
            PawnPath path = tempPathSurrogate.ToPawnPath(pawn);

            Log.Message($"[SYNC] {pawn.LabelShortCap} on {(Multiplayer.LocalServer != null ? "HOST" : "CLIENT")} is RECEIVING path with {(path.Found ? path.NodesLeftCount : 0)} nodes.");

            if (pawn.pather.curPath != null)
            {
                pawn.pather.curPath.ReleaseToPool();
            }

            pawn.pather.curPath = path;

            if (path.Found)
            {
                using (new Multiplayer.DontSync())
                {
                    pawn.pather.ResetToCurrentPosition();
                }
            }
            else
            {
                pawn.pather.PatherFailed();
            }
        }
    }



    public static class PawnPathSync_Extensions
    {
        private static readonly ConditionalWeakTable<PawnPath, StrongBox<bool>> syncedPaths =
            new ConditionalWeakTable<PawnPath, StrongBox<bool>>();

        public static bool IsSynced(this PawnPath path)
        {
            return syncedPaths.TryGetValue(path, out var box) && box.Value;
        }

        public static void SetSynced(this PawnPath path, bool value)
        {
            var box = syncedPaths.GetOrCreateValue(path);
            box.Value = value;
        }
    }

    public static class PawnPath_Initialization_Extensions
    {
        public static void InitializeFromNodeList(this PawnPath path, List<IntVec3> nodes, int cost, bool usedHeuristics)
        {
            path.NodesReversed.Clear();
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                path.NodesReversed.Add(nodes[i]);
            }

            var pathTraverser = Traverse.Create(path);
            pathTraverser.Field<float>("totalCostInt").Value = cost;
            pathTraverser.Field<int>("curNodeIndex").Value = path.NodesReversed.Count - 1;
            pathTraverser.Field<bool>("usedRegionHeuristics").Value = usedHeuristics;
            pathTraverser.Field<bool>("inUse").Value = true;
        }
    }
}
