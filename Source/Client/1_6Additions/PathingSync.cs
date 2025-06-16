// In new file: PathingSync.cs

using HarmonyLib;
using Iced.Intel;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
            //Log.Message("[Multiplayer] Applying FINAL pathfinding synchronization patches...");

            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)), prefix: new HarmonyMethod(typeof(Pawn_JobTracker_StartJob_Patch), nameof(Pawn_JobTracker_StartJob_Patch.Prefix)));
           // Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.StartJob");

            harmony.Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)), prefix: new HarmonyMethod(typeof(Pawn_PathFollower_PatherTick_Patch), nameof(Pawn_PathFollower_PatherTick_Patch.Prefix)));
            //Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.PatherTick");

            harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn)), postfix: new HarmonyMethod(typeof(Pawn_DeSpawn_Patch), nameof(Pawn_DeSpawn_Patch.Postfix)));
            //Log.Message("[Multiplayer] ... Patched Pawn.DeSpawn (for cache clearing)");

            // Re-adding combat patch as it's a good edge case to handle.
            harmony.Patch(
                AccessTools.Method(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition)),
                postfix: new HarmonyMethod(typeof(Toils_Combat_GotoCastPosition_Patch), nameof(Toils_Combat_GotoCastPosition_Patch.Postfix))
            );
            Log.Message("[Multiplayer] ... Patched Toils_Combat.GotoCastPosition");
        }
    }

    //############################################################################
    // SECTION 2: THE PATCH IMPLEMENTATIONS
    //############################################################################

    // The "catch-all" patch for starting any job.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Pawn_JobTracker_StartJob_Patch
    {
        public static bool Prefix(Pawn_JobTracker __instance, Job newJob, ThinkNode jobGiver, ThinkTreeDef thinkTree)
        {
            if (Multiplayer.Client == null || Multiplayer.dontSync) return true;

            // On the host, the AI runs normally.
            if (Multiplayer.LocalServer != null)
            {
                // This is a player-ordered job if jobGiver is null. Let it run.
                if (jobGiver == null) return true;

                // This is an AI job. The host runs it, and the path will be synced later.
                return true;
            }

            // On the client, block AI jobs.
            if (jobGiver != null) return false;

            // Allow player-ordered jobs on the client, but they will be desynced until we sync them.
            // For now, this is fine as we are focused on the main AI pathing.
            return true;
        }
    }

    // The patch to sync the host's pathfinding result.
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Pawn_PathFollower_PatherTick_Patch
    {

        // A simple cache to remember the last path we synced for each pawn.
        // We use pawn.thingIDNumber as the key for safety.
        private static Dictionary<int, PawnPathSurrogate> lastSyncedPathCache = new Dictionary<int, PawnPathSurrogate>();

        public static bool Prefix(Pawn_PathFollower __instance)
        {
            if (Multiplayer.Client == null || Multiplayer.dontSync || !__instance.pawn.Spawned) return true;

            // This logic is now ONLY for the host.
            if (Multiplayer.LocalServer != null)
            {
                // The host's PatherTick runs normally. We observe it.
                // The TryGetPath check is the key moment when a new path is ready.
                if (__instance.curPathRequest != null && __instance.curPathRequest.TryGetPath(out PawnPath outPath))
                {
                    // A new path has been calculated for the host's pawn.
                    // This is the authoritative path. We must send it to the clients.
                    __instance.DisposeAndClearCurPathRequest();
                    var surrogate = new PawnPathSurrogate(outPath);
                    // Check if we have sent a path for this pawn before, and if the new path is different.
                    if (!lastSyncedPathCache.TryGetValue(__instance.pawn.thingIDNumber, out var lastPath) || !surrogate.IsSameAs(lastPath))
                    {
                        Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: New unique path found. Syncing path with {surrogate.NodeCount} nodes.");

                        // It's a new path, so we sync it and update our cache.
                        SyncedActions.SetPawnPath(__instance.pawn, surrogate);
                        lastSyncedPathCache[__instance.pawn.thingIDNumber] = surrogate;
                    }
                    // If the path is the same as the last one we sent, we do nothing to avoid network spam.

                    outPath.Dispose();

                    return false;
                }
            }

            // Allow the original PatherTick to run for the host.
            // This is what makes the host's pawns move.
            // Clients are blocked from starting jobs, so their PatherTick will do nothing useful anyway.
            return true;
        }
        // Helper to clear the cache when a pawn is despawned to prevent memory leaks.
        public static void ClearCacheForPawn(Pawn pawn)
        {
            if (pawn != null)
            {
                lastSyncedPathCache.Remove(pawn.thingIDNumber);
            }
        }
    }

    // Patch to clear our cache when a pawn is destroyed.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Pawn_DeSpawn_Patch
    {
        public static void Postfix(Pawn __instance)
        {
            if (Multiplayer.Client != null)
            {
                Pawn_PathFollower_PatherTick_Patch.ClearCacheForPawn(__instance);
            }
        }
    }

    // The patch for the combat edge case.
    [HarmonyPatch(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition))]
    public static class Toils_Combat_GotoCastPosition_Patch
    {
        public static void Postfix(Toil __result)
        {
            var originalInit = __result.initAction;
            if (originalInit == null) return;
            __result.initAction = () =>
            {
                originalInit();
                var pawn = __result.actor;
                var job = pawn.CurJob;
                if (job.verbToUse != null && Multiplayer.ShouldSync)
                {
                    SyncedActions.SetJobVerb(pawn, new JobParams(job));
                }
            };
        }
    }


    //############################################################################
    // SECTION 3: DATA TRANSFER OBJECTS (DTOs)
    //############################################################################

    public class StartJobContext : ISynchronizable
    {
        public byte lastJobEndConditionByte;
        public bool resumeCurJobAfterwards;
        public bool cancelBusyStances;
        public bool hasTag;
        public byte tagValueByte;
        public bool fromQueue;
        public bool canReturnCurJobToPool;
        public bool hasCarryOverride;
        public bool carryOverrideValue;
        public bool continueSleeping;
        public bool preToilReservationsCanFail;

        public StartJobContext() { }

        public StartJobContext(JobCondition lastJobEndCondition, bool resumeCurJobAfterwards, bool cancelBusyStances, JobTag? tag, bool fromQueue, bool canReturnCurJobToPool, bool? keepCarryingThingOverride, bool continueSleeping, bool preToilReservationsCanFail)
        {
            this.lastJobEndConditionByte = (byte)lastJobEndCondition;
            this.resumeCurJobAfterwards = resumeCurJobAfterwards;
            this.cancelBusyStances = cancelBusyStances;
            this.hasTag = tag.HasValue;
            this.tagValueByte = (byte)tag.GetValueOrDefault();
            this.fromQueue = fromQueue;
            this.canReturnCurJobToPool = canReturnCurJobToPool;
            this.hasCarryOverride = keepCarryingThingOverride.HasValue;
            this.carryOverrideValue = keepCarryingThingOverride.GetValueOrDefault();
            this.continueSleeping = continueSleeping;
            this.preToilReservationsCanFail = preToilReservationsCanFail;
        }

        public void Sync(SyncWorker worker)
        {
            worker.Bind(ref lastJobEndConditionByte);
            worker.Bind(ref resumeCurJobAfterwards);
            worker.Bind(ref cancelBusyStances);
            worker.Bind(ref hasTag);
            worker.Bind(ref tagValueByte);
            worker.Bind(ref fromQueue);
            worker.Bind(ref canReturnCurJobToPool);
            worker.Bind(ref hasCarryOverride);
            worker.Bind(ref carryOverrideValue);
            worker.Bind(ref continueSleeping);
            worker.Bind(ref preToilReservationsCanFail);
        }
    }

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
            if (!this.isValid) return true; // Both are invalid, so they are the same.
            if (this.NodeCount != other.NodeCount) return false;
            if (this.totalCost != other.totalCost) return false;

            // Check if the first and last nodes are the same. This is a good enough heuristic
            // to catch most meaningful path changes without comparing every single node.
            if (this.nodes[0] != other.nodes[0] || this.nodes[this.NodeCount - 1] != other.nodes[other.NodeCount - 1])
            {
                return false;
            }

            return true;
        }

        public PawnPathSurrogate(PawnPath path)
        {
            if (path == null || !path.Found) { isValid = false; return; }
            isValid = true;
            totalCost = (int)path.TotalCost;
            usedRegionHeuristics = path.UsedRegionHeuristics;
            nodes = new List<IntVec3>();
            nodes = new List<IntVec3>(path.NodesReversed);
        }

        public PawnPath ToPawnPath(Pawn pawn)
        {
            if (!isValid) return PawnPath.NotFound;
            PawnPath newPath = pawn.Map.pawnPathPool.GetPath();
            newPath.InitializeFromReversedNodeList(nodes, totalCost, usedRegionHeuristics);
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
            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, job.jobGiver, false, true, job.jobGiverThinkTree);
            }
        }

        [SyncMethod]
        public static void StartJob(Pawn pawn, JobParams jobParams, StartJobContext context)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;
            Job job = jobParams.ToJob();
            JobCondition lastJobEndCondition = (JobCondition)context.lastJobEndConditionByte;
            JobTag? tag = context.hasTag ? new JobTag?((JobTag)context.tagValueByte) : null;
            bool? keepCarryingThingOverride = context.hasCarryOverride ? new bool?(context.carryOverrideValue) : null;
            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, lastJobEndCondition, job.jobGiver, context.resumeCurJobAfterwards, context.cancelBusyStances, job.jobGiverThinkTree, tag, context.fromQueue, context.canReturnCurJobToPool, keepCarryingThingOverride, context.continueSleeping, context.preToilReservationsCanFail);
            }
        }

        [SyncMethod]
        public static void SetJobVerb(Pawn pawn, JobParams jobParams)
        {
            Job job = pawn?.CurJob;
            if (job == null) return;
            var reconstructedJob = jobParams.ToJob();
            job.verbToUse = reconstructedJob.verbToUse;
        }


        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surrogate)
        {
            if (Multiplayer.LocalServer != null) return;
            if (pawn == null || pawn.pather == null || surrogate == null) return;

            PawnPath path = surrogate.ToPawnPath(pawn);
            Log.Message($"[CLIENT] {pawn.LabelShortCap} is RECEIVING path with {surrogate.NodeCount} nodes.");

            if (pawn.pather.curPath != null) pawn.pather.curPath.ReleaseToPool();
            pawn.pather.curPath = path;

            if (path.Found) pawn.pather.ResetToCurrentPosition();
            else pawn.pather.PatherFailed();
        }
    }

    //############################################################################
    // SECTION 5: HELPER EXTENSIONS
    //############################################################################

    public static class PawnPath_Initialization_Extensions
    {
        public static void InitializeFromReversedNodeList(this PawnPath path, List<IntVec3> reversedNodes, int cost, bool usedHeuristics)
        {
            path.NodesReversed.Clear();
            if (reversedNodes != null)
            {
                path.NodesReversed.AddRange(reversedNodes);
            }
            var pathTraverser = Traverse.Create(path);
            pathTraverser.Field<float>("totalCostInt").Value = cost;
            pathTraverser.Field<int>("curNodeIndex").Value = path.NodesReversed.Count - 1;
            pathTraverser.Field<bool>("usedRegionHeuristics").Value = usedHeuristics;
            pathTraverser.Field<bool>("inUse").Value = true;
        }
    }
}
