using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using CS.Core.Systems;
using CS.Core.Networking;

namespace CS.UI
{    
    public class ChronoSyncRCPLobbyMembersPanel : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private GameObject panel; // Root panel to enable/disable
        [SerializeField] private Transform content; // Parent for member items
        [SerializeField] private GameObject memberItemPrefab; // Row prefab with TMP_Text + optional Kick Button
        [SerializeField] private TMP_Text headerText; // Optional: shows lobby name
        [SerializeField] private Button cancelButton; 
        [SerializeField] private Message messageUI; // Optional: assign if Message is located elsewhere in the scene
        [Header("Start Match")]
        [SerializeField] private Button startMatchButton; // Botão "Iniciar partida"
        [Tooltip("Apenas o host pode iniciar a partida")]
        [SerializeField] private bool onlyHostStarts = true;
        [Tooltip("Nome da Scene da partida (adicione em Build Settings)")]
        [SerializeField] private string gameSceneName = "";

        private ChronoSyncRCPWebSocket webSocket;
    private ChronoSyncCore coreRef;
        private string currentLobby = "";
        private class MemberInfo { public string id; public string name; }
        private readonly List<MemberInfo> members = new List<MemberInfo>();

        // Opcional: outros scripts podem ouvir mudanças de quantidade de membros
        public System.Action<int> OnMemberCountChanged;

