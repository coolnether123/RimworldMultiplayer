// Multiplayer/Client/Paths/SyncedPaths.cs

using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using HarmonyLib; // Required for Traverse

namespace Multiplayer.Client
{
    public static class SyncedPaths
    {
        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, List<IntVec3> nodes, int cost, bool usedRegionHeuristics)
        {
            // DEBUG: This log will appear on EVERY client's machine when they receive a path.
            Log.Message($"[SYNC] {pawn?.LabelShortCap ?? "NULL PAWN"} is RECEIVING path with {nodes?.Count ?? 0} nodes from host.");

            if (pawn == null || pawn.pather == null || nodes == null) return;

            // If there's an existing path, clear it first.
            if (pawn.pather.curPath != null)
            {
                pawn.pather.curPath.ReleaseToPool();
            }

            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            path.InitializeFromNodeList(nodes, cost, usedRegionHeuristics);

            using (new Multiplayer.DontSync())
            {
                // This logic is tricky. Directly setting curPath and calling SetupMoveIntoNextCell is the goal.
                // We must also ensure any old path request is cleared.
                pawn.pather.DisposeAndClearCurPathRequest();
                pawn.pather.curPath = path;
                pawn.pather.ResetToCurrentPosition(); // This resets state and calls SetupMoveIntoNextCell internally.
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
