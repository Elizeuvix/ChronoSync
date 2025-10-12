using System;
using System.Collections.Generic;
using UnityEngine;
using CS.Core.Networking;
using CS.Core.Config;

namespace CS.Core.Systems
{
    /// <summary>
    /// Núcleo de configuração/estado do ChronoSync.
    /// - Mantém configurações da sala (RoomName, MaxPlayers, PlayerCount)
    /// - Exponde getters/setters com eventos de alteração
    /// - Notifica entrada/saída de membros (espelha GameSessionManager)
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PersistentRoot))]
    [RequireComponent(typeof(GameSessionManager))]
    [RequireComponent(typeof(ChronoSyncRCPWebSocket))]
    [DefaultExecutionOrder(-9000)]
    public sealed class ChronoSyncCore : MonoBehaviour
    {
        public static ChronoSyncCore Instance { get; private set; }

    [Header("Room Config")]
        [SerializeField] private string roomName = string.Empty;
        [SerializeField] [Min(1)] private int maxPlayers = 2;
        [SerializeField] [ReadOnlyInspector] private int playerCount = 0; // derivado da lista de membros

    [Header("Config Defaults")]
    [Tooltip("Apply defaults from ChronoSyncConfig when scene/inspector values are empty.")]
    [SerializeField] private bool applyDefaultsFromConfig = true;

        [Header("References (auto)")]
        [SerializeField] private GameSessionManager gameSessionManager;
        [SerializeField] private ChronoSyncRCPWebSocket webSocket;

        // Eventos de configuração
        public event Action<string> OnRoomNameChanged;
        public event Action<int> OnMaxPlayersChanged;
        public event Action<int> OnPlayerCountChanged;
        /// <summary>Disparado quando qualquer configuração principal muda (roomName, maxPlayers, playerCount)</summary>
        public event Action<string, int, int> OnRoomConfigChanged; // (roomName, maxPlayers, playerCount)
    // Owner tracking
    [SerializeField] [ReadOnlyInspector] private string lobbyOwnerId = string.Empty;
    public event Action<string> OnOwnerChanged; // owner player id
    // In-room tracking
    [SerializeField] [ReadOnlyInspector] private bool isInRoom = false;
    public event Action<bool> OnIsInRoomChanged;

        // Eventos de membros
        public event Action<string, string> OnMemberJoined; // (playerId, displayName)
        public event Action<string> OnMemberLeft;           // (playerId)

    // Rooms/lobbies currently advertised by server
    public event Action<List<string>> OnRoomsListUpdated;
    private readonly List<string> _rooms = new List<string>();

        // Cache de membros atuais para diff
        private readonly Dictionary<string, string> _members = new Dictionary<string, string>();
    // Ordem de membros (mantém a mesma ordem informada pelo GameSessionManager)
    private readonly List<string> _orderedIds = new List<string>();

        #region Lifecycle
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure refs from the same persistent Systems object
            if (gameSessionManager == null) gameSessionManager = GetComponent<GameSessionManager>();
            if (webSocket == null) webSocket = GetComponent<ChronoSyncRCPWebSocket>();
        }

        private void Start()
        {
            if (gameSessionManager == null) gameSessionManager = GameSessionManager.Instance ?? GetComponent<GameSessionManager>();
            if (webSocket == null) webSocket = GetComponent<ChronoSyncRCPWebSocket>();

            if (applyDefaultsFromConfig)
            {
                // Apply default values if not set in scene
                if (string.IsNullOrEmpty(roomName)) roomName = ChronoSyncConfig.DEFAULT_ROOM_NAME;
                if (maxPlayers < 1) maxPlayers = Mathf.Max(1, ChronoSyncConfig.DEFAULT_MAX_PLAYERS);
            }

            if (gameSessionManager != null)
            {
                gameSessionManager.OnLobbyMembersChanged += OnGsmLobbyMembersChanged;
                // Inicializar estado a partir do GSM
                roomName = gameSessionManager.currentLobby ?? string.Empty;
                isInRoom = !string.IsNullOrEmpty(roomName);
                RebuildMembersFromGsm(gameSessionManager.lobbyMemberIds, gameSessionManager.lobbyMemberDisplayNames, notifyAsJoin: false);
            }

            if (webSocket != null)
            {
                webSocket.OnMessageReceived -= OnWsMessage;
                webSocket.OnMessageReceived += OnWsMessage;
                if (webSocket.IsConnected)
                {
                    RequestRoomsList();
                }
                else
                {
                    webSocket.OnConnected -= OnWsConnected;
                    webSocket.OnConnected += OnWsConnected;
                }
            }

            // Emite estado inicial
            EmitConfigChanged();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (gameSessionManager != null)
            {
                gameSessionManager.OnLobbyMembersChanged -= OnGsmLobbyMembersChanged;
            }
        }
        #endregion

        #region Public API (Getters/Sets)
        public string GetRoomName() => roomName;
        public int GetMaxPlayers() => maxPlayers;
        public int GetPlayerCount() => playerCount;
    public string GetLobbyOwnerId() => lobbyOwnerId;
    public bool IsInRoom() => isInRoom;

        public void SetRoomName(string newName)
        {
            newName = newName ?? string.Empty;
            if (roomName == newName) return;
            roomName = newName;
            OnRoomNameChanged?.Invoke(roomName);
            EmitConfigChanged();
            // Opcional: notificar servidor de alteração de nome (se existir suporte)
            // if (webSocket != null && webSocket.IsConnected)
            //     webSocket.Send($"{{\"event\":\"update_lobby\",\"lobby\":\"{Escape(roomName)}\"}}");
        }

        public void SetMaxPlayers(int newMax)
        {
            newMax = Mathf.Max(1, newMax);
            if (maxPlayers == newMax) return;
            maxPlayers = newMax;
            OnMaxPlayersChanged?.Invoke(maxPlayers);
            EmitConfigChanged();
            // Opcional: notificar servidor da nova capacidade
            // if (webSocket != null && webSocket.IsConnected && !string.IsNullOrEmpty(roomName))
            //     webSocket.Send($"{{\"event\":\"set_lobby_capacity\",\"lobby\":\"{Escape(roomName)}\",\"max_players\":{maxPlayers}}}");
        }

        public IReadOnlyDictionary<string, string> GetMembersSnapshot()
        {
            return new Dictionary<string, string>(_members);
        }

        // Retorna lista de IDs na ordem de entrada comunicada pelo GSM
        public List<string> GetPlayerListById()
        {
            return new List<string>(_orderedIds);
        }

        // Retorna lista de DisplayNames alinhada com GetPlayerListById()
        public List<string> GetPlayerListByName()
        {
            var names = new List<string>(_orderedIds.Count);
            for (int i = 0; i < _orderedIds.Count; i++)
            {
                var id = _orderedIds[i];
                if (_members.TryGetValue(id, out var name)) names.Add(name);
                else names.Add(id);
            }
            return names;
        }

        // Rooms API
        public IReadOnlyList<string> GetRoomsList() => _rooms.AsReadOnly();
        public void RequestRoomsList()
        {
            if (webSocket != null && webSocket.IsConnected)
            {
                webSocket.Send("{\"event\":\"request_lobby_list\"}");
            }
        }

        // Join/Leave by room name or by a playerId-like token (heuristic resolution)
        public void JoinRoom(string playerIdOrName)
        {
            if (webSocket == null || !webSocket.IsConnected) return;
            var target = ResolveLobbyFromParam(playerIdOrName);
            if (string.IsNullOrWhiteSpace(target))
            {
                // Try to refresh list; consumer should retry after update
                RequestRoomsList();
                return;
            }
            webSocket.Send("{\"event\":\"join_lobby\",\"lobby\":\"" + Escape(target) + "\"}");
            // Optimistic local update; server will confirm and Core will sync as well
            SetRoomName(target);
        }

        public void LeaveRoom(string playerIdOrName)
        {
            if (webSocket == null || !webSocket.IsConnected) return;

            // Determine current lobby context
            var lobby = !string.IsNullOrEmpty(roomName)
                ? roomName
                : (gameSessionManager != null ? gameSessionManager.currentLobby : string.Empty);

            // If caller provided a player id different from the local one, interpret as a kick/remove
            if (!string.IsNullOrEmpty(playerIdOrName) && !string.IsNullOrEmpty(lobby))
            {
                bool looksPlayerId = playerIdOrName.StartsWith("p-", StringComparison.OrdinalIgnoreCase) ||
                                      (playerIdOrName.Length >= 6 && playerIdOrName.Contains("-"));
                var localId = gameSessionManager != null ? gameSessionManager.localPlayerId : null;
                if (looksPlayerId && !string.Equals(playerIdOrName, localId, StringComparison.Ordinal))
                {
                    // Host removing a specific member
                    webSocket.Send("{\"event\":\"remove_from_lobby\",\"lobby\":\"" + Escape(lobby) + "\",\"player_id\":\"" + Escape(playerIdOrName) + "\"}");
                    return;
                }
            }

            // Otherwise, leave the lobby ourselves
            var leaveLobby = !string.IsNullOrEmpty(lobby) ? lobby : ResolveLobbyFromParam(playerIdOrName);
            if (string.IsNullOrWhiteSpace(leaveLobby)) return;
            webSocket.Send("{\"event\":\"leave_lobby\",\"lobby\":\"" + Escape(leaveLobby) + "\"}");
            // Optimistic local update
            if (string.Equals(roomName, leaveLobby, StringComparison.Ordinal)) SetRoomName(string.Empty);
        }

        // Host-only: explicitly remove a member from the current lobby
        public void KickPlayer(string playerId)
        {
            if (webSocket == null || !webSocket.IsConnected) return;
            if (string.IsNullOrEmpty(playerId)) return;
            var lobby = !string.IsNullOrEmpty(roomName)
                ? roomName
                : (gameSessionManager != null ? gameSessionManager.currentLobby : string.Empty);
            if (string.IsNullOrEmpty(lobby)) return;
            webSocket.Send("{\"event\":\"remove_from_lobby\",\"lobby\":\"" + Escape(lobby) + "\",\"player_id\":\"" + Escape(playerId) + "\"}");
        }
        #endregion

        #region Internal sync with GameSessionManager
        private void OnGsmLobbyMembersChanged(List<string> ids, List<string> names)
        {
            // Keep roomName in sync with GSM when it changes via server events
            if (gameSessionManager != null)
            {
                var gsmLobby = gameSessionManager.currentLobby ?? string.Empty;
                if (!string.Equals(gsmLobby, roomName, StringComparison.Ordinal))
                {
                    SetRoomName(gsmLobby);
                }
            }
            RebuildMembersFromGsm(ids, names, notifyAsJoin: true);
        }

        private void RebuildMembersFromGsm(List<string> ids, List<string> names, bool notifyAsJoin)
        {
            // Detecta quem saiu
            var toRemove = new List<string>();
            foreach (var kv in _members)
            {
                if (ids == null || !ids.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            foreach (var id in toRemove)
            {
                _members.Remove(id);
                OnMemberLeft?.Invoke(id);
            }

            // Adiciona/atualiza quem entrou
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    var name = (names != null && i < names.Count) ? names[i] : id;
                    if (!_members.ContainsKey(id))
                    {
                        _members[id] = name;
                        if (notifyAsJoin) OnMemberJoined?.Invoke(id, name);
                    }
                    else
                    {
                        _members[id] = name; // atualiza display name se mudou
                    }
                }
            }

            // Atualiza ordem
            _orderedIds.Clear();
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (_members.ContainsKey(id)) _orderedIds.Add(id);
                }
            }
            // Garante que qualquer id presente no mapa, mas não informado em ids, entre no final (robustez)
            foreach (var id in _members.Keys)
            {
                if (!_orderedIds.Contains(id)) _orderedIds.Add(id);
            }

            // Atualiza contagem
            int newCount = _members.Count;
            if (playerCount != newCount)
            {
                playerCount = newCount;
                OnPlayerCountChanged?.Invoke(playerCount);
            }
            EmitConfigChanged();
        }
        #endregion

        #region Utils
        private void OnWsConnected()
        {
            try { RequestRoomsList(); }
            catch { }
        }

        private void OnWsMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (msg.Contains("\"event\":\"lobby_list\""))
            {
                var list = ParseRoomsFromLobbyList(msg);
                if (list != null)
                {
                    _rooms.Clear();
                    _rooms.AddRange(list);
                    OnRoomsListUpdated?.Invoke(new List<string>(_rooms));
                }
            }

            // Keep room name in sync with server events
            if (msg.Contains("\"event\":\"match_start\"") || msg.Contains("\"event\":\"join_lobby\""))
            {
                var lobby = ExtractJsonValue(msg, "lobby");
                if (!string.IsNullOrEmpty(lobby)) SetRoomName(lobby);
                var owner = ExtractJsonValue(msg, "owner_id");
                if (!string.IsNullOrEmpty(owner)) SetOwner(owner);
                var maxStr = ExtractJsonValue(msg, "max_players");
                if (int.TryParse(maxStr, out var mx) && mx > 0) SetMaxPlayers(mx);
                if (!string.IsNullOrEmpty(lobby)) SetIsInRoom(true);
            }
            if (msg.Contains("\"event\":\"leave_lobby\"") || msg.Contains("\"event\":\"lobby_cancel\"") || msg.Contains("\"event\":\"lobby_closed\""))
            {
                SetRoomName(string.Empty);
                SetOwner(string.Empty);
                SetIsInRoom(false);
            }
            if (msg.Contains("\"event\":\"lobby_members\""))
            {
                var owner = ExtractJsonValue(msg, "owner_id");
                if (!string.IsNullOrEmpty(owner)) SetOwner(owner);
                var maxStr = ExtractJsonValue(msg, "max_players");
                if (int.TryParse(maxStr, out var mx) && mx > 0) SetMaxPlayers(mx);
            }
            if (msg.Contains("\"event\":\"kicked_from_lobby\""))
            {
                SetRoomName(string.Empty);
                SetOwner(string.Empty);
                SetIsInRoom(false);
            }
        }

        private List<string> ParseRoomsFromLobbyList(string json)
        {
            // Expected shape: {"event":"lobby_list","lobbies":["A","B"]}
            var result = new List<string>();
            int keyIdx = json.IndexOf("\"lobbies\":");
            if (keyIdx == -1) return result;
            int arrStart = json.IndexOf('[', keyIdx);
            int arrEnd = json.IndexOf(']', arrStart + 1);
            if (arrStart == -1 || arrEnd == -1 || arrEnd <= arrStart) return result;
            var inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            if (inner.IndexOf('{') != -1) return result; // ignore malformed format
            var parts = inner.Split(',');
            foreach (var token in parts)
            {
                var t = token.Trim();
                if (t.Length < 2) continue;
                if (t[0] != '"' || t[t.Length - 1] != '"') continue;
                var value = t.Substring(1, t.Length - 2);
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            }
            return result;
        }

        private string ExtractJsonValue(string json, string key)
        {
            int idx = json.IndexOf("\"" + key + "\":");
            if (idx == -1) return string.Empty;
            int start = json.IndexOf('"', idx + key.Length + 3);
            int end = json.IndexOf('"', start + 1);
            if (start == -1 || end == -1) return string.Empty;
            return json.Substring(start + 1, end - start - 1);
        }

        private string ResolveLobbyFromParam(string playerIdOrName)
        {
            if (string.IsNullOrWhiteSpace(playerIdOrName)) return string.Empty;
            // If exact match in rooms list, use it
            for (int i = 0; i < _rooms.Count; i++)
            {
                if (string.Equals(_rooms[i], playerIdOrName, StringComparison.Ordinal)) return _rooms[i];
            }
            // If param looks like a player id (e.g., starts with 'p-' or similar), and we have members, try matching by display name too
            // Note: Without a server-side mapping (room->owner/player), we fallback to direct match
            // Heuristic: case-insensitive contains or equals
            string candidate = null;
            for (int i = 0; i < _rooms.Count; i++)
            {
                var r = _rooms[i];
                if (r.Equals(playerIdOrName, StringComparison.OrdinalIgnoreCase)) return r;
                if (candidate == null && r.IndexOf(playerIdOrName, StringComparison.OrdinalIgnoreCase) >= 0) candidate = r;
            }
            return candidate ?? string.Empty;
        }
        private void EmitConfigChanged()
        {
            OnRoomConfigChanged?.Invoke(roomName, maxPlayers, playerCount);
        }

        private void SetOwner(string newOwner)
        {
            newOwner = newOwner ?? string.Empty;
            if (lobbyOwnerId == newOwner) return;
            lobbyOwnerId = newOwner;
            OnOwnerChanged?.Invoke(lobbyOwnerId);
        }

        private void SetIsInRoom(bool value)
        {
            if (isInRoom == value) return;
            isInRoom = value;
            OnIsInRoomChanged?.Invoke(isInRoom);
        }

        private string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
        #endregion
    }

    // Pequeno atributo para exibir como somente leitura no Inspetor (visual)
    internal sealed class ReadOnlyInspectorAttribute : PropertyAttribute { }
}