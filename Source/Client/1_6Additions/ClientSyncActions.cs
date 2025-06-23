using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;

namespace Multiplayer.Client._1_6Additions
{
    public static class ClientSyncActions
    {
        // This is the code that actually runs on the client.
        // It is NOT a [SyncMethod].
        public static void SetPawnPath(Pawn pawn, PawnPathSurrogate surr)
        {
            if (pawn?.pather == null) return;
            var pf = pawn.pather;
            pf.curPath?.ReleaseToPool();
            pf.curPath = surr.ToPawnPath(pawn);
            if (pf.curPath.Found)
                pf.ResetToCurrentPosition();
            else
                pf.PatherFailed();
        }

        public static void StartJobAI(Pawn pawn, JobParams prms)
        {
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
