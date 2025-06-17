// In CoreTick_Patch.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class CoreTick_Patch
    {
        public static void Postfix_JobTrackerTickInterval(Pawn_JobTracker __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.LocalServer == null) return;
            if (__instance.curJob == null && __instance.pawn.IsHashIntervalTick(120) && __instance.pawn.mindState.Active)
            {
                __instance.CheckForJobOverride();
            }
        }
    }
}

