using UnityEngine;
using UnityEngine.SceneManagement;
using CS.UI;

namespace CS.Core.Systems
{
    public class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        public enum AppState { Auth, Lobby, Match }
        public AppState State { get; private set; } = AppState.Auth;

        [Header("Pre-Game UI Roots")]
        public GameObject authRoot;   // Painel de login/registro
        public GameObject lobbyRoot;  // Painel de browser/lobby (lista/criação)
        public ChronoSyncRCPLobbyMembersPanel lobbyMembersPanel; // Painel de membros

        [Header("Match Setup")]
        [Tooltip("Cena padrão da partida, usada se nenhuma for informada")]
        public string defaultMatchScene = "";

        [Header("Shared/Persistent UI (opcional)")]
        public GameObject chatRoot; // Se desejar manter chat overlay entre cenas

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

        public void EnterAuth()
        {
            State = AppState.Auth;
            SafeSetActive(authRoot, true);
            SafeSetActive(lobbyRoot, false);
            if (lobbyMembersPanel != null) lobbyMembersPanel.Hide();
            // chatRoot fica a critério do jogo; aqui não alteramos
        }

        public void EnterLobby()
        {
            State = AppState.Lobby;
            SafeSetActive(authRoot, false);
            SafeSetActive(lobbyRoot, true);
            // Painel de membros é aberto/fechado pelo fluxo do Lobby
        }

        public void EnterMatch(string sceneName = null)
        {
            State = AppState.Match;
            // Esconder UIs pré-jogo
            SafeSetActive(authRoot, false);
            SafeSetActive(lobbyRoot, false);
            if (lobbyMembersPanel != null) lobbyMembersPanel.Hide();

            var target = string.IsNullOrWhiteSpace(sceneName) ? defaultMatchScene : sceneName;
            if (string.IsNullOrWhiteSpace(target))
            {
                // fallback: use first match scene from GameSessionManager if available
                var gsm = GameSessionManager.Instance ?? GameSessionManager.Ensure();
                if (gsm != null && gsm.matchSceneNames != null && gsm.matchSceneNames.Length > 0)
                {
                    target = gsm.matchSceneNames[0];
                }
            }
            if (string.IsNullOrWhiteSpace(target))
            {
                Debug.LogWarning("[GameFlowManager] Nenhuma cena de partida definida (defaultMatchScene vazio e parâmetro nulo).");
                return;
            }
            try
            {
                SceneManager.LoadScene(target);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameFlowManager] Falha ao carregar cena '{target}': {ex.Message}");
            }
        }

        private void SafeSetActive(GameObject go, bool active)
        {
            if (go != null) go.SetActive(active);
        }
    }
}