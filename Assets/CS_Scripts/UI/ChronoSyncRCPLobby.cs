using UnityEngine;
using System;
using TMPro;
using UnityEngine.UI;
using CS.Core.Networking;
using CS.Core.Systems;

//[RequireComponent(typeof(ChronoSyncRCPWebSocket))]
namespace CS.UI
{
    public class ChronoSyncRCPLobby : MonoBehaviour
    {
        // Suporte às duas variantes de InputField (TMP e UGUI). Preencha uma delas no Inspector.
        [SerializeField] private TMP_InputField lobbyNameTMP;
        private InputField lobbyNameInput;
        [Header("Configuração do Lobby")]
    [SerializeField] private TMP_InputField maxPlayersTMP; // opcional: campo numérico no UI para capacidade (TMP)
    [SerializeField] private InputField maxPlayersInput;   // opcional: alternativa UGUI InputField
        [Range(2, 500)] public int maxPlayers = 2; // valor padrão se não houver campo
        public TMP_Text statusText;
        public ChronoSyncRCPWebSocket webSocket;
        public Button createLobbyButton;
        public Button cancelLobbyButton;

        [SerializeField] private Transform lobbyListContainer;
        [SerializeField] private GameObject lobbyListItemPrefab;
        [Header("UI Containers")]
        [SerializeField] private GameObject lobbyBrowserRoot; // Painel com lista/criação de lobby
        [SerializeField] private ChronoSyncRCPLobbyMembersPanel lobbyMembersPanel; // Painel de membros (pode estar inativo)
    [SerializeField] private Message messageUI; // Optional: assign if Message UI lives elsewhere

        private bool isHost = false;
        private bool playerJoined = false;
        private System.Collections.Generic.List<string> availableLobbies = new System.Collections.Generic.List<string>();
        private ChronoSyncRCPLobbyMembersPanel membersPanel;
        private string currentLobbyName = string.Empty;
    // Track list items and member counts for real-time updates
    private System.Collections.Generic.Dictionary<string, LobbyListItem> lobbyItems = new System.Collections.Generic.Dictionary<string, LobbyListItem>(System.StringComparer.Ordinal);
    private System.Collections.Generic.Dictionary<string, int> lobbyCounts = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
    private System.Collections.Generic.Dictionary<string, int> lobbyMaxPlayers = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);

        // Expor status para outros componentes (somente leitura)
        public bool IsHost => isHost;
        public string CurrentLobby => currentLobbyName;
    private ChronoSyncCore coreRef;

        private void Awake()
        {
            // Garantir componente WebSocket
            if (webSocket == null)
                webSocket = GetComponent<ChronoSyncRCPWebSocket>();

            // Proteger contra referências nulas no Inspector
            if (createLobbyButton != null)
                createLobbyButton.onClick.AddListener(CreateLobby);
            else
                Debug.LogWarning("[ChronoSyncRCPLobby] 'createLobbyButton' não atribuído no Inspector.");

            if (cancelLobbyButton != null)
                cancelLobbyButton.onClick.AddListener(CancelLobby);
            else
                Debug.LogWarning("[ChronoSyncRCPLobby] 'cancelLobbyButton' não atribuído no Inspector.");

            ClearLobbyListUI();
            // Encontrar painel de membros (mesmo se estiver inativo)
            if (lobbyMembersPanel != null)
                membersPanel = lobbyMembersPanel;
            else
                membersPanel = FindObjectOfType<ChronoSyncRCPLobbyMembersPanel>(true);

            // Core reference for room state
            coreRef = ChronoSyncCore.Instance != null ? ChronoSyncCore.Instance : FindObjectOfType<ChronoSyncCore>(true);
        }

