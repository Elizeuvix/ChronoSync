using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CS.Core.Identity
{    
    public class PlayerStats : MonoBehaviour
    {
        public TMP_Text usernameText;

        public void SetUsername(string username)
        {
            if (usernameText != null)
            {
                usernameText.text = username;
            }
            else
            {
                Debug.LogWarning("Player não atribuído.");
            }
        }
    }
}