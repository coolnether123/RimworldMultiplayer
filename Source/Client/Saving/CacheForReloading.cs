using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{

    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.RegenerateEverythingNow))]
    public static class MapDrawerRegenPatch
    {
        public static Dictionary<int, MapDrawer> copyFrom = new();

        // These are readonly so they need to be set using reflection
        private static FieldInfo mapDrawerMap = AccessTools.Field(typeof(MapDrawer), nameof(MapDrawer.map));
        private static FieldInfo sectionMap = AccessTools.Field(typeof(Section), nameof(Section.map));

        static bool Prefix(MapDrawer __instance)
        {
            Map map = __instance.map;
            if (!copyFrom.TryGetValue(map.uniqueID, out MapDrawer keepDrawer)) return true;

            map.mapDrawer = keepDrawer;
            mapDrawerMap.SetValue(keepDrawer, map);

            foreach (Section section in keepDrawer.sections)
            {
                sectionMap.SetValue(section, map);

                for (int i = 0; i < section.layers.Count; i++)
                {
                    SectionLayer layer = section.layers[i];

                    if (!ShouldKeep(layer))
                        section.layers[i] = (SectionLayer)Activator.CreateInstance(layer.GetType(), section);
                    else if (layer is SectionLayer_TerrainScatter scatter)
                        scatter.scats.Do(s => s.map = map);
                }
            }

            foreach (Section s in keepDrawer.sections)
                foreach (SectionLayer layer in s.layers)
                    if (!ShouldKeep(layer))
                        layer.Regenerate();

            copyFrom.Remove(map.uniqueID);

            return false;
        }

        static bool ShouldKeep(SectionLayer layer)
        {
            return layer.GetType().Assembly == typeof(Game).Assembly;
        }
    }

    [HarmonyPatch(typeof(WorldGrid), MethodType.Constructor)]
    public static class WorldGridCachePatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance, ref int ___cachedTraversalDistance, ref int ___cachedTraversalDistanceForStart, ref int ___cachedTraversalDistanceForEnd)
        {
            if (copyFrom == null) return true;

            WorldGrid grid = __instance;

            //grid.viewAngle = copyFrom.viewAngle;
            AccessTools.Property(typeof(WorldGrid), "SurfaceViewAngle").SetValue(grid, copyFrom.SurfaceViewAngle);
            //grid.viewCenter = copyFrom.viewCenter;
            AccessTools.Property(typeof(WorldGrid), "SurfaceViewCenter").SetValue(grid, copyFrom.SurfaceViewCenter);
            //grid.verts = copyFrom.verts;
            AccessTools.Property(typeof(WorldGrid), "UnsafeVerts").SetValue(grid, copyFrom.UnsafeVerts);

            //grid.tileIDToNeighbors_offsets = copyFrom.tileIDToNeighbors_offsets;
            AccessTools.Property(typeof(WorldGrid), "UnsafeTileIDToNeighbors_offsets").SetValue(grid, copyFrom.UnsafeTileIDToNeighbors_offsets);

            //grid.tileIDToNeighbors_values = copyFrom.tileIDToNeighbors_values;
            AccessTools.Property(typeof(WorldGrid), "UnsafeTileIDToNeighbors_values").SetValue(grid, copyFrom.UnsafeTileIDToNeighbors_values);

            //grid.tileIDToVerts_offsets = copyFrom.tileIDToVerts_offsets;
            AccessTools.Property(typeof(WorldGrid), "UnsafeTileIDToVerts_offsets").SetValue(grid, copyFrom.UnsafeTileIDToVerts_offsets);

            //grid.averageTileSize = copyFrom.averageTileSize;
            AccessTools.Property(typeof(WorldGrid), "AverageTileSize").SetValue(grid, copyFrom.AverageTileSize);

            
            
            var tiles = new List<Tile>();
            ___cachedTraversalDistance = -1;
            ___cachedTraversalDistanceForStart = -1;
            ___cachedTraversalDistanceForEnd = -1;

            AccessTools.Property(typeof(WorldGrid), "Tiles").SetValue(grid, tiles);

            copyFrom = null;

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.ExposeData))]
    public static class WorldGridExposeDataPatch
    {
        public static WorldGrid copyFrom;

        static bool Prefix(WorldGrid __instance)
        {
            if (copyFrom == null) return true;

            WorldGrid grid = __instance;

            List<SurfaceTile> copyTiles = copyFrom.Tiles.ToList<SurfaceTile>();
            List<SurfaceTile> gridTiles = grid.Tiles.ToList<SurfaceTile>();

            for(int i = 0; i < copyTiles.Count; i++)
            {
                gridTiles[i].biome = copyFrom[i].biome;
                gridTiles[i].elevation = copyFrom[i].elevation;
                gridTiles[i].hilliness = copyFrom[i].hilliness;
                gridTiles[i].temperature = copyFrom[i].temperature;
                gridTiles[i].rainfall = copyFrom[i].rainfall;
                gridTiles[i].swampiness = copyFrom[i].swampiness;
                gridTiles[i].feature = copyFrom[i].feature;
                //gridTiles[i].Roads = copyFrom[i].hilliness;
                AccessTools.Property(typeof(WorldGrid), "Roads").SetValue(gridTiles[i], copyFrom[i].Roads);
                gridTiles[i].potentialRoads = copyFrom[i].potentialRoads;
                gridTiles[i].riverDist = copyFrom[i].riverDist;
                //gridTiles[i].Rivers = copyFrom[i].hilliness;
                AccessTools.Property(typeof(WorldGrid), "Rivers").SetValue(gridTiles[i], copyFrom[i].riverDist);
                gridTiles[i].potentialRivers = copyFrom[i].potentialRivers;

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
            
            //grid.tiles = copyFrom.tiles;
            AccessTools.Property(typeof(WorldGrid), "Tiles").SetValue(grid, copyFrom.Tiles);

            // ExposeData runs multiple times but WorldGrid only needs LoadSaveMode.LoadingVars
            copyFrom = null;

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldRenderer), MethodType.Constructor)]
    public static class WorldRendererCachePatch
    {
        public static WorldRenderer copyFrom;

        static bool Prefix(WorldRenderer __instance)
        {
            if (copyFrom == null) return true;


            //__instance.ayers = copyFrom.layers;
            AccessTools.Property(typeof(WorldGrid), "AllDrawLayers").SetValue(__instance, copyFrom.AllDrawLayers);

            copyFrom = null;

            return false;
        }
    }
}
