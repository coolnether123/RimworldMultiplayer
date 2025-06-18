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
            Log.Message("[Multiplayer-Pathing] Applying definitive sync patches (v18)...");

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
        public static bool IsExecutingSyncCommand = false;
        public static int InSyncAction = 0; // Use an int counter, not a bool

        private static Dictionary<int, PawnPath> lastPathInstanceCache = new();
        private static Dictionary<int, PawnPathSurrogate> lastSyncedSurrogateCache = new();

        // This patch now ONLY has one job: stop clients from starting AI jobs.
        public static bool Prefix_StartJob(Pawn_JobTracker __instance, ThinkNode jobGiver)
        {
            if (IsExecutingSyncCommand) return true;

            if (PathingPatches.InSyncAction > 0)
            {
                // This log is now the most important one. If we see this, we are on the right track.
                MpTrace.Info($"Allowing job ({__instance.pawn.CurJob?.def.defName}) for {__instance.pawn.LabelShortCap} because InSyncAction > 0.");
                return true;
            }

            if (Multiplayer.Client != null && Multiplayer.LocalServer == null && jobGiver != null)
            {
                // We are a client, and this is an AI job. Block it.
                MpTrace.Verbose($"Blocking AI jobgiver {jobGiver.GetType().Name} for {__instance.pawn.LabelShortCap}.");
                return false;
            }
            // Host runs everything. Player-ordered jobs run everywhere (but are synced separately).
            return true;
        }

        public static void Postfix_StartJob(Pawn_JobTracker __instance, ThinkNode jobGiver)
        {
            if (Multiplayer.LocalServer != null && jobGiver != null && __instance.curJob != null)
            {
                MpTrace.Verbose($"Host detected new AI job ({__instance.curJob.def.defName}) for {__instance.pawn.LabelShortCap}. Sending sync.");
                SyncedActions.StartJobAI(__instance.pawn, new JobParams(__instance.curJob));
            }
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

            // Check if the path has changed since the last tick
            if (currentPath != cachedPathInstance)
            {
                lastPathInstanceCache[pawn.thingIDNumber] = currentPath;

                bool isValid = currentPath != null && currentPath.Found;
                int totalCost = isValid ? (int)currentPath.TotalCost : 0;

                // Manual serialization of the path nodes
                int[] nodes = null;
                if (isValid)
                {
                    var nodeList = currentPath.NodesReversed;
                    nodes = new int[nodeList.Count * 2];
                    for (int i = 0; i < nodeList.Count; i++)
                    {
                        nodes[i * 2] = nodeList[i].x;
                        nodes[i * 2 + 1] = nodeList[i].z;
                    }
                }

                // Call the new, simplified SyncMethod
                SyncedActions.SetPawnPath(pawn, isValid, totalCost, nodes);
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
        

        public bool playerForced; // Temporarily disable
        public bool canBashDoors; // Temporarily disable
        public bool canBashFences; // Temporarily disable
        public HaulMode haulMode; // Temporarily disable
        public Faction lordFaction; // Temporarily disable
        public int takeExtraIngestibles; // Temporarily disable
        private ThinkTreeDef thinkTreeDef; // DEFINITELY disable this
        private int jobGiverKey; // DEFINITELY disable this
        private Thing verbCaster; // DEFINITELY disable this
        private string verbLabel; // DEFINITELY disable this

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
    // SECTION 4: SYNCED ACTIONS
    //############################################################################

    public static class SyncedActions
    {
        [SyncMethod]
        public static void StartJobAI(Pawn pawn, JobParams jobParams)
        {
            MpTrace.Info($"Executing synced AI job ({jobParams.def.defName}) for {pawn?.LabelShortCap}.");

            if (Multiplayer.LocalServer != null) return;

            // vvv WRAP THE LOGIC IN TRY/FINALLY vvv
            try
            {
                PathingPatches.InSyncAction++;
                PathingPatches.IsExecutingSyncCommand = true;
                Job job = jobParams.ToJob();
                using (new Multiplayer.DontSync())
                {
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced, job.jobGiver, resumeCurJobAfterwards: false, cancelBusyStances: true, thinkTree: job.jobGiverThinkTree);
                }
            }
            finally
            {
                PathingPatches.InSyncAction--;
                PathingPatches.IsExecutingSyncCommand = false;
            }
            // ^^^ WRAP THE LOGIC IN TRY/FINALLY ^^^
        }

        [SyncMethod]
        public static void TakeOrderedJob(Pawn pawn, JobParams jobParams, JobTag? tag)
        {
            MpTrace.Info($"Executing synced ordered job ({jobParams.def.defName}) for {pawn?.LabelShortCap}.");

            // vvv WRAP THE LOGIC IN TRY/FINALLY vvv
            try
            {
                PathingPatches.IsExecutingSyncCommand = true;
                Job job = jobParams.ToJob();
                using (new Multiplayer.DontSync())
                {
                    pawn.jobs.TryTakeOrderedJob(job, tag);
                }
            }
            finally
            {
                PathingPatches.IsExecutingSyncCommand = false;
            }
            // ^^^ WRAP THE LOGIC IN TRY/FINALLY ^^^
        }

        [SyncMethod]
        public static void SetPawnPath(Pawn pawn, bool isValid, int totalCost, int[] nodes)
        {
            // This log is now the most important one.
            MpTrace.Info($"Applying synced path to {pawn?.LabelShortCap}. (Valid: {isValid})");

            if (Multiplayer.LocalServer != null) return; // Host only sends
            if (pawn == null || pawn.pather == null) return;

            var pather = pawn.pather;
            pather.curPath?.ReleaseToPool(); // Always release the old path

            if (!isValid)
            {
                pather.curPath = PawnPath.NotFound;
                pather.PatherFailed();
                return;
            }

            // Reconstruct the path from the primitive data
            PawnPath newPath = pawn.Map.pawnPathPool.GetPath();
            var nativeNodes = new NativeList<IntVec3>(nodes.Length / 2, Allocator.Temp);
            for (int i = 0; i < nodes.Length / 2; i++)
            {
                nativeNodes.Add(new IntVec3(nodes[i * 2], 0, nodes[i * 2 + 1]));
            }

            newPath.Initialize(nativeNodes, totalCost);
            nativeNodes.Dispose();

            pather.curPath = newPath;
            pather.ResetToCurrentPosition();
        }
    }
}

