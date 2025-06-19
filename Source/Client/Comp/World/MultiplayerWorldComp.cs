using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;
using Multiplayer.Common;
using System;
using HarmonyLib;

namespace Multiplayer.Client;

public class MultiplayerWorldComp : IHasSessionData
{
    // SortedDictionary to ensure determinism
    public SortedDictionary<int, FactionWorldData> factionData = new();

    public World world;

    public TileTemperaturesComp uiTemperatures;
    public List<MpTradeSession> trading = new(); // Should only be modified from MpTradeSession in PostAdd/Remove and ExposeData
    public SessionManager sessionManager = new(null);

    public Faction spectatorFaction;

    private int currentFactionId;

    public MultiplayerWorldComp(World world)
    {
        this.world = world;
        uiTemperatures = new TileTemperaturesComp(world);
    }



    // Called from AsyncWorldTimeComp.ExposeData (for backcompat)
    public void ExposeData()
    {
        ExposeFactionData();

        sessionManager.ExposeSessions();
        // Ensure a pause lock session exists if there's any pause locks registered
        if (!PauseLockSession.pauseLocks.NullOrEmpty())
            sessionManager.AddSession(new PauseLockSession(null));

        // The DoBackCompat logic is now handled by the SaveLoad_SaveAndReload_Patch
        // DoBackCompat(); 
    }

    // Creates and fully initializes all world- and map-level data for a new faction.
    public void InitNewPlayerFaction(Faction faction)
    {
        Log.Message($"Multiplayer: Initializing all data for new faction {faction.Name} ({faction.loadID})");

        // Create and add the world-level data
        var newWorldData = FactionWorldData.New(faction.loadID);
        factionData[faction.loadID] = newWorldData;
        newWorldData.ReassignIds();

        // This creates the first data point and prevents the "Sequence contains no elements" error.
        newWorldData.history.FinalizeInit();

        // Tell all existing maps about this new faction
        foreach (var map in Find.Maps)
            MapSetup.InitNewFactionData(map, faction);
    }

    // This ensures all sub-managers, especially History, get their initial state.
    public void FinalizeInitFaction(Faction faction)
    {
        if (!factionData.TryGetValue(faction.loadID, out FactionWorldData worldData))
        {
            Log.Warning($"Multiplayer: Tried to finalize init for faction {faction.Name} ({faction.loadID}) but no FactionWorldData was found. Creating it now.");
            worldData = FactionWorldData.New(faction.loadID);
            factionData[faction.loadID] = worldData;
            worldData.ReassignIds();
        }

        var history = worldData.history;
        var recorderGroupsField = Traverse.Create(history).Field("autoRecorderGroups");
        var recorderGroups = recorderGroupsField.GetValue<List<HistoryAutoRecorderGroup>>();

        if (recorderGroups == null)
        {
            Log.Message($"Multiplayer: History recorders for {faction.Name} are null. Manually creating them.");
            Traverse.Create(history).Method("AddOrRemoveHistoryRecorderGroups").GetValue();
            recorderGroups = recorderGroupsField.GetValue<List<HistoryAutoRecorderGroup>>(); // Re-fetch the list after creation
        }

        // This prevents the "Sequence contains no elements" error.
        if (recorderGroups != null)
        {
            Log.Message($"Multiplayer: Manually priming {recorderGroups.Count} history recorder groups for {faction.Name}.");
            foreach (var group in recorderGroups)
            {
                group.Tick();
            }
        }
        else
        {
            Log.Error($"Multiplayer: Failed to create or find history recorders for {faction.Name}. This may cause issues.");
        }

        // Initialize map-level data for this faction on all maps
        foreach (var map in Find.Maps)
        {
            if (!map.MpComp().factionData.ContainsKey(faction.loadID))
            {
                MapSetup.InitNewFactionData(map, faction);
            }
        }

    }

