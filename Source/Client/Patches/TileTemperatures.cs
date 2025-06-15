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
        static void Postfix(ref int __result)
        {
            if (TickManager_Patch_State.TicksGame_Agnostic.HasValue)
            {
                __result = TickManager_Patch_State.TicksGame_Agnostic.Value;
            }
        }
    }

    /// <summary>
    /// This patch does the same as above for TicksAbs, which is also used in temperature calculations.
    /// For the purpose of temperature, using the map's current tick is sufficient and deterministic.
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TicksAbs), MethodType.Getter)]
    static class TickManager_TicksAbs_Patch
    {
        static void Postfix(ref int __result)
        {
            if (TickManager_Patch_State.TicksGame_Agnostic.HasValue)
            {
                __result = TickManager_Patch_State.TicksGame_Agnostic.Value;
            }
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp.CachedTileTemperatureData))]
    [HarmonyPatch(nameof(TileTemperaturesComp.CachedTileTemperatureData.CheckCache))]
    static class CachedTileTemperatureData_CheckCache
    {
        /// <summary>
        /// Before the temperature cache is checked, we set our tick override if needed.
        /// </summary>
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client == null) return;

            // If the map has a valid async time component, set the override.
            if (___map.AsyncTime() != null)
            {
                TickManager_Patch_State.TicksGame_Agnostic = ___map.AsyncTime().mapTicks;
            }
        }

        /// <summary>
        /// After the cache check is complete, we clear our override to restore normal game behavior.
        /// A Finalizer is used to guarantee this runs even if an error occurs.
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
