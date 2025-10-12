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

        // Eventos de membros
        public event Action<string, string> OnMemberJoined; // (playerId, displayName)
        public event Action<string> OnMemberLeft;           // (playerId)

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
        }

        private void Start()
        {
            if (gameSessionManager == null) gameSessionManager = GameSessionManager.Instance ?? FindObjectOfType<GameSessionManager>();
            if (webSocket == null) webSocket = FindObjectOfType<ChronoSyncRCPWebSocket>();

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
                RebuildMembersFromGsm(gameSessionManager.lobbyMemberIds, gameSessionManager.lobbyMemberDisplayNames, notifyAsJoin: false);
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
        #endregion

        #region Internal sync with GameSessionManager
        private void OnGsmLobbyMembersChanged(List<string> ids, List<string> names)
        {
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
        private void EmitConfigChanged()
        {
            OnRoomConfigChanged?.Invoke(roomName, maxPlayers, playerCount);
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