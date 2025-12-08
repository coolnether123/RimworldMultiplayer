using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Commands;
using Multiplayer.Common;
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
            AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick)),
            postfix: new HarmonyMethod(typeof(ClientPatherTick_Diagnostic), nameof(ClientPatherTick_Diagnostic.Postfix))
            );



            // Register SyncWorkers for our custom data types.
            MP.RegisterSyncWorker<JobParams>(SyncWorkers.ReadWriteJobParams);
            MP.RegisterSyncWorker<PawnPathSurrogate>(SyncWorkers.ReadWritePawnPathSurrogate);

            Log.Message("[Multiplayer-Pathing] Pathing sync patches applied successfully.");
        }
    }

    // This is a NEW, separate class for diagnostics.
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class ClientPatherTick_Diagnostic
    {
        // We use a Postfix here to ensure we see the state *after* any potential changes in the original method.
        public static void Postfix(Pawn_PathFollower __instance)
        {
            // Only run this diagnostic on the client.
            if (Multiplayer.Client == null || Multiplayer.LocalServer != null) return;

            Pawn pawn = __instance.pawn;

            // We only care about player pawns for this test.
            if (!MpTrace.IsPlayableColonist(pawn)) return;

            // Log this information once per second (60 ticks) to avoid spamming the log.
            if (pawn.IsHashIntervalTick(60))
            {
                MpTrace.Info(
                    $"[TICK_DIAGNOSTIC] Pawn: {pawn.LabelShort}, " +
                    $"Moving Flag: {__instance.Moving}, " +
                    $"Path Exists: {(__instance.curPath != null && __instance.curPath.Found)}, " +
                    $"Has Job: {pawn.CurJob != null}"
                );
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class AllowClientPatherTick
    {
        // Replaced the simple Prefix with a more detailed one for debugging.
        public static void Prefix(Pawn_PathFollower __instance)
        {
            // Only run this expensive logging on the client side.
            if (Multiplayer.Client == null || Multiplayer.LocalServer != null) return;

            // We only care about player colonists who should be moving.
            if (!__instance.Moving || !MpTrace.IsPlayableColonist(__instance.pawn)) return;

            var pather = __instance;
            var pawn = __instance.pawn;

            // Log all the critical variables that could cause PatherTick to exit early.
            MpTrace.Verbose(
                $"[CLIENT-PatherTick] Pawn: {pawn.LabelShort}, " +
                $"Moving: {pather.Moving}, " +
                $"StanceBusy: {pawn.stances.FullBodyBusy}, " +
                $"curPath: {(pather.curPath != null ? "Exists" : "Null")}, " +
                $"PathFound: {pather.curPath?.Found ?? false}, " +
                $"NodesLeft: {pather.curPath?.NodesLeftCount ?? -1}, " +
                $"NextCell: {pather.nextCell}, " +
                $"WillCollide: {pather.WillCollideNextCell}, " +
                $"CostLeft: {pather.nextCellCostLeft:F2}"
            );

            // Also specifically check for doors, as they are a common cause of pathing stalls.
            Building_Door door = pather.nextCell.GetDoor(pawn.Map);
            if (door != null)
            {
                MpTrace.Verbose(
                    $"[CLIENT-PatherTick] --> DoorCheck: {door.Label}, " +
                    $"Slows: {door.SlowsPawns}, " +
                    $"Open: {door.Open}, " +
                    $"TicksToOpen: {door.TicksTillFullyOpened}, " +
                    $"CanOpen: {door.PawnCanOpen(pawn)}"
                );
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
            if (Multiplayer.LocalServer != null && newJob != null && newJob.jobGiver != null && !newJob.playerForced)
            {
                // Create an instance of our custom command class
                var commandData = new ScheduledJobStartCommand(__instance.pawn, new JobParams(newJob));

                // Use the SERVER's command handler to send the command
                Multiplayer.LocalServer.commands.Send(
                    CommandType.SyncPawnJob,
                    factionId: __instance.pawn.Faction?.loadID ?? -1,
                    mapId: __instance.pawn.Map.uniqueID,
                    data: commandData.Serialize() // Serialize our custom object into the data payload
                );
            }
        }




        public static bool Prefix_TryTakeOrderedJob(Pawn_JobTracker __instance, Job job, JobTag? tag)
        {
            // This remains the same, as it's a player-driven action and can use the existing [SyncMethod]
            if (Multiplayer.Client == null || !Multiplayer.ShouldSync) return true;
            SyncedActions.TakeOrderedJob(__instance.pawn, new JobParams(job), tag);
            return false;
        }

        public static void Postfix_PatherTick(Pawn_PathFollower __instance)
        {
            if (Multiplayer.LocalServer == null || !__instance.pawn.Spawned || __instance.pawn.Drafted) return;

            int id = __instance.pawn.thingIDNumber;
            if (lastSyncTick.TryGetValue(id, out int last) && GenTicks.TicksGame < last + 30) return;

            try
            {
                var p = __instance.curPath;

                // ================== THE FIX ==================
                // If the path doesn't exist or wasn't found, don't send anything.
                if (p == null || !p.Found)
                {
                    return;
                }
                // =============================================

                var content = (p.FirstNode, p.LastNode, p.NodesLeftCount);

                if (lastContentCache.TryGetValue(id, out var prev) && prev.Equals(content)) return;

                lastContentCache[id] = content;
                lastSyncTick[id] = GenTicks.TicksGame;

                // Our previous logging. We can keep this for now.
                MpTrace.Info($"[HOST-SENDER] Creating SyncPawnPath for {__instance.pawn}. Path IsValid: {p.Found}");

                var commandData = new ScheduledPathUpdateCommand(__instance.pawn, new PawnPathSurrogate(p));
                byte[] serializedData = commandData.Serialize();

                MpTrace.Info($"[HOST-SENDER] Serialized SyncPawnPath. Size: {serializedData.Length}. Sending now.");

                Multiplayer.LocalServer.commands.Send(
                    CommandType.SyncPawnPath,
                    factionId: __instance.pawn.Faction?.loadID ?? -1,
                    mapId: __instance.pawn.Map.uniqueID,
                    data: serializedData
                );
            }
            catch (Exception e)
            {
                MpTrace.Error($"[HOST-SENDER] FAILED to send SyncPawnPath for {__instance.pawn}. Exception: {e}");
            }
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
            // Added a check for a null or empty node list.
            if (!isValid || nodes == null || nodes.Count == 0) return PawnPath.NotFound;

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

    // This class is for METHODS CALLED DIRECTLY BY THE GAME that should be synced.
    public static class SyncedActions
    {
        // This is a player-initiated action, so [SyncMethod] is correct.
        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void TakeOrderedJob(Pawn pawn, JobParams prms, JobTag? tag)
        {
            // The prefix in PathingPatches already handles sending this.
            // When the client receives the call, it should execute the job.
            // The guard prevents the host from re-running it after receiving its own packet.
            if (Multiplayer.LocalServer != null) return;

            try
            {
                PathingPatches.InSyncAction++;
                pawn.jobs.TryTakeOrderedJob(prms.ToJob(), tag);
            }
            finally
            {
                PathingPatches.InSyncAction--;
            }
        }

        // Your test methods are also fine here as they are triggered manually.
        [SyncMethod(context = SyncContext.CurrentMap)]
        public static void TestSync(string message)
        {
            bool isHost = Multiplayer.LocalServer != null;
            MpTrace.Info($"[TestSync] side={(isHost ? "HOST" : "CLIENT")} message={message}");
        }
    }
}
