using Multiplayer.Common;
using Verse;

namespace Multiplayer.Client.Commands
{
    // It does NOT inherit from ScheduledCommand
    public class ScheduledPathUpdateCommand
    {
        public Pawn pawn;
        public PawnPathSurrogate path;

        // An empty constructor for the client to create an instance before deserializing
        public ScheduledPathUpdateCommand() { }

        // A constructor for the host to create an instance before serializing
        public ScheduledPathUpdateCommand(Pawn pawn, PawnPathSurrogate path)
        {
            this.pawn = pawn;
            this.path = path;
        }

        // This is a normal public method, not an override
        public byte[] Serialize()
        {
            var writer = new ByteWriter();
            SyncSerialization.WriteSyncObject(writer, pawn, typeof(Pawn));
            SyncSerialization.WriteSyncObject(writer, path, typeof(PawnPathSurrogate));
            return writer.ToArray();
        }

        // This is a normal public method, not an override
        public void Deserialize(byte[] data)
        {
            var reader = new ByteReader(data);
            pawn = SyncSerialization.ReadSync<Pawn>(reader);
            path = SyncSerialization.ReadSync<PawnPathSurrogate>(reader);
        }
    }
}
