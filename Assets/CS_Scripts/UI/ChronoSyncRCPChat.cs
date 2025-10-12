using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using CS.Core.Config;
using CS.Core.Systems;
using CS.Core.Networking;
using CS.Core.Identity;

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
        [SerializeField] private TMP_InputField privateTargetID; // username alvo para privado (opcional)
        [SerializeField] private TMP_Dropdown membersDropdown;
        private InputField privateTargetLegacy;
        [SerializeField] private GameObject privateTargetContainer; // opcional: container para mostrar/esconder
        [SerializeField] private TMP_Text channelStatusText; // opcional: exibe canal atual
        [Header("Status (opcional)")]
        [SerializeField] private TMP_Text connectionStatusText;
        [SerializeField] private TMP_Text lobbyStatusText;

        [SerializeField] private ChronoSyncRCPWebSocket webSocket;
    private ChronoSyncCore core;
    private GameSessionManager gsm;
    private string currentLobby;
        private List<string> messages = new List<string>();
        [Header("Histórico")]
        [SerializeField] [Range(10, 500)] private int maxMessages = 40;
    [Header("Cores do Chat")] 
    [SerializeField] private string colorSelf = "#000000";        // preto
    [SerializeField] private string colorAlly = "#1E3A8A";        // azul escuro
    [SerializeField] private string colorEnemy = "#8B0000";       // vermelho escuro
    [SerializeField] private string colorGlobal = "#FF8C00";      // laranja escuro
    [SerializeField] private string colorPrivate = "#6A0DAD";     // roxo para mensagens privadas
        private enum ChatChannel { Global = 0, Room = 1, Privado = 2 }
        private ChatChannel currentChannel = ChatChannel.Room;
    // Cache for dropdown mapping: display names -> player ids (same index)
    private List<string> _dropdownIds = new List<string>();

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
            if (privateTargetID != null)
                privateTargetID.onValueChanged.AddListener(_ => { SaveLastPrivateTarget(); UpdateSendInteractable(); });
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

            // Auto-bind privateTargetID if missing
            if (privateTargetID == null)
            {
                Transform pt = null;
                if (privateTargetContainer != null)
                {
                    pt = privateTargetContainer.transform.Find("PrivateTargetID") ?? privateTargetContainer.transform.GetComponentInChildren<TMPro.TMP_InputField>(true)?.transform;
                }
                if (pt == null)
                {
                    pt = transform.Find("PrivateTargetID");
                }
                if (pt != null)
                {
                    privateTargetID = pt.GetComponent<TMPro.TMP_InputField>();
                }
            }

            // Resolve Core/GSM and subscribe to member changes for live dropdown updates
            core = ChronoSyncCore.Instance != null ? ChronoSyncCore.Instance : FindObjectOfType<ChronoSyncCore>(true);
            if (core != null)
            {
                core.OnMemberJoined += OnCoreMemberEvent;
                core.OnMemberLeft += OnCoreMemberEvent;
            }
            gsm = GameSessionManager.Instance != null ? GameSessionManager.Instance : FindObjectOfType<GameSessionManager>(true);
            if (gsm != null)
            {
                gsm.OnLobbyMembersChanged += OnGsmMembersChanged;
            }

            MembersDropdownUpdate(GetPlayerListByNames());

            RefreshChannelUI();
            UpdateSendInteractable();
            UpdateStatuses();
        }

        // Build member names list from Core if available, otherwise fallback to GSM
        private List<string> GetPlayerListByNames()
        {
            var names = new List<string>();
            if (core == null)
            {
                core = ChronoSyncCore.Instance != null ? ChronoSyncCore.Instance : FindObjectOfType<ChronoSyncCore>(true);
            }
            if (core != null)
            {
                try
                {
                    var list = core.GetPlayerListByName();
                    if (list != null && list.Count > 0) names.AddRange(list);
                }
                catch { }
            }
            if (names.Count == 0)
            {
                if (gsm == null) gsm = GameSessionManager.Instance != null ? GameSessionManager.Instance : FindObjectOfType<GameSessionManager>(true);
                if (gsm != null && gsm.lobbyMemberDisplayNames != null && gsm.lobbyMemberDisplayNames.Count > 0)
                {
                    names.AddRange(gsm.lobbyMemberDisplayNames);
                }
            }
            return names;
        }

        private void OnCoreMemberEvent(string id)
        {
            MembersDropdownUpdate(GetPlayerListByNames());
        }

        private void OnCoreMemberEvent(string id, string displayName)
        {
            MembersDropdownUpdate(GetPlayerListByNames());
        }

        private void OnGsmMembersChanged(List<string> ids, List<string> names)
        {
            MembersDropdownUpdate(GetPlayerListByNames());
        }

        private void MembersDropdownUpdate(List<string> memberNames){
            if (membersDropdown == null) return;
            membersDropdown.ClearOptions();
            if (memberNames == null || memberNames.Count == 0) return;
            // Build aligned ids list from Core or GSM to map selection to player_id
            _dropdownIds.Clear();
            if (core != null)
            {
                try
                {
                    var ids = core.GetPlayerListById();
                    if (ids != null) _dropdownIds.AddRange(ids);
                }
                catch { }
            }
            if (_dropdownIds.Count == 0 && gsm != null && gsm.lobbyMemberIds != null)
            {
                _dropdownIds.AddRange(gsm.lobbyMemberIds);
            }
            membersDropdown.AddOptions(memberNames);
            membersDropdown.onValueChanged.RemoveAllListeners();
            membersDropdown.onValueChanged.AddListener(index =>
            {
                if (index >= 0 && index < memberNames.Count)
                {
                    string selectedId = (index >= 0 && index < _dropdownIds.Count) ? _dropdownIds[index] : null;
                    if (!string.IsNullOrEmpty(selectedId))
                    {
                        // Fill privateTargetID with the player ID instead of display name
                        SetPrivateTarget(selectedId);
                        SaveLastPrivateTarget();
                        UpdateSendInteractable();
                    }
                }
            });
            // Also update the target immediately based on current selection (if any)
            var curIdx = membersDropdown.value;
            if (curIdx >= 0 && curIdx < _dropdownIds.Count)
            {
                var selectedId = _dropdownIds[curIdx];
                if (!string.IsNullOrEmpty(selectedId)) SetPrivateTarget(selectedId);
            }
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
            // Preserve history by default
            SetLobby(lobbyName, preserveHistory: true);
        }

        // Overload to control whether chat history should be cleared when changing lobby
        public void SetLobby(string lobbyName, bool preserveHistory)
        {
            bool leaving = string.IsNullOrEmpty(lobbyName);
            currentLobby = lobbyName;
            // Do not clear history — keep messages while player is online
            // Only append a system note when joining a lobby or explicitly noting leave without clearing
            if (!leaving)
            {
                AppendSystem($"Entrou no lobby: {lobbyName}");
            }
            else if (preserveHistory)
            {
                AppendSystem("Saiu do lobby.");
            }
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
                    var senderId = ExtractSenderIdFromJson(msg);
                    var color = GetSenderColorHex(senderId, isGlobal:false);
                    var line = $"<color={color}>[{timestamp}] {player}: {message}</color>";
                    messages.Insert(0, line);
                    EnforceHistoryLimit();
                    if (chatHistoryText != null)
                        chatHistoryText.text = string.Join("\n", messages);
                }
            }
            else if (msg.Contains("\"event\":\"chat_message_global\""))
            {
                if (TryExtractChatMessage(msg, out var player, out var message, out var timestamp))
                {
                    var color = GetSenderColorHex(null, isGlobal:true);
                    var line = $"<color={color}>[Geral] [{timestamp}] {player}: {message}</color>";
                    messages.Insert(0, line);
                    EnforceHistoryLimit();
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
                    // Mensagens privadas usam SEMPRE a cor privada, independente da relação
                    var line = $"<color={colorPrivate}>[Privado] [{timestamp}] {fromDisplay}: {message}</color>";
                    messages.Insert(0, line);
                    EnforceHistoryLimit();
                    if (chatHistoryText != null)
                        chatHistoryText.text = string.Join("\n", messages);
                }
            }
            else if (msg.Contains("\"event\":\"chat_history\""))
            {
                // Preserve existing history; optionally merge server history here if needed
                AppendSystem("Histórico do chat atualizado.");
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
            messages.Insert(0, line);
            EnforceHistoryLimit();
            if (chatHistoryText != null)
                chatHistoryText.text = string.Join("\n", messages);
        }

        private void EnforceHistoryLimit()
        {
            if (maxMessages <= 0) return;
            if (messages.Count > maxMessages)
            {
                // messages are newest-first, so remove the tail (oldest entries)
                messages.RemoveRange(maxMessages, messages.Count - maxMessages);
            }
        }

        private string ExtractSenderIdFromJson(string json)
        {
            int keyIdx = json.IndexOf("\"message\":{", System.StringComparison.Ordinal);
            if (keyIdx == -1) return ExtractJsonValue(json, "player_id");
            int objStart = json.IndexOf('{', keyIdx);
            if (objStart == -1) return string.Empty;
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
            if (depth != 0) return string.Empty;
            string obj = json.Substring(objStart, i - objStart);
            return ExtractJsonValue(obj, "player_id");
        }

        private string GetSenderColorHex(string senderId, bool isGlobal)
        {
            if (isGlobal) return colorGlobal;
            // Determine local id
            string localId = null;
            if (webSocket != null)
                localId = string.IsNullOrEmpty(webSocket.assignedPlayerId) ? webSocket.playerId : webSocket.assignedPlayerId;
            if (string.IsNullOrEmpty(localId) && GameSessionManager.Instance != null)
                localId = GameSessionManager.Instance.localPlayerId;

            if (!string.IsNullOrEmpty(senderId) && !string.IsNullOrEmpty(localId) && string.Equals(senderId, localId, System.StringComparison.Ordinal))
                return colorSelf;

            // Try to determine relation via PlayerIdentity teams
            var localIdentity = PlayerIdentity.Local;
            PlayerIdentity other = null;
            if (!string.IsNullOrEmpty(senderId))
            {
                // Try PlayerSpawner lookup first
                var spawner = GameSessionManager.Instance != null ? GameSessionManager.Instance.spawner : null;
                if (spawner != null && spawner.TryGetSpawned(senderId, out var go) && go != null)
                {
                    other = go.GetComponentInChildren<PlayerIdentity>();
                }
                if (other == null)
                {
                    // Fallback: scan scene for PlayerIdentity with matching id
                    var all = GameObject.FindObjectsOfType<PlayerIdentity>(true);
                    for (int k = 0; k < all.Length; k++)
                    {
                        if (all[k] != null && all[k].PlayerId == senderId) { other = all[k]; break; }
                    }
                }
            }

            if (localIdentity != null && other != null)
            {
                if (localIdentity.Team != Team.Neutral && other.Team != Team.Neutral)
                {
                    if (localIdentity.Team == other.Team) return colorAlly;
                    return colorEnemy;
                }
            }

            // Fallback color for others when relation unknown
            return colorAlly;
        }

        // Public wrapper to allow other components to append system messages
        public void AppendSystemMessage(string msg)
        {
            AppendSystem(msg);
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
            if (privateTargetID != null) return privateTargetID.text;
            if (privateTargetLegacy != null) return privateTargetLegacy.text;
            return string.Empty;
        }

        private void SetPrivateTarget(string value)
        {
            if (privateTargetID != null) privateTargetID.text = value;
            if (privateTargetLegacy != null) privateTargetLegacy.text = value;
        }

        private void OnChannelChanged(int value)
        {
            if (value < 0 || value > 2) value = 1; // Sala como fallback
            currentChannel = (ChatChannel)value;
            RefreshChannelUI();
            UpdateSendInteractable();
            UpdateStatuses();
            // If switching to Private, prefill target with current dropdown selection
            if (currentChannel == ChatChannel.Privado && membersDropdown != null && _dropdownIds != null)
            {
                var idx = membersDropdown.value;
                if (idx >= 0 && idx < _dropdownIds.Count)
                {
                    var selectedId = _dropdownIds[idx];
                    if (!string.IsNullOrEmpty(selectedId)) SetPrivateTarget(selectedId);
                }
            }
        }

        private void RefreshChannelUI()
        {
            bool showPrivate = currentChannel == ChatChannel.Privado;
            if (privateTargetContainer != null) privateTargetContainer.SetActive(showPrivate);
            else
            {
                // Se não tem container, mostrar/ocultar os campos diretamente
                if (privateTargetID != null) privateTargetID.gameObject.SetActive(showPrivate);
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
            if (core != null)
            {
                core.OnMemberJoined -= OnCoreMemberEvent;
                core.OnMemberLeft -= OnCoreMemberEvent;
            }
            if (gsm != null)
            {
                gsm.OnLobbyMembersChanged -= OnGsmMembersChanged;
            }
        }
    }
}
