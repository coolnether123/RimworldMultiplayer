// In file: PathfindingPatches.cs

using HarmonyLib;
using Verse;
using Verse.AI;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Pawn_PathFollower_PatherTick_Patch
    {
        // Using a Prefix to gain full control over the method's execution.
        public static bool Prefix(Pawn_PathFollower __instance)
        {
            // Standard guards. If not in MP, run the original game code.
            if (Multiplayer.Client == null || Multiplayer.dontSync || !__instance.pawn.Spawned)
            {
                return true;
            }

            // On the HOST, we are authoritative.
            if (Multiplayer.LocalServer != null)
            {
                // Check if the game's pathfinder has a result ready for us.
                if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out PawnPath outPath))
                {
                    // We now have the result, so we can clear the request.
                    __instance.DisposeAndClearCurPathRequest();

                    if (outPath.Found)
                    {
                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path FOUND with {outPath.NodesLeftCount} nodes. Triggering sync...");

                        var nodes = outPath.NodesReversed.ToList();
                        var nodeData = new int[nodes.Count * 3];
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            nodeData[i * 3] = nodes[i].x;
                            nodeData[i * 3 + 1] = nodes[i].y;
                            nodeData[i * 3 + 2] = nodes[i].z;
                        }

                        // Call the sync method to broadcast the path to everyone.
                        SyncedActions.SetPawnPathRaw(__instance.pawn, nodeData, (int)outPath.TotalCost, outPath.UsedRegionHeuristics);
                    }
                    else
                    {
                        // The pathfinder failed. Tell everyone that the path is empty.
                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path NOT found. Syncing empty path.");
                        SyncedActions.SetPawnPathRaw(__instance.pawn, new int[0], 0, false);
                    }

                    // Release the host's local copy of the path.
                    outPath.Dispose();

                    // CRITICAL: We return false here to prevent the original PatherTick from running on the host.
                    // The host's pawn will move when it receives its own SetPawnPathRaw command.
                    // This avoids the state corruption/crash we were seeing.
                    return false;
                }
            }
            else // This is a CLIENT
            {
                // Clients are not authoritative. They should not process their own path results.
                // We clear their pending request and stop the tick to make them wait for the host's command.
                if (__instance.curPathRequest != null)
                {
                    __instance.DisposeAndClearCurPathRequest();
                    return false;
                }
            }

            // If we are here, it means:
            // - We are the host, but there's no new path result to process this tick.
            // - We are a client, and we have no pending path request.
            // In both cases, we should let the original PatherTick run to handle normal movement along the existing (synced) path.
            return true;
        }
    }

    // This patch is no longer needed with the new Prefix logic, but leaving it doesn't hurt.
    [HarmonyPatch(typeof(PawnPath), nameof(PawnPath.ReleaseToPool))]
    public static class PawnPath_ReleaseToPool_Patch
    {
        public static void Postfix(PawnPath __instance)
        {
            // Reset our custom flag when the path is returned to the pool.
            __instance.SetSynced(false);
        }
    }
}