        private void OnEnable()
        {
            if (webSocket == null)
                webSocket = GetComponent<ChronoSyncRCPWebSocket>();
            // Subscribe once when enabled
            if (webSocket != null)
            {
                webSocket.OnMessageReceived -= OnWebSocketMessage;
                webSocket.OnMessageReceived += OnWebSocketMessage;
                // Requisitar lista assim que conectar, para evitar perder a mensagem se ainda não estiver conectado
                webSocket.OnConnected -= OnWsConnected;
                webSocket.OnConnected += OnWsConnected;
                // Ask server for current lobbies so UI populates immediately
                if (webSocket.IsConnected)
                    webSocket.Send("{\"event\":\"request_lobby_list\"}");
            }

            // Subscribe to Core room-state changes
            if (coreRef == null) coreRef = ChronoSyncCore.Instance != null ? ChronoSyncCore.Instance : FindObjectOfType<ChronoSyncCore>(true);
            if (coreRef != null)
            {
                coreRef.OnIsInRoomChanged -= HandleIsInRoomChanged;
                coreRef.OnIsInRoomChanged += HandleIsInRoomChanged;
            }
        }

        private void OnDisable()
        {
            if (webSocket != null)
            {
                webSocket.OnMessageReceived -= OnWebSocketMessage;
                webSocket.OnConnected -= OnWsConnected;
            }
            if (coreRef != null)
            {
                coreRef.OnIsInRoomChanged -= HandleIsInRoomChanged;
            }
        }

        private void OnWsConnected()
        {
            try
            {
                webSocket.Send("{\"event\":\"request_lobby_list\"}");
                // Se já estávamos em um lobby antes da desconexão, tentar reentrar
                if (!string.IsNullOrEmpty(currentLobbyName))
                {
                    if (isHost)
                    {
                        // Recria o lobby que você hospedava
                        // Inclui a capacidade definida anteriormente (se disponível)
                        int cap = Mathf.Clamp(maxPlayers, 2, 500);
                        webSocket.Send($"{{\"event\":\"match_start\",\"lobby\":\"{currentLobbyName}\",\"max_players\":{cap}}}");
                    }
                    else
                    {
                        // Tenta reentrar no lobby existente
                        webSocket.Send($"{{\"event\":\"join_lobby\",\"lobby\":\"{currentLobbyName}\"}}");
                    }
                }
            }
            catch { }
        }

        // Return to lobby browser because the local user was kicked; do not send cancel/leave back to server
        public void ReturnToBrowserDueToKick(string reasonMessage = null)
        {
            // Update status text
            if (statusText != null)
                statusText.text = string.IsNullOrEmpty(reasonMessage) ? "Você foi removido do lobby." : reasonMessage;

            // Reset local lobby state
            string kickedFrom = currentLobbyName;
            isHost = false;
            playerJoined = false;
            currentLobbyName = string.Empty;

            // UI: show lobby browser and hide members panel
            if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(true);
            if (membersPanel != null) membersPanel.Hide();

            // Clear lobby in chat
            var chat = FindObjectOfType<ChronoSyncRCPChat>(true);
            if (chat != null) chat.SetLobby("", preserveHistory: true);

            // Notify via Message UI if exists
            var msgUi = messageUI != null ? messageUI : FindObjectOfType<Message>(true);
            if (msgUi != null)
            {
                msgUi.SetMessage(string.IsNullOrEmpty(reasonMessage) ? "Você foi removido do lobby pelo host." : reasonMessage);
            }

            // Refresh lobby list for the browser
            if (webSocket != null && webSocket.IsConnected)
            {
                try { webSocket.Send("{\"event\":\"request_lobby_list\"}"); } catch {}
            }
        }
        public void CancelLobby()
        {
            // Atualiza status local
            if (statusText != null) statusText.text = "Lobby cancelado.";
            string lobbyToClose = currentLobbyName;
            bool wasHost = isHost;
            isHost = false;
            playerJoined = false;
            currentLobbyName = string.Empty;

            // Notifica servidor (se possível)
            if (webSocket != null && webSocket.IsConnected && !string.IsNullOrEmpty(lobbyToClose))
            {
                // Se você é o host, cancele o lobby; caso contrário, apenas saia
                if (wasHost)
                    webSocket.Send($"{{\"event\":\"lobby_cancel\",\"lobby\":\"{lobbyToClose}\"}}");
                else
                    webSocket.Send($"{{\"event\":\"leave_lobby\",\"lobby\":\"{lobbyToClose}\"}}");
                // Atualiza lista de lobbies para todos
                webSocket.Send("{\"event\":\"request_lobby_list\"}");
            }

            // UI: voltar para o navegador de lobbies e esconder painel de membros
            if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(true);
            if (membersPanel != null) membersPanel.Hide();

            // Limpar lobby no chat para refletir estado
            var chat = FindObjectOfType<ChronoSyncRCPChat>();
            if (chat != null)
                chat.SetLobby("", preserveHistory: true);
        }

