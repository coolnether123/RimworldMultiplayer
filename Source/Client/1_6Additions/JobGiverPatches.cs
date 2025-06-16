// Multiplayer/Client/Patches/JobGiverPatches.cs

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
        static bool Prefix(Pawn_JobTracker __instance, ref Job __state)
        {
            if (Multiplayer.Client == null) return true;
            if (Multiplayer.LocalServer == null) return false;

            // === HOST-ONLY LOGIC ===
            __state = __instance.curJob;

            Rand.PushState(Gen.HashCombineInt(__instance.pawn.thingIDNumber, __instance.pawn.Map.AsyncTime().mapTicks));

            return true;
        }

        static void Finalizer(Pawn_JobTracker __instance, Job __state)
        {
            if (Multiplayer.LocalServer == null) return;

            Rand.PopState();

            Job newJob = __instance.curJob;
            if (newJob != null && newJob != __state)
            {
                var jobParams = new JobParams(newJob);
                // Corrected: Pass the pawn from the instance (__instance.pawn)
                SyncedJobGiver.GiveJob(__instance.pawn, jobParams);
            }
        }
    }
}
