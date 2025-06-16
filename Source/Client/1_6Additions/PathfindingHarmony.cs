// In file: PathfindingHarmony.cs

using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public static class PathfindingHarmony
    {
        static PathfindingHarmony()
        {
            var harmony = Multiplayer.harmony;
            Log.Message("[Multiplayer] Applying pathfinding synchronization patches...");

            var startJobOriginal = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob));
            harmony.Patch(startJobOriginal, prefix: new HarmonyMethod(typeof(Pawn_JobTracker_StartJob_Patch), nameof(Pawn_JobTracker_StartJob_Patch.Prefix)));
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.StartJob");

            var patherTickOriginal = AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick));
            harmony.Patch(patherTickOriginal, prefix: new HarmonyMethod(typeof(Pawn_PathFollower_PatherTick_Patch), nameof(Pawn_PathFollower_PatherTick_Patch.Prefix)));
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.PatherTick");

            var gotoCastPositionOriginal = AccessTools.Method(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition));
            harmony.Patch(gotoCastPositionOriginal, postfix: new HarmonyMethod(typeof(Toils_Combat_GotoCastPosition_Patch), nameof(Toils_Combat_GotoCastPosition_Patch.Postfix)));
            Log.Message("[Multiplayer] ... Patched Toils_Combat.GotoCastPosition");
        }
    }
}