        private void HandleIsInRoomChanged(bool inRoom)
        {
            if (!inRoom)
            {
                // Neutral UI transition to browser when leaving any room
                if (statusText != null) statusText.text = "Fora do lobby.";
                isHost = false;
                playerJoined = false;
                currentLobbyName = string.Empty;
                if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(true);
                if (membersPanel != null) membersPanel.Hide();
                // Clear lobby in chat to reflect state
                var chat = FindObjectOfType<ChronoSyncRCPChat>(true);
                if (chat != null) chat.SetLobby("", preserveHistory: true);
                // Refresh list to reflect latest rooms
                if (webSocket != null && webSocket.IsConnected)
                {
                    try { webSocket.Send("{\"event\":\"request_lobby_list\"}"); } catch {}
                }
            }
        }

        public void CreateLobby()
        {
            string lobbyName = GetLobbyNameText();
            if (string.IsNullOrEmpty(lobbyName))
            {
                Debug.LogWarning("[ChronoSyncRCPLobby] Nenhum campo de nome de lobby atribuído ou vazio (TMP_InputField ou InputField).");
                return;
            }
            // Escape JSON-sensitive chars
            lobbyName = Escape(lobbyName.Trim());
            // Ler MaxPlayers a partir dos campos configurados (TMP ou UGUI)
            bool foundMax = false;
            if (maxPlayersTMP != null)
            {
                var txt = maxPlayersTMP.text;
                if (!string.IsNullOrWhiteSpace(txt) && int.TryParse(txt, out var parsedTMP))
                {
                    maxPlayers = Mathf.Clamp(parsedTMP, 2, 500);
                    foundMax = true;
                }
            }
            if (!foundMax && maxPlayersInput != null)
            {
                var txt = maxPlayersInput.text;
                if (!string.IsNullOrWhiteSpace(txt) && int.TryParse(txt, out var parsedUI))
                {
                    maxPlayers = Mathf.Clamp(parsedUI, 2, 500);
                    foundMax = true;
                }
            }
            if (!foundMax)
            {
                // Sem campos, mantém valor atual mas garantindo faixa
                maxPlayers = Mathf.Clamp(maxPlayers, 2, 500);
            }
            if (webSocket == null)
            {
                Debug.LogError("[ChronoSyncRCPLobby] ChronoSyncRCPWebSocket ausente. Abortando criação de lobby.");
                return;
            }

            // Garantir que identidade foi atribuída pelo servidor antes de criar lobby
            if (string.IsNullOrEmpty(webSocket.assignedPlayerId))
            {
                Debug.LogWarning("[ChronoSyncRCPLobby] assignedPlayerId ainda não recebido; aguardando antes de enviar match_start...");
                StartCoroutine(WaitIdentityAndCreate(lobbyName, maxPlayers));
                return;
            }

            if (statusText != null)
                statusText.text = $"Lobby '{lobbyName}' criado. Aguardando outro jogador...";
            isHost = true;
            currentLobbyName = lobbyName;
            // Seed capacidade localmente para atualizar lista quando visível
            lobbyMaxPlayers[currentLobbyName] = maxPlayers;
            if (lobbyItems.TryGetValue(currentLobbyName, out var item) && item != null)
            {
                item.UpdateMaxPlayers(maxPlayers);
            }
            // Evitar múltiplas inscrições duplicadas (já feito em OnEnable)
            webSocket.OnMessageReceived -= OnWebSocketMessage;
            webSocket.OnMessageReceived += OnWebSocketMessage;
            // Envia evento de criação de lobby para o servidor
            var payload = $"{{\"event\":\"match_start\",\"lobby\":\"{lobbyName}\",\"max_players\":{maxPlayers}}}";
            if (webSocket is ChronoSyncRCPWebSocket wsExt && wsExt.verboseLogging)
                wsExt.SendLogged(payload);
            else
                webSocket.Send(payload);
            StartCoroutine(WaitMatchStartConfirmation(lobbyName, maxPlayers));
            // Atualiza valor no Core (se existir)
            var core = ChronoSyncCore.Instance;
            if (core != null)
            {
                core.SetMaxPlayers(maxPlayers);
            }
            // Abrir painel de membros
            if (membersPanel != null) membersPanel.ShowForLobby(lobbyName);
            if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(false);
        }

