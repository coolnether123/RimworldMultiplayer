using HarmonyLib;
using Multiplayer.Common;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LudeonTK;
using Multiplayer.Client.AsyncTime;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class TickPatch
    {
        public static int Timer { get; private set; }

        public static int ticksToRun;
        public static int tickUntil; // Ticks < tickUntil can be simulated
        public static int workTicks;
        public static bool currentExecutingCmdIssuedBySelf;
        public static bool serverFrozen;
        public static int frozenAt;

        public const float StandardTimePerFrame = 1000.0f / 60.0f;

        // Time is in milliseconds
        private static float realTime;
        public static float avgFrameTime = StandardTimePerFrame;
        public static float serverTimePerTick;

        private static float frameTimeSentAt;

        public static TimeSpeed replayTimeSpeed;

        public static SimulatingData simulating;

        public static bool ShouldHandle => LongEventHandler.currentEvent == null && !Multiplayer.session.desynced;
        public static bool Simulating => simulating?.target != null;
        public static bool Frozen => serverFrozen && Timer >= frozenAt && !Simulating && ShouldHandle;

        public static IEnumerable<ITickable> AllTickables
        {
            get
            {
                yield return Multiplayer.AsyncWorldTime;

                var maps = Find.Maps;
                for (int i = maps.Count - 1; i >= 0; i--)
                    yield return maps[i].AsyncTime();
            }
        }

        static Stopwatch updateTimer = Stopwatch.StartNew();
        public static Stopwatch tickTimer = Stopwatch.StartNew();

        [TweakValue("Multiplayer")]
        public static bool doSimulate = true;

        public static void RunAgnosticUpdate()
        {
            if (MpVersion.IsDebug)
                SimpleProfiler.Start();

            
            if (LongEventHandler.eventQueue.Count == 0)
            {
                DoUpdate(out var worked);
                if (worked) workTicks++;
            }

            if (MpVersion.IsDebug)
                SimpleProfiler.Pause();
        }

        static bool Prefix()
        {
            if (Multiplayer.Client != null && !Multiplayer.IsReplay)
            {
                // Use a less spammy way to log this
                if (Find.TickManager.TicksGame % 60 == 0)
                {
                    //MpTrace.Info($"TickPatch.Prefix invoked. Timer: {Timer}, TicksToRun: {ticksToRun}, ShouldHandle: {ShouldHandle}");
                }
            }
            if (Multiplayer.Client == null) return true;

            // This is the definitive fix for all post-load issues.
            // It runs at the start of the very first TickManagerUpdate after a load.
            if (Multiplayer.justLoaded)
            {
                // Unset the flag so this only runs once.
                Multiplayer.justLoaded = false;

                //Log.Message("Multiplayer [TickPatch]: Running post-load initialization.");

                // Manually prime the history for all factions.
                if (Multiplayer.WorldComp != null)
                {
                    foreach (var factionId in Multiplayer.WorldComp.factionData.Keys.ToList())
                    {
                        var faction = Find.FactionManager.GetById(factionId);
                        if (faction != null)
                            Multiplayer.WorldComp.FinalizeInitFaction(faction);
                    }
                }

                // Reset the tick controller to a clean state.
                TickPatch.Reset();
            }
            TryProcessBufferedCommands();

            if (!ShouldHandle) return false;
            if (Frozen) return false;


            int ticksBehind = tickUntil - Timer;
            realTime += Time.deltaTime * 1000f;

            // Slow down when few ticksBehind to accumulate a buffer
            // Try to speed up when many ticksBehind
            // Else run at the speed from the server
            float stpt = ticksBehind <= 3 ? serverTimePerTick * 1.2f : ticksBehind >= 7 ? serverTimePerTick * 0.8f : serverTimePerTick;

            if (Multiplayer.IsReplay)
                stpt = StandardTimePerFrame * ReplayMultiplier();

            if (Timer >= tickUntil)
            {
                ticksToRun = 0;
            }
            else if (realTime > 0 && stpt > 0)
            {
                avgFrameTime = (avgFrameTime + Time.deltaTime * 1000f) / 2f;

                ticksToRun = Multiplayer.IsReplay ? Mathf.CeilToInt(realTime / stpt) : 1;
                realTime -= ticksToRun * stpt;
            }

            if (realTime > 0)
                realTime = 0;

            if (Time.time - frameTimeSentAt > 32f / 1000f)
            {
                Multiplayer.Client.Send(Packets.Client_FrameTime, avgFrameTime);
                frameTimeSentAt = Time.time;
            }

            if (Multiplayer.IsReplay && replayTimeSpeed == TimeSpeed.Paused || !doSimulate)
                ticksToRun = 0;

            if (simulating is { targetIsTickUntil: true })
                simulating.target = tickUntil;

            CheckFinishSimulating();

            // This is the single, correct call to the game update logic.
            // The redundant second call has been removed.
            RunAgnosticUpdate();

            CheckFinishSimulating();

            return false;
        }

        private static void CheckFinishSimulating()
        {
            if (simulating?.target != null && Timer >= simulating.target)
            {
                simulating.onFinish?.Invoke();
                ClearSimulating();
            }
        }

        public static void SetSimulation(int ticks = 0, bool toTickUntil = false, Action onFinish = null, Action onCancel = null, string cancelButtonKey = null, bool canEsc = false, string simTextKey = null)
        {
            simulating = new SimulatingData
            {
                target = ticks,
                targetIsTickUntil = toTickUntil,
                onFinish = onFinish,
                onCancel = onCancel,
                canEsc = canEsc,
                cancelButtonKey = cancelButtonKey ?? "CancelButton",
                simTextKey = simTextKey ?? "MpSimulating"
            };
        }

        static ITickable CurrentTickable()
        {
            if (WorldRendererUtility.WorldRendered)
                return Multiplayer.AsyncWorldTime;

            if (Find.CurrentMap != null)
                return Find.CurrentMap.AsyncTime();

            return null;
        }

        static void Postfix()
        {
            if (Multiplayer.Client == null || Find.CurrentMap == null) return;
            Shader.SetGlobalFloat(ShaderPropertyIDs.GameSeconds, Find.CurrentMap.AsyncTime().mapTicks.TicksToSeconds());
        }

        private static bool RunCmds()
        {

            int curTimer = Timer;
            foreach (ITickable tickable in AllTickables)
            {
                while (tickable.Cmds.Count > 0 && tickable.Cmds.Peek().ticks == curTimer)
                {
                    ScheduledCommand cmd = tickable.Cmds.Dequeue();
                    tickable.ExecuteCmd(cmd);
                    if (LongEventHandler.eventQueue.Count > 0) return true;
                }
            }
            return false;
        }

        public static void DoUpdate(out bool worked)
        {
            worked = false;
            updateTimer.Restart();

            // Run commands once per frame, unconditionally.
            if (RunCmds()) return;

            while (Simulating ? (Timer < simulating.target && updateTimer.ElapsedMilliseconds < 25) : (ticksToRun > 0))
            {
                // Re-check commands inside the loop in case a tick produces a command for the current tick (e.g., quests)
                if (RunCmds()) return;

                if (DoTick(ref worked)) return;
            }
        }

        public static void DoTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                bool worked = false;
                DoTick(ref worked);
            }
        }

        public static bool DoTick(ref bool worked)
        {

            //MpTrace.Info($"-- DoTick START for Timer: {Timer} --");

            tickTimer.Restart();

            // First, process commands for the current tick for ALL tickables.
            if (RunCmds()) return true;

            // Now, tick the simulation for any unpaused tickables.
            foreach (ITickable tickable in AllTickables)
            {
                if (tickable.TimePerTick(tickable.DesiredTimeSpeed) > 0) // Only tick if not paused
                {
                    worked = true;
                    TickTickable(tickable);
                }
            }

            ConstantTicker.Tick();

            if (Multiplayer.Client != null && Find.CurrentMap != null)
            {
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    // Only tick pathers on maps that are not being viewed.
                    // The main game loop handles the current map.
                    if (map != Find.CurrentMap)
                    {
                        // Use a temporary list to prevent collection modification errors.
                        List<Pawn> pawnsOnMap = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
                        foreach (Pawn pawn in pawnsOnMap)
                        {
                            if (pawn.pather != null)
                            {
                                // This specific call updates the pawn's visual position.
                                // It does NOT run AI logic and is safe to call.
                                pawn.pather.PatherTick();
                            }
                        }
                    }
                }
            }

            Timer++;
            ticksToRun--;

            tickTimer.Stop();

            if (Multiplayer.session.desynced || Timer >= tickUntil || LongEventHandler.eventQueue.Count > 0)
            {
                ticksToRun = 0;
                return true;
            }

            return false;
        }

        private static void TickTickable(ITickable tickable)
        {
            float timePerTick = tickable.TimePerTick(tickable.DesiredTimeSpeed);
            if (timePerTick == 0) return;

            tickable.TimeToTickThrough += 1f;
            while (tickable.TimeToTickThrough >= 0)
            {
                tickable.TimeToTickThrough -= timePerTick;

                try
                {
                    if (tickable is AsyncWorldTimeComp)
                        AsyncWorldTimeComp.tickingWorld = true;
                    else if (tickable is AsyncTimeComp comp)
                        AsyncTimeComp.tickingMap = comp.map;

                    tickable.Tick();
                }
                catch (Exception e)
                {
                    Log.Error($"Exception during ticking {tickable}: {e}");
                }
                finally
                {
                    if (tickable is AsyncWorldTimeComp)
                        AsyncWorldTimeComp.tickingWorld = false;
                    else if (tickable is AsyncTimeComp)
                        AsyncTimeComp.tickingMap = null;
                }
            }
        }

        private static void TryProcessBufferedCommands()
        {
            var session = Multiplayer.session;
            if (session.bufferedCommands.Count > 0)
            {
                // === NEW LOGGING ===
                MpTrace.Info($"-- TryProcessBufferedCommands: Buffer has {session.bufferedCommands.Sum(kv => kv.Value.Count)} items. Checking maps... --");
            }
            else
            {
                return;
            }
            if (session.bufferedCommands.Count == 0) return;

            // Use a temporary list to avoid modifying the collection while iterating
            List<int> processedMapIds = new List<int>();

            foreach (var entry in session.bufferedCommands)
            {
                int mapId = entry.Key;
                Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);

                if (map != null && map.AsyncTime() != null)
                {
                    MpTrace.Info($"Map {mapId} is now available. Processing {entry.Value.Count} buffered commands.");
                    var mapQueue = map.AsyncTime().cmds;
                    foreach (var cmd in entry.Value)
                    {
                        mapQueue.Enqueue(cmd);
                    }
                    processedMapIds.Add(mapId);
                }
            }

            // Remove the processed commands from the buffer
            foreach (int mapId in processedMapIds)
            {
                session.bufferedCommands.Remove(mapId);
            }
        }

        private static float ReplayMultiplier()
        {
            if (!Multiplayer.IsReplay || Simulating) return 1f;

            if (replayTimeSpeed == TimeSpeed.Paused)
                return 0f;

            ITickable tickable = CurrentTickable();
            if (tickable.TimePerTick(tickable.DesiredTimeSpeed) == 0f)
                return 1 / 100f; // So paused sections of the timeline are skipped through

            return tickable.ActualRateMultiplier(tickable.DesiredTimeSpeed) / tickable.ActualRateMultiplier(replayTimeSpeed);
        }

        public static float TimePerTick(this ITickable tickable, TimeSpeed speed)
        {
            if (tickable.ActualRateMultiplier(speed) == 0f)
                return 0f;
            return 1f / tickable.ActualRateMultiplier(speed);
        }

        public static float ActualRateMultiplier(this ITickable tickable, TimeSpeed speed)
        {
            if (Multiplayer.GameComp.asyncTime)
                return tickable.TickRateMultiplier(speed);

            var rate = Multiplayer.AsyncWorldTime.TickRateMultiplier(speed);
            foreach (var map in Find.Maps)
                rate = Math.Min(rate, map.AsyncTime().TickRateMultiplier(speed));

            return rate;
        }

        public static void ClearSimulating()
        {
            simulating = null;
        }

        public static void Reset()
        {
            ClearSimulating();
            Timer = 0;
            tickUntil = 0;
            ticksToRun = 0;
            serverFrozen = false;
            workTicks = 0;
            serverTimePerTick = 0;
            avgFrameTime = StandardTimePerFrame;
            realTime = 0;
            TimeControlPatch.prePauseTimeSpeed = null;
        }

        public static void SetTimer(int value)
        {
            Timer = value;
        }

        public static ITickable TickableById(int tickableId)
        {
            return AllTickables.FirstOrDefault(t => t.TickableId == tickableId);
        }
    }

    public class SimulatingData
    {
        public int? target;
        public bool targetIsTickUntil; // When true, the target field is always up-to-date with TickPatch.tickUntil
        public Action onFinish;
        public bool canEsc;
        public Action onCancel;
        public string cancelButtonKey;
        public string simTextKey;
    }
}
