using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using Multiplayer.Client.Util;

namespace Multiplayer.Client.Saving
{
    [HarmonyPatch(typeof(SaveLoad), nameof(SaveLoad.SaveAndReload))]
    static class SaveLoad_SaveAndReload_Patch
    {
        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            Log.Message("Multiplayer [SaveLoad_SaveAndReload_Patch]: Running final initialization hook.");

            // Temporarily set the game's tick manager to a valid, non-zero state for initialization.
            int currentTicks = Find.TickManager.TicksGame;
            if (currentTicks == 0) Find.TickManager.ticksGameInt = 1;

            // Initialize all player-like factions with a valid time context.
            if (Multiplayer.WorldComp != null)
            {
                foreach (var factionId in Multiplayer.WorldComp.factionData.Keys.ToList())
                {
                    var faction = Find.FactionManager.GetById(factionId);
                    if (faction != null)
                    {
                        Multiplayer.WorldComp.FinalizeInitFaction(faction);
                    }
                }
            }

            // Re-initialize the SyncCoordinator with a valid time context.
            if (Multiplayer.game?.sync != null)
            {
                Multiplayer.game.sync.knownClientOpinions.Clear();
                // Ensure TickPatch.Timer is also valid before creating the opinion.
                if (TickPatch.Timer == 0) TickPatch.SetTimer(1);
                Multiplayer.game.sync.currentOpinion = new ClientSyncOpinion(TickPatch.Timer)
                {
                    isLocalClientsOpinion = true
                };
                Log.Message("Multiplayer [SaveLoad_SaveAndReload_Patch]: SyncCoordinator state initialized.");
            }

            // Restore the original tick count.
            Find.TickManager.ticksGameInt = currentTicks;

            TickPatch.Reset();
            Log.Message("Multiplayer [SaveLoad_SaveAndReload_Patch]: TickPatch state reset. Ready for gameplay.");
        }
    }
}
