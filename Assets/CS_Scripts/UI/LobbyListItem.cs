using UnityEngine;
using TMPro;
using UnityEngine.UI;
using CS.Core.Networking;

namespace CS.UI
{
    public class LobbyListItem : MonoBehaviour
    {
        public TMP_Text lobbyNameText;
        public Button selectButton;

        private string lobbyName;
        private ChronoSyncRCPLobby lobbyManager;

        public void Setup(string name, ChronoSyncRCPLobby manager)
        {
            lobbyName = name;
            lobbyManager = manager;
            lobbyNameText.text = lobbyName;
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnSelect);
        }

        private void OnSelect()
        {
            if (lobbyManager != null)
                lobbyManager.JoinLobby(lobbyName);
        }
    }
}