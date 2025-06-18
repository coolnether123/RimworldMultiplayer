// File: Source/Client/Syncing/PathingSync.cs

using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    //############################################################################
    // SECTION 1: INITIALIZATION & PATCH APPLYING
    //############################################################################

    [StaticConstructorOnStartup]
    public static class PathingSyncInit
    {
        static PathingSyncInit()
        {
            if (!MP.enabled) return;            // Skip in single-player
            MP.RegisterAll();                    // Discover all [SyncMethod] & [SyncWorker]
            new Harmony("coolnether123.pathingsync").PatchAll();
        }
    }

    //############################################################################
    // SECTION 2: HARMONY PATCH CLASSES
    //############################################################################

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    [HarmonyPatch(new[]
    {
        typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool)
    })]
    static class Pawn_JobTracker_StartJob_Patch
    {
        static bool Prefix(Pawn_JobTracker __instance,
                           Job newJob,
                           JobCondition _cond,
                           ThinkNode jobGiver,
                           bool _resume)
        {
            // Allow if coming from a synced action
            if (PathingPatches.InSyncAction > 0) return true;

            // Block AI jobs on pure clients
            if (Multiplayer.Client != null &&
                Multiplayer.LocalServer == null &&
                jobGiver != null)
                return false;

            return true;
        }

        static void Postfix(Pawn_JobTracker __instance,
                            Job newJob,
                            JobCondition _cond,
                            ThinkNode jobGiver,
                            bool _resume)
        {
            if (Multiplayer.LocalServer == null || jobGiver == null) return;
            SyncedActions.StartJobAI(__instance.pawn, new JobParams(newJob));
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    [HarmonyPatch(new[] { typeof(Job), typeof(JobTag?) })]
    static class Pawn_JobTracker_TryTakeOrderedJob_Patch
    {
        static bool Prefix(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;
            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    static class Pawn_PathFollower_PatherTick_Patch
    {
        static void Postfix(Pawn_PathFollower __instance)
        {
            if (Multiplayer.LocalServer == null) return;
            Pawn pawn = __instance.pawn;
            if (!pawn.Spawned || pawn.Drafted) return;

            int id = pawn.thingIDNumber;
            if (PathingPatches.lastSyncTick.TryGetValue(id, out int last) &&
                GenTicks.TicksGame < last + 30)
                return;

            PawnPath p = __instance.curPath;
            var content = (p is { Found: true }
                ? (p.FirstNode, p.LastNode, p.NodesLeftCount)
                : (IntVec3.Invalid, IntVec3.Invalid, 0));

            if (PathingPatches.lastContentCache.TryGetValue(id, out var prev) &&
                prev.Equals(content))
                return;

            PathingPatches.lastContentCache[id] = content;
            PathingPatches.lastSyncTick[id] = GenTicks.TicksGame;

            // Send the full surrogate path
            SyncedActions.SetPawnPath(pawn, new PawnPathSurrogate(p));
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.SetNewPathRequest))]
    static class Pawn_PathFollower_SetNewPathRequest_Patch
    {
        static bool Prefix(Pawn_PathFollower __instance)
        {
            return Multiplayer.Client == null || Multiplayer.LocalServer != null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    static class Pawn_DeSpawn_Patch
    {
        static void Postfix(Pawn __instance)
        {
            int id = __instance.thingIDNumber;
            PathingPatches.lastContentCache.Remove(id);
            PathingPatches.lastSyncTick.Remove(id);
        }
    }

    //############################################################################
    // SECTION 3: PATCH IMPLEMENTATIONS & CACHES
    //############################################################################

    public static class PathingPatches
    {
        internal static int InSyncAction = 0;
        internal static readonly Dictionary<int, (IntVec3 first, IntVec3 last, int left)> lastContentCache
            = new Dictionary<int, (IntVec3, IntVec3, int)>();
        internal static readonly Dictionary<int, int> lastSyncTick
            = new Dictionary<int, int>();
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
        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void StartJobAI(Pawn pawn, JobParams prms)
        {
            if (Multiplayer.LocalServer != null) return;
            try
            {
                PathingPatches.InSyncAction++;
                pawn.jobs.StartJob(prms.ToJob(), JobCondition.InterruptForced, null, false);
            }
            finally { PathingPatches.InSyncAction--; }
        }

        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void TakeOrderedJob(Pawn pawn, JobParams prms, JobTag? tag)
        {
            if (Multiplayer.LocalServer != null) return;
            try
            {
                PathingPatches.InSyncAction++;
                pawn.jobs.TryTakeOrderedJob(prms.ToJob(), tag);
            }
            finally { PathingPatches.InSyncAction--; }
        }

        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surr)
        {
            if (Multiplayer.LocalServer != null || pawn?.pather == null) return;
            var pf = pawn.pather;
            pf.curPath?.ReleaseToPool();
            pf.curPath = surr.ToPawnPath(pawn);
            if (pf.curPath.Found) pf.ResetToCurrentPosition();
            else pf.PatherFailed();
        }
    }
}
