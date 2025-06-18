using Multiplayer.API;

namespace Multiplayer.Client
{
    public static class SyncWorkers
    {
        // --- Custom DTO: JobParams ------------------------------------------
        [SyncWorker]
        static void SyncJobParams(SyncWorker sync, ref JobParams obj)
        {
            obj.Sync(sync);
        }

        // --- Custom DTO: PawnPathSurrogate -----------------------------------
        [SyncWorker]
        static void SyncPawnPath(SyncWorker sync, ref PawnPathSurrogate obj)
        {
            obj.Sync(sync);
        }
        // NOTE: No Pawn worker – the MP API provides one already.
    }
}
