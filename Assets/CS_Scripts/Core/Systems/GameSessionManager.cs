using UnityEngine;
using UnityEngine.SceneManagement;
using CS.Core.Spawning;
using CS.Core.Identity;
#if CINEMACHINE
using Cinemachine;
#endif
using CS.Core.Networking;

namespace CS.Core.Systems
{
    public class GameSessionManager : MonoBehaviour
    {
        public static GameSessionManager Instance { get; private set; }

        public static GameSessionManager Ensure()
        {
            if (Instance != null) return Instance;
            var existing = FindObjectOfType<GameSessionManager>();
            if (existing != null)
            {
                Instance = existing;
                // Garantir persistência
                if (Instance != null) DontDestroyOnLoad(Instance.gameObject);
                return Instance;
            }
            var go = new GameObject("GameSessionManager");
            var gsm = go.AddComponent<GameSessionManager>();
            // Awake cuidará do restante (Instance/DontDestroyOnLoad)
            return gsm;
        }

        [Header("References")]
        public PlayerSpawner spawner;
        public ChronoSyncRCPWebSocket webSocket;

        [Header("State")]
        public string localPlayerId; // pode vir do backend após conexão
        public string localNickname; // vem do Auth UI
        public Team localTeam = Team.TeamA;
        [Header("Lobby")]
        public string currentLobby;
    public event System.Action<string> OnCurrentLobbyChanged; // fires when currentLobby changes
    [Header("Lobby Members (read-only order-aligned)")]
    public System.Collections.Generic.List<string> lobbyMemberIds = new System.Collections.Generic.List<string>();
    public System.Collections.Generic.List<string> lobbyMemberDisplayNames = new System.Collections.Generic.List<string>();
    // Notifica quando a lista de membros mudar (ids e nomes no mesmo índice)
    public event System.Action<System.Collections.Generic.List<string>, System.Collections.Generic.List<string>> OnLobbyMembersChanged;
        [Header("Remote Defaults")]
        public Team defaultRemoteTeam = Team.Neutral;

        [Header("Scenes")]
        [Tooltip("Nomes das scenes onde a partida acontece (spawns ocorrem). Ex.: level1")]
        public string[] matchSceneNames = new[] { "level1" };

        private bool _pendingSpawnOnSceneLoad;

    [Header("Camera (Cinemachine)")]
#if CINEMACHINE
    [Tooltip("Cinemachine Virtual Camera to control (optional; auto-found if null)")]
    public CinemachineVirtualCamera vcam;
    [Tooltip("Cinemachine Free Look camera to control (optional; auto-found if null)")]
    public CinemachineFreeLook freeLook;
#endif
    [Tooltip("Override follow target; if null, will search child 'CameraTarget' or use player root")]
    public Transform cameraFollowOverride;
    [Tooltip("Override look-at target; if null, will search child 'CameraTarget' or use player root")]
    public Transform cameraLookAtOverride;

        private void Awake()
        {
            
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            if (webSocket == null) webSocket = GameObject.FindGameObjectWithTag("Systems").GetComponent<ChronoSyncRCPWebSocket>();
            if (spawner == null) spawner = FindObjectOfType<PlayerSpawner>();

            if (webSocket != null)
            {
                webSocket.OnMessageReceived += OnWsMessage;
                // Quando a identidade estiver pronta (id definitivo + nome), atualiza estado local
                webSocket.OnIdentityReady -= OnWsIdentityReady;
                webSocket.OnIdentityReady += OnWsIdentityReady;
            }
        }

