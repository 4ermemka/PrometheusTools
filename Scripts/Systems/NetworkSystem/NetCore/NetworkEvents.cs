using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Assets.Scripts.Network.NetCore
{
    public enum ClientConnectionState { Disconnected, Connecting, Handshaking, Synchronizing, Ready }

    public sealed class ClientConnection
    {
        public Guid ClientId { get; }
        public Guid ConnectionId { get; }
        public Guid ServerSessionId { get; }
        public bool IsReconnect { get; }
        public ClientConnection(Guid clientId, Guid connectionId, Guid serverSessionId, bool isReconnect)
        {
            ClientId = clientId;
            ConnectionId = connectionId;
            ServerSessionId = serverSessionId;
            IsReconnect = isReconnect;
        }
    }

    /// <summary>Only Drain invokes application callbacks. One faulty observer cannot stop the others.</summary>
    internal sealed class NetworkEvents
    {
        private readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
        public void Enqueue(Action action) => actions.Enqueue(action);
        public void Drain()
        {
            while (actions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
        public static void Raise(Action handlers)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception ex) { Debug.LogException(ex); }
        }
        public static void Raise<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
