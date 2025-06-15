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
        /// <summary>
        /// This prefix now returns a bool to control whether the original method runs.
        /// It handles the critical edge case of map generation.
        /// </summary>
        static bool Prefix(PlanetTile ___tile, ref TimeSnapshot? __state)
        {
            if (Multiplayer.Client == null)
                return true; // Let vanilla run if not in multiplayer

            Map map = Current.Game.FindMap(___tile.tileId);
            if (map == null)
                return true; // No map exists, let vanilla handle it (likely for world view)

            // This is the key change. If AsyncTime is null, the map is being generated.
            // It is SAFE to let the vanilla method run because the entire MapGenerator
            // process is already inside a deterministically seeded block.
            if (map.AsyncTime() == null)
            {
                return true; // Let vanilla code run within the seeded map-gen context
            }

            // If the map and its components are fully loaded, we use our multiplayer logic.
            __state = TimeSnapshot.GetAndSetFromMap(map);

            // Return false to prevent the original (potentially unseeded) method from running.
            return false;
        }

        /// <summary>
        /// The postfix remains the same, restoring the time snapshot if one was taken.
        /// </summary>
        static void Postfix(TimeSnapshot? __state)
        {
            // This only runs if the prefix returned false, as it's paired with __state.
            __state?.Set();
        }
    }



    [HarmonyPatch(typeof(TileTemperaturesComp), nameof(TileTemperaturesComp.RetrieveCachedData))]
    static class RetrieveCachedData_Patch
    {
        static bool Prefix(TileTemperaturesComp __instance, PlanetTile tile, ref TileTemperaturesComp.CachedTileTemperatureData __result)
        {
            if (Multiplayer.InInterface && __instance != Multiplayer.WorldComp.uiTemperatures)
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
        static Dictionary<int, float[]> averageTileTemps = new Dictionary<int, float[]>();

        static bool Prefix(PlanetTile tile, Twelfth twelfth)
        {
            return !averageTileTemps.TryGetValue(tile.tileId, out float[] arr) || float.IsNaN(arr[(int)twelfth]);
        }

        static void Postfix(PlanetTile tile, Twelfth twelfth, ref float __result)
        {
            if (averageTileTemps.TryGetValue(tile.tileId, out float[] arr) && !float.IsNaN(arr[(int)twelfth]))
            {
                __result = arr[(int)twelfth];
                return;
            }

            if (arr == null)
                averageTileTemps[tile.tileId] = Enumerable.Repeat(float.NaN, 12).ToArray();

            averageTileTemps[tile.tileId][(int)twelfth] = __result;
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
