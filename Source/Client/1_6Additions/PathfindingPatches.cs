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

                    // Create the surrogate object from the path.
                    var surrogate = new PawnPathSurrogate(outPath);

                    Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path FOUND with {(outPath.Found ? outPath.NodesLeftCount : 0)} nodes. Triggering sync...");

                    // Call the sync method, passing the surrogate object.
                    SyncedActions.SetPawnPath(__instance.pawn, surrogate);

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
