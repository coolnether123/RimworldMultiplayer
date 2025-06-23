using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Commands
{
    // It does NOT inherit from ScheduledCommand
    public class ScheduledJobStartCommand
    {
        public Pawn pawn;
        public JobParams jobParams;

        // An empty constructor for the client to create an instance before deserializing
        public ScheduledJobStartCommand() { }

        // A constructor for the host to create an instance before serializing
        public ScheduledJobStartCommand(Pawn pawn, JobParams jobParams)
        {
            this.pawn = pawn;
            this.jobParams = jobParams;
        }

        // This is a normal public method, not an override
        public byte[] Serialize()
        {
            var writer = new ByteWriter();
            SyncSerialization.WriteSyncObject(writer, pawn, typeof(Pawn));
            SyncSerialization.WriteSyncObject(writer, jobParams, typeof(JobParams));
            return writer.ToArray();
        }

        // This is a normal public method, not an override
        public void Deserialize(byte[] data)
        {
            var reader = new ByteReader(data);
            pawn = SyncSerialization.ReadSync<Pawn>(reader);
            jobParams = SyncSerialization.ReadSync<JobParams>(reader);
        }
    }
}