        private void Awake()
        {
            if (panel == null) panel = gameObject;
            webSocket = FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (webSocket != null)
            {
                webSocket.OnMessageReceived += OnWsMessage;
            }

            // Subscribe to Core owner changes to refresh Kick visibility when server sets owner_id
            coreRef = ChronoSyncCore.Instance != null ? ChronoSyncCore.Instance : FindObjectOfType<ChronoSyncCore>(true);
            if (coreRef != null)
            {
                coreRef.OnOwnerChanged -= HandleOwnerChanged;
                coreRef.OnOwnerChanged += HandleOwnerChanged;
                coreRef.OnIsInRoomChanged -= HandleIsInRoomChanged;
                coreRef.OnIsInRoomChanged += HandleIsInRoomChanged;
            }

            if (startMatchButton != null)
            {
                startMatchButton.onClick.RemoveAllListeners();
                startMatchButton.onClick.AddListener(OnStartMatchClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(OnCancelClicked);
            }

            Hide();
        }

        private void OnEnable()
        {
            UpdateStartButtonInteractable();
        }

        private void OnDestroy()
        {
            if (webSocket != null)
                webSocket.OnMessageReceived -= OnWsMessage;
            if (startMatchButton != null)
                startMatchButton.onClick.RemoveListener(OnStartMatchClicked);
            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(OnCancelClicked);
            if (coreRef != null)
            {
                coreRef.OnOwnerChanged -= HandleOwnerChanged;
                coreRef.OnIsInRoomChanged -= HandleIsInRoomChanged;
            }
        }

        private void HandleOwnerChanged(string newOwnerId)
        {
            // Rebuild UI to update Kick visibility when ownership changes/promotes
            RebuildUI();
        }

        private void HandleIsInRoomChanged(bool inRoom)
        {
            if (!inRoom)
            {
                Hide();
            }
        }

        public void ShowForLobby(string lobby)
        {
            currentLobby = lobby;
            if (headerText != null) headerText.text = $"Lobby: {lobby}";
            panel.SetActive(true);
            RequestMembers();
            UpdateStartButtonInteractable();
        }

        public void Hide()
        {
            panel.SetActive(false);
            currentLobby = "";
            ClearUI();
            UpdateStartButtonInteractable();
        }

        private void RequestMembers()
        {
            if (webSocket == null || string.IsNullOrEmpty(currentLobby)) return;
            var msg = $"{{\"event\":\"request_lobby_members\",\"lobby\":\"{Escape(currentLobby)}\"}}";
            webSocket.Send(msg);
        }

        private void OnWsMessage(string msg)
        {
            // Update full list
            if (msg.Contains("\"event\":\"lobby_members\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (lobby != currentLobby) return;
                var objs = ExtractMembersArray(msg);
                members.Clear();
                // Remove potential duplicates of the local user when both a temp id and assigned id are present
                if (webSocket != null)
                {
                    var tmpId = webSocket.playerId;
                    var assigned = webSocket.assignedPlayerId;
                    if (!string.IsNullOrEmpty(assigned))
                    {
                        // Keep assigned; drop temporary local id entries
                        objs.RemoveAll(m => m.id == tmpId);
                    }
                }
                members.AddRange(objs);
                DeduplicateAndPreferNames();
                EnsureSelfMember();
                RebuildUI();
                NotifyCountAndRefreshButton();
            }
            // Join and leave events
            else if (msg.Contains("\"event\":\"player_joined_lobby\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (lobby != currentLobby) return;
                var pid = ExtractJsonValue(msg, "player_id");
                var pname = ExtractJsonValue(msg, "display_name");
                if (string.IsNullOrEmpty(pname)) pname = ExtractJsonValue(msg, "player_name");
                // Ignore events about the local user to avoid self-duplication
                if (webSocket != null)
                {
                    var selfAssigned = webSocket.assignedPlayerId;
                    var selfTemp = webSocket.playerId;
                    if ((!string.IsNullOrEmpty(selfAssigned) && pid == selfAssigned) || pid == selfTemp)
                    {
                        EnsureSelfMember();
                        RebuildUI();
                        NotifyCountAndRefreshButton();
                        return;
                    }
                }
                if (!string.IsNullOrEmpty(pid))
                {
                    var existing = members.Find(m => m.id == pid);
                    if (existing == null)
                    {
                        members.Add(new MemberInfo{ id = pid, name = string.IsNullOrWhiteSpace(pname) ? pid : pname });
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(pname)) existing.name = pname; // sempre prefira o display name recebido
                    }
                    DeduplicateAndPreferNames();
                    EnsureSelfMember();
                    RebuildUI();
                    NotifyCountAndRefreshButton();
                }
            }
            // Atualização de display name (dependendo do servidor, pode vir com estes eventos)
            else if (msg.Contains("\"event\":\"display_name_updated\"") || msg.Contains("\"event\":\"set_display_name\""))
            {
                var pid = ExtractJsonValue(msg, "player_id");
                var pname = ExtractJsonValue(msg, "display_name");
                if (!string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(pname))
                {
                    var m = members.Find(x => x.id == pid);
                    if (m != null)
                    {
                        m.name = pname;
                        DeduplicateAndPreferNames();
                        RebuildUI();
                    }
                }
            }
            else if (msg.Contains("\"event\":\"player_left_lobby\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (lobby != currentLobby) return;
                var pid = ExtractJsonValue(msg, "player_id");
                // Try to determine the display name of the player leaving before we mutate the list
                string removedName = null;
                if (!string.IsNullOrEmpty(pid))
                {
                    var existing = members.Find(m => m.id == pid);
                    if (existing != null) removedName = existing.name;
                    if (string.IsNullOrEmpty(removedName))
                    {
                        var maybeName = ExtractJsonValue(msg, "display_name");
                        if (string.IsNullOrWhiteSpace(maybeName)) maybeName = ExtractJsonValue(msg, "player_name");
                        if (!string.IsNullOrWhiteSpace(maybeName)) removedName = maybeName;
                    }
                    if (string.IsNullOrEmpty(removedName)) removedName = pid;
                }
                // If it's me, immediately leave to Lobby panel and notify
                if (webSocket != null)
                {
                    var localId = string.IsNullOrEmpty(webSocket.assignedPlayerId) ? webSocket.playerId : webSocket.assignedPlayerId;
                    if (!string.IsNullOrEmpty(localId) && string.Equals(pid, localId, System.StringComparison.Ordinal))
                    {
                        var lobbyUi = FindObjectOfType<ChronoSyncRCPLobby>(true);
                        if (lobbyUi != null)
                        {
                            if (lobbyUi.gameObject.activeInHierarchy)
                            {
                                // Show browser and hide this panel
                                lobbyUi.CancelLobby(); // ensures proper teardown and UI state
                            }
                            else
                            {
                                Hide();
                            }
                        }
                        var msgUi = messageUI != null ? messageUI : FindObjectOfType<Message>(true);
                        if (msgUi != null) msgUi.SetMessage("Você foi removido do lobby pelo host.");
                        return;
                    }
                }
                int idx = members.FindIndex(m => m.id == pid);
                if (!string.IsNullOrEmpty(pid) && idx >= 0)
                {
                    members.RemoveAt(idx);
                    RebuildUI();
                    NotifyCountAndRefreshButton();
                    // Notify others with the removed player's display name
                    if (!string.IsNullOrEmpty(removedName))
                    {
                        var msgUi = messageUI != null ? messageUI : FindObjectOfType<Message>(true);
                        if (msgUi != null) msgUi.SetMessage($"{removedName} foi removido do lobby.");
                    }
                }
            }
            else if (msg.Contains("\"event\":\"lobby_closed\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (lobby == currentLobby)
                    Hide();
            }
            else if (msg.Contains("\"event\":\"game_start\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (lobby == currentLobby)
                {
                    // Se houver GameFlowManager, deixe ele orquestrar a transição
                    if (GameFlowManager.Instance != null)
                    {
                        GameFlowManager.Instance.EnterMatch(null);
                    }
                    else
                    {
                        TryLoadGameScene();
                    }
                }
            }
        }

        // Ensure the local player appears in the members list even if the server omits it
        private void EnsureSelfMember()
        {
            if (webSocket == null) return;
            // Only auto-add self when we have a definitive assigned id to avoid duplicates with temp id entries
            if (string.IsNullOrEmpty(webSocket.assignedPlayerId)) return;
            var localId = webSocket.assignedPlayerId;
            if (!members.Exists(m => m.id == localId))
            {
                string display = GameFlowManager.Instance != null ? GameFlowManager.Instance.GetComponent<GameSessionManager>()?.localNickname : null;
                if (string.IsNullOrWhiteSpace(display))
                {
                    var gsm = CS.Core.Systems.GameSessionManager.Instance;
                    if (gsm != null) display = gsm.localNickname;
                }
                if (string.IsNullOrWhiteSpace(display)) display = localId;
                members.Add(new MemberInfo{ id = localId, name = display });
            }
        }

        private void NotifyCountAndRefreshButton()
        {
            OnMemberCountChanged?.Invoke(members.Count);
            UpdateStartButtonInteractable();
        }

        private void ClearUI()
        {
            if (content == null) return;
            foreach (Transform child in content)
                GameObject.Destroy(child.gameObject);
        }

        private void RebuildUI()
        {
            if (content == null || memberItemPrefab == null) return;
            ClearUI();
            DeduplicateAndPreferNames();
            bool isHost = false;
            var core = ChronoSyncCore.Instance != null ? ChronoSyncCore.Instance : coreRef;
            var localId = string.IsNullOrEmpty(webSocket.assignedPlayerId) ? webSocket.playerId : webSocket.assignedPlayerId;
            if (core != null && !string.IsNullOrEmpty(localId))
            {
                // Prefer server-truth when available
                var ownerId = core.GetLobbyOwnerId();
                if (!string.IsNullOrEmpty(ownerId))
                {
                    isHost = string.Equals(ownerId, localId, System.StringComparison.Ordinal);
                }
            }
            if (!isHost)
            {
                // Fallback to UI state in case owner_id hasn't arrived yet (server will enforce permissions anyway)
                var lobbyScript = FindObjectOfType<ChronoSyncRCPLobby>(true);
                if (lobbyScript != null) isHost = lobbyScript.IsHost;
            }
            foreach (var m in members)
            {
                var go = GameObject.Instantiate(memberItemPrefab, content);
                // Preferred path: RoomMemberItem component controls its own UI
                var item = go.GetComponent<RoomMemberItem>();
                if (item != null)
                {
                    bool canKick = isHost && !string.IsNullOrEmpty(localId) && localId != m.id;
                    item.Setup(m.name, canKick);
                    item.SetKickAction(canKick ? (UnityEngine.Events.UnityAction)(() => KickMember(m.id)) : null);
                }
                else
                {
                    // Fallback: text field + manual kick button
                    var text = go.GetComponentInChildren<TMP_Text>();
                    if (text != null) text.text = m.name;
                    // Try common names/paths for the kick button
                    Button btn = null;
                    var t1 = go.transform.Find("KickButton");
                    if (t1 != null) btn = t1.GetComponent<Button>();
                    if (btn == null)
                    {
                        var t2 = go.transform.Find("ButtonKick");
                        if (t2 != null) btn = t2.GetComponent<Button>();
                    }
                    if (btn == null)
                    {
                        // last resort: scan children for a button with name containing 'kick'
                        foreach (var b in go.GetComponentsInChildren<Button>(true))
                        {
                            if (b != null && b.name.ToLowerInvariant().Contains("kick"))
                            {
                                btn = b; break;
                            }
                        }
                    }
                    bool canKick = isHost && !string.IsNullOrEmpty(localId) && localId != m.id;
                    if (btn != null)
                    {
                        btn.gameObject.SetActive(canKick);
                        btn.onClick.RemoveAllListeners();
                        if (canKick)
                        {
                            btn.onClick.AddListener(() => KickMember(m.id));
                        }
                    }
                }
            }
        }

        // Remove duplicatas e prefere nomes diferentes do id (display names) quando disponíveis
        private void DeduplicateAndPreferNames()
        {
            if (members == null || members.Count <= 1) return;
            var map = new Dictionary<string, MemberInfo>();
            foreach (var m in members)
            {
                if (string.IsNullOrEmpty(m?.id)) continue;
                if (!map.TryGetValue(m.id, out var existing))
                {
                    map[m.id] = m;
                }
                else
                {
                    bool existingIsId = string.Equals(existing.name, existing.id, System.StringComparison.Ordinal);
                    bool candidateIsId = string.Equals(m.name, m.id, System.StringComparison.Ordinal);
                    if (existingIsId && !candidateIsId)
                        map[m.id] = m; // prefer display name
                }
            }
            members.Clear();
            members.AddRange(map.Values);
        }

        private void UpdateStartButtonInteractable()
        {
            if (startMatchButton == null) return;
            bool hasMinPlayers = members.Count >= 2;
            bool hostOk = true;
            if (onlyHostStarts)
            {
                var lobby = FindObjectOfType<ChronoSyncRCPLobby>(true);
                hostOk = lobby != null && lobby.IsHost;
            }
            startMatchButton.interactable = panel != null && panel.activeSelf && hasMinPlayers && hostOk && !string.IsNullOrEmpty(currentLobby);
        }

        private void OnStartMatchClicked()
        {
            if (webSocket == null || string.IsNullOrEmpty(currentLobby)) return;
            var payload = $"{{\"event\":\"start_match\",\"lobby\":\"{Escape(currentLobby)}\"}}";
            webSocket.Send(payload);
            // Aguarda broadcast 'game_start' para carregar a cena em todos
        }

        private void TryLoadGameScene()
        {
            if (string.IsNullOrWhiteSpace(gameSceneName))
            {
                Debug.LogWarning("[LobbyMembersPanel] gameSceneName não configurado nas propriedades do componente. Scene não será carregada.");
                return;
            }
            try
            {
                SceneManager.LoadScene(gameSceneName);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LobbyMembersPanel] Falha ao carregar cena '{gameSceneName}': {ex.Message}");
            }
        }

        private void OnCancelClicked()
        {
            // Prefer delegating to the Lobby controller to handle UI and server event
            var lobbyUi = FindObjectOfType<ChronoSyncRCPLobby>(true);
            if (lobbyUi != null)
            {
                lobbyUi.CancelLobby();
                return;
            }

            // Fallback: send leave and hide this panel
            if (!string.IsNullOrEmpty(currentLobby))
            {
                var core = ChronoSyncCore.Instance;
                if (core != null)
                {
                    core.LeaveRoom(currentLobby);
                }
                else if (webSocket != null && webSocket.IsConnected)
                {
                    webSocket.Send($"{{\"event\":\"leave_lobby\",\"lobby\":\"{Escape(currentLobby)}\"}}");
                }
            }
            Hide();
        }

        private string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private List<MemberInfo> ExtractMembersArray(string json)
        {
            // Dedupe por id e preferir display_name quando disponível
            var map = new Dictionary<string, MemberInfo>();

            int idx = json.IndexOf("\"members\":");
            if (idx == -1) return new List<MemberInfo>();
            int arrStart = json.IndexOf('[', idx);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            if (arrStart == -1 || arrEnd == -1 || arrEnd <= arrStart) return new List<MemberInfo>();
            string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            // 1) Tentar objetos { player_id, display_name }
            var objs = inner.Split(new string[]{"},{"}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var o in objs)
            {
                string block = o;
                if (!block.StartsWith("{")) block = "{" + block;
                if (!block.EndsWith("}")) block = block + "}";
                string pid = ExtractJsonValue(block, "player_id");
                if (string.IsNullOrEmpty(pid)) continue;
                string name = ExtractJsonValue(block, "display_name");
                if (string.IsNullOrEmpty(name)) name = ExtractJsonValue(block, "player_name");
                var candidate = new MemberInfo{ id = pid, name = string.IsNullOrWhiteSpace(name) ? pid : name };
                if (map.TryGetValue(pid, out var existing))
                {
                    // Se já existe com nome pior (igual ao id), substitui por um com display_name
                    bool existingIsId = string.Equals(existing.name, existing.id, System.StringComparison.Ordinal);
                    bool candidateIsId = string.Equals(candidate.name, candidate.id, System.StringComparison.Ordinal);
                    if (existingIsId && !candidateIsId)
                        map[pid] = candidate;
                }
                else map[pid] = candidate;
            }

            // 2) Tentar array de strings ["id1","id2"] e adicionar apenas os que faltam
            var ids = ExtractStringArray(json, "members");
            if (ids != null)
            {
                foreach (var pid in ids)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!map.ContainsKey(pid))
                        map[pid] = new MemberInfo{ id = pid, name = pid };
                }
            }

            return new List<MemberInfo>(map.Values);
        }

        private List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            int idx = json.IndexOf("\"" + key + "\":");
            if (idx == -1) return result;
            int arrStart = json.IndexOf('[', idx);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            if (arrStart == -1 || arrEnd == -1 || arrEnd <= arrStart) return result;
            var inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            var parts = inner.Split(',');
            foreach (var p in parts)
            {
                var s = p.Trim().Trim('"');
                if (!string.IsNullOrEmpty(s) && !s.Contains(":")) // heurística simples: ignora objetos
                    result.Add(s);
            }
            return result;
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

        private void KickMember(string playerId)
        {
            if (string.IsNullOrEmpty(currentLobby) || string.IsNullOrEmpty(playerId)) return;
            var core = ChronoSyncCore.Instance;
            if (core != null)
            {
                // Before kicking, broadcast a chat message to the room so all members see who was removed
                TryAnnounceKickViaChat(playerId);
                // Explicit host kick for determinism
                core.KickPlayer(playerId);
                return;
            }
            // Fallback: direct WS call
            if (webSocket != null)
            {
                TryAnnounceKickViaChat(playerId);
                var payload = $"{{\"event\":\"remove_from_lobby\",\"lobby\":\"{Escape(currentLobby)}\",\"player_id\":\"{Escape(playerId)}\"}}";
                webSocket.Send(payload);
            }
        }

        private void TryAnnounceKickViaChat(string playerId)
        {
            try
            {
                if (webSocket == null || !webSocket.IsConnected) return;
                if (string.IsNullOrEmpty(currentLobby)) return;
                // Resolve a friendly display name for the target
                string display = null;
                var m = members != null ? members.Find(x => x.id == playerId) : null;
                if (m != null && !string.IsNullOrWhiteSpace(m.name)) display = m.name;
                if (string.IsNullOrEmpty(display)) display = playerId;
                string msg = $"{display} foi removido do lobby pelo host.";
                var chatPayload = $"{{\"event\":\"chat_message\",\"lobby\":\"{Escape(currentLobby)}\",\"message\":\"{Escape(msg)}\"}}";
                webSocket.Send(chatPayload);
            }
            catch { }
        }
    }

}