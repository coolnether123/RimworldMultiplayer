// In new file: PathingSync.cs

using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    //############################################################################
    // SECTION 1: HARMONY PATCHER
    // This class runs at startup and applies all our patches.
    //############################################################################

    [StaticConstructorOnStartup]
    public static class PathingSyncHarmony
    {
        static PathingSyncHarmony()
        {
            var harmony = Multiplayer.harmony;
            Log.Message("[Multiplayer] Applying pathfinding and job synchronization patches for 1.6...");

            // Intercepts ALL job starts to sync them.
            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_StartJob)));
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.StartJob");

            // Intercepts player-ordered jobs to sync them.
            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_TryTakeOrderedJob)));
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.TryTakeOrderedJob");

            // Observes the host's pather to sync new paths.
            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_PatherTick)));
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.PatherTick");

            // Prevents clients from generating their own paths.
            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.SetNewPathRequest)),
                prefix: new HarmonyMethod(typeof(PathingPatches), nameof(PathingPatches.Prefix_SetNewPathRequest)));
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.SetNewPathRequest");

            // Clears path cache on despawn to prevent memory leaks.
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
        private static Dictionary<int, PawnPathSurrogate> lastSyncedPathCache = new();

        // Patch 1: Sync all jobs at their source.
        public static bool Prefix_StartJob(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, ThinkNode jobGiver)
        {
            // If not in a multiplayer game, or if this is a non-AI job (player ordered), run original.
            // Player-ordered jobs are handled by Prefix_TryTakeOrderedJob.
            if (Multiplayer.Client == null || jobGiver == null) return true;

            // On the host, we let the AI decide the job and then sync it.
            if (Multiplayer.LocalServer != null)
            {
                SyncedActions.StartJobAI(__instance.pawn, new JobParams(newJob));
                // We let the original method run on the host.
                return true;
            }

            // On the client, we block the AI from starting any jobs. They will receive the job from the host.
            return false;
        }

        // Patch 2: Sync player-ordered jobs.
        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;

            // Sync the action, then block the original method. The host will send back the command to run the job on all clients.
            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }

        // Patch 3: Observe the host's pathfinding and sync results.
        public static bool Prefix_PatherTick(Pawn_PathFollower __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.dontSync || !__instance.pawn.Spawned) return true;

            // Only the host generates and sends paths.
            if (Multiplayer.LocalServer != null)
            {
                // We check if a new path has been calculated.
                if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out PawnPath outPath))
                {
                    // A new path is ready. Create a surrogate for syncing.
                    var surrogate = new PawnPathSurrogate(outPath);

                    // Check if it's different from the last one we sent.
                    if (!lastSyncedPathCache.TryGetValue(__instance.pawn.thingIDNumber, out var lastPath) || !surrogate.IsSameAs(lastPath))
                    {
                        // It's a new, unique path. Sync it.
                        SyncedActions.SetPawnPath(__instance.pawn, surrogate);
                        lastSyncedPathCache[__instance.pawn.thingIDNumber] = surrogate;
                    }

                    // CRITICAL: We DO NOT dispose the path or return false.
                    // The original PatherTick method needs `outPath` to continue its logic and make the host's pawn move.
                }
            }

            // Always allow the original PatherTick to run.
            // - For the host, this is essential for pawn movement.
            // - For clients, this is needed to process movement along the synced path and handle arrival.
            return true;
        }

        // Patch 4: Prevent clients from ever requesting a path.
        public static bool Prefix_SetNewPathRequest(Pawn_PathFollower __instance)
        {
            // If we're in a multiplayer game AND we are a client (not the host), block this method.
            if (Multiplayer.Client != null && Multiplayer.LocalServer == null)
            {
                // Clients must not generate their own paths. They wait for the host's instruction.
                // By returning false, we prevent the client from creating a new PathRequest.
                return false;
            }

            // Allow the host to run this method freely.
            return true;
        }

        // Patch 5: Clear cache on despawn.
        public static void Postfix_PawnDeSpawn(Pawn __instance)
        {
            if (Multiplayer.Client != null)
            {
                lastSyncedPathCache.Remove(__instance.thingIDNumber);
            }
        }
    }

    //############################################################################
    // SECTION 3: DATA TRANSFER OBJECTS (DTOs)
    // Your DTOs were well-structured and are kept largely the same.
    // I removed StartJobContext as we can now sync StartJob and TryTakeOrderedJob
    // directly, which is cleaner.
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
        private bool isValid;
        private List<IntVec3> nodes;
        private int totalCost;
        private bool usedRegionHeuristics;

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

            // For performance, we only do a shallow check. If you still see pathing desyncs,
            // a full list comparison could be enabled for debugging.
            // return this.nodes.SequenceEqual(other.nodes);
            return true;
        }

        public PawnPathSurrogate(PawnPath path)
        {
            if (path == null || !path.Found) { isValid = false; return; }
            isValid = true;
            totalCost = (int)path.TotalCost;
            usedRegionHeuristics = path.UsedRegionHeuristics;
            nodes = new List<IntVec3>(path.NodesReversed);
        }

        public PawnPath ToPawnPath(Pawn pawn)
        {
            if (!isValid) return PawnPath.NotFound;
            PawnPath newPath = pawn.Map.pawnPathPool.GetPath();

            // We need to use reflection to set the private fields correctly, as there's no public initializer.
            var traverser = Traverse.Create(newPath);
            traverser.Field<List<IntVec3>>("nodes").Value.Clear();
            traverser.Field<List<IntVec3>>("nodes").Value.AddRange(nodes);
            traverser.Field<float>("totalCostInt").Value = totalCost;
            traverser.Field<int>("curNodeIndex").Value = nodes.Count - 1;
            traverser.Field<bool>("usedRegionHeuristics").Value = usedRegionHeuristics;
            traverser.Field<bool>("inUse").Value = true;

            return newPath;
        }

        public void Sync(SyncWorker worker)
        {
            worker.Bind(ref isValid);
            if (!isValid) return;
            worker.Bind(ref nodes);
            worker.Bind(ref totalCost);
            worker.Bind(ref usedRegionHeuristics);
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

            // This code now runs on all clients, initiated by the host.
            // We use DontSync to prevent this from creating a feedback loop.
            using (new Multiplayer.DontSync())
            {
                // Let the JobTracker start the job. We use InterruptForced because an AI job
                // should override whatever the pawn was doing.
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
                // We directly call the original logic on all clients now.
                // The prefix on TryTakeOrderedJob will be skipped because of ShouldSync.
                pawn.jobs.TryTakeOrderedJob(job, tag);
            }
        }

        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate)
        {
            if (Multiplayer.LocalServer != null) return; // Host doesn't need to receive its own paths
            if (pawn == null || pawn.pather == null || surrogate == null) return;

            PawnPath path = surrogate.ToPawnPath(pawn);

            // Release the old path to the pool to prevent memory leaks
            pawn.pather.curPath?.ReleaseToPool();

            pawn.pather.curPath = path;

            // If a valid path was received, reset the pather's state.
            // If the path is invalid (not found), make the pather fail gracefully.
            if (path.Found)
            {
                // ResetToCurrentPosition makes the pather re-evaluate its state with the new path
                // and start moving from its current cell. It also handles setting up the first move.
                pawn.pather.ResetToCurrentPosition();
            }
            else
            {
                pawn.pather.PatherFailed();
            }
        }
    }
}
