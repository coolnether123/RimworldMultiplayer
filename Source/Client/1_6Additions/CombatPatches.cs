// In new file: CombatPatches.cs

using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition))]
    public static class Toils_Combat_GotoCastPosition_Patch
    {
        // This Postfix needs to be public as well
        public static void Postfix(Toil __result)
        {
            var originalInit = __result.initAction;
            if (originalInit == null) return;

            __result.initAction = () =>
            {
                originalInit();

                var pawn = __result.actor;
                var job = pawn.CurJob;

                if (job.verbToUse != null && Multiplayer.ShouldSync)
                {
                    // Call the sync method, passing the PAWN, not the JOB.
                    SyncedActions.SetJobVerb(pawn, new JobParams(job));
                }
            };
        }
    }
}
