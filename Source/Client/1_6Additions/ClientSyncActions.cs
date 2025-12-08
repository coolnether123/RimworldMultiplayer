using Verse;
using Verse.AI;

namespace Multiplayer.Client._1_6Additions
{
    // This class is for ACTIONS EXECUTED ON THE CLIENT after receiving a ScheduledCommand.
    public static class ClientSyncActions
    {
        // This is called by AsyncTimeComp.ExecuteCmd when a SyncPawnPath command arrives.
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surr)
        {
            // This guard is CRITICAL. It prevents the host from corrupting its own pathfinding.
            if (Multiplayer.LocalServer != null) return;
            if (pawn?.pather == null) return;

            var pf = pawn.pather;

            // Release any old path to the pool.
            pf.curPath?.ReleaseToPool();

            // Create the new path from the server's data.
            pf.curPath = surr.ToPawnPath(pawn);

            // If the path from the server is invalid for any reason, fail gracefully.
            // If it IS valid, we DO NOTHING MORE. The next PatherTick will handle movement.
            if (!pf.curPath.Found)
            {
                pf.PatherFailed();
            }
        }

        // This is called by AsyncTimeComp.ExecuteCmd when a SyncPawnJob command arrives.
        public static void StartJobAI(Pawn pawn, JobParams prms)
        {
            // This guard is CRITICAL. It prevents the host from starting the same job twice.
            if (Multiplayer.LocalServer != null) return;

            try
            {
                PathingPatches.InSyncAction++;
                pawn.jobs.StartJob(prms.ToJob(), JobCondition.InterruptForced);
            }
            finally
            {
                PathingPatches.InSyncAction--;
            }
        }
    }
}
