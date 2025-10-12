using UnityEngine;
using TMPro;
using UnityEngine.UI;
using CS.Core.Networking;

namespace CS.UI
{
    public class LobbyListItem : MonoBehaviour
    {
        public TMP_Text lobbyNameText;
        public TMP_Text memberCountText;
        public Button selectButton;

        private string lobbyName;
        private ChronoSyncRCPLobby lobbyManager;
        private int _currentCount = 0;
        private int _maxPlayers = 0; // unknown until provided

        public void Setup(string name, ChronoSyncRCPLobby manager)
        {
            lobbyName = name;
            lobbyManager = manager;
            lobbyNameText.text = lobbyName;
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnSelect);
            RefreshCountLabel();
        }

        private void OnSelect()
        {
            if (lobbyManager != null)
                lobbyManager.JoinLobby(lobbyName);
        }

        public void UpdateMemberCount(int count)
        {
            _currentCount = Mathf.Max(0, count);
            RefreshCountLabel();
        }

        public void UpdateMaxPlayers(int max)
        {
            _maxPlayers = Mathf.Max(0, max);
            RefreshCountLabel();
        }

        private void RefreshCountLabel()
        {
            if (memberCountText == null) return;
            if (_maxPlayers > 0)
                memberCountText.text = _currentCount.ToString() + "/" + _maxPlayers.ToString();
            else
                memberCountText.text = _currentCount.ToString() + "/?"; // unknown capacity
        }
    }
}