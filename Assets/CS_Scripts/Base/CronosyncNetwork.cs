using UnityEngine;
using CS.Core.Networking;
using CS.Core.Systems;
using CS.Core.Spawning;

namespace CS.Base
{
    [AddComponentMenu("ChronoSync/Compat/CronosyncNetwork")]
    public class CronosyncNetwork : MonoBehaviour
    {
        public static CronosyncNetwork Instance { get; private set; }

        [Header("Refs")]
        public ChronoSyncRCPWebSocket webSocket;
        public GameSessionManager session;
        public PlayerSpawner spawner;

        public string LocalPlayerId => (webSocket != null && !string.IsNullOrEmpty(webSocket.assignedPlayerId)) ? webSocket.assignedPlayerId : (webSocket != null ? webSocket.playerId : "");
        public string CurrentLobby => (session != null ? session.currentLobby : "");

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (webSocket == null) webSocket = FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (session == null) session = GameSessionManager.Ensure();
            if (spawner == null) spawner = FindObjectOfType<PlayerSpawner>();
        }

        public void RaiseEvent(int code, object content, string entityId = null)
        {
            if (webSocket == null || !webSocket.IsConnected) return;
            string payload = "{\"event\":\"custom_event\",\"code\":" + code + "," +
                             (string.IsNullOrEmpty(entityId) ? "" : "\"entity_id\":\"" + Escape(entityId) + "\",") +
                             "\"content\":" + MiniJson.Serialize(content) + "}";
            webSocket.Send(payload);
        }

        private string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
