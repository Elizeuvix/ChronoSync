using UnityEngine;
using TMPro;

namespace CS.UI
{
    public class Message : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText; // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            if (messageText == null) messageText = GetComponentInChildren<TMP_Text>();
            if (messageText != null) messageText.text = "ChronoSyncRCP is updating...";
        }
        public void SetMessage(string message)
        {
            if (messageText != null) messageText.text = message;
            Debug.Log("Update Message: " + message);
        }
    }
}