        private void OnDestroy()
        {
            if (webSocket != null)
            {
                webSocket.OnMessageReceived -= OnWsMessage;
                webSocket.OnIdentityReady -= OnWsIdentityReady;
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        private void OnWsIdentityReady(string id, string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                SetLocalNickname(name);
            }
            if (!string.IsNullOrWhiteSpace(id))
            {
                UpdateLocalPlayerId(id);
            }
        }

        public void SetLocalNickname(string nickname)
        {
            localNickname = nickname;
        }

        // Called when the server assigns a definitive player id; renames existing local spawn to avoid duplicates
        public void UpdateLocalPlayerId(string newPlayerId)
        {
            if (string.IsNullOrWhiteSpace(newPlayerId)) return;
            if (string.Equals(localPlayerId, newPlayerId, System.StringComparison.Ordinal)) return;
            var oldId = localPlayerId;
            localPlayerId = newPlayerId;
            if (spawner == null) spawner = FindObjectOfType<PlayerSpawner>();
            if (spawner != null && !string.IsNullOrWhiteSpace(oldId))
            {
                spawner.RenameSpawnedId(oldId, newPlayerId);
            }
        }

        private void OnWsMessage(string msg)
        {
            // Não redefinir o localPlayerId aqui; usar o id fornecido pelo backend no login

            // Guardar lobby atual quando criar/entrar
            if (msg.Contains("\"event\":\"match_start\"") || msg.Contains("\"event\":\"join_lobby\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby))
                {
                    if (!string.Equals(currentLobby, lobby, System.StringComparison.Ordinal))
                    {
                        currentLobby = lobby;
                        OnCurrentLobbyChanged?.Invoke(currentLobby);
                    }
                }
            }

            // Recebe lista completa de membros do lobby
            if (msg.Contains("\"event\":\"lobby_members\""))
            {
                var map = ExtractMembersMap(msg);
                ReplaceLobbyMembers(map);
                TryHydrateMembers(msg); // manter comportamento atual de spawn
            }

            // Ao iniciar o jogo, aguardar a cena de partida para spawnar
            if (msg.Contains("\"event\":\"game_start\""))
            {
                _pendingSpawnOnSceneLoad = true;
            }

            // Quando um jogador entra no lobby atual
            if (msg.Contains("\"event\":\"player_joined_lobby\"") || msg.Contains("\"event\":\"player_joined\""))
            {
                var pid = ExtractJsonValue(msg, "player_id");
                if (!string.IsNullOrEmpty(pid) && pid != localPlayerId && spawner != null)
                {
                    var name = ExtractJsonValue(msg, "display_name");
                    var teamStr = ExtractJsonValue(msg, "team");
                    var team = ParseTeam(teamStr, defaultRemoteTeam);
                    // Evitar duplicata se já existir instância com esse id
                    if (!spawner.TryGetSpawned(pid, out _))
                    {
                        spawner.SpawnRemote(pid, string.IsNullOrWhiteSpace(name) ? pid : name, team);
                    }
                }
                // Atualiza lista de membros
                var joinId = ExtractJsonValue(msg, "player_id");
                var joinName = ExtractJsonValue(msg, "display_name");
                if (!string.IsNullOrEmpty(joinId)) UpsertLobbyMember(joinId, joinName);
            }

            // Quando um jogador sai do lobby ou desconecta
            if (msg.Contains("\"event\":\"player_left_lobby\"") || msg.Contains("\"event\":\"player_left\"") || msg.Contains("\"event\":\"player_disconnected\""))
            {
                var pid = ExtractJsonValue(msg, "player_id");
                if (!string.IsNullOrEmpty(pid) && spawner != null)
                {
                    spawner.Despawn(pid);
                }
                if (!string.IsNullOrEmpty(pid)) RemoveLobbyMember(pid);
            }

            // Quando o lobby é cancelado/fechado pelo host, remover todos os remotos
            if (msg.Contains("\"event\":\"lobby_cancel\"") || msg.Contains("\"event\":\"lobby_closed\""))
            {
                if (spawner != null)
                {
                    spawner.DespawnAllExcept(localPlayerId);
                }
                ClearLobbyMembers();
                if (!string.IsNullOrEmpty(currentLobby))
                {
                    currentLobby = string.Empty;
                    OnCurrentLobbyChanged?.Invoke(currentLobby);
                }
            }

            // Opcional: se vier uma lista de membros, hidratar todos (formato esperado simplificado)
            if (msg.Contains("\"members\":["))
            {
                TryHydrateMembers(msg);
                var map = ExtractMembersMap(msg);
                if (map.Count > 0) ReplaceLobbyMembers(map);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsMatchScene(scene.name)) return;
            // Ao carregar a cena da partida, instanciar o player local (uma vez) e requisitar os remotos
            bool alreadySpawned = false;
            if (spawner == null) spawner = FindObjectOfType<PlayerSpawner>();
            if (spawner != null && !string.IsNullOrEmpty(localPlayerId))
            {
                alreadySpawned = spawner.TryGetSpawned(localPlayerId, out _);
            }

            if (_pendingSpawnOnSceneLoad && !alreadySpawned)
            {
                TrySpawnLocal();
                _pendingSpawnOnSceneLoad = false;
            }
            else if (!alreadySpawned)
            {
                // Caso abriu a cena direto pelo editor sem game_start, garantir um único spawn
                TrySpawnLocal();
            }
            // Sempre tente hidratar membros após a cena carregar
            RequestLobbyMembers();
        }

        private void RequestLobbyMembers()
        {
            if (webSocket == null) webSocket = FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (webSocket != null && webSocket.IsConnected)
            {
                if (!string.IsNullOrEmpty(currentLobby))
                {
                    var lobbyEscaped = Escape(currentLobby);
                    webSocket.Send("{\"event\":\"request_lobby_members\",\"lobby\":\"" + lobbyEscaped + "\"}");
                }
                else
                {
                    webSocket.Send("{\"event\":\"request_lobby_list\"}");
                }
            }
        }

        public void TrySpawnLocal()
        {
            if (!IsMatchScene(SceneManager.GetActiveScene().name))
            {
                // Não spawnar fora de scenes de partida
                return;
            }
            if (spawner == null) spawner = FindObjectOfType<PlayerSpawner>();
            if (spawner == null)
            {
                // Em cena de partida, o spawner é necessário
                Debug.LogWarning("GameSessionManager: PlayerSpawner não encontrado na cena de partida.");
                return;
            }
            if (!string.IsNullOrEmpty(localPlayerId) && spawner.TryGetSpawned(localPlayerId, out _))
            {
                // Já existe instância do local
                return;
            }
            // Do NOT spawn local until we have a definitive id from API
            if (string.IsNullOrWhiteSpace(localPlayerId)) return;
            var go = spawner.SpawnLocal(localPlayerId, string.IsNullOrWhiteSpace(localNickname) ? localPlayerId : localNickname, localTeam);
            if (go != null)
            {
                SetupCameraForLocal(go);
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

        private Team ParseTeam(string teamStr, Team fallback)
        {
            if (string.IsNullOrWhiteSpace(teamStr)) return fallback;
            if (System.Enum.TryParse<Team>(teamStr, true, out var parsed)) return parsed;
            // aceitar strings comuns
            switch (teamStr.Trim().ToLowerInvariant())
            {
                case "a": case "teama": case "1": return Team.TeamA;
                case "b": case "teamb": case "2": return Team.TeamB;
                case "neutral": case "0": return Team.Neutral;
            }
            return fallback;
        }

        private void TryHydrateMembers(string json)
        {
            if (spawner == null) spawner = FindObjectOfType<PlayerSpawner>();
            if (spawner == null) return;

            bool spawnedAny = false;

            // Formato A: objetos com player_id/display_name/team
            int idx = 0;
            while (true)
            {
                int pidKey = json.IndexOf("\"player_id\":\"", idx);
                if (pidKey == -1) break;
                int pidStart = pidKey + "\"player_id\":\"".Length;
                int pidEnd = json.IndexOf("\"", pidStart);
                if (pidEnd == -1) break;
                string pid = json.Substring(pidStart, pidEnd - pidStart);

                var name = ExtractJsonValue(json.Substring(pidKey), "display_name");
                var teamStr = ExtractJsonValue(json.Substring(pidKey), "team");
                if (!string.IsNullOrEmpty(pid) && pid != localPlayerId)
                {
                    if (!spawner.TryGetSpawned(pid, out _))
                        spawner.SpawnRemote(pid, string.IsNullOrWhiteSpace(name) ? pid : name, ParseTeam(teamStr, defaultRemoteTeam));
                    spawnedAny = true;
                }
                idx = pidEnd + 1;
            }

            // Formato B: array de strings com ids
            if (!spawnedAny)
            {
                var ids = ExtractStringArray(json, "members");
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var pid = ids[i];
                        if (!string.IsNullOrEmpty(pid) && pid != localPlayerId)
                        {
                            if (!spawner.TryGetSpawned(pid, out _))
                                spawner.SpawnRemote(pid, pid, defaultRemoteTeam);
                            spawnedAny = true;
                        }
                    }
                }
            }
        }

        private System.Collections.Generic.List<string> ExtractStringArray(string json, string key)
        {
            var result = new System.Collections.Generic.List<string>();
            int idx = json.IndexOf("\"" + key + "\":");
            if (idx == -1) return result;
            int arrStart = json.IndexOf('[', idx);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            if (arrStart == -1 || arrEnd == -1 || arrEnd <= arrStart) return result;
            var inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            // If array contains objects (e.g., {"player_id":"..."}), do NOT treat as string array
            if (inner.IndexOf('{') != -1) return result;

            var parts = inner.Split(',');
            foreach (var token in parts)
            {
                var t = token.Trim();
                if (t.Length == 0) continue;
                // Only accept proper string literals: "value"
                if (!(t[0] == '"' && t[t.Length - 1] == '"')) continue;
                var s = t.Substring(1, t.Length - 2);
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
            return result;
        }

        // Extrai um mapa id->nome a partir de uma carga contendo "members": [...]
        private System.Collections.Generic.Dictionary<string, string> ExtractMembersMap(string json)
        {
            var map = new System.Collections.Generic.Dictionary<string, string>();
            int idxMembers = json.IndexOf("\"members\":");
            if (idxMembers == -1) return map;
            int arrStart = json.IndexOf('[', idxMembers);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            if (arrStart == -1 || arrEnd == -1 || arrEnd <= arrStart) return map;
            string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            // 1) Objetos { player_id, display_name }
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
                // sanitize accidental JSON fragments
                if (pid.StartsWith("{") || pid.StartsWith("[")) continue;
                string candidate = string.IsNullOrWhiteSpace(name) ? pid : name;
                if (map.TryGetValue(pid, out var existing))
                {
                    bool existingIsId = string.Equals(existing, pid, System.StringComparison.Ordinal);
                    bool candidateIsId = string.Equals(candidate, pid, System.StringComparison.Ordinal);
                    if (existingIsId && !candidateIsId) map[pid] = candidate;
                }
                else map[pid] = candidate;
            }

            // 2) Array de strings ["id1","id2"] — só adiciona se ainda não existir
            var ids = ExtractStringArray(json, "members");
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    var pid = ids[i];
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (pid.StartsWith("{") || pid.StartsWith("[")) continue;
                    if (!map.ContainsKey(pid)) map[pid] = pid;
                }
            }

            return map;
        }

