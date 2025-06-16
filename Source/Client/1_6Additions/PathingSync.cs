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
            Log.Message("[Multiplayer] Applying FINAL pathfinding synchronization patches...");

            // This patch now ONLY blocks clients from starting jobs.
            harmony.Patch(
                AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
                prefix: new HarmonyMethod(typeof(Pawn_JobTracker_StartJob_Patch), nameof(Pawn_JobTracker_StartJob_Patch.Prefix))
            );
            Log.Message("[Multiplayer] ... Patched Pawn_JobTracker.StartJob");

            // This patch is now the core of the sync logic.
            harmony.Patch(
                AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
                prefix: new HarmonyMethod(typeof(Pawn_PathFollower_PatherTick_Patch), nameof(Pawn_PathFollower_PatherTick_Patch.Prefix))
            );
            Log.Message("[Multiplayer] ... Patched Pawn_PathFollower.PatherTick");
        }
    }

    //############################################################################
    // SECTION 2: THE PATCH IMPLEMENTATIONS
    //############################################################################

    // The "catch-all" patch for starting any job.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Pawn_JobTracker_StartJob_Patch
    {
        public static bool Prefix()
        {
            // If we are in multiplayer AND we are not the host, block the job.
            if (Multiplayer.Client != null && Multiplayer.LocalServer == null)
            {
                return false;
            }
            // The host and singleplayer run the original method.
            return true;
        }
    }

    // The patch to sync the host's pathfinding result.
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class Pawn_PathFollower_PatherTick_Patch
    {
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

                    var surrogate = new PawnPathSurrogate(outPath);
                    Log.Message($"[HOST] {__instance.pawn.LabelShortCap}: Path result found. Syncing path with {surrogate.NodeCount} nodes.");

                    // Call the sync method to broadcast this path.
                    SyncedActions.SetPawnPath(__instance.pawn, surrogate);
                }
            }

            // Allow the original PatherTick to run for the host.
            // This is what makes the host's pawns move.
            // Clients are blocked from starting jobs, so their PatherTick will do nothing useful anyway.
            return true;
        }
    }

    /* The patch for the combat edge case.
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
    }*/


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

            if (pawn.pather.curPath != null)
            {
                pawn.pather.curPath.ReleaseToPool();
            }

            pawn.pather.curPath = path;

            if (path.Found)
            {
                pawn.pather.ResetToCurrentPosition();
            }
            else
            {
                pawn.pather.PatherFailed();
            }
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
