// In new file: PawnPathPatches.cs

using HarmonyLib;
using Verse.AI;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Multiplayer.Client
{
    // A simple container to hold the raw path data temporarily.
    public static class RawPathData
    {
        public static (List<IntVec3> nodes, int cost) TempData;
    }

    [HarmonyPatch(typeof(PawnPath), nameof(PawnPath.Initialize))]
    public static class PawnPath_Initialize_Patch
    {
        // This prefix runs right before a PawnPath is created.
        // It captures the raw, reliable data from the pathfinding job.
        static void Prefix(NativeList<IntVec3> points, int cost)
        {
            var nodes = new List<IntVec3>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                nodes.Add(points[i]);
            }
            RawPathData.TempData = (nodes, cost);
        }

        // The postfix attaches the raw data to the newly created PawnPath object.
        static void Postfix(PawnPath __instance)
        {
            // We use our extension method to "tag" the PawnPath instance
            // with the data we just captured in the prefix.
            __instance.SetRawPathData(RawPathData.TempData);
        }
    }
}
