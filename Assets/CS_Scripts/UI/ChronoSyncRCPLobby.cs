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
        [SerializeField] private TMP_InputField maxPlayersTMP; // opcional: campo numérico no UI para capacidade
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

        private bool isHost = false;
        private bool playerJoined = false;
        private System.Collections.Generic.List<string> availableLobbies = new System.Collections.Generic.List<string>();
        private ChronoSyncRCPLobbyMembersPanel membersPanel;
        private string currentLobbyName = string.Empty;

        // Expor status para outros componentes (somente leitura)
        public bool IsHost => isHost;
        public string CurrentLobby => currentLobbyName;

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
        }

        private void OnDisable()
        {
            if (webSocket != null)
            {
                webSocket.OnMessageReceived -= OnWebSocketMessage;
                webSocket.OnConnected -= OnWsConnected;
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
                chat.SetLobby("");
        }

        public void CreateLobby()
        {
            string lobbyName = GetLobbyNameText();
            if (string.IsNullOrEmpty(lobbyName))
            {
                Debug.LogWarning("[ChronoSyncRCPLobby] Nenhum campo de nome de lobby atribuído ou vazio (TMP_InputField ou InputField).");
                return;
            }
            // Se houver um campo TMP para max players, tentar parsear
            if (maxPlayersTMP != null && !string.IsNullOrWhiteSpace(maxPlayersTMP.text))
            {
                if (int.TryParse(maxPlayersTMP.text, out var parsed))
                {
                    maxPlayers = Mathf.Clamp(parsed, 2, 500);
                }
            }
            else
            {
                maxPlayers = Mathf.Clamp(maxPlayers, 2, 500);
            }
            if (webSocket == null)
            {
                Debug.LogError("[ChronoSyncRCPLobby] ChronoSyncRCPWebSocket ausente. Abortando criação de lobby.");
                return;
            }

            if (statusText != null)
                statusText.text = $"Lobby '{lobbyName}' criado. Aguardando outro jogador...";
            isHost = true;
            currentLobbyName = lobbyName;
            // Evitar múltiplas inscrições duplicadas (já feito em OnEnable)
            webSocket.OnMessageReceived -= OnWebSocketMessage;
            webSocket.OnMessageReceived += OnWebSocketMessage;
            // Envia evento de criação de lobby para o servidor
            webSocket.Send($"{{\"event\":\"match_start\",\"lobby\":\"{lobbyName}\",\"max_players\":{maxPlayers}}}");
            // Abrir painel de membros
            if (membersPanel != null) membersPanel.ShowForLobby(lobbyName);
            if (lobbyBrowserRoot != null) lobbyBrowserRoot.SetActive(false);
        }

        private void ClearLobbyListUI()
        {
            if (lobbyListContainer == null) return;
            foreach (Transform child in lobbyListContainer)
            {
                Destroy(child.gameObject);
            }
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
                }
            }
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
                        foreach (var lobby in arrayContent.Split(','))
                        {
                            string clean = lobby.Trim().Trim('"');
                            if (!string.IsNullOrEmpty(clean)) lobbies.Add(clean);
                        }
                        availableLobbies = lobbies;
                        UpdateLobbyListUI();
                    }
                }
                catch { }
            }

            // Ao receber confirmação de match_start ou join_lobby, configurar lobby no chat e garantir painel aberto
            if (msg.Contains("\"event\":\"match_start\"") || msg.Contains("\"event\":\"join_lobby\""))
            {
                string lobby = ExtractJsonValue(msg, "lobby");
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
    }
}