        private System.Collections.IEnumerator WaitIdentityAndCreate(string lobbyName, int maxPlayers)
        {
            float start = Time.realtimeSinceStartup;
            while (string.IsNullOrEmpty(webSocket.assignedPlayerId) && Time.realtimeSinceStartup - start < 5f)
            {
                yield return null;
            }
            if (!string.IsNullOrEmpty(webSocket.assignedPlayerId))
            {
                Debug.Log("[ChronoSyncRCPLobby] Identidade confirmada; enviando match_start atrasado.");
                CreateLobby(); // re-enter; will pass identity check now
            }
            else
            {
                if (statusText != null) statusText.text = "Falha: identidade não confirmada.";
                isHost = false; currentLobbyName = string.Empty;
            }
        }

        private System.Collections.IEnumerator WaitMatchStartConfirmation(string lobbyName, int maxPlayers)
        {
            float start = Time.realtimeSinceStartup;
            bool confirmed = false;
            while (Time.realtimeSinceStartup - start < 5f)
            {
                if (!string.IsNullOrEmpty(currentLobbyName) && string.Equals(currentLobbyName, lobbyName, StringComparison.Ordinal))
                {
                    // Recebemos algum evento que manteve o lobby atual; tentativa de confirmar match_start
                    confirmed = true; break;
                }
                yield return null;
            }
            if (!confirmed)
            {
                Debug.LogWarning("[ChronoSyncRCPLobby] match_start não confirmado em 5s; tentando fallback create_lobby.");
                var fallback = $"{{\"event\":\"create_lobby\",\"lobby\":\"{lobbyName}\",\"max_players\":{maxPlayers}}}";
                if (webSocket is ChronoSyncRCPWebSocket wsExt && wsExt.verboseLogging)
                    wsExt.SendLogged(fallback);
                else
                    webSocket.Send(fallback);
                // Espera mais um pouco por qualquer confirmação
                float start2 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start2 < 5f)
                {
                    if (!string.IsNullOrEmpty(currentLobbyName) && string.Equals(currentLobbyName, lobbyName, StringComparison.Ordinal))
                    { confirmed = true; break; }
                    yield return null;
                }
                if (!confirmed)
                {
                    Debug.LogError("[ChronoSyncRCPLobby] Criação de lobby não confirmada. Revertendo estado.");
                    isHost = false; currentLobbyName = string.Empty;
                    if (membersPanel != null) membersPanel.Hide();
                    if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(true);
                    if (statusText != null) statusText.text = "Falha ao criar lobby.";
                }
            }
        }

        private void ClearLobbyListUI()
        {
            if (lobbyListContainer == null) return;
            foreach (Transform child in lobbyListContainer)
            {
                Destroy(child.gameObject);
            }
            // Reset tracked UI and counts
            lobbyItems.Clear();
            lobbyCounts.Clear();
            lobbyMaxPlayers.Clear();
        }

        private void UpdateLobbyListUI()
        {
            ClearLobbyListUI();
            foreach (var lobby in availableLobbies)
            {
                var item = Instantiate(lobbyListItemPrefab, lobbyListContainer);
                var lobbyItemScript = item.GetComponent<LobbyListItem>();
                if (lobbyItemScript != null)
                {
                    lobbyItemScript.Setup(lobby, this);
                    lobbyItems[lobby] = lobbyItemScript;
                    // Initialize visible count (0 until server replies)
                    if (lobbyCounts.TryGetValue(lobby, out var c)) lobbyItemScript.UpdateMemberCount(c); else lobbyItemScript.UpdateMemberCount(0);
                    if (lobbyMaxPlayers.TryGetValue(lobby, out var mx) && mx > 0) lobbyItemScript.UpdateMaxPlayers(mx);
                }
            }
            // Ask server for current members of each lobby to populate counts
            RequestCountsForVisibleLobbies();
        }

        public void JoinLobby(string lobbyName)
        {
            if (statusText != null)
                statusText.text = $"Entrando no lobby '{lobbyName}'...";
            isHost = false;
            currentLobbyName = lobbyName;
            if (webSocket == null)
            {
                Debug.LogError("[ChronoSyncRCPLobby] ChronoSyncRCPWebSocket ausente. Abortando entrada no lobby.");
                return;
            }
            // Evitar múltiplas inscrições duplicadas (já feito em OnEnable)
            webSocket.OnMessageReceived -= OnWebSocketMessage;
            webSocket.OnMessageReceived += OnWebSocketMessage;
            // Envia evento de entrada no lobby para o servidor
            webSocket.Send($"{{\"event\":\"join_lobby\",\"lobby\":\"{lobbyName}\"}}");
            // Abrir painel de membros
            if (membersPanel != null) membersPanel.ShowForLobby(lobbyName);
            if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(false);
        }

        private void OnWebSocketMessage(string msg)
        {
            // Outro player entrou no meu lobby atual
            if (msg.Contains("\"event\":\"player_joined_lobby\"") && !playerJoined)
            {
                playerJoined = true;
                statusText.text = "Outro jogador entrou! Iniciando partida...";
                // Aqui pode iniciar a lógica do jogo
            }

            // Atualiza lista de lobbys disponíveis
            if (msg.Contains("\"event\":\"lobby_list\""))
            {
                // Espera JSON: {"event":"lobby_list","lobbies":["Lobby1","Lobby2"]}
                try
                {
                    int start = msg.IndexOf("[", StringComparison.Ordinal);
                    int end = msg.IndexOf("]", StringComparison.Ordinal);
                    if (start != -1 && end != -1 && end > start)
                    {
                        string arrayContent = msg.Substring(start + 1, end - start - 1);
                        var lobbies = new System.Collections.Generic.List<string>();
                        // If array contains objects, extract name and max_players per entry
                        if (arrayContent.IndexOf('{') != -1)
                        {
                            var entries = arrayContent.Split(new string[]{"},{"}, System.StringSplitOptions.RemoveEmptyEntries);
                            foreach (var e in entries)
                            {
                                string block = e;
                                if (!block.StartsWith("{")) block = "{" + block;
                                if (!block.EndsWith("}")) block = block + "}";
                                string name = ExtractJsonValue(block, "name");
                                if (string.IsNullOrEmpty(name)) name = ExtractJsonValue(block, "lobby");
                                if (string.IsNullOrEmpty(name)) continue;
                                lobbies.Add(name);
                                int mx = ExtractMaxPlayers(block);
                                if (mx > 0) lobbyMaxPlayers[name] = mx;
                            }
                        }
                        else
                        {
                            // Fallback: plain string array
                            foreach (var lobby in arrayContent.Split(','))
                            {
                                string clean = lobby.Trim().Trim('"');
                                if (!string.IsNullOrEmpty(clean)) lobbies.Add(clean);
                            }
                        }
                        availableLobbies = lobbies;
                        UpdateLobbyListUI();
                    }
                }
                catch { }
            }

            // When a full members list arrives for some lobby, update its count (and capacity if present)
            if (msg.Contains("\"event\":\"lobby_members\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby))
                {
                    int count = CountMembersFromJson(msg);
                    SetLobbyCount(lobby, count);
                    int max = ExtractMaxPlayers(msg);
                    if (max > 0 && lobbyItems.TryGetValue(lobby, out var li) && li != null)
                    {
                        li.UpdateMaxPlayers(max);
                    }
                }
            }

            // Incremental updates for counts
            if (msg.Contains("\"event\":\"player_joined_lobby\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby)) AdjustLobbyCount(lobby, +1);
            }
            if (msg.Contains("\"event\":\"player_left_lobby\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby)) AdjustLobbyCount(lobby, -1);
            }
            if (msg.Contains("\"event\":\"lobby_closed\"") || msg.Contains("\"event\":\"lobby_cancel\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby)) RemoveLobbyFromList(lobby);
            }

            // Ao receber confirmação de match_start ou join_lobby, configurar lobby no chat e garantir painel aberto
            if (msg.Contains("\"event\":\"match_start\"") || msg.Contains("\"event\":\"join_lobby\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                string owner = ExtractJsonValue(msg, "owner_id");
                var chat = FindObjectOfType<ChronoSyncRCPChat>();
                if (chat != null && !string.IsNullOrEmpty(lobby))
                {
                    chat.SetLobby(lobby);
                }
                currentLobbyName = string.IsNullOrEmpty(lobby) ? currentLobbyName : lobby;
                if (membersPanel != null && !string.IsNullOrEmpty(lobby))
                {
                    membersPanel.ShowForLobby(lobby);
                    if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(false);
                }
                // Sync IsHost when owner info is present
                if (!string.IsNullOrEmpty(owner) && webSocket != null)
                {
                    var localId = string.IsNullOrEmpty(webSocket.assignedPlayerId) ? webSocket.playerId : webSocket.assignedPlayerId;
                    isHost = !string.IsNullOrEmpty(localId) && string.Equals(localId, owner, System.StringComparison.Ordinal);
                }
            }

            // Iniciar a partida quando o servidor mandar 'game_start'
            if (msg.Contains("\"event\":\"game_start\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby) && membersPanel != null)
                {
                    // Deixa o MembersPanel carregar a cena conforme configurado nele
                    // Nada a fazer aqui se o MembersPanel já escuta 'game_start'.
                    // Garantir que o player local seja instanciado
                    if (GameSessionManager.Instance != null)
                    {
                        GameSessionManager.Instance.TrySpawnLocal();
                    }
                }
            }

            // Capture capacity from match_start to inform other clients' browser
            if (msg.Contains("\"event\":\"match_start\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                int mx = ExtractMaxPlayers(msg);
                if (!string.IsNullOrEmpty(lobby) && mx > 0)
                {
                    lobbyMaxPlayers[lobby] = mx;
                    if (lobbyItems.TryGetValue(lobby, out var li) && li != null)
                    {
                        li.UpdateMaxPlayers(mx);
                    }
                }
            }

            // If the server kicked this local player from the lobby, immediately return to the lobby browser and notify
            if (msg.Contains("\"event\":\"remove_from_lobby\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
                string pid = ExtractJsonValue(msg, "player_id");
                var localId = webSocket != null && !string.IsNullOrEmpty(webSocket.assignedPlayerId) ? webSocket.assignedPlayerId : (webSocket != null ? webSocket.playerId : null);
                if (!string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(localId) && string.Equals(pid, localId, System.StringComparison.Ordinal))
                {
                    ReturnToBrowserDueToKick("Você foi removido do lobby pelo host.");
                }
            }

            // New authoritative target-only event from server when you are kicked
            if (msg.Contains("\"event\":\"kicked_from_lobby\""))
            {
                string reason = ExtractJsonValue(msg, "reason");
                string text = string.IsNullOrEmpty(reason) ? "Você foi removido do lobby pelo host." : TranslateKickReason(reason);
                ReturnToBrowserDueToKick(text);
            }
        }

        // Classe de payload antiga removida (não utilizada)

        private void OnDestroy()
        {
            if (webSocket != null)
            {
                webSocket.OnMessageReceived -= OnWebSocketMessage;
                webSocket.OnConnected -= OnWsConnected;
            }
        }

        private string GetLobbyNameText()
        {
            if (lobbyNameTMP != null) return lobbyNameTMP.text;
            if (lobbyNameInput != null) return lobbyNameInput.text;
            return string.Empty;
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

        private string TranslateKickReason(string reason)
        {
            switch (reason)
            {
                case "kicked_by_host": return "Você foi removido do lobby pelo host.";
                case "left": return "Você saiu do lobby.";
                default: return "Você foi removido do lobby.";
            }
        }

        private void RequestCountsForVisibleLobbies()
        {
            if (webSocket == null || !webSocket.IsConnected) return;
            for (int i = 0; i < availableLobbies.Count; i++)
            {
                var l = availableLobbies[i];
                if (!string.IsNullOrEmpty(l))
                {
                    try
                    {
                        webSocket.Send($"{{\"event\":\"request_lobby_members\",\"lobby\":\"{Escape(l)}\"}}");
                    }
                    catch { }
                }
            }
        }

        private void SetLobbyCount(string lobby, int count)
        {
            if (string.IsNullOrEmpty(lobby)) return;
            lobbyCounts[lobby] = Math.Max(0, count);
            if (lobbyItems.TryGetValue(lobby, out var item) && item != null)
            {
                item.UpdateMemberCount(lobbyCounts[lobby]);
            }
        }

        private void AdjustLobbyCount(string lobby, int delta)
        {
            if (string.IsNullOrEmpty(lobby)) return;
            if (!lobbyCounts.TryGetValue(lobby, out var c)) c = 0;
            SetLobbyCount(lobby, c + delta);
        }

        private void RemoveLobbyFromList(string lobby)
        {
            if (string.IsNullOrEmpty(lobby)) return;
            if (lobbyItems.TryGetValue(lobby, out var item) && item != null)
            {
                Destroy(item.gameObject);
            }
            lobbyItems.Remove(lobby);
            lobbyCounts.Remove(lobby);
            availableLobbies.Remove(lobby);
        }

        private int CountMembersFromJson(string json)
        {
            // Try to locate the members array first
            int idx = json.IndexOf("\"members\":");
            if (idx == -1) return 0;
            int arrStart = json.IndexOf('[', idx);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            if (arrStart == -1 || arrEnd == -1 || arrEnd <= arrStart) return 0;
            string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            if (string.IsNullOrWhiteSpace(inner)) return 0;

            // If members are objects, count occurrences of "player_id"
            if (inner.IndexOf("\"player_id\"") != -1)
            {
                int count = 0;
                int search = 0;
                while (true)
                {
                    int hit = inner.IndexOf("\"player_id\"", search);
                    if (hit == -1) break;
                    count++;
                    search = hit + 10;
                }
                return count;
            }

            // Otherwise, assume array of strings and count quoted entries
            int n = 0;
            var parts = inner.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                var t = parts[i].Trim();
                if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') n++;
            }
            return n;
        }

        private int ExtractMaxPlayers(string json)
        {
            // try locate "max_players":<number>
            int idx = json.IndexOf("\"max_players\":");
            if (idx == -1) return 0;
            int start = idx + "\"max_players\":".Length;
            // read until next comma or closing brace
            int endComma = json.IndexOf(',', start);
            int endBrace = json.IndexOf('}', start);
            int end = (endComma == -1) ? endBrace : ((endBrace == -1) ? endComma : Mathf.Min(endComma, endBrace));
            if (end == -1) end = json.Length;
            var numStr = json.Substring(start, end - start).Trim().Trim('"');
            // Be tolerant of potential quotes or whitespace
            if (int.TryParse(numStr, out var value)) return value;
            return 0;
        }

        private string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}