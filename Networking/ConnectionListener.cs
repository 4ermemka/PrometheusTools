using PrometheusTools.Shared.Abstract;
using PrometheusTools.Shared.Enums;
using System.Net;
using System.Net.Sockets;

namespace PrometheusTools.Shared.Networking
{ 
    public class ConnectionListener : ILogableObject
    {
        public string Name { get; set; }
        public Action<LogType, string> OnLog {  get; set; }
    
        public Action OnServerStop;
        public Action<TcpClient> OnUserConnected;
        public Action<TcpClient> OnUserDisconnected;
    
        private TcpListener _listener;
        private string _host;
        private int _port = 3535;
        private CancellationTokenSource _listenNewClientsCancellationTokenSource;
        private CancellationToken _listenNewClientsCancellationToken;
    
    
        public ConnectionListener() 
        {
            Name = GetType().Name;
    
            _listenNewClientsCancellationTokenSource = new CancellationTokenSource();
            _listenNewClientsCancellationToken = _listenNewClientsCancellationTokenSource.Token;
        }
    
        public void Start()
        {
            _listener = new TcpListener(IPAddress.Parse("192.168.0.104"), _port);
            try
            {
                _listener.Start();
                OnLog.Invoke(LogType.Info, $"Server on {_listener.Server.LocalEndPoint} started");
                StartAcceptingNewConnections();
            }
            catch (Exception ex)
            {
                OnLog.Invoke(LogType.Fatal, $"{ex.Message}, {ex.StackTrace}");
            }
        }
    
        public void Start(string host, int port)
        {
            _host = host;
            _port = port;
    
            _listener = new TcpListener(IPAddress.Parse(_host), _port);
            try
            {
                _listener.Start();
                OnLog.Invoke(LogType.Info, $"Server on {_listener.Server.LocalEndPoint} started");
                StartAcceptingNewConnections();
            }
            catch (Exception ex)
            {
                OnLog.Invoke(LogType.Fatal, $"{ex.Message}, {ex.StackTrace}");
            }
        }
    
        public void Stop()
        {
            _listenNewClientsCancellationTokenSource.Cancel();
            _listener?.Stop();
    
            OnServerStop?.Invoke();
    
            OnLog.Invoke(LogType.Info, $"Server stopped");
        }
        
        private void StartAcceptingNewConnections()
        {
            Task.Run(() =>
            {
                while (!_listenNewClientsCancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        OnLog.Invoke(LogType.Info, $"Accepting new clients...");
                        var client = _listener.AcceptTcpClient();
                        OnLog.Invoke(LogType.Info, $"Accepted client : {client.Client.RemoteEndPoint}");
    
                        OnUserConnected?.Invoke(client);
    
                    }
                    catch (Exception ex) 
                    { 
                        OnLog.Invoke(LogType.Fatal, $"{ex.Message}, {ex.StackTrace}");
                    }
                }
                OnLog.Invoke(LogType.Info, $"Accepting new clients ended");
            }, _listenNewClientsCancellationToken);
        }
    }
}