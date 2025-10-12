using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using CS.Core.Config;
using CS.Core.Systems;
using CS.Core.Networking;

namespace CS.UI
{
    public class ChronoSyncRCPChat : MonoBehaviour
    {
        // Suporte às duas variantes de InputField (TMP e UGUI). Preencha uma delas no Inspector.
        [SerializeField] private TMP_InputField chatInputTMP;
        private InputField chatInput;
        public Button sendButton;
        public TMP_Text chatHistoryText;
        [Header("Canal do Chat")]
        [SerializeField] private TMP_Dropdown channelDropdown; // 0=Geral, 1=Sala, 2=Privado
        [SerializeField] private TMP_InputField privateTarget; // username alvo para privado (opcional)
        private InputField privateTargetLegacy;
        [SerializeField] private GameObject privateTargetContainer; // opcional: container para mostrar/esconder
        [SerializeField] private TMP_Text channelStatusText; // opcional: exibe canal atual
        [Header("Status (opcional)")]
        [SerializeField] private TMP_Text connectionStatusText;
        [SerializeField] private TMP_Text lobbyStatusText;

        [SerializeField] private ChronoSyncRCPWebSocket webSocket;
        private string currentLobby;
        private List<string> messages = new List<string>();
        private enum ChatChannel { Global = 0, Room = 1, Privado = 2 }
        private ChatChannel currentChannel = ChatChannel.Room;

        void Awake()
        {
            //webSocket = FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (sendButton != null)
                sendButton.onClick.AddListener(SendMessage);
            else
                Debug.LogWarning("[ChronoSyncRCPChat] 'sendButton' não atribuído no Inspector.");

            // Procurar um WebSocket já conectado na cena (inclui objetos inativos)
            webSocket = FindObjectOfType<ChronoSyncRCPWebSocket>(true);
            if (webSocket == null)
                webSocket = GetComponent<ChronoSyncRCPWebSocket>();
            if (webSocket != null)
            {
                webSocket.OnMessageReceived += OnWebSocketMessage;
                webSocket.OnConnected += () => { UpdateSendInteractable(); UpdateStatuses(); };
                webSocket.OnDisconnected += () => { UpdateSendInteractable(); AppendSystem("Desconectado do servidor."); UpdateStatuses(); };
                webSocket.OnError += (e) => AppendSystem($"Erro de conexão: {e}");
            }
            else
                Debug.LogError("[ChronoSyncRCPChat] ChronoSyncRCPWebSocket não encontrado.");

            // Bind canal (dropdown) e alvo privado para atualizar UI/estado
            if (channelDropdown != null)
            {
                channelDropdown.onValueChanged.AddListener(OnChannelChanged);
                // Garantir valor dentro do enum
                if (channelDropdown.value < 0 || channelDropdown.value > 2) channelDropdown.value = 1; // Sala por padrão
                currentChannel = (ChatChannel)channelDropdown.value;
            }
            if (privateTarget != null)
                privateTarget.onValueChanged.AddListener(_ => { SaveLastPrivateTarget(); UpdateSendInteractable(); });
            if (privateTargetLegacy != null)
                privateTargetLegacy.onValueChanged.AddListener(_ => { SaveLastPrivateTarget(); UpdateSendInteractable(); });

            // Auto-bind channelStatusText por nome se não setado
            if (channelStatusText == null)
            {
                var t = transform.Find("ChannelStatusText");
                if (t != null) channelStatusText = t.GetComponent<TMPro.TMP_Text>();
            }

            // Carregar último alvo privado salvo
            LoadLastPrivateTarget();

            // Auto-bind status labels by name if not set
            if (connectionStatusText == null)
            {
                var t = transform.Find("ConnectionStatusText");
                if (t != null) connectionStatusText = t.GetComponent<TMPro.TMP_Text>();
            }
            if (lobbyStatusText == null)
            {
                var t = transform.Find("LobbyStatusText");
                if (t != null) lobbyStatusText = t.GetComponent<TMPro.TMP_Text>();
            }

            RefreshChannelUI();
            UpdateSendInteractable();
            UpdateStatuses();
        }

        void OnEnable()
        {
            // Atualiza estado do botão ao reativar o painel
            RefreshChannelUI();
            UpdateSendInteractable();
            UpdateStatuses();
        }

        public void SetLobby(string lobbyName)
        {
            currentLobby = lobbyName;
            messages.Clear();
            if (chatHistoryText != null) chatHistoryText.text = "";
            AppendSystem($"Entrou no lobby: {lobbyName}");
            UpdateSendInteractable();
            UpdateStatuses();
        }

