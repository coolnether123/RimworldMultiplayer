// To be placed in a new file, e.g., Multiplayer/Client/Jobs/JobParams.cs

using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
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
        public Verb verbToUse;
        public bool aTakeClosest;
        public bool bTakeClosest;

        // We also need to sync the source of the job to aid in debugging and ensure correctness.
        public ThinkNode jobGiver;
        public ThinkTreeDef thinkTree;

        // Constructor for serialization
        public JobParams() { }

        // Constructor to capture a live job
        public JobParams(Job job)
        {
            def = job.def;
            targetA = job.targetA;
            targetB = job.targetB;
            targetC = job.targetC;
            targetQueueA = job.targetQueueA;
            targetQueueB = job.targetQueueB;
            count = job.count;
            playerForced = job.playerForced;
            canBashDoors = job.canBashDoors;
            canBashFences = job.canBashFences;
            haulMode = job.haulMode;
            lordFaction = job.lord?.LordJob.GetFaction();
            verbToUse = job.verbToUse;
            aTakeClosest = job.takeExtraIngestibles; // Assuming this is the intended use
            bTakeClosest = job.haulOpportunisticFilter != null; // Placeholder for similar logic

            jobGiver = job.jobGiver;
            thinkTree = job.jobGiverThinkTree;
        }

        // Creates a new Job object from the stored parameters
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
            job.verbToUse = verbToUse;
            job.jobGiver = jobGiver;
            job.jobGiverThinkTree = thinkTree;
            // Note: Some Job fields like lord may need special handling if not rebuilt automatically.
            return job;
        }

        public void Sync(SyncWorker worker)
        {
            // Use the multiplayer mod's existing serialization system
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
            worker.Bind(ref verbToUse);
            worker.Bind(ref aTakeClosest);
            worker.Bind(ref bTakeClosest);
            worker.Bind(ref jobGiver);
            worker.Bind(ref thinkTree);
        }
    }
}