        private void ReplaceLobbyMembers(System.Collections.Generic.Dictionary<string, string> idToName)
        {
            lobbyMemberIds.Clear();
            lobbyMemberDisplayNames.Clear();
            foreach (var kv in idToName)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                // Filter out tokens that look like embedded JSON fragments
                if (kv.Key.StartsWith("{") || kv.Key.StartsWith("[")) continue;
                var value = string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : kv.Value;
                lobbyMemberIds.Add(kv.Key);
                lobbyMemberDisplayNames.Add(value);
            }
            OnLobbyMembersChanged?.Invoke(lobbyMemberIds, lobbyMemberDisplayNames);
        }

        private void UpsertLobbyMember(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (id.StartsWith("{") || id.StartsWith("[")) return; // ignore malformed token
            int idx = lobbyMemberIds.IndexOf(id);
            string candidate = string.IsNullOrWhiteSpace(name) ? id : name;
            if (idx < 0)
            {
                lobbyMemberIds.Add(id);
                lobbyMemberDisplayNames.Add(candidate);
            }
            else
            {
                // Prefere atualizar quando o nome novo não é apenas o id
                bool existingIsId = string.Equals(lobbyMemberDisplayNames[idx], id, System.StringComparison.Ordinal);
                bool candidateIsId = string.Equals(candidate, id, System.StringComparison.Ordinal);
                if (existingIsId && !candidateIsId)
                {
                    lobbyMemberDisplayNames[idx] = candidate;
                }
            }
            OnLobbyMembersChanged?.Invoke(lobbyMemberIds, lobbyMemberDisplayNames);
        }

