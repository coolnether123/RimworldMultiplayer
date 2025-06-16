// In file: PathfindingPatches.cs

using HarmonyLib;
using Verse;
using Verse.AI;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Multiplayer.Common;

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
                        // ===== NEW, ROBUST NODE EXTRACTION =====
                        var nodes = new List<IntVec3>();
                        // The path is stored in reverse, so we iterate backwards to get the correct order.
                        for (int i = outPath.NodesLeftCount - 1; i >= 0; i--)
                        {
                            nodes.Add(outPath.Peek(i));
                        }
                        // =======================================

                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path FOUND with {nodes.Count} nodes. Triggering sync...");

                        // NEW: Serialize the node data into a byte array immediately.
                        var writer = new ByteWriter();
                        writer.WriteInt32(nodes.Count);
                        foreach (var node in nodes)
                        {
                            writer.WriteInt32(node.x);
                            writer.WriteInt32(node.y);
                            writer.WriteInt32(node.z);
                        }
                        byte[] pathBytes = writer.ToArray();

                        Log.Message($"[HOST-DEBUG] Path for {__instance.pawn.LabelShortCap} found with {nodes.Count} nodes. Serialized to byte array of length: {pathBytes.Length}. Triggering sync...");

                        // Call the sync method with the serialized byte array.
                        SyncedActions.SetPawnPathBytes(__instance.pawn, pathBytes, (int)outPath.TotalCost, outPath.UsedRegionHeuristics);
                    }
                    else
                    {
                        // The pathfinder failed. Tell everyone that the path is empty.
                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path NOT found. Syncing empty path.");
                        SyncedActions.SetPawnPathBytes(__instance.pawn, new byte[0], 0, false);
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
