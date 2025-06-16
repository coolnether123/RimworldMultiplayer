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
        // We will now use a Postfix. This is much safer.
        // It lets the original PatherTick run its course, and we just correct the result.
        public static void Postfix(Pawn_PathFollower __instance)
        {
            // Standard guards
            if (Multiplayer.Client == null || Multiplayer.dontSync || !__instance.pawn.Spawned) return;

            // This logic now runs on the HOST only.
            if (Multiplayer.LocalServer != null)
            {
                // We check if the original PatherTick just assigned a new path.
                // The `curPath.inUse` flag is set when a path is assigned.
                // We also check if this path has been synced by us already to avoid loops.
                if (__instance.curPath != null && __instance.curPath.inUse && !__instance.curPath.IsSynced())
                {
                    Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Original PatherTick assigned a new path with {__instance.curPath.NodesLeftCount} nodes. Triggering sync...");

                    var nodes = __instance.curPath.NodesReversed.ToList();
                    var nodeData = new int[nodes.Count * 3];
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        nodeData[i * 3] = nodes[i].x;
                        nodeData[i * 3 + 1] = nodes[i].y;
                        nodeData[i * 3 + 2] = nodes[i].z;
                    }

                    // Trigger the sync for all clients
                    SyncedActions.SetPawnPathRaw(__instance.pawn, nodeData, (int)__instance.curPath.TotalCost, __instance.curPath.UsedRegionHeuristics);

                    // Mark the host's local path as synced to prevent re-sending it next tick.
                    __instance.curPath.SetSynced(true);
                }
            }
            // Clients do nothing in this patch. They just receive the synced path.
        }
    }

    // We need to add a new field to PawnPath to track if it has been synced.
    // This requires another patch.
    [HarmonyPatch(typeof(PawnPath), nameof(PawnPath.ReleaseToPool))]
    public static class PawnPath_ReleaseToPool_Patch
    {
        public static void Postfix(PawnPath __instance)
        {
            // Reset our custom flag when the path is returned to the pool.
            __instance.SetSynced(false);
        }
    }

    // And an extension method to access our new field safely.
    public static class PawnPathSync_Extensions
    {
        // Use a ConditionalWeakTable to avoid memory leaks with pooled objects.
        // This is a much safer way to attach data to existing objects.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PawnPath, StrongBox<bool>> syncedPaths =
            new System.Runtime.CompilerServices.ConditionalWeakTable<PawnPath, StrongBox<bool>>();

        public static bool IsSynced(this PawnPath path)
        {
            if (syncedPaths.TryGetValue(path, out var box))
            {
                return box.Value;
            }
            return false;
        }

        public static void SetSynced(this PawnPath path, bool value)
        {
            var box = syncedPaths.GetOrCreateValue(path);
            box.Value = value;
        }
    }
}
