// In new file: PathingSync.cs

using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections; // Required for NativeList
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    //############################################################################
    // SECTION 1: HARMONY PATCHER
    //############################################################################

    [StaticConstructorOnStartup]
    public static class PathingSyncHarmony
    {
        static PathingSyncHarmony()
        {
            var harmony = Multiplayer.harmony;
            Log.Message("[Multiplayer-Pathing] Applying instrumentation patches (v9)...");

            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_StartJob)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_StartJob)));


            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_TryTakeOrderedJob)));
            

            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_PatherTick)));
            

            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.SetNewPathRequest)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_SetNewPathRequest)));
            

            harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_PawnDeSpawn)));
            Log.Message("[Multiplayer-Pathing] Patches applied successfully.");
        }
    }

    //############################################################################
    // SECTION 2: THE PATCH IMPLEMENTATIONS
    //############################################################################

    public static class PathingPatches
    {
        private static Dictionary<int, PawnPath> lastPathInstanceCache = new();
        private static Dictionary<int, PawnPathSurrogate> lastSyncedSurrogateCache = new();
        private static Job lastSyncedJob;

        // CHECKPOINT 1: Intercepting an AI Job
        public static bool Prefix_StartJob(Pawn_JobTracker __instance, Job newJob, ThinkNode jobGiver)
        {
            if (Multiplayer.Client == null) return true;

            // Block job churn
            if (__instance.curJob != null && jobGiver != null && newJob.JobIsSameAs(__instance.pawn, __instance.curJob))
            {
                return false;
            }

            if (Multiplayer.LocalServer == null && jobGiver != null)
            {
                // A client should not start an AI job.
                return false;
            }

            // Host will run the original StartJob. We will sync it in the Postfix.
            // We mark the job we intend to sync.
            if (Multiplayer.LocalServer != null && jobGiver != null)
            {
                lastSyncedJob = newJob;
            }

            return true;
        }

        public static void Postfix_StartJob(Pawn_JobTracker __instance)
        {
            // If we marked a job to be synced, and the pawn's current job is that job, then sync it.
            if (lastSyncedJob != null && __instance.curJob == lastSyncedJob)
            {
                Log.Message($"[Pathing-Checkpoint 1A - HOST] AI job {__instance.curJob.def.defName} for {__instance.pawn.LabelShortCap} started successfully. Syncing.");
                SyncedActions.StartJobAI(__instance.pawn, new JobParams(__instance.curJob));
            }
            lastSyncedJob = null; // Clear the marker
        }

        // CHECKPOINT 2: Intercepting a Player-Ordered Job
        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;
            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }

        // CHECKPOINT 3: Detecting a New Path on Host
        public static void Postfix_PatherTick(Pawn_PathFollower __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.LocalServer == null || !__instance.pawn.Spawned) return;
            Pawn pawn = __instance.pawn;
            PawnPath currentPath = __instance.curPath;
            lastPathInstanceCache.TryGetValue(pawn.thingIDNumber, out var cachedPathInstance);
            if (currentPath != cachedPathInstance)
            {
                lastPathInstanceCache[pawn.thingIDNumber] = currentPath;
                var newSurrogate = new PawnPathSurrogate(currentPath);
                lastSyncedSurrogateCache.TryGetValue(pawn.thingIDNumber, out var lastSentSurrogate);
                if (!newSurrogate.IsSameAs(lastSentSurrogate))
                {
                    SyncedActions.SetPawnPath(pawn, newSurrogate);
                    PathingClientUtil.SetPawnPath(pawn, newSurrogate, isLocal: true);
                    lastSyncedSurrogateCache[pawn.thingIDNumber] = newSurrogate;
                }
            }
        }

        public static bool Prefix_SetNewPathRequest(Pawn_PathFollower __instance)
        {
            return Multiplayer.Client == null || Multiplayer.LocalServer != null;
        }

        public static void Postfix_PawnDeSpawn(Pawn __instance)
        {
            if (Multiplayer.Client == null) return;
            lastPathInstanceCache.Remove(__instance.thingIDNumber);
            lastSyncedSurrogateCache.Remove(__instance.thingIDNumber);
        }
    }

    //############################################################################
    // SECTION 3: DATA TRANSFER OBJECTS (DTOs)
    //############################################################################

    public class JobParams : ISynchronizable
    {
        public JobDef def;
        public LocalTargetInfo targetA;
        public LocalTargetInfo targetB;
        public LocalTargetInfo targetC;
        public List<LocalTargetInfo> targetQueueA;
        public List<LocalTargetInfo> targetQueueB;
        public int count = -1;
        public bool playerForced;
        public bool canBashDoors;
        public bool canBashFences;
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
            Job job = JobMaker.MakeJob(def);
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
            if (thinkTreeDef != null && jobGiverKey != -1 && thinkTreeDef.TryGetThinkNodeWithSaveKey(jobGiverKey, out ThinkNode node))
            {
                job.jobGiver = node;
                job.jobGiverThinkTree = thinkTreeDef;
            }
            if (verbCaster != null && !verbLabel.NullOrEmpty() && verbCaster is IVerbOwner owner)
            {
                job.verbToUse = owner.VerbTracker?.AllVerbs.FirstOrDefault(v => v.verbProps.label == verbLabel);
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

        public int NodeCount => nodes?.Count ?? 0;

        public PawnPathSurrogate() { }

        public bool IsSameAs(PawnPathSurrogate other)
        {
            if (other == null) return false;
            if (this.isValid != other.isValid) return false;
            if (!this.isValid) return true;
            if (this.NodeCount != other.NodeCount) return false;
            if (this.totalCost != other.totalCost) return false;
            if (this.NodeCount == 0) return true;
            if (this.nodes[0] != other.nodes[0]) return false;
            if (this.nodes[this.NodeCount - 1] != other.nodes[other.NodeCount - 1]) return false;
            return true;
        }

        public PawnPathSurrogate(PawnPath path)
        {
            if (path == null || !path.Found) { isValid = false; return; }
            isValid = true;
            totalCost = (int)path.TotalCost;
            nodes = new List<IntVec3>(path.NodesReversed);
        }

        public PawnPath ToPawnPath(Pawn pawn)
        {
            if (!isValid) return PawnPath.NotFound;
            PawnPath newPath = pawn.Map.pawnPathPool.GetPath();

            var nativeNodes = new NativeList<IntVec3>(nodes.Count, Allocator.Temp);
            foreach (var node in nodes)
            {
                nativeNodes.Add(node);
            }

            newPath.Initialize(nativeNodes, totalCost);
            nativeNodes.Dispose();

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
    // SECTION 4: CLIENT-SIDE LOGIC (The missing piece)
    //############################################################################

    public static class PathingClientUtil
    {
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate, bool isLocal = false)
        {
            string side = isLocal ? "HOST (Local Client)" : "CLIENT (Remote)";
            if (pawn == null || pawn.pather == null || surrogate == null)
            {
                Log.Warning($"[{side}] Invalid SetPawnPath call.");
                return;
            }
            var pather = pawn.pather;
            PawnPath newPath = surrogate.ToPawnPath(pawn);
            pather.curPath?.ReleaseToPool();
            pather.curPath = newPath;
            if (newPath.Found) pather.ResetToCurrentPosition();
            else pather.PatherFailed();
        }
    }

    //############################################################################
    // SECTION 5: SYNCED ACTIONS
    //############################################################################

    public static class SyncedActions
    {
        [SyncMethod]
        public static void StartJobAI(Pawn pawn, JobParams jobParams)
        {
            Job job = jobParams.ToJob();
            using (new Multiplayer.DontSync())
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, job.jobGiver, resumeCurJobAfterwards: false, cancelBusyStances: true, thinkTree: job.jobGiverThinkTree);
        }

        [SyncMethod]
        public static void TakeOrderedJob(Pawn pawn, JobParams jobParams, JobTag? tag)
        {
            Job job = jobParams.ToJob();
            using (new Multiplayer.DontSync())
                pawn.jobs.TryTakeOrderedJob(job, tag);
        }

        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate)
        {
            if (Multiplayer.LocalServer == null)
                PathingClientUtil.SetPawnPath(pawn, surrogate, isLocal: false);
        }
    }
}
