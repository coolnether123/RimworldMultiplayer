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
        public static void SetPawnPathBytes(Pawn pawn, byte[] pathBytes, int cost, bool usedRegionHeuristics)
        {
            Log.Message($"[SYNC-DEBUG] SetPawnPathBytes called for {pawn?.LabelShortCap}. Received byte array with length: {pathBytes?.Length ?? -1}");

            var nodes = new List<IntVec3>();
            if (pathBytes != null && pathBytes.Length > 0)
            {
                var reader = new ByteReader(pathBytes);
                // Add a try-catch block for safety during debugging
                try
                {
                    int count = reader.ReadInt32();
                    if (count < 0 || count > 1000) // Sanity check
                    {
                        Log.Error($"[SYNC-DEBUG] Invalid node count {count} from pathBytes of length {pathBytes.Length}");
                        count = 0;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        nodes.Add(new IntVec3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
                    }
                }
                catch (System.Exception e)
                {
                    Log.Error($"[SYNC-DEBUG] Exception deserializing path bytes: {e}");
                }
            }

            Log.Message($"[SYNC] {pawn?.LabelShortCap ?? "NULL PAWN"} on {(Multiplayer.LocalServer != null ? "HOST" : "CLIENT")} is RECEIVING path with {nodes.Count} nodes.");

            if (pawn == null || pawn.pather == null) return;

            if (pawn.pather.curPath != null)
            {
                pawn.pather.curPath.ReleaseToPool();
            }

            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            path.InitializeFromNodeList(nodes, cost, usedRegionHeuristics);

            using (new Multiplayer.DontSync())
            {
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
