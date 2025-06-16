// To be placed in a new file, e.g., Multiplayer/Client/Patches/JobGiverPatches.cs

using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryFindAndStartJob))]
    public static class Pawn_JobTracker_TryFindAndStartJob_Patch
    {
        // __state is used to pass the job from before execution to the finalizer.
        static bool Prefix(Pawn_JobTracker __instance, ref Job __state)
        {
            // If not in a multiplayer game, run the original method without changes.
            if (Multiplayer.Client == null) return true;

            // This is a client, not the host. The host will tell us what job to do.
            // Returning false skips the original vanilla method entirely for this client.
            if (Multiplayer.LocalServer == null) return false;

            // === HOST-ONLY LOGIC ===
            // We are the host, so we will run the original method to find a job,
            // but first, we set up a deterministic environment.
            Pawn pawn = __instance.pawn;

            // Store the current job to detect if a new one is assigned after the method runs.
            __state = __instance.curJob;

            // Push a deterministic random state. The seed must be based on synced game state.
            // A pawn's ID and the map's current tick count are reliable choices.
            // This ensures that any RNG inside job givers produces the same result.
            Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, pawn.Map.AsyncTime().mapTicks));

            // Returning true allows the original TryFindAndStartJob to execute for the host.
            return true;
        }

        static void Finalizer(Pawn_JobTracker __instance, Job __state)
        {
            // Only the host ran the original method, so only the host executes this logic.
            if (Multiplayer.LocalServer == null) return;

            // Always pop the RNG state to avoid breaking other parts of the game.
            Rand.PopState();

            // Check if a new job was assigned by comparing the current job with the one from before.
            Job newJob = __instance.curJob;
            if (newJob != null && newJob != __state)
            {
                // A new job was successfully given by the AI.
                // Now, we broadcast this decision to all clients.
                var jobParams = new JobParams(newJob);
                SyncedJobGiver.GiveJob(pawn, jobParams);
            }
        }
    }
}
