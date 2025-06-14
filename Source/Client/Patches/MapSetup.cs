using System;
using System.Collections.Generic;
using System.Linq;
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
}
