// Multiplayer/Client/Jobs/SyncedJobGiver.cs

using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    public static class SyncedJobGiver
    {
        [SyncMethod]
        public static void GiveJob(Pawn pawn, JobParams jobParams)
        {
            if (pawn == null || pawn.jobs == null) return;

            Job newJob = jobParams.ToJob();

            pawn.jobs.StartJob(
                newJob,
                JobCondition.InterruptForced,
                newJob.jobGiver,
                resumeCurJobAfterwards: false,
                cancelBusyStances: true,
                thinkTree: newJob.jobGiverThinkTree,
                tag: null,
                fromQueue: false,
                addToJobsThisTick: false
            );
        }
    }
}
