// Multiplayer/Client/Patches/JobGiverPatches.cs

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryFindAndStartJob))]
    public static class Pawn_JobTracker_TryFindAndStartJob_Patch
    {
        // This Prefix now controls the entire job-giving process in multiplayer.
        // It returns false to prevent the original method from EVER running for any player.
        static bool Prefix(Pawn_JobTracker __instance)
        {
            if (Multiplayer.Client == null) return true; // Singleplayer runs the original.

            // Only the host should be thinking for the AI.
            if (Multiplayer.LocalServer != null)
            {
                Pawn pawn = __instance.pawn;
                if (pawn.thinker == null) return false;

                // Use deterministic RNG
                Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, pawn.Map.AsyncTime().mapTicks));

                try
                {
                    // This is the core logic from the original game's DetermineNextJob method.
                    ThinkResult thinkResult = pawn.thinker.MainThinkNodeRoot.TryIssueJobPackage(pawn, new JobIssueParams());

                    if (thinkResult.IsValid)
                    {
                        // We have a valid job. Package it and send it to everyone.
                        // We do NOT call StartJob here on the host. We wait for the sync response.
                        var jobParams = new JobParams(thinkResult.Job);
                        jobParams.jobGiver = thinkResult.SourceNode; // Store the source of the job
                        jobParams.thinkTree = pawn.thinker.MainThinkTree;

                        // Give the job to everyone (including ourself) through the sync system
                        SyncedJobGiver.GiveJob(pawn, jobParams);
                    }
                }
                finally
                {
                    // Always pop the state
                    Rand.PopState();
                }
            }

            // Clients do nothing and wait for the host's command.
            // Returning false skips the original method.
            return false;
        }
    }
}
