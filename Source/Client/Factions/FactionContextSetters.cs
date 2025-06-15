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

[HarmonyPatch(typeof(GetOrGenerateMapUtility), nameof(GetOrGenerateMapUtility.GetOrGenerateMap), new[] { typeof(PlanetTile), typeof(IntVec3), typeof(WorldObjectDef), typeof(IEnumerable<GenStepWithParams>), typeof(bool) })]
static class MapGenFactionPatch
{
    static void Prefix(PlanetTile tile)
    {
        Faction factionToSet = null;

        // NEW: Check if we have a stored faction for this tile
        factionToSet = TileFactionContext.GetFactionForTile(tile.tileId);

        if (factionToSet == null)
        {
            // FALLBACK: Use the old logic for all other cases
            var mapParent = Find.WorldObjects.MapParentAt(tile);
            factionToSet = mapParent?.Faction;
        }

        if (factionToSet == null && Multiplayer.Client != null)
        {
            Log.Warning($"Couldn't set the faction context for map gen at {tile.tileId}: no world object and no stored faction.");
        }

        FactionContext.Push(factionToSet);
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
