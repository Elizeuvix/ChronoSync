using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using TMPro;
using CS.Core.Auth;
using CS.Core.Networking;
using CS.Core.Systems;

namespace CS.UI
{
    // Garante que o componente ChronoSyncRCPAuth estará presente
    [RequireComponent(typeof(ChronoSyncRCPAuth))]
    public class ChronoSyncRCPAuthUI : MonoBehaviour
    {
        // Campos originais (TMP) mantidos para não quebrar referências existentes
        public TMP_InputField usernameInput;
        public TMP_InputField passwordInput;
        // Campos opcionais (Legacy UGUI) para compatibilidade — preencha apenas se não usar TMP
        private InputField usernameInputLegacy;
        private InputField passwordInputLegacy;
        public Button registerButton;
        public Button loginButton;

        [SerializeField] private GameObject panelLogin;
        [SerializeField] private GameObject panelGroupLobby;

        private ChronoSyncRCPAuth auth;
        public ChronoSyncRCPWebSocket webSocket;

        private Message message;

        void Start()
        {
            Debug.Log($"usernameInput type: {usernameInput?.GetType()}");
            Debug.Log($"passwordInput type: {passwordInput?.GetType()}");
            Debug.Log($"registerButton type: {registerButton?.GetType()}");
            Debug.Log($"loginButton type: {loginButton?.GetType()}");

            message = GameObject.Find("PanelMessage").GetComponent<Message>();
            webSocket = GameObject.FindGameObjectWithTag("Systems").GetComponent<ChronoSyncRCPWebSocket>();

            // Ativa/desativa paineis somente se informados
            if (panelGroupLobby != null) panelGroupLobby.SetActive(false); else Debug.LogWarning("[AuthUI] panelConnection não atribuído.");
            if (panelLogin != null) panelLogin.SetActive(true); else Debug.LogWarning("[AuthUI] panelLogin não atribuído.");

            // Obtém o componente de Auth no mesmo GameObject, ou busca na cena como fallback
            auth = GetComponent<ChronoSyncRCPAuth>();
            if (auth == null) auth = FindObjectOfType<ChronoSyncRCPAuth>();
            if (auth == null) Debug.LogError("[AuthUI] Componente ChronoSyncRCPAuth não encontrado na cena. Adicione-o ao mesmo GameObject do AuthUI ou a outro GameObject.");

            // Protege assinatura de botões
            if (registerButton != null) registerButton.onClick.AddListener(OnRegister); else Debug.LogWarning("[AuthUI] registerButton não atribuído.");
            if (loginButton != null) loginButton.onClick.AddListener(OnLogin); else Debug.LogWarning("[AuthUI] loginButton não atribuído.");
        }

        async void OnRegister()
        {
            string typedName = GetUsernameText()?.Trim();
            if (string.IsNullOrEmpty(typedName))
            {
                SetStatus("Informe um nome de usuário.");
                return;
            }
            string upperLogin = typedName.ToUpperInvariant();
            // Reflete visualmente no campo
            if (usernameInput != null) usernameInput.text = upperLogin; else if (usernameInputLegacy != null) usernameInputLegacy.text = upperLogin;
            SetStatus($"Registrando {upperLogin}...");
            if (auth == null)
            {
                SetStatus("Configuração inválida: componente Auth ausente.");
                return;
            }
            bool success = await auth.Register(upperLogin, GetPasswordText());
            if (success)
                SetStatus($"Player '{upperLogin}' cadastrado no servidor!");
            else
                SetStatus($"Erro ao cadastrar '{upperLogin}'. Tente outro nome ou verifique a conexão.");
        }

        async void OnLogin()
        {
            string typedName = GetUsernameText()?.Trim();
            if (string.IsNullOrEmpty(typedName))
            {
                SetStatus("Informe um nome de usuário.");
                return;
            }
            string upperLogin = typedName.ToUpperInvariant();
            // Reflete visualmente no campo
            if (usernameInput != null) usernameInput.text = upperLogin; else if (usernameInputLegacy != null) usernameInputLegacy.text = upperLogin;
            SetStatus($"Logando {upperLogin}...");
            if (auth == null)
            {
                SetStatus("Configuração inválida: componente Auth ausente.");
                return;
            }
            bool success = await auth.Login(upperLogin, GetPasswordText());
            if (success)
            {
                webSocket.SetDisplayName(upperLogin);

                SetStatus($"Bem-vindo, {upperLogin}! Login realizado com sucesso.");
                panelGroupLobby.SetActive(true);
                // Enviar nome de exibição ao servidor (playerId é decidido pelo backend)
                var ws = FindObjectOfType<ChronoSyncRCPWebSocket>();
                if (ws != null)
                {
                    // Somente identificar após a API fornecer o id; depois enviar display_name
                    if (!string.IsNullOrEmpty(auth.lastPlayerId))
                    {
                        ws.SetPlayerId(auth.lastPlayerId);
                        ws.SetDisplayName(typedName); // mantém display como foi digitado originalmente
                    }
                }
                // Persistir para o fluxo de jogo/spawner
                var gsm = GameSessionManager.Ensure();
                gsm.SetLocalNickname(typedName); // nickname visível pode manter o formato original
                if (!string.IsNullOrEmpty(auth.lastPlayerId))
                {
                    gsm.UpdateLocalPlayerId(auth.lastPlayerId);
                }
                // Transição centralizada para o Lobby
                if (GameFlowManager.Instance != null)
                    GameFlowManager.Instance.EnterLobby();
                else
                {
                    // Fallback: manter comportamento antigo
                    if (panelGroupLobby != null) panelGroupLobby.SetActive(true);
                    if (panelLogin != null) panelLogin.SetActive(false);
                }
            }
            else
                SetStatus($"Erro ao logar '{upperLogin}'. Verifique usuário/senha ou tente novamente.");
        }

        private string GetUsernameText()
        {
            if (usernameInput != null) return usernameInput.text;
            if (usernameInputLegacy != null) return usernameInputLegacy.text;
            Debug.LogWarning("[ChronoSyncRCPAuthUI] Nenhum campo de username (TMP_InputField ou InputField) atribuído.");
            return string.Empty;
        }

        private string GetPasswordText()
        {
            if (passwordInput != null) return passwordInput.text;
            if (passwordInputLegacy != null) return passwordInputLegacy.text;
            Debug.LogWarning("[ChronoSyncRCPAuthUI] Nenhum campo de password (TMP_InputField ou InputField) atribuído.");
            return string.Empty;
        }

        private void SetStatus(string text)
        {
            message.SetMessage(text);
        }
    }
}
