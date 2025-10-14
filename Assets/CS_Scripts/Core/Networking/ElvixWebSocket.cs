
using System;
using System.Text;
using UnityEngine;
#if UNITY_STANDALONE || UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace CS.Core.Networking
{
    public class ElvixWebSocket : MonoBehaviour
    {
    public string wsUrl = "ws://192.168.3.196:8000/ws/game";
        public bool IsConnected { get; private set; }
        public bool AutoReconnect = true;

#if UNITY_STANDALONE || UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellation;
    private bool disposed = false;
    private bool reconnectScheduled = false;
    private int reconnectDelayMs = 1000;
    private const int reconnectMaxDelayMs = 10000;
#endif

        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        public void Connect()
        {
#if UNITY_STANDALONE || UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        ConnectStandalone();
#else
            Debug.LogWarning("ElvixWebSocket: WebSocket nativo só funciona em Standalone/Editor Desktop. Para WebGL ou Mobile, implemente uma solução específica.");
            OnError?.Invoke("WebSocket não suportado nesta plataforma.");
#endif
        }

#if UNITY_STANDALONE || UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
    public async void ConnectStandalone()
    {
        webSocket = new ClientWebSocket();
        // Envia pings periódicos para manter a conexão viva (evita timeouts ociosos)
        try { webSocket.Options.KeepAliveInterval = System.TimeSpan.FromSeconds(30); } catch {}
        cancellation = new CancellationTokenSource();
        try
        {
            await webSocket.ConnectAsync(new Uri(wsUrl), cancellation.Token);
            IsConnected = true;
            reconnectDelayMs = 1000; // reset backoff
            OnConnected?.Invoke();
            ReceiveLoop();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex.Message);
            TryScheduleReconnect();
        }
    }

    public async void Send(string message)
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(message);
        var buffer = new ArraySegment<byte>(bytes);
        await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellation.Token);
    }

    private async void ReceiveLoop()
    {
        var buffer = new byte[4096];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellation.Token);
                    IsConnected = false;
                    OnDisconnected?.Invoke();
                    TryScheduleReconnect();
                }
                else
                {
                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    OnMessageReceived?.Invoke(msg);
                }
            }
        }
        catch (Exception ex)
        {
            // Muitos servidores fecham abruptamente sem handshake completo; trate como desconexão normal
            var em = ex.Message ?? string.Empty;
            if (em.IndexOf("closed the WebSocket connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                em.IndexOf("The remote party closed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                IsConnected = false;
                OnDisconnected?.Invoke();
                TryScheduleReconnect();
            }
            else
            {
                OnError?.Invoke(ex.Message);
                TryScheduleReconnect();
            }
        }
    }

    public async void Disconnect()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", cancellation.Token);
            IsConnected = false;
            OnDisconnected?.Invoke();
        }
        cancellation?.Cancel();
        disposed = true;
    }
#else
        public void Send(string message)
        {
            Debug.LogWarning("ElvixWebSocket: Send não suportado nesta plataforma.");
        }
        public void Disconnect()
        {
            Debug.LogWarning("ElvixWebSocket: Disconnect não suportado nesta plataforma.");
        }
#endif

        private void OnDestroy()
        {
            Disconnect();
        }

        private async void TryScheduleReconnect()
        {
            if (!AutoReconnect || disposed || reconnectScheduled) return;
            reconnectScheduled = true;
            try
            {
                await System.Threading.Tasks.Task.Delay(reconnectDelayMs);
                if (!disposed && !IsConnected)
                {
                    ConnectStandalone();
                    reconnectDelayMs = Math.Min(reconnectDelayMs * 2, reconnectMaxDelayMs);
                }
            }
            finally
            {
                reconnectScheduled = false;
            }
        }
    }
}