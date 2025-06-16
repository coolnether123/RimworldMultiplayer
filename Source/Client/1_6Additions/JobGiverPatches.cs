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
        static bool Prefix(Pawn_JobTracker __instance)
        {
            if (Multiplayer.Client == null) return true;

            if (Multiplayer.LocalServer != null)
            {
                Pawn pawn = __instance.pawn;
                if (pawn.thinker == null) return false;

                Rand.PushState(Gen.HashCombineInt(pawn.thingIDNumber, pawn.Map.AsyncTime().mapTicks));

                try
                {
                    ThinkResult thinkResult = pawn.thinker.MainThinkNodeRoot.TryIssueJobPackage(pawn, new JobIssueParams());

                    if (thinkResult.IsValid)
                    {
                        // MODIFICATION: Explicitly set the source on the job before creating JobParams
                        Job newJob = thinkResult.Job;
                        newJob.jobGiver = thinkResult.SourceNode;
                        newJob.jobGiverThinkTree = pawn.thinker.MainThinkTree;

                        var jobParams = new JobParams(newJob);
                        SyncedJobGiver.GiveJob(pawn, jobParams);
                    }
                }
                finally
                {
                    Rand.PopState();
                }
            }

            return false;
        }
    }
}
