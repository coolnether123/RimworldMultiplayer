using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client.Patches;
using Multiplayer.Common;
using RimWorld;
using Verse;
using Verse.AI;

namespace Multiplayer.Client.Desyncs
{
    [EarlyPatch]
    [HarmonyPatch]
    public static class DeferredStackTracing
    {
        public static int ignoreTraces;
        public static long maxTraceDepth;
        public static int randCalls;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Value));
            yield return AccessTools.PropertyGetter(typeof(Rand), nameof(Rand.Int));
        }

        public static int acc;

        public static void Postfix()
        {
            if (Native.LmfPtr == 0) return;
            if (!ShouldAddStackTraceForDesyncLog()) return;

            var logItem = StackTraceLogItemRaw.GetFromPool();
            var trace = logItem.raw;
            int hash = 0;
            int depth = DeferredStackTracingImpl.TraceImpl(trace, ref hash);

            Multiplayer.game.sync.TryAddStackTraceForDesyncLogRaw(logItem, depth, hash);

            acc++;
        }

        public static bool ShouldAddStackTraceForDesyncLog()
        {

            // The native stack walker is causing a NullReferenceException on game start.
            // As this is a debugging tool, we can safely disable it entirely to allow
            // the game to run. This line forces the method to always return false,
            // preventing the crash-causing code from ever executing.
            return false;

            if (Multiplayer.initializing) return false;

            if (Multiplayer.Client == null) return false;
            if (Multiplayer.settings.desyncTracingMode == DesyncTracingMode.None) return false;
            if (Multiplayer.game == null) return false;

            if (!Multiplayer.game.gameComp.logDesyncTraces) return false;

            if (Rand.stateStack.Count > 1) return false;
            if (Multiplayer.IsReplay) return false;

            if (!Multiplayer.Ticking && !Multiplayer.ExecutingCmds) return false;

            return ignoreTraces == 0;
        }
    }

    [HarmonyPatch(typeof(UniqueIDsManager), nameof(UniqueIDsManager.GetNextID))]
    public static class UniqueIdsPatch
    {
        static void Postfix()
        {
            DeferredStackTracing.Postfix();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    public static class ThingSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (__instance.def.HasThingIDNumber)
                DeferredStackTracing.Postfix();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    public static class ThingDeSpawnPatch
    {
        static void Postfix(Thing __instance)
        {
            if (__instance.def.HasThingIDNumber)
                DeferredStackTracing.Postfix();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class EndCurrentJobPatch
    {
        static void Prefix(Pawn_JobTracker __instance)
        {
            if (MpVersion.IsDebug && __instance.curJob != null && DeferredStackTracing.ShouldAddStackTraceForDesyncLog())
                Multiplayer.game.sync.TryAddInfoForDesyncLog($"EndCurrentJob for {__instance.pawn}: {__instance.curJob}", "");
        }
    }

    /*[HarmonyPatch(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick))]
    static class WildAnimalSpawnerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick))]
    static class WildPlantSpawnerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick))]
    static class SteadyEnvironmentEffectsTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellForWorker))]
    static class FindBestStorageCellTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }

    [HarmonyPatch(typeof(IntermittentSteamSprayer), nameof(IntermittentSteamSprayer.SteamSprayerTick))]
    static class SteamSprayerTickTraceIgnore
    {
        static void Prefix() => DeferredStackTracing.ignoreTraces++;
        static void Finalizer() => DeferredStackTracing.ignoreTraces--;
    }
    [HarmonyPatch]
    static class DeterministicMapTickers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            // These methods all run periodically on the map and use Rand, causing desyncs.
            yield return AccessTools.Method(typeof(WildPlantSpawner), nameof(WildPlantSpawner.WildPlantSpawnerTick));
            yield return AccessTools.Method(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.WildAnimalSpawnerTick));
            yield return AccessTools.Method(typeof(SteadyEnvironmentEffects), nameof(SteadyEnvironmentEffects.SteadyEnvironmentEffectsTick));
            yield return AccessTools.Method(typeof(WeatherDecider), nameof(WeatherDecider.WeatherDeciderTick));
        }

        /// <summary>
        /// Before any of the targeted methods run, we push a new state to the random number generator.
        /// We seed it with the map's current simulation tick count.
        /// </summary>
        static void Prefix(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
            {
                Rand.PushState(___map.AsyncTime().mapTicks);
            }
        }

        /// <summary>
        /// After the method finishes (or if it crashes), we pop the state.
        /// This restores the RNG to its previous state, preventing our seeded state
        /// from affecting other parts of the game.
        /// </summary>
        static void Finalizer(Map ___map)
        {
            if (Multiplayer.Client != null && ___map?.AsyncTime() != null)
            {
                Rand.PopState();
            }
        }
    }*/
}
