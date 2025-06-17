// In CoreTick_Patch.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    static class CoreTick_Patch
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null || Multiplayer.LocalServer == null)
                return;

            // Only log this every 2 seconds to avoid spam
            if (Find.TickManager.TicksGame % 120 == 0)
            {
                int pawnCount = 0;
                int tickedCount = 0;

                foreach (var map in Find.Maps)
                {
                    if (map.mapPawns == null) continue;

                    foreach (var pawn in map.mapPawns.AllPawns)
                    {
                        pawnCount++;

                        if (pawn.needs == null)
                        {
                            // This can happen for mechs or other special pawns
                            // Log.Message($"[CoreTick_Patch] Skipping {pawn.LabelShortCap}: No needs component.");
                            continue;
                        }
                        if (pawn.jobs == null)
                        {
                            // Should be very rare
                            Log.Message($"[CoreTick_Patch] Skipping {pawn.LabelShortCap}: No jobs component.");
                            continue;
                        }
                        if (pawn.mindState == null || !pawn.mindState.Active)
                        {
                            // This is a common reason. Downed, deathresting, mental break, etc.
                            // Log.Message($"[CoreTick_Patch] Skipping {pawn.LabelShortCap}: MindState is not active.");
                            continue;
                        }
                        if (pawn.Dead || !pawn.Spawned)
                        {
                            // Basic safety checks
                            continue;
                        }

                        // If we passed all checks, tick the job tracker.
                        pawn.jobs.JobTrackerTickInterval(1);
                        tickedCount++;
                    }
                }

                // Summary log
                Log.Message($"[CoreTick_Patch] AI Tick Fired. Total Pawns: {pawnCount}. Ticked AI for: {tickedCount} pawns.");
            }
        }
    }
}
