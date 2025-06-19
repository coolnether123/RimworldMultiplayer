using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Verse;
using Verse.AI;
using static Multiplayer.Client.PathingPatches;

namespace Multiplayer.Client
{
    //############################################################################
    // SECTION 1: INITIALIZATION & HARMONY PATCHING
    //############################################################################

    [StaticConstructorOnStartup]
    static class PathingSetup
    {
        static PathingSetup()
        {
            if (!MP.enabled) return;

            var harmony = new Harmony("coolnether123.pathingsync.final.v2");

            // Manually and explicitly patch each method.
            harmony.Patch(
                AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_StartJob)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_StartJob))
            );

            harmony.Patch(
                AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_TryTakeOrderedJob))
            );

            harmony.Patch(
                AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_PatherTick))
            );

            harmony.Patch(
                AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.SetNewPathRequest)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_SetNewPathRequest))
            );

            harmony.Patch(
                AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_PawnDeSpawn))
            );

            harmony.Patch(
                AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTickInterval)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_JobTrackerTickInterval))
            );
            harmony.Patch(
        AccessTools.Method(typeof(TickManager), nameof(TickManager.TickManagerUpdate)),
        postfix: new HarmonyMethod(typeof(ProcessPendingSyncs), nameof(ProcessPendingSyncs.Postfix))
    );


            // Register SyncWorkers for our custom data types.
            MP.RegisterSyncWorker<JobParams>(SyncWorkers.ReadWriteJobParams);
            MP.RegisterSyncWorker<PawnPathSurrogate>(SyncWorkers.ReadWritePawnPathSurrogate);

            Log.Message("[Multiplayer-Pathing] Pathing sync patches applied successfully.");
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class AllowClientPatherTick
    {
        public static bool Prefix(Pawn_PathFollower __instance)
        {
            bool isClient = Multiplayer.Client != null && Multiplayer.LocalServer == null;
            if (isClient)
                MpTrace.Verbose($"[Client-PatherTick] animating pawn={__instance.pawn}");
            // always return true so Tick still runs (but we block path requests elsewhere)
            return true;
        }
    }

    // Add this new patch to process pending syncs:
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class ProcessPendingSyncs
    {
        public static void Postfix()
        {
            if (pendingSyncs?.Count > 0)
            {
                for (int i = pendingSyncs.Count - 1; i >= 0; i--)
                {
                    var pending = pendingSyncs[i];
                    if (GenTicks.TicksGame >= pending.executeAtTick)
                    {
                        try
                        {
                            if (pending.pawn?.Spawned == true)
                            {
                                SyncedActions.TestSync("PathTest");
                                SyncedActions.StartJobAI(pending.pawn, pending.jobParams);
                            }
                        }
                        catch (Exception e)
                        {
                            MpTrace.Error($"[PendingSync] Exception: {e}");
                        }

                        pendingSyncs.RemoveAt(i);
                    }
                }
            }
        }
    }

    //############################################################################
    // SECTION 2: THE PATCH IMPLEMENTATIONS
    //############################################################################

    public static class PathingPatches
    {
        // Shared state variables
        public static int InSyncAction = 0;
        private static readonly Dictionary<int, (IntVec3 first, IntVec3 last, int left)> lastContentCache = new();
        private static readonly Dictionary<int, int> lastSyncTick = new();

        // Patches now reference the shared state from this single class definition.

        public static bool Prefix_StartJob(Pawn_JobTracker __instance, Job newJob)
        {
            if (InSyncAction > 0) return true;
            if (Multiplayer.Client != null && Multiplayer.LocalServer == null && newJob?.jobGiver != null) return false;
            return true;
        }

        public static void Postfix_StartJob(Pawn_JobTracker __instance, Job newJob)
        {
            if (Multiplayer.LocalServer != null && newJob != null && !newJob.playerForced)
            {
                var pawn = __instance.pawn;
                var jobParams = new JobParams(newJob);

                MpTrace.Info($"[StartJob-Host] will SYNC (next tick) {pawn} ← {newJob.def.defName}");

                // Add to pending sync queue - safer than LongEventHandler
                if (pendingSyncs == null) pendingSyncs = new List<PendingSync>();
                pendingSyncs.Add(new PendingSync(pawn, jobParams, GenTicks.TicksGame + 1));
            }
        }

        // Add these to your PathingPatches class:
        public static List<PendingSync> pendingSyncs;

        public class PendingSync
        {
            public Pawn pawn;
            public JobParams jobParams;
            public int executeAtTick;

            public PendingSync(Pawn pawn, JobParams jobParams, int executeAtTick)
            {
                this.pawn = pawn;
                this.jobParams = jobParams;
                this.executeAtTick = executeAtTick;
            }
        }

        

        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;
            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }

        public static void Postfix_PatherTick(Pawn_PathFollower __instance)
        {

            // DEBUG: Check why the early return is happening
            bool isHost = Multiplayer.LocalServer != null;
            bool isSpawned = __instance.pawn.Spawned;

            MpTrace.Info($"[PatherTick-Debug] {__instance.pawn} isHost={isHost} isSpawned={isSpawned} " +
                        $"map={__instance.pawn.Map?.uniqueID ?? -1}");

            if (Multiplayer.LocalServer == null || !__instance.pawn.Spawned) return;

            MpTrace.Info($"[PatherTick-AfterEarlyReturn] {__instance.pawn}");

            var p = __instance.curPath;
            MpTrace.Info($"[PathSend-About-To-Call] {__instance.pawn} pathValid={p?.Found} " +
                         $"willSendPath={p is { Found: true }}");

            if (__instance.pawn.Drafted) return; // Skip your original filter
            int id = __instance.pawn.thingIDNumber;

            bool hasLastTick = lastSyncTick.TryGetValue(id, out int last);
            bool tooSoon = hasLastTick && GenTicks.TicksGame < last + 30;
            MpTrace.Info($"[Throttle-Check] {__instance.pawn} hasLastTick={hasLastTick} tooSoon={tooSoon}");

            if (tooSoon) return;

            var content = p is { Found: true } ? (p.FirstNode, p.LastNode, p.NodesLeftCount) : (IntVec3.Invalid, IntVec3.Invalid, 0);
            bool hasCache = lastContentCache.TryGetValue(id, out var prev);
            bool contentSame = hasCache && prev.Equals(content);
            MpTrace.Info($"[Cache-Check] {__instance.pawn} hasCache={hasCache} contentSame={contentSame}");

            if (contentSame) return;

            MpTrace.Info($"[PathSend-Calling-Now] {__instance.pawn}");
            SyncedActions.TestSync("PathTest"); // ADD THIS LINE
            SyncedActions.SetPawnPath(__instance.pawn, new PawnPathSurrogate(p));
        }



        public static bool Prefix_SetNewPathRequest() => Multiplayer.Client == null || Multiplayer.LocalServer != null;

        public static void Postfix_PawnDeSpawn(Pawn __instance)
        {
            int id = __instance.thingIDNumber;
            lastContentCache.Remove(id);
            lastSyncTick.Remove(id);
        }

        public static void Postfix_JobTrackerTickInterval(Pawn_JobTracker __instance)
        {
            if (Multiplayer.LocalServer == null) return;
            Pawn pawn = __instance.pawn;
            if (pawn.Map == Find.CurrentMap) return;
            if (__instance.curJob == null && pawn.mindState.Active)
            {
                __instance.TryFindAndStartJob();
            }
        }
    }

    //############################################################################
    // SECTION 3: SHARED STATE
    //############################################################################

    public static class SyncWorkers
    {
        

        public static void ReadWriteJobParams(SyncWorker worker, ref JobParams p)
        {
            MpTrace.Verbose($"[SyncWorker-JobParams] isWriting={worker.isWriting}");
            if (!worker.isWriting) p = new JobParams();
            p.Sync(worker);
            MpTrace.Verbose($"[SyncWorker-JobParams] completed successfully");
        }

        public static void ReadWritePawnPathSurrogate(SyncWorker worker, ref PawnPathSurrogate p)
        {
            MpTrace.Verbose($"[SyncWorker-PathSurrogate] isWriting={worker.isWriting}");
            if (!worker.isWriting) p = new PawnPathSurrogate();
            p.Sync(worker);
            MpTrace.Verbose($"[SyncWorker-PathSurrogate] completed successfully");
        }
    }

    //############################################################################
    // SECTION 4: DATA TRANSFER OBJECTS (DTOs)
    //############################################################################

    public class JobParams : ISynchronizable
    {
        public JobDef def;
        public LocalTargetInfo targetA, targetB, targetC;
        public List<LocalTargetInfo> targetQueueA, targetQueueB;
        public int count = -1;
        public bool playerForced, canBashDoors, canBashFences;
        public HaulMode haulMode;
        public Faction lordFaction;
        public int takeExtraIngestibles;
        private ThinkTreeDef thinkTreeDef;
        private int jobGiverKey;
        private Thing verbCaster;
        private string verbLabel;

        public JobParams() { }
        public JobParams(Job job)
        {
            def = job.def;
            targetA = job.targetA;
            targetB = job.targetB;
            targetC = job.targetC;
            targetQueueA = job.targetQueueA?.ToList();
            targetQueueB = job.targetQueueB?.ToList();
            count = job.count;
            playerForced = job.playerForced;
            canBashDoors = job.canBashDoors;
            canBashFences = job.canBashFences;
            haulMode = job.haulMode;
            lordFaction = job.lord?.faction;
            takeExtraIngestibles = job.takeExtraIngestibles;
            thinkTreeDef = job.jobGiverThinkTree;
            jobGiverKey = job.jobGiver?.UniqueSaveKey ?? -1;
            if (job.verbToUse != null)
            {
                verbCaster = job.verbToUse.Caster;
                verbLabel = job.verbToUse.verbProps.label;
            }
        }

        public Job ToJob()
        {
            var job = JobMaker.MakeJob(def);
            job.targetA = targetA;
            job.targetB = targetB;
            job.targetC = targetC;
            job.targetQueueA = targetQueueA;
            job.targetQueueB = targetQueueB;
            job.count = count;
            job.playerForced = playerForced;
            job.canBashDoors = canBashDoors;
            job.canBashFences = canBashFences;
            job.haulMode = haulMode;
            job.takeExtraIngestibles = takeExtraIngestibles;
            if (thinkTreeDef != null &&
                thinkTreeDef.TryGetThinkNodeWithSaveKey(jobGiverKey, out var node))
            {
                job.jobGiver = node;
                job.jobGiverThinkTree = thinkTreeDef;
            }
            if (verbCaster is IVerbOwner owner && !verbLabel.NullOrEmpty())
            {
                job.verbToUse = owner.VerbTracker.AllVerbs
                    .FirstOrDefault(v => v.verbProps.label == verbLabel);
            }
            return job;
        }

        public void Sync(SyncWorker worker)
        {
            worker.Bind(ref def);
            worker.Bind(ref targetA);
            worker.Bind(ref targetB);
            worker.Bind(ref targetC);
            worker.Bind(ref targetQueueA);
            worker.Bind(ref targetQueueB);
            worker.Bind(ref count);
            worker.Bind(ref playerForced);
            worker.Bind(ref canBashDoors);
            worker.Bind(ref canBashFences);
            worker.Bind(ref haulMode);
            worker.Bind(ref lordFaction);
            worker.Bind(ref takeExtraIngestibles);
            worker.Bind(ref thinkTreeDef);
            worker.Bind(ref jobGiverKey);
            worker.Bind(ref verbCaster);
            worker.Bind(ref verbLabel);
        }
    }

    public class PawnPathSurrogate : ISynchronizable
    {
        public bool isValid;
        private List<IntVec3> nodes;
        private int totalCost;

        public PawnPathSurrogate() { }
        public PawnPathSurrogate(PawnPath path)
        {
            if (path == null || !path.Found)
            {
                isValid = false;
                return;
            }
            isValid = true;
            totalCost = (int)path.TotalCost;
            nodes = new List<IntVec3>(path.NodesReversed);
        }

        public PawnPath ToPawnPath(Pawn pawn)
        {
            if (!isValid) return PawnPath.NotFound;
            var newPath = pawn.Map.pawnPathPool.GetPath();
            var native = new NativeList<IntVec3>(nodes.Count, Allocator.Temp);
            foreach (var n in nodes) native.Add(n);
            newPath.Initialize(native, totalCost);
            native.Dispose();
            return newPath;
        }

        public void Sync(SyncWorker worker)
        {
            worker.Bind(ref isValid);
            if (!isValid) return;
            worker.Bind(ref nodes);
            worker.Bind(ref totalCost);
        }
    }

    //############################################################################
    // SECTION 5: SYNCED ACTIONS
    //############################################################################

    public static class SyncedActions
    {
        [SyncMethod]
        public static void MinimalTest()
        {
            bool isHost = Multiplayer.LocalServer != null;
            MpTrace.Info($"[MinimalTest] side={(isHost ? "HOST" : "CLIENT")} - BASIC SYNC WORKING");
        }

        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void TestSync(string message)
        {
            bool isHost = Multiplayer.LocalServer != null;
            bool inMultiplayer = MP.IsInMultiplayer;
            bool shouldSync = Multiplayer.ShouldSync;
            if (!shouldSync && isHost)
            {
                // Debug the internal conditions
                bool dontSync = Multiplayer.dontSync;
                bool executingCmds = Multiplayer.ExecutingCmds;
                bool inInterface = Multiplayer.InInterface;

                MpTrace.Info($"[ShouldSync-Debug] dontSync={dontSync} executingCmds={executingCmds} inInterface={inInterface}");
            }

            // ADD THESE DETAILED CHECKS:
            bool isPlaying = Current.ProgramState == ProgramState.Playing;
            bool gameInitialized = Current.Game != null;
            bool hasActiveMap = Find.CurrentMap != null;
            bool isPaused = Find.TickManager.Paused;

            MpTrace.Info($"[TestSync-Detailed] side={(isHost ? "HOST" : "CLIENT")} " +
                        $"shouldSync={shouldSync} isPlaying={isPlaying} gameInit={gameInitialized} " +
                        $"hasMap={hasActiveMap} isPaused={isPaused}");

            MpTrace.Info($"[TestSync] side={(isHost ? "HOST" : "CLIENT")} message={message}");
        }

        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void StartJobAI(Pawn pawn, JobParams prms)
        {
            bool isHost = Multiplayer.LocalServer != null;
            MpTrace.Info($"[StartJobAI] side={(isHost ? "HOST" : "CLIENT")} pawn={pawn} job={prms.def.defName}");

            // Both host and client should execute the job
            try
            {
                PathingPatches.InSyncAction++;
                pawn.jobs.StartJob(prms.ToJob(), JobCondition.InterruptForced);
            }
            finally
            {
                PathingPatches.InSyncAction--;
            }
        }

        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void TakeOrderedJob(Pawn pawn, JobParams prms, JobTag? tag)
        {
            if (Multiplayer.LocalServer != null) return;
            try { PathingPatches.InSyncAction++; pawn.jobs.TryTakeOrderedJob(prms.ToJob(), tag); }
            finally { PathingPatches.InSyncAction--; }
        }

        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surr)
        {
            bool isHost = Multiplayer.LocalServer != null;
            int mapId = pawn?.Map?.uniqueID ?? -1;
            MpTrace.Info($"[PathRecv] side={(isHost ? "HOST" : "CLIENT")} pawn={pawn} " +
                        $"surrogateValid={surr.isValid} mapId={mapId}");

            if (Multiplayer.LocalServer != null || pawn?.pather == null) return;
            var pf = pawn.pather;
            pf.curPath?.ReleaseToPool();
            pf.curPath = surr.ToPawnPath(pawn);
            if (pf.curPath.Found) pf.ResetToCurrentPosition(); else pf.PatherFailed();
        }
    }
}
