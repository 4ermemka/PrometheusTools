using PrometheusTools.Shared.Abstract;
using PrometheusTools.Shared.Enums;
using PrometheusTools.Shared.Networking;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public class Server : ILogableObject
{
    public Action<LogType, string> OnLog { get; set; }
    public Action<TcpClient> OnUserConnected;
    public Action<TcpClient> OnUserDisconnected;

    public string Name { get; set; } = "Server";

    private readonly ConnectionListener _connectionListener;

    private readonly Dictionary<TcpClient, NetworkStream> _clients = new();
    private readonly object _clientsLock = new();


    public Server()
    {
        _connectionListener = new ConnectionListener();

        _connectionListener.OnUserConnected += ClientConnected;
        _connectionListener.OnUserDisconnected += ClientDisconnected;
        _connectionListener.OnServerStop += ServerStopped;
    }

    public void Start(string host = "192.168.0.104", int port = 3535)
    {
        _connectionListener.Start(host, port);
    }

    public void Stop()
    {
        _connectionListener.Stop();

        lock (_clientsLock)
        {
            foreach (var client in _clients.Keys)
            {
                try
                {
                    client.Close();
                }
                catch { }
            }
            _clients.Clear();
        }
    }

    private void ClientConnected(TcpClient client)
    {
        OnLog?.Invoke(LogType.Info, $"Client connected {client.Client.RemoteEndPoint}");

        lock (_clientsLock)
        {
            _clients[client] = client.GetStream();
        }
        _ = Task.Run(() => ListenClientMessages(client));

        OnUserConnected?.Invoke(client);
    }

    private void ClientDisconnected(TcpClient client)
    {
        OnLog?.Invoke(LogType.Info, $"Client disconnected {client.Client.RemoteEndPoint}");
        lock (_clientsLock)
        {
            if (_clients.ContainsKey(client))
            {
                _clients[client].Close();
                client.Close();
                _clients.Remove(client);
            }
        }

        OnUserDisconnected?.Invoke(client);
    }

    private void ServerStopped()
    {
        OnLog?.Invoke(LogType.Info, "Server stopped event received");
    }

    private async Task ListenClientMessages(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new byte[4096];
        try
        {
            while (client.Connected)
            {
                if (!stream.CanRead) break;
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    // Client disconnected
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                OnLog?.Invoke(LogType.Info, $"Received from {client.Client.RemoteEndPoint}: {message}");

                // TODO: Обработка сообщения, логика сервера
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke(LogType.Fatal, $"Error reading from client {client.Client.RemoteEndPoint}: {ex.Message}");
        }
        finally
        {
            ClientDisconnected(client);
        }
    }

    public async Task SendMessageAsync(TcpClient client, string message)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrEmpty(message)) return;

        NetworkStream stream;
        lock (_clientsLock)
        {
            if (!_clients.TryGetValue(client, out stream) || !stream.CanWrite)
            {
                OnLog?.Invoke(LogType.Warning, $"Attempted to send message to disconnected client {client.Client.RemoteEndPoint}");
                return;
            }
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();

            OnLog?.Invoke(LogType.Info, $"Sent to {client.Client.RemoteEndPoint}: {message}");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke(LogType.Error, $"Error sending to client {client.Client.RemoteEndPoint}: {ex.Message}");
        }
    }
}

