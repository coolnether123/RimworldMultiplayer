// In new file: SyncedActions.cs

using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Runtime.CompilerServices;

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

        // MODIFIED METHOD: The original SetPawnPath is replaced by one that takes raw data.
        [SyncMethod]
        public static void SetPawnPathRaw(Pawn pawn, int[] nodeData, int cost, bool usedRegionHeuristics)
        {
            var nodes = new List<IntVec3>(nodeData.Length / 3);
            for (int i = 0; i < nodeData.Length; i += 3)
            {
                nodes.Add(new IntVec3(nodeData[i], nodeData[i + 1], nodeData[i + 2]));
            }

            Log.Message($"[SYNC] {pawn?.LabelShortCap ?? "NULL PAWN"} on {(Multiplayer.LocalServer != null ? "HOST" : "CLIENT")} is RECEIVING path with {nodes.Count} nodes.");

            if (pawn == null || pawn.pather == null) return;

            // On clients (and host), we forcibly overwrite their current path with the host's authoritative one.
            if (pawn.pather.curPath != null)
            {
                pawn.pather.curPath.ReleaseToPool();
            }

            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            path.InitializeFromNodeList(nodes, cost, usedRegionHeuristics);
            path.SetSynced(true);  // Mark this path as synced so the host doesn't try to re-sync it.

            using (new Multiplayer.DontSync())
            {
                // This part is critical. We directly replace the pather's current path.
                pawn.pather.curPath = path;
                pawn.pather.ResetToCurrentPosition();
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
