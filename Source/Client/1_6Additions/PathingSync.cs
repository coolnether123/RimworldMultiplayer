// In new file: PathingSync.cs

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
    // SECTION 1: HARMONY PATCHER
    //############################################################################

    [StaticConstructorOnStartup]
    public static class PathingSyncHarmony
    {
        static PathingSyncHarmony()
        {
            var harmony = Multiplayer.harmony;
            Log.Message("[Multiplayer] Applying pathfinding and job synchronization patches for 1.6 (v5)...");

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
        // We now need two caches: one for the PawnPath instance itself, and one for the surrogate.
        // This helps us detect changes efficiently without creating surrogates every tick.
        private static Dictionary<int, PawnPath> lastPathInstanceCache = new();
        private static Dictionary<int, PawnPathSurrogate> lastSyncedSurrogateCache = new();

        public static bool Prefix_StartJob(Pawn_JobTracker __instance, Job newJob, ThinkNode jobGiver)
        {
            if (Multiplayer.Client == null) return true;

            // THE JOB CHURN FIX:
            // If the pawn already has a job and the new AI-generated job is identical,
            // just block it. This prevents the constant job restarting loop.
            if (__instance.curJob != null && newJob.JobIsSameAs(__instance.pawn, __instance.curJob))
            {
                return false; // Block the redundant job start
            }

            // Player-ordered jobs are handled by TryTakeOrderedJob
            if (jobGiver == null) return true;

            if (Multiplayer.LocalServer != null)
            {
                // Host determines the job and syncs it.
                SyncedActions.StartJobAI(__instance.pawn, new JobParams(newJob));
                return true; // Host runs the original method
            }

            // Clients block AI jobs and wait for the host's command.
            return false;
        }

        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;

            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }

        // NEW LOGIC: Using a postfix is safer. We let the game do its thing, then we check the result.
        public static void Postfix_PatherTick(Pawn_PathFollower __instance)
        {
            // We only care about the host's pathfinding results.
            if (Multiplayer.Client == null || Multiplayer.LocalServer == null || !__instance.pawn.Spawned) return;

            Pawn pawn = __instance.pawn;
            PawnPath currentPath = __instance.curPath;

            // Get the last path instance we saw for this pawn.
            lastPathInstanceCache.TryGetValue(pawn.thingIDNumber, out var cachedPathInstance);

            // If the current path is different from the one we cached, it's a new path.
            if (currentPath != cachedPathInstance)
            {
                // Update the instance cache immediately.
                lastPathInstanceCache[pawn.thingIDNumber] = currentPath;

                // Create a surrogate from the new path to compare and sync.
                var newSurrogate = new PawnPathSurrogate(currentPath);

                // Get the last surrogate we actually *sent* over the network.
                lastSyncedSurrogateCache.TryGetValue(pawn.thingIDNumber, out var lastSentSurrogate);

                // If the new path is meaningfully different from the last one we sent, sync it.
                if (!newSurrogate.IsSameAs(lastSentSurrogate))
                {
                    Log.Message($"[HOST] Pawn:{pawn.LabelShortCap} | Detected new path. Syncing {newSurrogate.NodeCount} nodes.");
                    // We simply call the sync method. The framework handles sending it.
                    SyncedActions.SetPawnPath(pawn, newSurrogate);
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
    // Same as before, these are solid.
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

            // The vanilla PathFinder creates a NativeList to pass to PawnPath.Initialize. We will do the same.
            var nativeNodes = new NativeList<IntVec3>(nodes.Count, Allocator.Temp);
            foreach (var node in nodes)
            {
                nativeNodes.Add(node);
            }

            // This is the vanilla method for setting up a path.
            newPath.Initialize(nativeNodes, totalCost);

            // Free the temporary native list memory.
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
    // SECTION 4: SYNCED ACTIONS
    //############################################################################

    public static class SyncedActions
    {
        [SyncMethod]
        public static void StartJobAI(Pawn pawn, JobParams jobParams)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;
            Job job = jobParams.ToJob();

            // This now runs on the Host (from the original call) and all clients (from the network)
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

            // This runs on all clients after being broadcast by the host.
            using (new Multiplayer.DontSync())
            {
                pawn.jobs.TryTakeOrderedJob(job, tag);
            }
        }

        [SyncMethod]
        private static void StartJobAI_Impl(Pawn pawn, JobParams jobParams)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;
            Job job = jobParams.ToJob();
            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, job.jobGiver, false, true, job.jobGiverThinkTree);
            }
        }

        [SyncMethod]
        private static void TakeOrderedJob_Impl(Pawn pawn, JobParams jobParams, JobTag? tag)
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
            string side = Multiplayer.LocalServer != null ? "HOST" : "CLIENT";

            // The host originates paths, it should never process a synced one.
            // This guard is important to prevent the local call from the SyncMethod framework
            // from being processed on the host.
            if (Multiplayer.LocalServer != null) return;

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
                Log.Message($"[{side}] Pawn:{pawn.LabelShortCap} | Path is valid. Resetting pather to current position.");
                pather.ResetToCurrentPosition();
            }
            else
            {
                Log.Message($"[{side}] Pawn:{pawn.LabelShortCap} | Path not found. Pather failing.");
                pather.PatherFailed();
            }
        }
    }

    /* Helper extension method for logging
    public static class PawnPathSurrogateExtensions
    {
        public static bool IsValid(this PawnPathSurrogate surrogate)
        {
            return (bool)Traverse.Create(surrogate).Field("isValid").GetValue();
        }
    }*/
}
