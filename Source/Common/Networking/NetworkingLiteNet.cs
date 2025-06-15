using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Multiplayer.Common
{
    public class MpServerNetListener : INetEventListener
    {
        private MultiplayerServer server;
        private bool arbiter;

        public MpServerNetListener(MultiplayerServer server, bool arbiter)
        {
            this.server = server;
            this.arbiter = arbiter;
        }

        public void OnConnectionRequest(ConnectionRequest req)
        {
            ServerLog.Log($"[DBG] ConnReq from {req.RemoteEndPoint.Address}");
            var result = server.playerManager.OnPreConnect(req.RemoteEndPoint.Address);
            if (result != null)
            {
                ServerLog.Log($"[DBG] ConnReq rejected: {result}");
                req.Reject(ConnectionBase.GetDisconnectBytes(result.Value));
                return;
            }

            ServerLog.Log("[DBG] ConnReq accepted");
            req.Accept();
        }

        public void OnPeerConnected(NetPeer peer)
        {
            ServerLog.Log($"[DBG] Peer accepted: {peer.EndPoint}");
            ConnectionBase conn = new LiteNetConnection(peer);
            conn.ChangeState(ConnectionStateEnum.ServerJoining);
            peer.Tag = conn;

            var player = server.playerManager.OnConnected(conn);

            // If the game isn’t ready yet, close the peer with a proper payload
            // so the client sees “ServerStarting” instead of a generic failure.
            if (!server.FullyStarted)
            {
                conn.Close(MpDisconnectReason.ServerStarting);
                    return;
            }

            if (arbiter)
            {
                player.type = PlayerType.Arbiter;
                player.color = new ColorRGB(128, 128, 128);
            }
            ServerLog.Log($"[DBG] State after accept: {conn.State}");
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            ConnectionBase conn = peer.GetConnection();
            server.playerManager.SetDisconnected(conn, MpDisconnectReason.ClientLeft);
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            peer.GetConnection().Latency = latency;
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            peer.GetConnection().serverPlayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}
