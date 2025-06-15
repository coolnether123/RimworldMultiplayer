using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Using HarmonyPriority.First ensures our seeding runs before any other patches on this method.
        /// </summary>
        [HarmonyPriority(Priority.First)]
        static void Prefix(Map ___map)
        {
            // Before the original method runs, push a new state to the random number generator,
            // using the map's unique ID as a seed. This ID is deterministic for all players.
            if (Multiplayer.Client != null)
            {
                Rand.PushState(___map.uniqueID);
            }
        }

        /// <summary>
        /// The original logic from your old patch is now integrated into a transpiler,
        /// which is a more robust way to handle this kind of conditional logic change.
        /// It checks if there are any weathers and, if not, skips to the end of the method.
        /// </summary>
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
        {
            var original = instructions.ToList();
            var label = gen.DefineLabel(); // A jump target for our new logic

            // This transpiler will inject a check at the start of the method.
            // It's equivalent to: if (this.WeatherCommonalities.Any()) { ... original code ... }

            // Load `this` (the WeatherDecider instance)
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            // Access its `WeatherCommonalities` property
            yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(WeatherDecider), nameof(WeatherDecider.WeatherCommonalities)));

            // Check if the enumerable has any elements
            // THIS IS THE CORRECTED LINE:
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Enumerable), nameof(Enumerable.Any)).MakeGenericMethod(typeof(WeatherCommonalityRecord)));

            // If it DOES have elements (is not empty), jump to the original code.
            yield return new CodeInstruction(OpCodes.Brtrue_S, label);

            // If the check was false (no weathers), this code runs:
            // Set weather to Clear and return, skipping the original logic.
            yield return new CodeInstruction(OpCodes.Ldarg_0); // this
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(WeatherDecider), "map")); // this.map
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Map), nameof(Map.weatherManager))); // this.map.weatherManager
            yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(WeatherDefOf), nameof(WeatherDefOf.Clear))); // WeatherDefOf.Clear
            yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(WeatherManager), nameof(WeatherManager.curWeather))); // .curWeather = ...
            yield return new CodeInstruction(OpCodes.Ret); // return;

            // This is the label where we jump to if weathers exist.
            original.First().labels.Add(label);
            foreach (var inst in original)
            {
                yield return inst;
            }
        }

        /// <summary>
        /// This finalizer will always run, ensuring we pop the random state
        /// even if the original method has an error.
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