        private void RemoveLobbyMember(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            int idx = lobbyMemberIds.IndexOf(id);
            if (idx >= 0)
            {
                lobbyMemberIds.RemoveAt(idx);
                lobbyMemberDisplayNames.RemoveAt(idx);
                OnLobbyMembersChanged?.Invoke(lobbyMemberIds, lobbyMemberDisplayNames);
            }
        }

        private void ClearLobbyMembers()
        {
            lobbyMemberIds.Clear();
            lobbyMemberDisplayNames.Clear();
            OnLobbyMembersChanged?.Invoke(lobbyMemberIds, lobbyMemberDisplayNames);
        }

        private string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private bool IsMatchScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || matchSceneNames == null || matchSceneNames.Length == 0)
                return false;
            for (int i = 0; i < matchSceneNames.Length; i++)
            {
                if (string.Equals(sceneName, matchSceneNames[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void SetupCameraForLocal(GameObject player)
        {
            // Resolve camera references if not assigned
            #if CINEMACHINE
            if (vcam == null && freeLook == null)
            {
                freeLook = FindObjectOfType<CinemachineFreeLook>();
                if (freeLook == null)
                    vcam = FindObjectOfType<CinemachineVirtualCamera>();
            }
            #endif

            // Determine follow/lookAt targets
            Transform follow = cameraFollowOverride != null ? cameraFollowOverride : FindChildByNames(player.transform, new[] { "CameraTarget", "Head", "LookAt", "Spine" }) ?? player.transform;
            Transform lookAt = cameraLookAtOverride != null ? cameraLookAtOverride : follow;

            // Ensure player has a CameraTarget child for user camera scripts
            if (player.transform.Find("CameraTarget") == null)
            {
                var camT = new GameObject("CameraTarget").transform;
                camT.SetParent(player.transform, false);
                camT.localPosition = new Vector3(0f, 1.6f, 0f);
                // if follow was player root, prefer the new camera target
                if (follow == player.transform) follow = camT;
                if (lookAt == player.transform) lookAt = camT;
            }

            // If a custom camera follow script is present, let it handle things
            var customFollow = FindObjectOfType<CameraFollowLocalPlayer>();
            if (customFollow != null)
            {
                return;
            }

            if (
#if CINEMACHINE
                freeLook == null && vcam == null
#else
                true
#endif
               )
            {
                var mainCam = Camera.main;
#if CINEMACHINE
                if (mainCam != null && mainCam.GetComponent<CinemachineBrain>() == null)
                {
                    // Only add Cinemachine brain if available
                    mainCam.gameObject.AddComponent<CinemachineBrain>();
                }
#endif
                var goName = "Gameplay Camera (Auto)";
#if CINEMACHINE
                var go = new GameObject(goName);
                freeLook = go.AddComponent<CinemachineFreeLook>();
                freeLook.Priority = 100;
                freeLook.m_CommonLens = true;
                freeLook.m_Lens.FieldOfView = 40f;
#else
                // If Cinemachine is not installed, use main camera if available; otherwise create one
                Camera cam = mainCam != null ? mainCam : new GameObject(goName).AddComponent<Camera>();
                cam.fieldOfView = 40f;
                var followCam = cam.gameObject.GetComponent<SimpleFollowCamera>();
                if (followCam == null) followCam = cam.gameObject.AddComponent<SimpleFollowCamera>();
                followCam.target = follow;
                followCam.lookAt = lookAt;
#endif
            }

            if (
#if CINEMACHINE
                freeLook != null
#else
                false
#endif
               )
            {
#if CINEMACHINE
                freeLook.Follow = follow;
                freeLook.LookAt = lookAt;
                // Optionally boost priority to ensure it becomes the active vcam
                if (freeLook.Priority < 100) freeLook.Priority = 100;
#endif
            }
            else if (
#if CINEMACHINE
                vcam != null
#else
                false
#endif
                )
            {
#if CINEMACHINE
                vcam.Follow = follow;
                vcam.LookAt = lookAt;
                if (vcam.Priority < 100) vcam.Priority = 100;
#endif
            }
        }

        private Transform FindChildByNames(Transform root, string[] names)
        {
            foreach (var n in names)
            {
                var t = root.Find(n);
                if (t != null) return t;
            }
            // Deep search as fallback
            foreach (Transform child in root)
            {
                var r = FindChildByNames(child, names);
                if (r != null) return r;
            }
            return null;
        }
    }
}