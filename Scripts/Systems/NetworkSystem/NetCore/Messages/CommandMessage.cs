using System;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>Application command; the server supplies identity from the authenticated connection.</summary>
    public sealed class CommandMessage
    {
        public Guid RequestId { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public string Payload { get; set; }
    }

    public sealed class ClientCommand
    {
        public ClientConnection Connection { get; }
        public CommandMessage Message { get; }
        public ClientCommand(ClientConnection connection, CommandMessage message)
        { Connection = connection; Message = message; }
    }

    public sealed class CommandReply
    {
        public Guid RequestId { get; set; }
        public string Error { get; set; }
    }
}
