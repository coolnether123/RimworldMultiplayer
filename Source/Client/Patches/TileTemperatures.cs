using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using Verse;


namespace Multiplayer.Client
{
    /// <summary>
    /// This static class holds a temporary override for the game's tick count.
    /// This allows us to make methods that rely on Find.TickManager.TicksGame use
    /// a specific map's async time instead of the global one.
    /// </summary>
    public static class TickManager_Patch_State
    {
        public static int? TicksGame_Agnostic = null;
    }

    /// <summary>
    /// This patch intercepts calls to get the current game tick. If our override is set,
    /// it returns the override value (the map's specific tick count).
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TicksGame), MethodType.Getter)]
    static class TickManager_TicksGame_Patch
    {
        static bool Prefix(ref int __result)
        {
            if (TickManager_Patch_State.TicksGame_Agnostic.HasValue)
            {
                __result = TickManager_Patch_State.TicksGame_Agnostic.Value;
                return false; // Skip the original method
            }
            return true; // Let the original method run
        }
    }

    /// <summary>
    /// This patch does the same as above for TicksAbs, which is also used in temperature calculations.
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TicksAbs), MethodType.Getter)]
    static class TickManager_TicksAbs_Patch
    {
        static bool Prefix(ref int __result)
        {
            if (TickManager_Patch_State.TicksGame_Agnostic.HasValue)
            {
                __result = TickManager_Patch_State.TicksGame_Agnostic.Value;
                return false; // Skip the original method
            }
            return true; // Let the original method run
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp.CachedTileTemperatureData))]
    [HarmonyPatch(nameof(TileTemperaturesComp.CachedTileTemperatureData.CheckCache))]
    static class CachedTileTemperatureData_CheckCache
    {
        /// <summary>
        /// This Prefix correctly uses the available `___tile` field from the CachedTileTemperatureData instance
        /// to look up the map and set the tick override.
        /// </summary>
        static void Prefix(PlanetTile ___tile)
        {
            if (Multiplayer.Client == null) return;

            // Use the provided tile to find the map, if it exists.
            Map map = Current.Game.FindMap(___tile.tileId);

            // If the map is null OR its multiplayer components are not ready, do nothing.
            // The vanilla logic will run, which is safe during the seeded map generation process.
            if (map == null || map.AsyncTime() == null) return;

            // If the map is fully loaded, set our static override to this map's AsyncTime tick count.
            // The TickManager patches will ensure this value is used by the subsequent vanilla calculations.
            TickManager_Patch_State.TicksGame_Agnostic = map.AsyncTime().mapTicks;
        }

        /// <summary>
        /// The Finalizer ensures our tick override is always cleared, restoring normal game behavior.
        /// </summary>
        static void Finalizer()
        {
            // Clear the override so the rest of the game uses the real TicksGame.
            TickManager_Patch_State.TicksGame_Agnostic = null;
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
