// Multiplayer/Client/Paths/SyncedPaths.cs (NEW FILE)

using HarmonyLib;
using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SyncedPaths
    {
        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, List<IntVec3> nodes, int cost, bool usedRegionHeuristics)
        {
            if (pawn == null || pawn.pather == null || nodes == null) return;

            // Recreate the PawnPath object from the synced data
            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            // We need a way to initialize it from a simple list. Let's assume PawnPath has a method for this,
            // or we'll create an extension method.
            path.InitializeFromNodeList(nodes, cost, usedRegionHeuristics);

            // Directly assign the host's path to the client's pather.
            // Use DontSync to prevent our PatherTick patch from re-syncing this.
            using (new Multiplayer.DontSync())
            {
                pawn.pather.curPath = path;
                pawn.pather.nextCell = pawn.Position; // Reset pather state
                pawn.pather.SetupMoveIntoNextCell();
            }
        }
    }

    // Helper extension method to initialize a PawnPath from a list.
    // This may need to be adapted based on the actual PawnPath structure.
    public static class PawnPath_Extensions
    {
        public static void InitializeFromNodeList(this PawnPath path, List<IntVec3> nodes, int cost, bool usedRegionHeuristics)
        {
            // The path nodes are stored in reverse order in PawnPath
            path.NodesReversed.Clear();
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                path.NodesReversed.Add(nodes[i]);
            }

            // Reflection or a custom setter might be needed if these fields are private.
            // Let's assume we can set them for now.
            var pathTraverser = Traverse.Create(path);
            pathTraverser.Field("totalCostInt").SetValue((float)cost);
            pathTraverser.Field("curNodeIndex").SetValue(path.NodesReversed.Count - 1);
            pathTraverser.Field("usedRegionHeuristics").SetValue(usedRegionHeuristics);
            pathTraverser.Field("inUse").SetValue(true);
        }
    }
}
