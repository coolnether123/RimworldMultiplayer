using System;
using Multiplayer.Common;
using Verse;

namespace Multiplayer.Common
{
    public class CommandHandler
    {
        private MultiplayerServer server;

        public int SentCmds { get; private set; }

        public CommandHandler(MultiplayerServer server)
        {
            this.server = server;
        }

        public void Send(CommandType cmd, int factionId, int mapId, byte[] data, ServerPlayer? sourcePlayer = null, ServerPlayer? fauxSource = null)
        {
            // We are looking for CommandType.Sync, which is used for [SyncMethod] calls.
            // This log can be changed to a verbose/debug trace once we confirm things are working.
            Log.Message($"[SERVER-COMMANDHANDLER] Send called. CommandType: {cmd}, MapID: {mapId}, Data Length: {data.Length}");

            // policy checks
            if (sourcePlayer != null)
            {
                bool debugCmd =
                    cmd == CommandType.DebugTools ||
                    cmd == CommandType.Sync && server.initData!.DebugOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (debugCmd && !CanUseDevMode(sourcePlayer))
                    return;

                bool hostOnly = cmd == CommandType.Sync && server.initData!.HostOnlySyncCmds.Contains(BitConverter.ToInt32(data, 0));
                if (hostOnly && !sourcePlayer.IsHost)
                    return;

                if (cmd is CommandType.MapTimeSpeed or CommandType.GlobalTimeSpeed &&
                    server.settings.timeControl == TimeControl.HostOnly && !sourcePlayer.IsHost)
                    return;
            }

            // Create a single, reusable command object.
            // We will set its issuedBySelf flag to false for saving to the world data.
            var commandToSend = new ScheduledCommand(
                cmd,
                server.gameTimer,
                factionId,
                mapId,
                sourcePlayer?.id ?? fauxSource?.id ?? ScheduledCommand.NoPlayer,
                data
            )
            { issuedBySelf = false }; // Default to false for the saved version.

            // Serialize it once for saving to disk/replay.
            byte[] serializedCommandToSave = ScheduledCommand.Serialize(commandToSend);

            // Save the command to the world data and temporary command list.
            // todo cull target players if not global
            server.worldData.mapCmds.GetOrAddNew(mapId).Add(serializedCommandToSave);
            server.worldData.tmpMapCmds?.GetOrAddNew(mapId).Add(serializedCommandToSave);

            // Now, loop through players to send the personalized network packet.
            foreach (ServerPlayer player in server.PlayingPlayers)
            {
                // Set the flag specifically for this player.
                commandToSend.issuedBySelf = (sourcePlayer == player);

                // Serialize the command with the correct flag for this specific player.
                byte[] packetData = ScheduledCommand.Serialize(commandToSend);

                // Send the personalized packet.
                player.conn.Send(Packets.Server_Command, packetData);
            }

            SentCmds++;
        }

        public void PauseAll()
        {
            if (server.settings.timeControl == TimeControl.LowestWins)
                Send(
                    CommandType.TimeSpeedVote,
                    ScheduledCommand.NoFaction,
                    ScheduledCommand.Global,
                    ByteWriter.GetBytes(TimeVote.ResetGlobal, -1)
                );
            else
                Send(
                    CommandType.PauseAll,
                    ScheduledCommand.NoFaction,
                    ScheduledCommand.Global,
                    Array.Empty<byte>()
                );
        }

        public bool CanUseDevMode(ServerPlayer player) =>
            server.settings.debugMode && server.settings.devModeScope switch
            {
                DevModeScope.Everyone => true,
                DevModeScope.HostOnly => player.IsHost
            };
    }
}
