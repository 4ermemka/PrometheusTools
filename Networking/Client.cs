using PrometheusTools.Shared.Abstract;
using PrometheusTools.Shared.Enums;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PrometheusTools.Shared.Networking
{
    public class Client : ILogableObject
    {
        public string Name { get; set; }
        public Action<LogType, string> OnLog { get; set; }

        public Action OnConnectedToServer;
        public Action OnConnectionToServerRefused;
        public Action OnDisconnectedFromServer;

        public Action<byte[]> OnReceiveMessage;

        public string ConnectionAddress;

        private TcpClient _client;
        private NetworkStream _stream;

        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;

        public Client()
        {
            Name = GetType().Name;

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            ConnectionAddress = $"EmptyConnectionAddress";
        }

        public void Connect(string ip, int port)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            try
            {
                OnLog.Invoke(LogType.Info, $"Connecting to server: {ip}:{port}");
                _client = new TcpClient();
                _client.Connect(ip, port);
                ConnectionAddress = $"{ip}:{port}";
                OnLog.Invoke(LogType.Info, $"Client connection complete, checking...");

                if (_client.Connected)
                {
                    _stream = _client.GetStream();
                    OnLog.Invoke(LogType.Info, $"Connected to server: {ConnectionAddress} Client connected {_client.Connected}, address: {_client.Client.LocalEndPoint}");
                    OnConnectedToServer?.Invoke();
                    StartListeningToServer();
                }
                else
                {
                    OnLog.Invoke(LogType.Error, $"Client NOT connected {_client.Connected}, address: {_client.Client.LocalEndPoint}");
                }
            }
            catch (Exception ex)
            {
                OnLog.Invoke(LogType.Fatal, $"{ex.Message}, {ex.StackTrace}");
                OnConnectionToServerRefused?.Invoke();
            }
        }

        public void SendMessage(byte[] bytes)
        {
            _stream.Write(bytes);
            OnLog.Invoke(LogType.Info, $"Sending {_client?.Client.RemoteEndPoint} bytes [{bytes.Length}]");
        }

        public void Stop()
        {
            _client?.Close();
            _client?.Dispose();
            OnLog.Invoke(LogType.Info, $"Client stopped");
            OnDisconnectedFromServer?.Invoke();
            ConnectionAddress = $"EmptyConnectionAddress";
        }

        private void StartListeningToServer()
        {
            Task.Run(() =>
            {
                OnLog.Invoke(LogType.Info, $"Starting reading messages from server {_client.Client.RemoteEndPoint}");
                while (!_cancellationTokenSource.IsCancellationRequested && _client.Connected)
                {
                    OnLog.Invoke(LogType.Info, $"Reading...");
                    try
                    {
                        if (_stream.CanRead)
                        {
                            byte[] myReadBuffer = new byte[4096];
                            StringBuilder myCompleteMessage = new StringBuilder();
                            int numberOfBytesRead = 0;
                            do
                            {
                                numberOfBytesRead = _stream.Read(myReadBuffer, 0, myReadBuffer.Length);
                            }
                            while (_stream.DataAvailable);
                            OnLog.Invoke(LogType.Info, $"{numberOfBytesRead} bytes received");
                            OnReceiveMessage?.Invoke(myReadBuffer);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLog.Invoke(LogType.Fatal, $"Disconnection due to: {ex.Message}");
                        _cancellationTokenSource.Cancel();
                        Stop();
                    }
                }
            }, _cancellationToken);
        }
    }
}