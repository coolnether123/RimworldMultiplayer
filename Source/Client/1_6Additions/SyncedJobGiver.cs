// In file: SyncedJobGiver.cs

using Multiplayer.API;
using Verse;
using Verse.AI;

namespace Multiplayer.Client
{
    // Make this class partial so it can be extended in other files.
    public static partial class SyncedJobs
    {
        [SyncMethod]
        public static void StartJob(Pawn pawn, JobParams jobParams, JobCondition lastJobEndCondition)
        {
            if (pawn == null || pawn.jobs == null || pawn.Dead) return;

            Job job = jobParams.ToJob();

            // Use DontSync to prevent our StartJob patch from firing again and causing an infinite loop.
            using (new Multiplayer.DontSync())
            {
                pawn.jobs.StartJob(job, lastJobEndCondition, job.jobGiver, false, true, job.jobGiverThinkTree, null, false);
            }
        }
    }
}
