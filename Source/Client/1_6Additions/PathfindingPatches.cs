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

            // DEBUG: Log entry for every pawn this is called for.
            // Note: This can be spammy, but is essential for debugging.
            if (Multiplayer.settings.showDevInfo)
                Log.Message($"PatherTick for {__instance.pawn?.LabelShortCap ?? "NULL PAWN"} on {(Multiplayer.LocalServer != null ? "HOST" : "CLIENT")}");

            // Client logic: Wait for the host's command.
            if (Multiplayer.LocalServer == null)
            {
                if (__instance.curPathRequest != null)
                {
                    // DEBUG: Confirm the client is correctly waiting.
                    if (Multiplayer.settings.showDevInfo)
                        Log.Message($" - Client {__instance.pawn?.LabelShortCap} is waiting for host path.");
                    return false; // Stop the client from processing its own path result.
                }
                return true; // Let the original method run for other logic (like arrival checks).
            }

            // Host logic: Find a path and sync it.
            if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out PawnPath outPath))
            {
                __instance.DisposeAndClearCurPathRequest();
                if (outPath.Found)
                {
                    Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path FOUND with {outPath.NodesLeftCount} nodes. Syncing now...");

                    // Manually serialize the path nodes into a simple int array
                    var nodes = outPath.NodesReversed.ToList();
                    var nodeData = new int[nodes.Count * 3];
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        nodeData[i * 3] = nodes[i].x;
                        nodeData[i * 3 + 1] = nodes[i].y;
                        nodeData[i * 3 + 2] = nodes[i].z;
                    }

                    // Call the new, raw data sync method
                    SyncedActions.SetPawnPathRaw(__instance.pawn, nodeData, (int)outPath.TotalCost, outPath.UsedRegionHeuristics);
                }
                else
                {
                    Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path NOT found. Pather will fail.");
                    // Send an empty path to ensure clients also fail their pathing
                    SyncedActions.SetPawnPathRaw(__instance.pawn, new int[0], 0, false);
                }
                outPath.Dispose();
            }

            return true; // Let the original PatherTick run for the host to handle movement.
        }
    }
}
