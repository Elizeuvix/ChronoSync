
using UnityEngine;
using System;
using CS.Core.Config;
using CS.Core.Auth;

namespace CS.Core.Networking
{
    public class ChronoSyncRCPWebSocket : ElvixWebSocket
    {

        [Tooltip("ID do jogador (use o username do login)")]
        public string playerId = "player1";
        [Tooltip("Nome de exibição enviado ao servidor após login")]
        public string displayName = "";
        [Tooltip("ID atribuído pelo servidor a esta conexão")] public string assignedPlayerId = "";

        [Header("Connection")]
        [Tooltip("Se true e wsUrl estiver vazio, será derivado do apiUrl do ChronoSyncRCPAuth (http->ws + /ws/game)")]
        public bool autoDeriveWsUrl = true;
        [Tooltip("Caminho do endpoint WS quando derivar automaticamente")] public string wsPath = "/ws/game";

    [Header("Security")]
    [Tooltip("Override para a API key do desenvolvedor usada no WebSocket (se vazio, usa ChronoSyncConfig.API_KEY)")]
    public string apiKeyOverride = "";

    [Header("Diagnostics")] public bool verboseLogging = true;
    private readonly System.Collections.Generic.Queue<string> lastSentQueue = new System.Collections.Generic.Queue<string>();
    private const int MaxBufferedMessages = 10;
    public string lastSentMessage = string.Empty;

        // Dispara quando a identidade está pronta para uso (id atribuído pelo servidor e, opcionalmente, displayName preenchido)
        public event Action<string, string> OnIdentityReady;
        // Conecta e identifica o jogador quando a conexão estiver ativa
        void Start()
        {
            // Derivar wsUrl do config global/ apiUrl do Auth quando não configurado explicitamente
            if (string.IsNullOrWhiteSpace(wsUrl) && !string.IsNullOrWhiteSpace(ChronoSyncConfig.WS_URL))
            {
                wsUrl = ChronoSyncConfig.WS_URL.Trim();
            }
            if (autoDeriveWsUrl && string.IsNullOrWhiteSpace(wsUrl))
            {
                var auth = FindObjectOfType<ChronoSyncRCPAuth>();
                if (auth != null && !string.IsNullOrWhiteSpace(auth.apiUrl))
                {
                    try
                    {
                        var uriHttp = new Uri(auth.apiUrl);
                        var scheme = uriHttp.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
                        var builder = new UriBuilder(uriHttp) { Scheme = scheme };
                        // UriBuilder mantém porta; adiciona caminho
                        var basePath = string.IsNullOrEmpty(builder.Path) || builder.Path == "/" ? string.Empty : builder.Path.TrimEnd('/');
                        builder.Path = basePath + (wsPath.StartsWith("/") ? wsPath : "/" + wsPath);
                        wsUrl = builder.Uri.ToString();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[WS] Falha ao derivar wsUrl de {auth.apiUrl}: {e.Message}");
                    }
                }
            }
            // Append API key from developer-defined config (not PlayerPrefs)
            var apiKey = string.IsNullOrEmpty(apiKeyOverride) ? ChronoSyncConfig.API_KEY : apiKeyOverride;
            if (!string.IsNullOrEmpty(apiKey) && !wsUrl.Contains("key="))
            {
                // Use System.Uri to escape without depending on UnityWebRequest module
                wsUrl += (wsUrl.Contains("?") ? "&" : "?") + "key=" + Uri.EscapeDataString(apiKey);
            }
            OnConnected += () =>
            {
                Debug.Log($"ChronoSyncRCPWebSocket conectado em {wsUrl}!");
                // Do not identify or send player-related events until API provides an id
            };
            OnMessageReceived += (msg) =>
            {
                Debug.Log($"Mensagem recebida: {msg}");
                if (msg.Contains("\"event\":\"player_connected\""))
                {
                    var id = ExtractJsonValue(msg, "player_id");
                    if (!string.IsNullOrEmpty(id))
                    {
                        assignedPlayerId = id;
                        // Assim que o servidor confirma o id, envie o displayName (se houver) e notifique ouvintes
                        TrySendDisplayNameAndEmit();
                    }
                }
            };
            OnError += (err) =>
            {
                Debug.LogError($"Erro WebSocket: {err}");
            };
            // Conecta automaticamente para permitir que a UI (Lobby/Chat) funcione assim que abrir
            Connect();
        }

        // Permite definir/atualizar o playerId após o login
        public void SetPlayerId(string id)
        {
            if (!string.IsNullOrEmpty(id))
                playerId = id;
            if (IsConnected)
            {
                IdentifyPlayer();
                // O displayName será enviado assim que o servidor atribuir o assignedPlayerId
            }
        }

        public void ConnectWithPlayerId(string id)
        {
            SetPlayerId(id);
            if (!IsConnected)
                Connect();
        }

        private void IdentifyPlayer()
        {
            var escaped = Escape(playerId);
            Send($"{{\"event\":\"player_connected\",\"player_id\":\"{escaped}\"}}");
        }

        private string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void SetDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            displayName = name;
            // Envie após assignedPlayerId estar disponível (ou aguarde evento player_connected)
            TrySendDisplayNameAndEmit();
        }

        private void TrySendDisplayNameAndEmit()
        {
            if (!IsConnected || string.IsNullOrEmpty(assignedPlayerId))
            {
                // Ainda não é possível enviar; aguarde confirmação do servidor
                return;
            }
            if (!string.IsNullOrEmpty(displayName))
            {
                var escapedName = Escape(displayName);
                SendLogged($"{{\"event\":\"set_display_name\",\"display_name\":\"{escapedName}\"}}");
            }
            // Notificar ouvintes que a identidade está pronta (id + nome atual se houver)
            OnIdentityReady?.Invoke(assignedPlayerId, displayName);
        }

        public new void Send(string message)
        {
            base.Send(message);
        }

        public void SendLogged(string message)
        {
            lastSentMessage = message;
            lastSentQueue.Enqueue(message);
            while (lastSentQueue.Count > MaxBufferedMessages) lastSentQueue.Dequeue();
            if (verboseLogging) Debug.Log($"[WS ->] {message}");
            base.Send(message);
        }

        public string GetRecentSentMessages()
        {
            return string.Join("\n", lastSentQueue.ToArray());
        }

        private string ExtractJsonValue(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\":");
            if (idx == -1) return "";
            int start = json.IndexOf("\"", idx + key.Length + 3);
            int end = json.IndexOf("\"", start + 1);
            if (start == -1 || end == -1) return "";
            return json.Substring(start + 1, end - start - 1);
        }

        void OnDestroy()
        {
            Disconnect();
        }
    }
}