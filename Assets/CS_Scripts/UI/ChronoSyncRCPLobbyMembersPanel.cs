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

        [Header("Start Match")]
        [SerializeField] private Button startMatchButton; // Botão "Iniciar partida"
        [Tooltip("Apenas o host pode iniciar a partida")]
        [SerializeField] private bool onlyHostStarts = true;
        [Tooltip("Nome da Scene da partida (adicione em Build Settings)")]
        [SerializeField] private string gameSceneName = "";

        private ChronoSyncRCPWebSocket webSocket;
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

            if (startMatchButton != null)
            {
                startMatchButton.onClick.RemoveAllListeners();
                startMatchButton.onClick.AddListener(OnStartMatchClicked);
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
                int idx = members.FindIndex(m => m.id == pid);
                if (!string.IsNullOrEmpty(pid) && idx >= 0)
                {
                    members.RemoveAt(idx);
                    RebuildUI();
                    NotifyCountAndRefreshButton();
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
            var lobbyScript = FindObjectOfType<ChronoSyncRCPLobby>(true);
            bool isHost = lobbyScript != null && lobbyScript.IsHost;
            foreach (var m in members)
            {
                var go = GameObject.Instantiate(memberItemPrefab, content);
                var text = go.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = m.name;
                // If prefab has a Button as child named "KickButton", wire it when host and not self
                if (isHost)
                {
                    var btn = go.transform.Find("KickButton")?.GetComponent<Button>();
                    if (btn != null)
                    {
                        // don't allow kicking self (assumes LocalPlayerId is known via webSocket.assignedPlayerId)
                        var localId = string.IsNullOrEmpty(webSocket.assignedPlayerId) ? webSocket.playerId : webSocket.assignedPlayerId;
                        bool canKick = !string.IsNullOrEmpty(localId) && localId != m.id;
                        btn.gameObject.SetActive(canKick);
                        if (canKick)
                        {
                            btn.onClick.RemoveAllListeners();
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
            if (webSocket == null || string.IsNullOrEmpty(currentLobby) || string.IsNullOrEmpty(playerId)) return;
            var payload = $"{{\"event\":\"remove_from_lobby\",\"lobby\":\"{Escape(currentLobby)}\",\"player_id\":\"{Escape(playerId)}\"}}";
            webSocket.Send(payload);
        }
    }

}