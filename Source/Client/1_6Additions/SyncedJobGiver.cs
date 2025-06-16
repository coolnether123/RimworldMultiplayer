// In file: SyncedJobGiver.cs (or a new file like SyncedJobs.cs)

using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SyncedJobs
    {
        [SyncMethod]
        public static void StartJob(Pawn pawn, JobParams jobParams, JobCondition lastJobEndCondition)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;

            Job job = jobParams.ToJob();

            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, lastJobEndCondition, job.jobGiver, false, true, job.jobGiverThinkTree, null, false);
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
    }
}
