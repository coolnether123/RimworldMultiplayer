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
            // This log can be changed to MpTrace.Verbose once we confirm things are working.
            Log.Message($"[SERVER-COMMANDHANDLER] Send called. CommandType: {cmd}, MapID: {mapId}, Data Length: {data.Length}");

            // policy
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

            // Create a base command object to be saved to the world data.
            // For saving purposes, issuedBySelf can be considered false.
            var commandToSave = new ScheduledCommand(
                cmd,
                server.gameTimer,
                factionId,
                mapId,
                sourcePlayer?.id ?? fauxSource?.id ?? ScheduledCommand.NoPlayer,
                data
            )
            { issuedBySelf = false }; // Explicitly false for the saved version.

            // Serialize it once for saving.
            byte[] serializedCommandToSave = ScheduledCommand.Serialize(commandToSave);

            // Save the command to the world data and temporary command list.
            // todo cull target players if not global
            server.worldData.mapCmds.GetOrAddNew(mapId).Add(serializedCommandToSave);
            server.worldData.tmpMapCmds?.GetOrAddNew(mapId).Add(serializedCommandToSave);

            // Now, loop through players to send the network packet.
            foreach (ServerPlayer player in server.PlayingPlayers)
            {
                // We reuse the commandToSave object and just modify the one flag that changes per player.
                // This is slightly more efficient than creating a new object every time.
                commandToSave.issuedBySelf = (sourcePlayer == player);

                // Serialize the command with the correct flag for this specific player.
                byte[] packetData = ScheduledCommand.Serialize(commandToSave);

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
