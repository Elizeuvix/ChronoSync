
using UnityEngine;
using System;
using CS.Core.Config;

namespace CS.Core.Networking
{
    public class ChronoSyncRCPWebSocket : ElvixWebSocket
    {

        [Tooltip("ID do jogador (use o username do login)")]
        public string playerId = "player1";
        [Tooltip("Nome de exibição enviado ao servidor após login")]
        public string displayName = "";
        [Tooltip("ID atribuído pelo servidor a esta conexão")] public string assignedPlayerId = "";

        // Dispara quando a identidade está pronta para uso (id atribuído pelo servidor e, opcionalmente, displayName preenchido)
        public event Action<string, string> OnIdentityReady;
        // Conecta e identifica o jogador quando a conexão estiver ativa
        void Start()
        {
            // Append API key from developer-defined config (not PlayerPrefs)
            var apiKey = ChronoSyncConfig.API_KEY;
            if (!string.IsNullOrEmpty(apiKey) && !wsUrl.Contains("key="))
            {
                // Use System.Uri to escape without depending on UnityWebRequest module
                wsUrl += (wsUrl.Contains("?") ? "&" : "?") + "key=" + Uri.EscapeDataString(apiKey);
            }
            OnConnected += () =>
            {
                Debug.Log("ChronoSyncRCPWebSocket conectado!");
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
                Send($"{{\"event\":\"set_display_name\",\"display_name\":\"{escapedName}\"}}");
            }
            // Notificar ouvintes que a identidade está pronta (id + nome atual se houver)
            OnIdentityReady?.Invoke(assignedPlayerId, displayName);
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