// In file: SyncedActions.cs

using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Runtime.CompilerServices;

namespace Multiplayer.Client
{
    // This is the data container for PawnPath.
    public class PawnPathSurrogate : ISynchronizable
    {
        private bool isValid;
        private List<IntVec3> nodes;
        private int totalCost;
        private bool usedRegionHeuristics;

        public PawnPathSurrogate() { }

        public PawnPathSurrogate(PawnPath path)
        {
            if (path == null || !path.Found) { isValid = false; return; }
            isValid = true;
            totalCost = (int)path.TotalCost;
            usedRegionHeuristics = path.UsedRegionHeuristics;
            nodes = new List<IntVec3>();
            for (int i = path.NodesLeftCount - 1; i >= 0; i--) { nodes.Add(path.Peek(i)); }
        }

        public PawnPath ToPawnPath(Pawn pawn)
        {
            if (!isValid) return PawnPath.NotFound;
            PawnPath newPath = pawn.Map.pawnPathPool.GetPath();
            newPath.InitializeFromNodeList(nodes, totalCost, usedRegionHeuristics);
            return newPath;
        }

        public void Sync(SyncWorker worker)
        {
            worker.Bind(ref isValid);
            if (!isValid) return;
            worker.Bind(ref nodes);
            worker.Bind(ref totalCost);
            worker.Bind(ref usedRegionHeuristics);
        }
        public PawnPathSurrogate(List<IntVec3> nodes, int cost, bool usedHeuristics)
        {
            if (nodes == null)
            {
                isValid = false;
                return;
            }

            isValid = true;
            this.nodes = nodes;
            this.totalCost = cost;
            this.usedRegionHeuristics = usedHeuristics;
        }
    }

    public static class SyncedActions
    {
        [SyncMethod]
        public static void StartJob(Pawn pawn, JobParams jobParams, StartJobContext context)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;
            Job job = jobParams.ToJob();
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
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate)
        {
            if (pawn == null || pawn.pather == null) return;

            PawnPath path = surrogate.ToPawnPath(pawn);

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

    // This extension method is still needed by the surrogate to create the path.
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
    public static class PawnPath_RawData_Extensions
    {
        private static readonly ConditionalWeakTable<PawnPath, object> rawPathData =
            new ConditionalWeakTable<PawnPath, object>();

        public static (List<IntVec3> nodes, int cost) GetRawPathData(this PawnPath path)
        {
            if (rawPathData.TryGetValue(path, out var data))
            {
                return ((List<IntVec3>, int))data;
            }
            return (null, 0);
        }

        public static void SetRawPathData(this PawnPath path, (List<IntVec3> nodes, int cost) data)
        {
            rawPathData.Add(path, data);
        }
    }
}
