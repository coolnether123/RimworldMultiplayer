using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client
{
    /// <summary>
    /// FINAL FIX: This patch was trying to be clever by reusing old graphical data (SectionLayers)
    /// to speed up the host reload cycle. This process is fragile and was causing the lighting system
    /// to break, resulting in the "Red Screen of Death".
    /// By changing the Prefix to simply "return true", we disable all of this custom logic and
    /// allow the original vanilla method to run. The vanilla code knows how to regenerate all graphics
    /// safely from scratch, which fixes the rendering errors.
    /// </summary>
    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<int, MapDrawer> copyFrom = new();

        static bool Prefix()
        {
            // By returning true, we skip all the old, broken caching logic and
            // let the original game method run. This is the fix for the red screen.
            return true;
        }
    }

    /// <summary>
    /// This patch targets the WorldGrid constructor. It was previously used to reset static values
    /// that are no longer static in 1.6. The old logic caused a crash. This no-op (no operation)
    /// replacement allows the constructor to run normally without errors.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), MethodType.Constructor)]
    static class WorldGridConstructor_Patch
    {
        static bool Prefix()
        {
            // The original patch logic is obsolete and has been removed. Let the vanilla constructor run.
            return true;
        }
    }

    /// <summary>
    /// This patch handles copying the WorldGrid data during the host reload cycle.
    /// It includes a critical null-check for the 'surface' field to prevent crashes
    /// when the grid is not yet fully initialized.
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.ExposeData))]
    public static class WorldGridExposeDataPatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance)
        {
            // SAFETY CHECK: If the surface layer isn't initialized, this object is not ready.
            var surfaceLayer = Traverse.Create(__instance).Field("surface").GetValue<SurfaceLayer>();
            if (surfaceLayer == null)
            {
                return true;
            }

            if (copyFrom == null) return true;

            Log.Message("Multiplayer: Performing WorldGrid copy for reload.");

            var gridTiles = __instance.Tiles.ToList();
            var copyTiles = copyFrom.Tiles.ToList();

            for (int i = 0; i < copyTiles.Count; i++)
            {
                var gridTile = gridTiles[i];
                var copyTile = copyTiles[i];

                gridTile.biome = copyTile.biome;
                gridTile.elevation = copyTile.elevation;
                gridTile.hilliness = copyTile.hilliness;
                gridTile.temperature = copyTile.temperature;
                gridTile.rainfall = copyTile.rainfall;
                gridTile.swampiness = copyTile.swampiness;
                gridTile.feature = copyTile.feature;
                // Direct assignment of the backing fields for Roads/Rivers is correct and sufficient.
                gridTile.potentialRoads = copyTile.potentialRoads;
                gridTile.potentialRivers = copyTile.potentialRivers;
            }


            //grid.tileBiome = copyFrom.tileBiome;
            //grid.tileElevation = copyFrom.tileElevation;
            //grid.tileHilliness = copyFrom.tileHilliness;
            //grid.tileTemperature    = copyFrom.tileTemperature;
            //grid.tileRainfall       = copyFrom.tileRainfall;
            //grid.tileSwampiness     = copyFrom.tileSwampiness;
            //grid.tileFeature        = copyFrom.tileFeature;
            //grid.tileRoadOrigins    = copyFrom.tileRoadOrigins;
            //grid.tileRoadAdjacency  = copyFrom.tileRoadAdjacency;
            //grid.tileRoadDef        = copyFrom.tileRoadDef;
            //grid.tileRiverOrigins   = copyFrom.tileRiverOrigins;
            //grid.tileRiverAdjacency = copyFrom.tileRiverAdjacency;
            //grid.tileRiverDef       = copyFrom.tileRiverDef;

            // This is plain old data apart from the WorldFeature feature field which is a reference
            // It later gets reset in WorldFeatures.ExposeData though so it can be safely copied

            // ExposeData runs multiple times but WorldGrid only needs LoadSaveMode.LoadingVars
            copyFrom = null;

            // Skip the original method because we've handled the data copy.
            return false;
        }
    }

    /// <summary>
    /// This patch handles caching and restoring the WorldRenderer, similar to the WorldGrid patch.
    /// </summary>
    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    public static class WorldRendererCachePatch
    {
        public static WorldRenderer copyFrom;

        static bool Prefix(WorldRenderer __instance)
        {
            if (copyFrom == null) return true;

            // Use Traverse (reflection) to safely set the private 'layers' field.
            var layersToCopy = Traverse.Create(copyFrom).Field("layers").GetValue();
            Traverse.Create(__instance).Field("layers").SetValue(layersToCopy);

            copyFrom = null;

            return false;
        }
    }
}
