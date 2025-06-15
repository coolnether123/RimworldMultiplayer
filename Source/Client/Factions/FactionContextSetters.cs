using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.Factions;

[HarmonyPatch(typeof(SettlementUtility), nameof(SettlementUtility.AttackNow))]
static class AttackNowPatch
{
    static void Prefix(Caravan caravan)
    {
        FactionContext.Push(caravan.Faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

public static class TileFactionContext
{
    private static readonly Dictionary<int, Faction> tileFactions = new Dictionary<int, Faction>();
    private static readonly object lockObject = new object();

    public static void SetFactionForTile(int tileId, Faction faction)
    {
        lock (lockObject)
        {
            tileFactions[tileId] = faction;
        }
    }

    public static Faction GetFactionForTile(int tileId)
    {
        lock (lockObject)
        {
            return tileFactions.TryGetValue(tileId, out Faction faction) ? faction : null;
        }
    }

    public static void ClearTile(int tileId)
    {
        lock (lockObject)
        {
            tileFactions.Remove(tileId);
        }
    }
}

// Patch the SetupCamp method to store faction by tile ID
[HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.SetupCamp))]
static class SetupCamp_StoreFactionByTile_Patch
{
    static void Prefix(Caravan caravan)
    {
        if (caravan != null)
            TileFactionContext.SetFactionForTile(caravan.Tile, caravan.Faction);
    }
}

/// <summary>
/// This patch intercepts the "Setup Camp" command action. Instead of letting it run its
/// local, unsynced logic, it replaces the action with a call to our new SyncedSetupCamp method.
/// </summary>
[HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.SetupCamp))]
public static class SetupCamp_SyncGizmo_Patch
{
    static void Postfix(Command __result, Caravan caravan)
    {
        // Ensure we are only modifying a Command_Action, as expected.
        if (__result is Command_Action cmd)
        {
            // Store the original action for debugging or reference if needed.
            var originalAction = cmd.action;

            // Replace the original action with a new one that calls our SyncMethod.
            cmd.action = () =>
            {
                // Before queuing the map generation, store the caravan's faction
                // context in our thread-safe dictionary, keyed by the tile ID.
                TileFactionContext.SetFactionForTile(caravan.Tile, caravan.Faction);

                // Now call the synced method that all clients will execute.
                SyncMethods.SyncedSetupCamp(caravan);
            };
        }
    }
}

[HarmonyPatch(typeof(GetOrGenerateMapUtility), nameof(GetOrGenerateMapUtility.GetOrGenerateMap), new[] { typeof(PlanetTile), typeof(IntVec3), typeof(WorldObjectDef), typeof(IEnumerable<GenStepWithParams>), typeof(bool) })]
static class MapGenFactionPatch
{
    /// <summary>
    /// The Prefix method signature now correctly accepts a PlanetTile object as its first parameter.
    /// We use its .tileId property for lookups.
    /// </summary>
    static void Prefix(PlanetTile tile)
    {
        // Try to get a faction from our thread-safe context using the integer tile ID.
        var faction = TileFactionContext.GetFactionForTile(tile.tileId);

        // If not found (e.g., not a caravan camp), fall back to the faction of the MapParent at the tile.
        if (faction == null)
        {
            // Use tile.tileId here to correctly call MapParentAt.
            var mapParent = Find.WorldObjects.MapParentAt(tile.tileId);
            faction = mapParent?.Faction;
        }

        // Push the determined faction to the context stack.
        FactionContext.Push(faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

// Clean up after map generation is complete
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
static class CleanupTileFactionContext
{
    static void Finalizer(MapParent parent)
    {
        if (parent != null)
            TileFactionContext.ClearTile(parent.Tile);
    }
}

public static class CaravanFactionContext
{
    public static Faction Current { get; private set; }

    public static void Push(Faction faction) => Current = faction;
    public static void Pop() => Current = null;
}

// This is the new patch that captures the context
[HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.SetupCamp))]
static class SetupCamp_FactionContext_Patch
{
    static void Prefix(Caravan caravan)
    {
        // Before SetupCamp runs, grab the caravan's faction and store it.
        if (caravan != null)
            CaravanFactionContext.Push(caravan.Faction);
    }

    static void Finalizer()
    {
        // After SetupCamp is completely finished (or has an error),
        // clear the stored faction to prevent it from leaking into other operations.
        CaravanFactionContext.Pop();
    }
}

[HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter), new[] { typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool) })]
static class CaravanEnterFactionPatch
{
    static void Prefix(Caravan caravan)
    {
        FactionContext.Push(caravan.Faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount))]
static class WealthRecountFactionPatch
{
    static void Prefix(WealthWatcher __instance)
    {
        FactionContext.Push(__instance.map.ParentFaction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(FactionIdeosTracker), nameof(FactionIdeosTracker.RecalculateIdeosBasedOnPlayerPawns))]
static class RecalculateFactionIdeosContext
{
    static void Prefix(FactionIdeosTracker __instance)
    {
        FactionContext.Push(__instance.faction);
    }

    static void Finalizer()
    {
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(Bill), nameof(Bill.ValidateSettings))]
static class BillValidateSettingsPatch
{
    static void Prefix(Bill __instance)
    {
        if (Multiplayer.Client == null) return;
        FactionContext.Push(__instance.pawnRestriction?.Faction); // todo HostFaction, SlaveFaction?
    }

    static void Finalizer()
    {
        if (Multiplayer.Client == null) return;
        FactionContext.Pop();
    }
}

[HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ValidateSettings))]
static class BillProductionValidateSettingsPatch
{
    static void Prefix(Bill_Production __instance, ref Map __state)
    {
        if (Multiplayer.Client == null) return;

        if (__instance.Map != null && __instance.billStack?.billGiver is Thing { Faction: { } faction })
        {
            __instance.Map.PushFaction(faction);
            __state = __instance.Map;
        }
    }

    static void Finalizer(Map __state)
    {
        __state?.PopFaction();
    }
}
