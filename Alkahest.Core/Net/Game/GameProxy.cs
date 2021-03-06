using Alkahest.Core.Logging;
using Alkahest.Core.Net.Game.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Alkahest.Core.Net.Game
{
    public sealed class GameProxy : IDisposable
    {
        static readonly Log _log = new Log(typeof(GameProxy));

        public ServerInfo Info { get; }

        internal ObjectPool<SocketAsyncEventArgs> ArgsPool { get; }

        public PacketSerializer Serializer { get; }

        public int Backlog { get; }

        public int MaxClients { get; }

        public TimeSpan Timeout { get; }

        public IReadOnlyCollection<GameClient> Clients
        {
            get
            {
                lock (_clients)
                    return _clients.ToArray();
            }
        }

        readonly HashSet<GameClient> _clients = new HashSet<GameClient>();

        readonly ManualResetEventSlim _event = new ManualResetEventSlim();

        readonly Socket _serverSocket;

        bool _disposed;

        public event EventHandler<GameClient> ClientConnected;

        public event EventHandler<GameClient> ClientDisconnected;

        public event RefEventHandler<PacketEventArgs> PacketReceived;

        public event RefEventHandler<PacketEventArgs> PacketSent;

        public GameProxy(ServerInfo info, ObjectPool<SocketAsyncEventArgs> pool,
            PacketSerializer serializer, int backlog, int maxClients, TimeSpan timeout)
        {
            if (backlog < 0)
                throw new ArgumentOutOfRangeException(nameof(backlog));

            if (maxClients < 1)
                throw new ArgumentOutOfRangeException(nameof(maxClients));

            Info = info ?? throw new ArgumentNullException(nameof(info));
            ArgsPool = pool ?? throw new ArgumentNullException(nameof(pool));
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            Backlog = backlog;
            MaxClients = maxClients;
            Timeout = timeout;
            _serverSocket = new Socket(info.ProxyEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true,
                NoDelay = true,
            };
        }

        ~GameProxy()
        {
            RealDispose(false);
        }

        public void Dispose()
        {
            RealDispose(true);
            GC.SuppressFinalize(this);
        }

        void RealDispose(bool disposing)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_serverSocket == null)
                return;

            _serverSocket.SafeClose();
            _event.Wait();
            _event.Dispose();

            foreach (var client in _clients.ToArray())
                client.Disconnect();

            if (disposing)
                _log.Basic("Game proxy for {0} stopped", Info.Name);
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);

            _serverSocket.Bind(Info.ProxyEndPoint);
            _serverSocket.Listen(Backlog);

            Accept();

            _log.Basic("Game proxy for {0} listening at {1}", Info.Name,
                Info.ProxyEndPoint);
        }

        void Accept()
        {
            var args = ArgsPool.Get();

            args.Completed += OnAccept;

            Task.Factory.StartNew(() =>
            {
                if (!_serverSocket.AcceptAsync(args))
                    OnAccept(this, args);
            }, TaskCreationOptions.LongRunning);
        }

        void OnAccept(object sender, SocketAsyncEventArgs args)
        {
            var error = args.SocketError;
            var socket = args.AcceptSocket;
            var reject = _clients.Count >= MaxClients;

            args.Completed -= OnAccept;

            ArgsPool.TryPut(args);

            if (error != SocketError.Success || reject)
            {
                var isAbort = error == SocketError.OperationAborted;

                if (!isAbort)
                    _log.Info("Rejected connection to {0} from {1}: {2}", Info.Name, socket.RemoteEndPoint,
                        reject ? "Maximum amount of clients reached" : error.ToErrorString());

                socket.SafeClose();

                // Are we shutting down?
                if (isAbort)
                {
                    _event.Set();
                    return;
                }
            }
            else
            {
                var client = new GameClient(this, socket);

                _log.Info("Accepted connection to {0} from {1}", Info.Name, socket.RemoteEndPoint);
            }

            Accept();
        }

        internal void AddClient(GameClient client)
        {
            lock (_clients)
                _clients.Add(client);
        }

        internal void RemoveClient(GameClient client)
        {
            lock (_clients)
                _clients.Remove(client);
        }

        internal void InvokeConnected(GameClient client)
        {
            ClientConnected?.Invoke(this, client);
        }

        internal void InvokeDisconnected(GameClient client)
        {
            ClientDisconnected?.Invoke(this, client);
        }

        bool InvokePacketEvent(RefEventHandler<PacketEventArgs> handler, GameClient client,
            Direction direction, ushort code, ref Memory<byte> payload, bool direct)
        {
            if (handler == null)
                return true;

            var args = new PacketEventArgs(client, direction, code,
                direct ? payload.ToArray() : payload);

            handler(this, ref args);

            if (direct)
                return true;

            payload = args.Payload;

            return !args.Silence;
        }

        internal bool InvokeReceived(GameClient client, Direction direction, ushort code,
            ref Memory<byte> payload)
        {
            return InvokePacketEvent(PacketReceived, client, direction, code, ref payload, false);
        }

        internal bool InvokeSent(GameClient client, Direction direction, ushort code,
            ref Memory<byte> payload, bool direct)
        {
            return InvokePacketEvent(PacketSent, client, direction, code, ref payload, direct);
        }
    }
}
