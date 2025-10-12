using UnityEngine;
using CS.Core.Auth;
using CS.Core.Networking;
using CS.Core.Systems;

namespace CS.UI
{
    // Attach this to UiController root. It will find the Systems object and wire common references.
    [DefaultExecutionOrder(-50)]
    public sealed class UiAutoBinder : MonoBehaviour
    {
        [Tooltip("Optional explicit reference to the Systems root GameObject. If null, it will be found in scene by components.")]
        public GameObject systemsRoot;

        private void Awake()
        {
            // Find Systems components
            var auth = systemsRoot ? systemsRoot.GetComponentInChildren<ChronoSyncRCPAuth>(true) : FindObjectOfType<ChronoSyncRCPAuth>(true);
            var ws = systemsRoot ? systemsRoot.GetComponentInChildren<ChronoSyncRCPWebSocket>(true) : FindObjectOfType<ChronoSyncRCPWebSocket>(true);
            var gfm = systemsRoot ? systemsRoot.GetComponentInChildren<GameFlowManager>(true) : FindObjectOfType<GameFlowManager>(true);
            var gsm = systemsRoot ? systemsRoot.GetComponentInChildren<GameSessionManager>(true) : FindObjectOfType<GameSessionManager>(true);

            // Bind Auth UI
            foreach (var authUi in GetComponentsInChildren<ChronoSyncRCPAuthUI>(true))
            {
                // ChronoSyncRCPAuthUI busca Auth via GetComponent/FindObjectOfType como fallback, então aqui apenas asseguramos que existe
                if (auth == null)
                {
                    Debug.LogError("[UiAutoBinder] ChronoSyncRCPAuth não encontrado para AuthUI.");
                }
            }

            // Bind Lobby panel websocket
            foreach (var lobby in GetComponentsInChildren<ChronoSyncRCPLobby>(true))
            {
                if (lobby != null && lobby.webSocket == null && ws != null)
                {
                    lobby.webSocket = ws;
                }
            }

            foreach (var members in GetComponentsInChildren<ChronoSyncRCPLobbyMembersPanel>(true))
            {
                // Members panel resolves ws via FindObjectOfType, so no hard field to set.
                // Just ensure it will find the existing Systems instance.
                if (ws == null)
                {
                    Debug.LogWarning("[UiAutoBinder] WebSocket não encontrado para LobbyMembersPanel.");
                }
            }

            // Ensure managers singletons exist
            if (GameFlowManager.Instance == null && gfm == null)
            {
                Debug.LogWarning("[UiAutoBinder] GameFlowManager não encontrado na cena.");
            }
            if (GameSessionManager.Instance == null && gsm == null)
            {
                Debug.LogWarning("[UiAutoBinder] GameSessionManager não encontrado na cena.");
            }
        }
    }
}
