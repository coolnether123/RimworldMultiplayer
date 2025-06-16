// In new file: StartJobContext.cs

using Multiplayer.API;
using Verse.AI;

namespace Multiplayer.Client
{
    // This class encapsulates all the context for a StartJob call.
    public class StartJobContext : ISynchronizable
    {
        public byte lastJobEndConditionByte;
        public bool resumeCurJobAfterwards;
        public bool cancelBusyStances;
        public bool hasTag;
        public byte tagValueByte;
        public bool fromQueue;
        public bool canReturnCurJobToPool;
        public bool hasCarryOverride;
        public bool carryOverrideValue;
        public bool continueSleeping;
        public bool preToilReservationsCanFail;

        public void Sync(SyncWorker worker)
        {
            worker.Bind(ref lastJobEndConditionByte);
            worker.Bind(ref resumeCurJobAfterwards);
            worker.Bind(ref cancelBusyStances);
            worker.Bind(ref hasTag);
            worker.Bind(ref tagValueByte);
            worker.Bind(ref fromQueue);
            worker.Bind(ref canReturnCurJobToPool);
            worker.Bind(ref hasCarryOverride);
            worker.Bind(ref carryOverrideValue);
            worker.Bind(ref continueSleeping);
            worker.Bind(ref preToilReservationsCanFail);
        }
    }
}
