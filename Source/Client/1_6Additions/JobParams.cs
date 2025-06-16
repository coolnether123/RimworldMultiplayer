// Multiplayer/Client/Jobs/JobParams.cs

using Multiplayer.API;
using Multiplayer.Common;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
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
        public int takeExtraIngestibles;

        public ThinkNode jobGiver;
        public ThinkTreeDef thinkTree;

        private Thing verbCaster;
        private int verbIndex = -1;

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

            jobGiver = job.jobGiver;
            thinkTree = job.jobGiverThinkTree;

            if (job.verbToUse != null)
            {
                verbCaster = job.verbToUse.Caster;
                // CORRECTED: Cast to IVerbOwner to access VerbTracker
                var owner = verbCaster as IVerbOwner;
                verbIndex = owner?.VerbTracker?.AllVerbs.IndexOf(job.verbToUse) ?? -1;
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
            job.jobGiver = jobGiver;
            job.jobGiverThinkTree = thinkTree;

            if (verbCaster != null && verbIndex != -1)
            {
                // CORRECTED: Cast to IVerbOwner to access VerbTracker
                var owner = verbCaster as IVerbOwner;
                var tracker = owner?.VerbTracker;
                if (tracker != null && verbIndex >= 0 && verbIndex < tracker.AllVerbs.Count)
                {
                    job.verbToUse = tracker.AllVerbs[verbIndex];
                }
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
            worker.Bind(ref jobGiver);
            worker.Bind(ref thinkTree);

            worker.Bind(ref verbCaster);
            worker.Bind(ref verbIndex);
        }
    }
}
