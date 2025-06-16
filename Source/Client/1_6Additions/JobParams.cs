// Multiplayer/Client/Jobs/JobParams.cs

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
        public int takeExtraIngestibles; // Corrected from bool to int

        public ThinkNode jobGiver;
        public ThinkTreeDef thinkTree;

        public JobParams() { }

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
            lordFaction = job.lord?.faction; // Corrected to use lord.faction
            verbToUse = job.verbToUse;
            takeExtraIngestibles = job.takeExtraIngestibles; // Corrected field

            jobGiver = job.jobGiver;
            thinkTree = job.jobGiverThinkTree;
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
            job.verbToUse = verbToUse;
            job.takeExtraIngestibles = takeExtraIngestibles; // Corrected field
            job.jobGiver = jobGiver;
            job.jobGiverThinkTree = thinkTree;
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
            worker.Bind(ref verbToUse);
            worker.Bind(ref takeExtraIngestibles); // Corrected field
            worker.Bind(ref jobGiver);
            worker.Bind(ref thinkTree);
        }
    }
}
