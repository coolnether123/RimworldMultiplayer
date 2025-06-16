// To be placed in a new file, e.g., Multiplayer/Client/Jobs/SyncedJobGiver.cs

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
            // This method is called by the host, and because of [SyncMethod],
            // it's automatically re-broadcast and executed on all clients with the same parameters.

            if (pawn == null || pawn.jobs == null) return;

            Job newJob = jobParams.ToJob();

            // Finally, start the job on the pawn.
            // We use InterruptForced because the AI is overriding any previous state.
            // We set `canReturnCurJobToPool` to true as we are done with the old job.
            // Setting `addToJobsThisTick` to false prevents recursive job assignment checks within the same tick.
            pawn.jobs.StartJob(
                newJob,
                JobCondition.InterruptForced,
                newJob.jobGiver,
                resumeCurJobAfterwards: false,
                cancelBusyStances: true,
                thinkTree: newJob.jobGiverThinkTree,
                tag: null,
                fromQueue: false,
                canReturnCurJobToPool: true,
                addToJobsThisTick: false
            );
        }
    }
}