    private void DoBackCompat()
    {
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
            return;

        if (spectatorFaction == null)
        {
            void AddSpectatorFaction()
            {
                spectatorFaction = HostUtil.AddNewFaction("Spectator", FactionDefOf.PlayerColony);


                if (!factionData.ContainsKey(spectatorFaction.loadID))
                {
                    factionData[spectatorFaction.loadID] = FactionWorldData.New(spectatorFaction.loadID);
                }
            }

            void RemoveOpponentFaction()
            {
                // Test faction left in by mistake in version 0.7
                var opponent =
                    Find.FactionManager.AllFactions.FirstOrDefault(f => f.Name == "Opponent" && f.IsPlayer);

                if (opponent is not null)
                {
                    opponent.RemoveAllRelations();
                    Find.FactionManager.allFactions.Remove(opponent);
                    Log.Warning("Multiplayer removed dummy Opponent faction");
                }
            }

            AddSpectatorFaction();
            RemoveOpponentFaction();
        }
    }

    private void ExposeFactionData()
    {
        Scribe_References.Look(ref spectatorFaction, "spectatorFaction");

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            int currentFactionId = GetFactionId(Find.ResearchManager);
            Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

            var savedFactionData = new SortedDictionary<int, FactionWorldData>(factionData);
            savedFactionData.Remove(currentFactionId);

            Scribe_Custom.LookValueDeep(ref savedFactionData, "factionData");
        }
        else
        {
            // The faction whose data is currently set
            Scribe_Values.Look(ref currentFactionId, "currentFactionId");

            Scribe_Custom.LookValueDeep(ref factionData, "factionData");
            factionData ??= new SortedDictionary<int, FactionWorldData>();
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars && Multiplayer.session != null && Multiplayer.game != null)
        {
            Multiplayer.game.myFactionLoading =
                Find.FactionManager.GetById(Multiplayer.session.myFactionId) ?? spectatorFaction;
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            // Game manager order?
            factionData[currentFactionId] = FactionWorldData.FromCurrent(currentFactionId);
        }
    }

    public void WriteSessionData(ByteWriter writer)
    {
        sessionManager.WriteSessionData(writer);
    }

    public void ReadSessionData(ByteReader data)
    {
        sessionManager.ReadSessionData(data);
    }

    public void TickWorldSessions()
    {
        sessionManager.TickSessions();
    }

    public void RemoveTradeSession(MpTradeSession session)
    {
        // Cleanup and removal from `trading` field is handled in PostRemoveSession
        sessionManager.RemoveSession(session);
    }

    public void SetFaction(Faction faction)
    {
        if (!factionData.TryGetValue(faction.loadID, out FactionWorldData data))
            return;

        Game game = Current.Game;
        game.researchManager = data.researchManager;
        game.drugPolicyDatabase = data.drugPolicyDatabase;
        game.outfitDatabase = data.outfitDatabase;
        game.foodRestrictionDatabase = data.foodRestrictionDatabase;
        game.playSettings = data.playSettings;

        game.history = data.history;
        game.storyteller = data.storyteller;
        game.storyWatcher = data.storyWatcher;
    }

    public void DirtyColonyTradeForMap(Map map)
    {
        if (map == null) return;
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == map)
                session.deal.recacheColony = true;
    }

    public void DirtyTraderTradeForTrader(ITrader trader)
    {
        if (trader == null) return;
        foreach (MpTradeSession session in trading)
            if (session.trader == trader)
                session.deal.recacheTrader = true;
    }

    public void DirtyTradeForSpawnedThing(Thing t)
    {
        if (t is not { Spawned: true }) return;
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == t.Map)
                session.deal.recacheThings.Add(t);
    }

    public bool AnyTradeSessionsOnMap(Map map)
    {
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == map)
                return true;
        return false;
    }

    public int GetFactionId(ResearchManager researchManager)
    {
        return factionData.First(kv => kv.Value.researchManager == researchManager).Key;
    }

    public override string ToString()
    {
        return $"{nameof(MultiplayerWorldComp)}_{world}";
    }
}
