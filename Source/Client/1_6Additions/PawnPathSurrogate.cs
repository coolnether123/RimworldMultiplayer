// In new file: PawnPathSurrogate.cs

using Multiplayer.API;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace Multiplayer.Client
{
    public class PawnPathSurrogate : ISynchronizable
    {
        private bool isValid;
        private List<IntVec3> nodes;
        private int totalCost;
        private bool usedRegionHeuristics;

        public PawnPathSurrogate() { }

        public PawnPathSurrogate(PawnPath path)
        {
            if (path == null || !path.Found)
            {
                isValid = false;
                return;
            }

            isValid = true;
            totalCost = (int)path.TotalCost;
            usedRegionHeuristics = path.UsedRegionHeuristics;

            nodes = new List<IntVec3>();
            for (int i = path.NodesLeftCount - 1; i >= 0; i--)
            {
                nodes.Add(path.Peek(i));
            }
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
    }
}
