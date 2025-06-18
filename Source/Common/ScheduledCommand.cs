using System;
using System.Collections.Generic;

namespace Multiplayer.Common
{
    public class ScheduledCommand
    {
        public const int NoFaction = -1;
        public const int Global = -1;
        public const int NoPlayer = -1;

        public readonly CommandType type;
        public readonly int ticks;
        public readonly int factionId;
        public readonly int mapId;
        public readonly int playerId;
        public readonly byte[] data;

        // Client only, not serialized
        public bool issuedBySelf;

        public ScheduledCommand(CommandType type, int ticks, int factionId, int mapId, int playerId, byte[] data)
        {
            this.type = type;
            this.ticks = ticks;
            this.factionId = factionId;
            this.mapId = mapId;
            this.playerId = playerId;
            this.data = data;
        }

        public static byte[] Serialize(ScheduledCommand cmd)
        {
            ByteWriter writer = new ByteWriter();
            writer.WriteInt32(Convert.ToInt32(cmd.type));
            writer.WriteInt32(cmd.ticks);
            writer.WriteInt32(cmd.factionId);
            writer.WriteInt32(cmd.mapId);
            writer.WriteInt32(cmd.playerId);
            writer.WritePrefixedBytes(cmd.data);

            writer.WriteBool(cmd.issuedBySelf);

            return writer.ToArray();
        }

        public static ScheduledCommand Deserialize(ByteReader data)
        {
            CommandType cmd = (CommandType)data.ReadInt32();
            int ticks = data.ReadInt32();
            int factionId = data.ReadInt32();
            int mapId = data.ReadInt32();
            int playerId = data.ReadInt32();
            byte[] extraBytes = data.ReadPrefixedBytes()!;

            // Read the flag from the stream as part of deserialization.
            bool bySelf = data.ReadBool();

            var newCmd = new ScheduledCommand(cmd, ticks, factionId, mapId, playerId, extraBytes);
            newCmd.issuedBySelf = bySelf;

            return newCmd;
        }

        public override string ToString()
        {
            return $"Cmd: {type}, faction: {factionId}, map: {mapId}, ticks: {ticks}, player: {playerId}";
        }

        public static List<ScheduledCommand> DeserializeCmds(byte[] data)
        {
            var reader = new ByteReader(data);

            int count = reader.ReadInt32();
            var result = new List<ScheduledCommand>(count);
            for (int i = 0; i < count; i++)
                result.Add(Deserialize(new ByteReader(reader.ReadPrefixedBytes()!)));

            return result;
        }

        public static byte[] SerializeCmds(List<ScheduledCommand> cmds)
        {
            ByteWriter writer = new ByteWriter();

            writer.WriteInt32(cmds.Count);
            foreach (var cmd in cmds)
                writer.WritePrefixedBytes(Serialize(cmd));

            return writer.ToArray();
        }
    }
}
