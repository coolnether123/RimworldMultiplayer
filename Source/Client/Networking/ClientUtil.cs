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
            // We must queue the packet processing to happen on the main thread in the correct order.
            var dataBytes = data.ReadRaw(data.Length);

            // CHECKPOINT X1: Confirming the packet is being queued from the network thread.
            if (Multiplayer.Client != null)
                Log.Message($"[CLIENT-NET] Queuing packet. Size: {dataBytes.Length}, Reliable: {reliable}");

            OnMainThread.Enqueue(() =>
            {
                if (Multiplayer.Client == null)
                {
                    Log.Warning("[CLIENT-NET] Process queue: Multiplayer.Client is null, dropping packet.");
                    return;
                }

                try
                {
                    // CHECKPOINT X2: Confirming the queued action is being executed on the main game thread.
                    Log.Message($"[CLIENT-NET] Dequeued and processing packet. Size: {dataBytes.Length}");
                    Multiplayer.Client.HandleReceiveRaw(new ByteReader(dataBytes), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {Multiplayer.Client}: {e}");
                    Multiplayer.session.disconnectInfo.titleTranslated = "MpPacketErrorLocal".Translate();
                    ConnectionStatusListeners.TryNotifyAll_Disconnected();
                    Multiplayer.StopMultiplayer();
                }
            });
        }
    }
}