        private void SendMessage()
        {
            string text = GetChatText();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (webSocket == null || !webSocket.IsConnected)
            {
                AppendSystem("Não conectado ao servidor. Verifique a conexão.");
                UpdateSendInteractable();
                return;
            }
            switch (currentChannel)
            {
                case ChatChannel.Global:
                    webSocket.Send($"{{\"event\":\"chat_message_global\",\"message\":\"{EscapeForJson(text)}\"}}");
                    break;
                case ChatChannel.Room:
                    if (string.IsNullOrEmpty(currentLobby))
                    {
                        AppendSystem("Você precisa entrar em um lobby antes de enviar mensagens de sala.");
                        UpdateSendInteractable();
                        return;
                    }
                    webSocket.Send($"{{\"event\":\"chat_message\",\"lobby\":\"{currentLobby}\",\"message\":\"{EscapeForJson(text)}\"}}");
                    break;
                case ChatChannel.Privado:
                    var to = GetPrivateTarget();
                    if (string.IsNullOrEmpty(to))
                    {
                        AppendSystem("Defina o destinatário (username) para chat privado.");
                        return;
                    }
                    webSocket.Send($"{{\"event\":\"private_message\",\"to\":\"{EscapeForJson(to)}\",\"message\":\"{EscapeForJson(text)}\"}}");
                    break;
            }
            SetChatText("");
        }

