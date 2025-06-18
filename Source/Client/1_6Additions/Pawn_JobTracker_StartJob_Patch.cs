using HarmonyLib;
using Verse.AI;

namespace Multiplayer.Client.Patches
{
    // Patch the full four-parameter overload of StartJob
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    [HarmonyPatch(new[] {
        typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool)
    })]
    static class Pawn_JobTracker_StartJob_Patch
    {
        // ------------ block client-side autonomous AI -----------------------
        static bool Prefix(Pawn_JobTracker __instance,
                           Job newJob,
                           JobCondition _cond,
                           ThinkNode jobGiver,
                           bool _resume)
        {
            if (PathingPatches.InSyncAction > 0) return true;     // let synced action run

            if (Multiplayer.Client != null &&
                Multiplayer.LocalServer == null &&
                jobGiver != null)                                // AI job on client
                return false;

            return true;
        }

        // ------------ host broadcasts AI job --------------------------------
        static void Postfix(Pawn_JobTracker __instance,
                            Job newJob,
                            JobCondition _cond,
                            ThinkNode jobGiver,
                            bool _resume)
        {
            if (Multiplayer.LocalServer == null || jobGiver == null) return;   // host only
            SyncedActions.StartJobAI(__instance.pawn, new JobParams(newJob));
        }
    }
}
