// Multiplayer/Client/Patches/PlayerJobPatches.cs

using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Pawn_JobTracker_TryTakeOrderedJob_Patch
    {
        // Intercept player-issued jobs.
        static bool Prefix(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            // If not in a multiplayer game, or if this call is coming from our own sync handler, run the original method.
            if (!Multiplayer.ShouldSync) return true;

            // Player is issuing a command. Send it to the host for broadcasting.
            SyncedPlayerJob.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);

            // Return false to prevent the job from being taken locally. We wait for the synced response.
            return false;
        }
    }

    // The sync handler for player jobs
    public static class SyncedPlayerJob
    {
        [SyncMethod]
        public static void TakeOrderedJob(Pawn pawn, JobParams jobParams, JobTag? tag)
        {
            if (pawn == null || pawn.jobs == null) return;

            Job job = jobParams.ToJob();

            // This call will now be executed on all clients, including the one who issued it.
            // We use `Multiplayer.DontSync` to prevent our patch from re-intercepting this call and causing an infinite loop.
            using (new Multiplayer.DontSync())
            {
                pawn.jobs.TryTakeOrderedJob(job, tag);
            }
        }
    }
}
