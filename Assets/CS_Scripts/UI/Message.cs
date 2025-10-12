using UnityEngine;
using TMPro;
using System.Collections;

namespace CS.UI
{
    public class Message : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private float clearDelay = 20f; // tempo em segundos para limpar após atualizar a mensagem

        private Coroutine clearCoroutine;

        void Start()
        {
            if (messageText == null)
                messageText = GetComponentInChildren<TMP_Text>();

            if (messageText != null)
                messageText.text = "ChronoSyncRCP is updating...";
        }

        public void SetMessage(string message)
        {
            if (messageText != null)
                messageText.text = message;

            Debug.Log("Update Message: " + message);

            // Reinicia a contagem sempre que uma nova mensagem é definida
            if (clearCoroutine != null)
                StopCoroutine(clearCoroutine);

            clearCoroutine = StartCoroutine(ClearMessageAfterDelay());
        }

        private IEnumerator ClearMessageAfterDelay()
        {
            yield return new WaitForSeconds(clearDelay);

            ClearMessage();
            clearCoroutine = null;
        }

        public void ClearMessage()
        {
            if (messageText != null)
                messageText.text = "";

            Debug.Log("Message cleared after delay.");
        }
    }
}
