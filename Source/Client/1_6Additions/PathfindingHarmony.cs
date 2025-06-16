// Multiplayer/Client/Patches/PathfindingHarmony.cs (NEW FILE)

using HarmonyLib;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    // By using [StaticConstructorOnStartup], this class's static constructor
    // will be called automatically by RimWorld when the game loads.
    [StaticConstructorOnStartup]
    public static class PathfindingHarmony
    {
        static PathfindingHarmony()
        {
            var harmony = Multiplayer.harmony; // Use the existing Harmony instance from your mod

            Log.Message("[Multiplayer] Applying pathfinding synchronization patches...");

            // === Patch for Job Starting (Catch-All) ===
            var startJobOriginal = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob));
            var startJobPrefix = new HarmonyMethod(typeof(Pawn_JobTracker_StartJob_Patch), nameof(Pawn_JobTracker_StartJob_Patch.Prefix));
            harmony.Patch(startJobOriginal, prefix: startJobPrefix);
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.StartJob");

            // === Patch for Path Result Processing ===
            var patherTickOriginal = AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick));
            var patherTickPrefix = new HarmonyMethod(typeof(Pawn_PathFollower_PatherTick_Patch), nameof(Pawn_PathFollower_PatherTick_Patch.Prefix));
            harmony.Patch(patherTickOriginal, prefix: patherTickPrefix);
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.PatherTick");

            // === Patch for Combat Toil Verb Setting ===
            var gotoCastPositionOriginal = AccessTools.Method(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition));
            var gotoCastPositionPostfix = new HarmonyMethod(typeof(Toils_Combat_GotoCastPosition_Patch), nameof(Toils_Combat_GotoCastPosition_Patch.Postfix));
            harmony.Patch(gotoCastPositionOriginal, postfix: gotoCastPositionPostfix);
            Log.Message("[Multiplayer] ... Patched Toils_Combat.GotoCastPosition");

            Log.Message("[Multiplayer] Pathfinding patches applied.");
        }
    }
}
