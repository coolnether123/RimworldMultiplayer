// In file: PathfindingPatches.cs

using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Pawn_PathFollower_PatherTick_Patch
    {
        public static bool Prefix(Pawn_PathFollower __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.dontSync || !__instance.pawn.Spawned)
            {
                return true;
            }

            if (Multiplayer.LocalServer != null)
            {
                if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out PawnPath outPath))
                {
                    __instance.DisposeAndClearCurPathRequest();

                    if (outPath.Found)
                    {
                        // Get the raw, reliable data we attached earlier.
                        var (nodes, cost) = outPath.GetRawPathData();

                        // Create the surrogate directly from the reliable raw data.
                        var surrogate = new PawnPathSurrogate(nodes, cost, outPath.UsedRegionHeuristics);

                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path FOUND with {nodes.Count} nodes. Triggering sync...");

                        SyncedActions.SetPawnPath(__instance.pawn, surrogate);
                    }
                    else
                    {
                        // Path not found, sync an invalid surrogate.
                        SyncedActions.SetPawnPath(__instance.pawn, new PawnPathSurrogate(null));
                    }

                    outPath.Dispose();
                    return false;
                }
            }
            else
            {
                if (__instance.curPathRequest != null)
                {
                    __instance.DisposeAndClearCurPathRequest();
                    return false;
                }
            }

            return true;
        }
    }
}