        private void OnWebSocketMessage(string msg)
        {
            if (msg.Contains("\"event\":\"chat_message\""))
            {
                if (TryExtractChatMessage(msg, out var player, out var message, out var timestamp))
                {
                    messages.Add($"[{timestamp}] {player}: {message}");
                    if (chatHistoryText != null)
                        chatHistoryText.text = string.Join("\n", messages);
                }
            }
            else if (msg.Contains("\"event\":\"chat_message_global\""))
            {
                if (TryExtractChatMessage(msg, out var player, out var message, out var timestamp))
                {
                    messages.Add($"[Geral] [{timestamp}] {player}: {message}");
                    if (chatHistoryText != null)
                        chatHistoryText.text = string.Join("\n", messages);
                }
            }
            else if (msg.Contains("\"event\":\"private_message\""))
            {
                string from = ExtractJsonValue(msg, "from");
                string fromDisplay = ResolveDisplayName(from);
                if (TryExtractChatMessage(msg, out var player, out var message, out var timestamp))
                {
                    messages.Add($"[Privado] [{timestamp}] {fromDisplay}: {message}");
                    if (chatHistoryText != null)
                        chatHistoryText.text = string.Join("\n", messages);
                }
            }
            else if (msg.Contains("\"event\":\"chat_history\""))
            {
                messages.Clear();
                if (chatHistoryText != null)
                    chatHistoryText.text = "";
                // Aqui você pode adaptar para extrair o array de mensagens do JSON recebido
                AppendSystem("Histórico do chat carregado.");
            }
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

        private string GetChatText()
        {
            if (chatInputTMP != null) return chatInputTMP.text;
            if (chatInput != null) return chatInput.text;
            Debug.LogWarning("[ChronoSyncRCPChat] Nenhum campo de chat (TMP_InputField ou InputField) atribuído.");
            return string.Empty;
        }

        private void SetChatText(string value)
        {
            if (chatInputTMP != null) chatInputTMP.text = value;
            if (chatInput != null) chatInput.text = value;
        }

        private bool TryExtractChatMessage(string json, out string player, out string message, out string timestamp)
        {
            player = message = timestamp = "";
            int keyIdx = json.IndexOf("\"message\":{", System.StringComparison.Ordinal);
            if (keyIdx == -1) return false;
            int objStart = json.IndexOf('{', keyIdx);
            if (objStart == -1) return false;
            int depth = 0;
            int i = objStart;
            for (; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }
            if (depth != 0) return false;
            string obj = json.Substring(objStart, i - objStart);
            player = ExtractJsonValue(obj, "player_id");
            message = ExtractJsonValue(obj, "message");
            timestamp = ExtractJsonValue(obj, "timestamp");
            // Preferir display_name quando presente; se não, resolver via GameSessionManager/WebSocket
            string disp = ExtractJsonValue(obj, "display_name");
            if (!string.IsNullOrEmpty(disp)) player = disp; else if (!string.IsNullOrEmpty(player)) player = ResolveDisplayName(player);
            return !string.IsNullOrEmpty(message);
        }

        private string ResolveDisplayName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return playerId;
            // Tentar GameSessionManager
            var gsm = GameSessionManager.Instance != null ? GameSessionManager.Instance : FindObjectOfType<GameSessionManager>();
            if (gsm != null)
            {
                if (!string.IsNullOrEmpty(gsm.localPlayerId) && playerId == gsm.localPlayerId && !string.IsNullOrWhiteSpace(gsm.localNickname))
                    return gsm.localNickname;
                int idx = gsm.lobbyMemberIds.IndexOf(playerId);
                if (idx >= 0 && idx < gsm.lobbyMemberDisplayNames.Count)
                {
                    var name = gsm.lobbyMemberDisplayNames[idx];
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            // Tentar WebSocket para o local
            var ws = webSocket != null ? webSocket : FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (ws != null && playerId == ws.assignedPlayerId && !string.IsNullOrWhiteSpace(ws.displayName))
                return ws.displayName;
            return playerId; // fallback
        }

        private string EscapeForJson(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private void AppendSystem(string msg)
        {
            var line = $"[system] {msg}";
            messages.Add(line);
            if (chatHistoryText != null)
                chatHistoryText.text = string.Join("\n", messages);
        }

        private void UpdateSendInteractable()
        {
            if (sendButton != null)
            {
                bool canSend = webSocket != null && webSocket.IsConnected;
                if (currentChannel == ChatChannel.Room)
                    canSend &= !string.IsNullOrEmpty(currentLobby);
                if (currentChannel == ChatChannel.Privado)
                    canSend &= !string.IsNullOrEmpty(GetPrivateTarget());
                sendButton.interactable = canSend;
            }
        }

        private string GetPrivateTarget()
        {
            if (privateTarget != null) return privateTarget.text;
            if (privateTargetLegacy != null) return privateTargetLegacy.text;
            return string.Empty;
        }

        private void SetPrivateTarget(string value)
        {
            if (privateTarget != null) privateTarget.text = value;
            if (privateTargetLegacy != null) privateTargetLegacy.text = value;
        }

        private void OnChannelChanged(int value)
        {
            if (value < 0 || value > 2) value = 1; // Sala como fallback
            currentChannel = (ChatChannel)value;
            RefreshChannelUI();
            UpdateSendInteractable();
            UpdateStatuses();
        }

        private void RefreshChannelUI()
        {
            bool showPrivate = currentChannel == ChatChannel.Privado;
            if (privateTargetContainer != null) privateTargetContainer.SetActive(showPrivate);
            else
            {
                // Se não tem container, mostrar/ocultar os campos diretamente
                if (privateTarget != null) privateTarget.gameObject.SetActive(showPrivate);
                if (privateTargetLegacy != null) privateTargetLegacy.gameObject.SetActive(showPrivate);
            }

            if (channelStatusText != null)
            {
                string label = currentChannel == ChatChannel.Global ? "Global" : currentChannel == ChatChannel.Room ? "Room" : "Private";
                channelStatusText.text = $"Channel: {label}";
            }
        }

        private void SaveLastPrivateTarget()
        {
            string key = GetLastPrivateTargetKey();
            string value = GetPrivateTarget();
            if (!string.IsNullOrEmpty(key))
                PlayerPrefs.SetString(key, value ?? string.Empty);
        }

        private void LoadLastPrivateTarget()
        {
            string key = GetLastPrivateTargetKey();
            if (!string.IsNullOrEmpty(key) && PlayerPrefs.HasKey(key))
            {
                SetPrivateTarget(PlayerPrefs.GetString(key, ""));
            }
        }

        private string GetLastPrivateTargetKey()
        {
            string pid = webSocket != null ? webSocket.playerId : "default";
            return $"Chat_LastPrivateTarget_{pid}";
        }

        private void UpdateStatuses()
        {
            if (connectionStatusText != null)
                connectionStatusText.text = webSocket != null && webSocket.IsConnected ? "Conectado" : "Desconectado";
            if (lobbyStatusText != null)
                lobbyStatusText.text = string.IsNullOrEmpty(currentLobby) ? "Lobby: (nenhum)" : $"Lobby: {currentLobby}";
        }

        void OnDestroy()
        {
            if (webSocket != null)
                webSocket.OnMessageReceived -= OnWebSocketMessage;
        }
    }
}
