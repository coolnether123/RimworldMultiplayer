using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Client.AsyncTime;

// The old StorytellerTickPatch class has been removed from this file.
// Its job is now done by Storyteller_Tick_Sync in Patches.cs.

public static class StorytellerTickPatch
{
    public static bool updating;
}



[HarmonyPatch(typeof(Storyteller))]
[HarmonyPatch(nameof(Storyteller.AllIncidentTargets), MethodType.Getter)]
public class StorytellerTargetsPatch
{
    static void Postfix(List<IIncidentTarget> __result)
    {
        if (Multiplayer.Client == null || !Multiplayer.Ticking) return;

        // If we are currently ticking a specific map, only that map is a valid incident target.
        if (Multiplayer.MapContext != null)
        {
            __result.Clear();
            __result.Add(Multiplayer.MapContext);
        }
        // If we are currently ticking the world...
        else if (AsyncWorldTimeComp.tickingWorld)
        {
            // ...then only world-level objects are valid targets.
            // This filters out any player maps, which are handled by their own ticks.
            __result.RemoveAll(target => target is Map);

            // It's safer to add valid targets rather than clearing and re-adding.
            // Let's ensure all player-controlled caravans are included.
            foreach (var caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled && !__result.Contains(caravan))
                {
                    __result.Add(caravan);
                }
            }

            // Ensure the world itself is a target.
            if (!__result.Contains(Find.World))
            {
                __result.Add(Find.World);
            }
        }
        else
        {
            // If we are not ticking either the world or a map (e.g., in the UI),
            // there should be no valid incident targets.
            __result.Clear();
        }
    }
}
// The MP Mod's ticker calls Storyteller.StorytellerTick() on both the World and each Map, each tick
// This patch aims to ensure each "spawn raid" Quest is only triggered once, to prevent 2x or 3x sized raids
[HarmonyPatch(typeof(Quest), nameof(Quest.PartsListForReading), MethodType.Getter)]
public class QuestPartsListForReadingPatch
{
    static void Postfix(ref List<QuestPart> __result)
    {
        if (StorytellerTickPatch.updating)
        {
            __result = __result.Where(questPart => {
                if (questPart is QuestPart_ThreatsGenerator questPartThreatsGenerator)
                {
                    return questPartThreatsGenerator.mapParent?.Map == Multiplayer.MapContext;
                }
                return true;
            }).ToList();
        }
    }
}

[HarmonyPatch(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultParmsNow))]
static class MapContextIncidentParms
{
    static void Prefix(IIncidentTarget target, ref Map __state)
    {
        // This may be running inside a context already
        if (AsyncTimeComp.tickingMap != null)
            return;

        if (AsyncWorldTimeComp.tickingWorld && target is Map map)
        {
            AsyncTimeComp.tickingMap = map;
            map.AsyncTime().PreContext();
            __state = map;
        }
    }

    static void Finalizer(Map __state)
    {
        if (__state != null)
        {
            __state.AsyncTime().PostContext();
            AsyncTimeComp.tickingMap = null;
        }
    }
}

[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
static class MapContextIncidentExecute
{
    static void Prefix(IncidentParms parms, ref Map __state)
    {
        if (AsyncWorldTimeComp.tickingWorld && parms.target is Map map)
        {
            AsyncTimeComp.tickingMap = map;
            map.AsyncTime().PreContext();
            __state = map;
        }
    }

    static void Finalizer(Map __state)
    {
        if (__state != null)
        {
            __state.AsyncTime().PostContext();
            AsyncTimeComp.tickingMap = null;
        }
    }
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.IncidentTargetTags))]
static class SettlementIncidentTargetTagsPatch
{
    static IEnumerable<IncidentTargetTagDef> Postfix(IEnumerable<IncidentTargetTagDef> tags, Settlement __instance)
    {
        foreach (var tag in tags)
        {
            // Only return Map_Misc if player's faction is (heuristically) visiting the map
            // This affects multifaction where the storyteller ticks on every settlement for every faction separately
            if (tag != IncidentTargetTagDefOf.Map_Misc ||
                Find.AnyPlayerHomeMap != null && __instance.Map is { } m && m.mapPawns.AnyColonistSpawned)
                yield return tag;
        }
    }
}
