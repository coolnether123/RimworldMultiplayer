// Multiplayer/Client/Paths/PathfindingPatches.cs

using HarmonyLib;
using System.Linq;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Pawn_PathFollower_PatherTick_Patch
    {
        public static bool Prefix(Pawn_PathFollower __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.dontSync || !__instance.pawn.Spawned) return true;

            // This patch should ONLY handle the host's logic for finding and sending the path.
            // The logic to RECEIVE the path is handled by the SyncedActions.SetPawnPathRaw method.
            if (Multiplayer.LocalServer != null)
            {
                // Only the host should check for new path results.
                if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out PawnPath outPath))
                {
                    __instance.DisposeAndClearCurPathRequest(); // Important to clear the request
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

                        // This is the call that gets sent over the network.
                        SyncedActions.SetPawnPathRaw(__instance.pawn, nodeData, (int)outPath.TotalCost, outPath.UsedRegionHeuristics);
                    }
                    else
                    {
                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path NOT found. Syncing empty path.");
                        SyncedActions.SetPawnPathRaw(__instance.pawn, new int[0], 0, false);
                    }
                    outPath.Dispose(); // Clean up the host's local path object.

                    // We've handled the path result, but the pawn still needs to be ticked.
                    // However, we must prevent the pawn from using its own (potentially different) path.
                    // The best way is to simply stop the original method here and let the sync-call handle the pather state.
                    return false;
                }
            }
            else // This is a client
            {
                // Clients should not process their own path results. They must wait for the host.
                // We clear their local request so they stop trying to process it.
                if (__instance.curPathRequest != null)
                {
                    if (Multiplayer.settings.showDevInfo)
                        Log.Message($" - Client {__instance.pawn.LabelShortCap} is waiting for host path and clearing local request.");

                    __instance.DisposeAndClearCurPathRequest();
                    return false; // Prevent client pather from ticking with its own data.
                }
            }

            return true; // Allow the rest of PatherTick to run (e.g. to handle movement along the *synced* path).
        }
    }
}
