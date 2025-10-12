using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CS.UI
{
    public class ToggleUI : MonoBehaviour
    {
        public GameObject groupLobbyPanel;
        public GameObject chatPanel;
        public Toggle toggleButton;
        // Start is called before the first frame update
        void Start()
        {
            toggleButton.onValueChanged.AddListener(OnToggleChanged);
            chatPanel.SetActive(false); // Chat começa oculto
        }

        private void OnToggleChanged(bool isOn)
        {
            groupLobbyPanel.SetActive(!isOn);
            chatPanel.SetActive(isOn);
        }
    }
}