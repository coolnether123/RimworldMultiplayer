// Multiplayer/Client/Paths/PathfindingPatches.cs (NEW FILE)

using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    // This patch intercepts when a path result is ready to be used.
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Pawn_PathFollower_PatherTick_Patch
    {
        static bool Prefix(Pawn_PathFollower __instance)
        {
            // If not in multiplayer, or if a sync command is being executed, run original.
            if (!Multiplayer.ShouldSync) return true;

            // Only the host is allowed to process a path result from the pathfinder.
            if (Multiplayer.LocalServer == null)
            {
                // If the client has a path request pending, just wait.
                if (__instance.curPathRequest != null) return false;
                // Otherwise, let the original logic run (e.g., to handle arriving at destination).
                return true;
            }

            // HOST LOGIC
            PawnPath outPath;
            // Check if the host's pathfinder has finished a job.
            if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out outPath))
            {
                // The host has a new path.
                __instance.curPathRequest.ClaimCalculatedPath(); // Prevent it from being used twice
                __instance.DisposeAndClearCurPathRequest();

                if (outPath.Found)
                {
                    // Path found! Let's sync it.
                    var traverse = Traverse.Create(outPath);
                    SyncedPaths.SetPawnPath(
                        __instance.pawn,
                        outPath.NodesReversed.GetRange(0, outPath.NodesReversed.Count), // Send a copy
                        (int)outPath.TotalCost,
                        outPath.UsedRegionHeuristics
                    );
                }
                else
                {
                    // Path not found. The vanilla code handles this by calling PatherFailed.
                    // We let it run.
                }

                outPath.Dispose(); // We've synced it, so we can release it from the pool.
            }

            // Let the rest of the PatherTick logic run for the host.
            // This is important for handling movement, collisions, etc.
            return true;
        }
    }
}
