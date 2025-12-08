using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    // todo handle conversion to singleplayer and PostSplitOff

    [HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden), MethodType.Getter)]
    static class GetForbidPatch
    {
        static void Postfix(Thing ___parent, ref bool __result)
        {
            if (Multiplayer.Client == null) return;

            if (___parent.Spawned)
            {
                // FIX: Add null checks for map generation
                var mapComp = ___parent.Map?.MpComp();
                if (mapComp == null) return; // Let vanilla handle it during map generation

                var factionData = mapComp.GetCurrentCustomFactionData();
                if (factionData == null) return; // Faction data not ready yet

                __result = !factionData.unforbidden.Contains(___parent);
            }
            else
                __result = false; // Keeping track of unspawned things is more difficult, just say it's not forbidden
        }
    }

    [HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden), MethodType.Setter)]
    static class SetForbidPatch
    {
        static void Prefix(CompForbiddable __instance, Thing ___parent, bool value)
        {
            if (Multiplayer.Client == null) return;
            if (Multiplayer.InInterface) return; // Will get synced

            bool changed = false;

            if (Multiplayer.Client != null && ___parent.Spawned)
            {
                // FIX: Add null checks for map generation
                var mapComp = ___parent.Map?.MpComp();
                if (mapComp == null) return; // Skip during map generation

                var factionData = mapComp.GetCurrentCustomFactionData();
                if (factionData == null) return; // Faction data not ready yet

                var set = factionData.unforbidden;
                changed = value ? set.Remove(___parent) : set.Add(___parent);
            }

            // After the prefix the method early returns if (value == forbiddenInt)
            // Setting forbiddenInt to !value forces an update (prevents the early return)
            __instance.forbiddenInt = changed ? !value : value;
        }
    }

    [HarmonyPatch(typeof(CompForbiddable), nameof(CompForbiddable.UpdateOverlayHandle))]
    static class ForbiddablePostDrawPatch
    {
        [HarmonyPriority(MpPriority.MpFirst)]
        static void Prefix(CompForbiddable __instance, ref bool __state)
        {
            // FIX: Skip during map generation when faction context might not be available
            if (Multiplayer.RealPlayerFaction == null) return;

            FactionContext.Push(Multiplayer.RealPlayerFaction);
            __state = __instance.forbiddenInt;
            __instance.forbiddenInt = __instance.Forbidden;
        }

        [HarmonyPriority(MpPriority.MpLast)]
        static void Finalizer(CompForbiddable __instance, bool __state)
        {
            // FIX: Only pop if we pushed
            if (Multiplayer.RealPlayerFaction == null) return;

            __instance.forbiddenInt = __state;
            FactionContext.Pop();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    static class ThingSpawnSetForbidden
    {
        static void Prefix(Thing __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (Multiplayer.Client == null) return;

            // FIX: Check if map component exists
            var mapComp = map.MpComp();
            if (mapComp == null) return;

            if (ThingContext.stack.Any(p => p.Item1?.def == ThingDefOf.ActiveDropPod)) return;

            if (__instance is ThingWithComps t && t.GetComp<CompForbiddable>() != null)
                map.MpComp().GetCurrentCustomFactionData().unforbidden.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    static class ThingDespawnUnsetForbidden
    {
        static void Prefix(Thing __instance)
        {
            if (Multiplayer.Client == null) return;

            // FIX: Check if map component exists
            var mapComp = __instance.Map?.MpComp();
            if (mapComp == null) return;

            __instance.Map.MpComp().Notify_ThingDespawned(__instance);
        }
    }
}
