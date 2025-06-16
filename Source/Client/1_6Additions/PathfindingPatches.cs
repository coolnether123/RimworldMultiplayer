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

                    Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path FOUND with {(outPath.Found ? outPath.NodesLeftCount : 0)} nodes. Triggering sync...");

                    // Pass the PawnPath object directly. Our new hook will handle it.
                    SyncedActions.SetPawnPath(__instance.pawn, outPath);

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
