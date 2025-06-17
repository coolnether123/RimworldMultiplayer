using LiteNetLib;
using Multiplayer.Common;
using Steamworks;
using System;
using Verse;
using Multiplayer.Client.Networking;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnectWithWindow(string address, int port, bool returnToServerBrowser = true)
        {
            Find.WindowStack.Add(new ConnectingWindow(address, port) { returnToServerBrowser = returnToServerBrowser });

            Multiplayer.session = new MultiplayerSession
            {
                address = address,
                port = port
            };

            NetManager netClient = new NetManager(new MpClientNetListener())
            {
                EnableStatistics = true,
                IPv6Enabled = MpUtil.SupportsIPv6() ? IPv6Mode.SeparateSocket : IPv6Mode.Disabled
            };

            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address, port, "");
        }

        public static void TrySteamConnectWithWindow(CSteamID user, bool returnToServerBrowser = true)
        {
            Log.Message("Connecting through Steam");

            Multiplayer.session = new MultiplayerSession
            {
                client = new SteamClientConn(user) { username = Multiplayer.username },
                steamHost = user
            };

            Find.WindowStack.Add(new SteamConnectingWindow(user) { returnToServerBrowser = returnToServerBrowser });

            Multiplayer.session.ReapplyPrefs();
            Multiplayer.Client.ChangeState(ConnectionStateEnum.ClientSteam);
        }

        public static void HandleReceive(ByteReader data, bool reliable)
        {
            // Create a copy of the raw byte data IMMEDIATELY.
            // The buffer from LiteNetLib will be reused, so we can't delay reading it.
            var dataCopy = new byte[data.Length];
            Buffer.BlockCopy(data.ReadRaw(data.Length), 0, dataCopy, 0, dataCopy.Length);

            // Enqueue an action that uses the COPY of the data.
            OnMainThread.Enqueue(() => ProcessPacket(dataCopy, reliable));
        }

        private static void ProcessPacket(byte[] data, bool reliable)
        {
            // === STEP 1: LOG ARRIVAL ON MAIN THREAD ===
            MpTrace.Info("ProcessPacket: Packet has arrived on the main thread. About to process.");

            if (Multiplayer.Client == null)
            {
                MpTrace.Warning("ProcessPacket: Multiplayer.Client is null, aborting.");
                return;
            }

            try
            {
                Multiplayer.Client.HandleReceiveRaw(new ByteReader(data), reliable);
                // === STEP 2: LOG SUCCESSFUL PROCESSING ===
                MpTrace.Info("ProcessPacket: HandleReceiveRaw completed without error.");
            }
            catch (Exception e)
            {
                MpTrace.Error($"ProcessPacket: Exception during HandleReceiveRaw: {e}");
                Log.Error($"Exception handling packet by {Multiplayer.Client}: {e}");
                Multiplayer.session.disconnectInfo.titleTranslated = "MpPacketErrorLocal".Translate();
                ConnectionStatusListeners.TryNotifyAll_Disconnected();
                Multiplayer.StopMultiplayer();
            }
        }
    }
}
