using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client
{
    // This patch now explicitly targets the main GenerateMap method to avoid ambiguity.
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap), new[] { typeof(IntVec3), typeof(MapParent), typeof(MapGeneratorDef), typeof(IEnumerable<GenStepWithParams>), typeof(Action<Map>), typeof(bool), typeof(bool) })]
    public static class MapSetup
    {
        // The Postfix runs *after* the original method, receiving the generated map in __result.
        // This is the correct place to initialize multiplayer components for the map.
        static void Postfix(Map __result)
        {
            if (Multiplayer.Client == null || __result == null) return;
            SetupMap(__result);
        }

        public static void SetupMap(Map map)
        {
            Log.Message("MP: Setting up map " + map.uniqueID);

            // Initialize and store Multiplayer components
            var async = new AsyncTimeComp(map);
            Multiplayer.game.asyncTimeComps.Add(async);

            var mapComp = new MultiplayerMapComp(map);
            Multiplayer.game.mapComps.Add(mapComp);

            // Store all current managers for Faction.OfPlayer
            InitFactionDataFromMap(map, Faction.OfPlayer);

            // Add all other (non Faction.OfPlayer) factions to the map
            foreach (var faction in Find.FactionManager.AllFactions.Where(f => f.IsPlayer))
                if (faction != Faction.OfPlayer)
                    InitNewFactionData(map, faction);

            async.mapTicks = Find.Maps.Where(m => m != map).Select(m => m.AsyncTime()?.mapTicks).Max() ?? Find.TickManager.TicksGame;
            async.storyteller = new Storyteller(Find.Storyteller.def, Find.Storyteller.difficultyDef, Find.Storyteller.difficulty);
            async.storyWatcher = new StoryWatcher();

            if (!Multiplayer.GameComp.asyncTime)
                async.SetDesiredTimeSpeed(Find.TickManager.CurTimeSpeed);
        }

        private static void InitFactionDataFromMap(Map map, Faction f)
        {
            var mapComp = map.MpComp();
            mapComp.factionData[f.loadID] = FactionMapData.NewFromMap(map, f.loadID);

            var customData = mapComp.customFactionData[f.loadID] = CustomFactionMapData.New(f.loadID, map);

            foreach (var t in map.listerThings.AllThings)
                if (t is ThingWithComps tc &&
                    tc.GetComp<CompForbiddable>() is { forbiddenInt: false })
                    customData.unforbidden.Add(t);
        }

        public static void InitNewFactionData(Map map, Faction f)
        {
            var mapComp = map.MpComp();

            mapComp.factionData[f.loadID] = FactionMapData.New(f.loadID, map);
            mapComp.factionData[f.loadID].areaManager.AddStartingAreas();

            mapComp.customFactionData[f.loadID] = CustomFactionMapData.New(f.loadID, map);
        }
    }

    /// <summary>
    /// This patch fixes a desync during initial map setup and prevents a crash if a biome
    /// has no weathers defined.
    /// The vanilla `StartInitialWeather` method uses Rand without a deterministic seed,
    /// causing each client to generate different starting weather.
    /// This patch wraps the entire method in a seeded random block to ensure every client
    /// gets the same result.
    /// </summary>
    [HarmonyPatch(typeof(WeatherDecider), nameof(WeatherDecider.StartInitialWeather))]
    public static class WeatherDecider_StartInitialWeather_Patch
    {
        /// <summary>
        /// The Prefix runs before the original method. It will handle all our logic.
        /// It returns `true` to let the original method run, or `false` to skip it entirely.
        /// </summary>
        [HarmonyPriority(Priority.First)]
        static bool Prefix(WeatherDecider __instance, Map ___map)
        {
            // If not in multiplayer, do nothing and let the original method run.
            if (Multiplayer.Client == null) return true;

            // --- FIX 1: The Desync ---
            // Seed the random number generator with the map's unique ID. This is deterministic
            // across all clients and ensures everyone gets the same "random" weather.
            Rand.PushState(___map.uniqueID);

            // --- FIX 2: The Crash ---
            // Check if the current biome has any weathers defined.
            if (!__instance.WeatherCommonalities.Any())
            {
                // If not, we prevent the original method from running because it would crash.
                // Instead, we manually set a safe default (Clear weather).
                Log.Warning("Multiplayer: Biome has no weathers. Forcing Clear weather to prevent crash.");

                ___map.weatherManager.curWeather = WeatherDefOf.Clear;
                // Use AccessTools to set the private field `curWeatherDuration`.
                AccessTools.Field(typeof(WeatherDecider), "curWeatherDuration").SetValue(__instance, 10000);
                ___map.weatherManager.lastWeather = ___map.weatherManager.curWeather;
                ___map.weatherManager.curWeatherAge = 0;
                ___map.weatherManager.ResetSkyTargetLerpCache();

                // By returning false, we skip the original StartInitialWeather method entirely.
                // The Finalizer will still run to pop the RNG state.
                return false;
            }

            // If weathers *do* exist, return true to allow the original method to run.
            // It will now execute within our seeded RNG state, ensuring deterministic results.
            return true;
        }

        /// <summary>
        /// The Finalizer will always run after the Prefix (and the original method, if it ran).
        /// This guarantees that our seeded RNG state is popped, preventing it from affecting
        /// other parts of the game.
        /// </summary>
        static void Finalizer()
        {
            if (Multiplayer.Client != null)
            {
                Rand.PopState();
            }
        }
    }

    /// <summary>
    /// This patch fixes a crash during the "Host Server" process by ensuring a valid starting location exists.
    /// The hosting flow can skip interactive landing site selection, resulting in no player "Settlement"
    /// being created. The game then tries to generate a map on an invalid tile (like an ocean),
    /// causing a crash and a red screen. This prefix ensures a valid player settlement exists before the map is generated.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class Game_InitNewGame_Patch
    {
        static void Prefix(Game __instance)
        {
            if (Find.World == null) return;
            var playerFaction = Faction.OfPlayer;
            if (playerFaction == null) return;

            // Clean up any existing player settlements that might be on invalid tiles (like oceans).
            var settlements = Find.WorldObjects.Settlements.Where(s => s.Faction == playerFaction).ToList();
            foreach (var settlement in settlements)
            {
                if (!TileFinder.IsValidTileForNewSettlement(settlement.Tile))
                {
                    Log.Warning($"Multiplayer: Found existing player settlement '{settlement.Name}' on an invalid tile ({settlement.Tile}). Removing it.");
                    settlement.Destroy();
                }
            }

            // If, after cleanup, NO player settlement exists, create one.
            if (!Find.WorldObjects.Settlements.Any(s => s.Faction == playerFaction))
            {
                Log.Message("Multiplayer: No valid player settlement found. Creating a new one to ensure successful map generation.");

                // Ensure the tile in InitData is valid before using it.
                if (!TileFinder.IsValidTileForNewSettlement(__instance.InitData.startingTile))
                {
                    Log.Message($"Multiplayer: InitData's starting tile ({__instance.InitData.startingTile}) is invalid. Choosing a random valid tile.");
                    __instance.InitData.ChooseRandomStartingTile();
                }

                // Create the settlement object, which will serve as the MapParent for map generation.
                Settlement newSettlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                newSettlement.SetFaction(playerFaction);
                newSettlement.Tile = __instance.InitData.startingTile;
                newSettlement.Name = SettlementNameGenerator.GenerateSettlementName(newSettlement);
                Find.WorldObjects.Add(newSettlement);

                Log.Message($"Multiplayer: Created new player settlement '{newSettlement.Name}' at tile {newSettlement.Tile}.");
            }
        }
    }
}
