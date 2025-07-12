using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TileTemperaturesComp.CachedTileTemperatureData))]
    [HarmonyPatch(nameof(TileTemperaturesComp.CachedTileTemperatureData.CheckCache))]
    static class CachedTileTemperatureData_CheckCache
    {
        // 1) Change 'ref' to 'out' and initialize __state immediately
        static void Prefix(PlanetTile ___tile, out TimeSnapshot? __state)
        {
            __state = null;                                      // always assign
            if (Multiplayer.Client == null)                      // early exit if no client
                return;
            var map = Current.Game.FindMap(___tile);
            if (map == null)                                     // early exit if no map
                return;
            __state = TimeSnapshot.GetAndSetFromMap(map);        // otherwise capture your snapshot
        }

        // 2) Postfix now just takes the state by value
        static void Postfix(TimeSnapshot? __state)
        {
            if (__state.HasValue)
                __state.Value.Set();
        }
    }


    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.RetrieveCachedData))]
    static class RetrieveCachedData_Patch
    {
        // 1. Use the correct parameter type here:
        static bool Prefix(TileTemperaturesComp __instance,
                        RimWorld.Planet.PlanetTile tile,
                        ref TileTemperaturesComp.CachedTileTemperatureData __result)
        {
            if (Multiplayer.InInterface 
            && __instance != Multiplayer.WorldComp.uiTemperatures)
            {
                __result = Multiplayer.WorldComp.uiTemperatures.RetrieveCachedData(tile);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.WorldComponentTick))]
    static class TileTemperaturesTick_Patch
    {
        static void Prefix(TileTemperaturesComp __instance)
        {
            if (Multiplayer.InInterface && __instance != Multiplayer.WorldComp.uiTemperatures)
                Multiplayer.WorldComp.uiTemperatures.WorldComponentTick();
        }
    }

    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.AverageTemperatureAtTileForTwelfth))]
    static class CacheAverageTileTemperature
    {
        // Now we key by the tile's int ID
        static Dictionary<PlanetTile, float[]> averageTileTemps = new();

        // Prefix must take PlanetTile, not int
        static bool Prefix(PlanetTile tile, Twelfth twelfth)
        {
            int tileID = tile.tileId; // or tile.TileID depending on the decompiled name
            return !averageTileTemps.TryGetValue(tileID, out var arr)
                   || float.IsNaN(arr[(int)twelfth]);
        }

        // Postfix also takes PlanetTile
        static void Postfix(PlanetTile tile, Twelfth twelfth, ref float __result)
        {
            int tileID = tile.tileId; // same ID property
            if (averageTileTemps.TryGetValue(tileID, out var arr)
             && !float.IsNaN(arr[(int)twelfth]))
            {
                __result = arr[(int)twelfth];
                return;
            }

            // initialize if needed
            averageTileTemps[tileID] = Enumerable.Repeat(float.NaN, 12).ToArray();
            averageTileTemps[tileID][(int)twelfth] = __result;
        }

        public static void Clear()
        {
            averageTileTemps.Clear();
        }
    }

    [HarmonyPatch]
    static class ClearTemperatureCache
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.StandardizeTileData));
            yield return AccessTools.Method(typeof(WorldGenStep_Terrain), nameof(WorldGenStep_Terrain.GenerateFresh));
        }

        static void Postfix() => CacheAverageTileTemperature.Clear();
    }

}
