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
            Log.Message("[Multiplayer] Applying pathfinding and job synchronization patches for 1.6 (v6)...");

            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_StartJob)));
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.StartJob");

            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_TryTakeOrderedJob)));
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.TryTakeOrderedJob");

            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_PatherTick)));
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.PatherTick (Postfix)");

            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.SetNewPathRequest)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_SetNewPathRequest)));
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.SetNewPathRequest");

            harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn)),
                postfix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Postfix_PawnDeSpawn)));
            Log.Message("[Multiplayer] ... Patched Pawn.DeSpawn");
        }
    }

    //############################################################################
    // SECTION 2: THE PATCH IMPLEMENTATIONS
    //############################################################################

    public static class PathingPatches
    {
        private static Dictionary<int, PawnPath> lastPathInstanceCache = new();
        private static Dictionary<int, PawnPathSurrogate> lastSyncedSurrogateCache = new();

        public static bool Prefix_StartJob(Pawn_JobTracker __instance, Job newJob, ThinkNode jobGiver)
        {
            if (Multiplayer.Client == null) return true;

            if (__instance.curJob != null && newJob.JobIsSameAs(__instance.pawn, __instance.curJob))
            {
                return false;
            }

            if (jobGiver == null) return true;

            if (Multiplayer.LocalServer != null)
            {
                SyncedActions.StartJobAI(__instance.pawn, new JobParams(newJob));
                return true;
            }

            return false;
        }

        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;

            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }

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
                    Log.Message($"[HOST] Pawn:{pawn.LabelShortCap} | Detected new path. Syncing {newSurrogate.NodeCount} nodes.");

                    // Send to remote clients via the network
                    SyncedActions.SetPawnPath(pawn, newSurrogate);

                    // Manually apply to the local client instance to bypass broken local networking
                    PathingClientUtil.SetPawnPath(pawn, newSurrogate);

                    lastSyncedSurrogateCache[pawn.thingIDNumber] = newSurrogate;
                }
            }
        }

        public static bool Prefix_SetNewPathRequest(Pawn_PathFollower __instance)
        {
            if (Multiplayer.Client != null && Multiplayer.LocalServer == null)
            {
                return false;
            }
            return true;
        }

        public static void Postfix_PawnDeSpawn(Pawn __instance)
        {
            if (Multiplayer.Client != null)
            {
                lastPathInstanceCache.Remove(__instance.thingIDNumber);
                lastSyncedSurrogateCache.Remove(__instance.thingIDNumber);
            }
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
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate)
        {
            // This string helps us know if this log is from the host's own client instance or a remote one.
            string side = Multiplayer.LocalServer != null ? "HOST (Local Client)" : "CLIENT (Remote)";

            if (pawn == null || pawn.pather == null || surrogate == null)
            {
                Log.Warning($"[{side}] Received invalid SetPawnPath call. Pawn: {pawn?.ToString() ?? "null"}, Surrogate: {surrogate?.ToString() ?? "null"}");
                return;
            }

            Log.Message($"[{side}] Pawn:{pawn.LabelShortCap} ID:{pawn.thingIDNumber} | Processing synced path with {surrogate.NodeCount} nodes. IsValid: {surrogate.isValid}");

            var pather = pawn.pather;
            PawnPath newPath = surrogate.ToPawnPath(pawn);

            pather.curPath?.ReleaseToPool();
            pather.curPath = newPath;

            if (newPath.Found)
            {
                pather.ResetToCurrentPosition();
            }
            else
            {
                pather.PatherFailed();
            }
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
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;
            Job job = jobParams.ToJob();

            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, job.jobGiver, false, true, job.jobGiverThinkTree);
            }
        }

        [SyncMethod]
        public static void TakeOrderedJob(Pawn pawn, JobParams jobParams, JobTag? tag)
        {
            if (pawn == null || pawn.jobs == null) return;
            Job job = jobParams.ToJob();

            using (new Multiplayer.DontSync())
            {
                pawn.jobs.TryTakeOrderedJob(job, tag);
            }
        }

        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate)
        {
            // When a remote client receives this, call the logic method.
            // The host's local client will call this method directly from the PatherTick patch.
            PathingClientUtil.SetPawnPath(pawn, surrogate);
        }
    }
}
