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

        // CHANGED: Instead of syncing the ThinkNode object, we sync its definition and key.
        private ThinkTreeDef thinkTreeDef;
        private int jobGiverKey;

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

            // NEW: Safely capture the ThinkNode reference
            thinkTreeDef = job.jobGiverThinkTree;
            jobGiverKey = job.jobGiver?.UniqueSaveKey ?? -1;

            if (job.verbToUse != null)
            {
                verbCaster = job.verbToUse.Caster;
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

            // NEW: Reconstruct the ThinkNode reference
            if (thinkTreeDef != null && jobGiverKey != -1)
            {
                if (thinkTreeDef.TryGetThinkNodeWithSaveKey(jobGiverKey, out ThinkNode node))
                {
                    job.jobGiver = node;
                    job.jobGiverThinkTree = thinkTreeDef;
                }
            }

            if (verbCaster != null && verbIndex != -1)
            {
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

            // CHANGED: Syncing the Def and key is safe.
            worker.Bind(ref thinkTreeDef);
            worker.Bind(ref jobGiverKey);

            worker.Bind(ref verbCaster);
            worker.Bind(ref verbIndex);
        }
    }
}
