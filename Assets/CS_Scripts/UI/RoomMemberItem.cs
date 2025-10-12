using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

namespace CS.UI
{
    public class RoomMemberItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text memberNameText;
        [SerializeField] private Button kickButton;
        private string _memberName;
        private UnityEngine.Events.UnityAction _additionalKickAction;

        /// <summary>
        /// Initializes the UI entry for a room member.
        /// By default, the kick button is hidden; call <see cref="SetIsLocalMaster"/> to control its visibility.
        /// </summary>
        public void Setup(string memberName)
        {
            // Store the member name for later use (e.g., KickRequested event)
            _memberName = memberName;

            // Kick button should only be visible to the lobby master.
            // Since this class doesn't decide who is master, hide by default and
            // let the parent/manager call SetIsLocalMaster(true) when appropriate.
            if (kickButton != null)
            {
                kickButton.gameObject.SetActive(false);
                // Ensure our internal click handler is wired
                WireKickHandler();
            }
            if (memberNameText != null)
            {
                memberNameText.text = memberName;
            }
        }

        /// <summary>
        /// Overload that also configures kick button visibility in one call.
        /// </summary>
        public void Setup(string memberName, bool isLocalMaster)
        {
            Setup(memberName);
            SetIsLocalMaster(isLocalMaster);
        }

        /// <summary>
        /// Controls the visibility of the kick button according to whether the local player is the lobby master.
        /// </summary>
        public void SetIsLocalMaster(bool isLocalMaster)
        {
            if (kickButton != null)
            {
                kickButton.gameObject.SetActive(isLocalMaster);
            }
        }

        /// <summary>
        /// Fired when the kick button is pressed. The string argument is the member name associated with this item.
        /// Parent UI/managers should subscribe to remove the player from the lobby/session.
        /// </summary>
        public event Action<string> KickRequested;

        private void WireKickHandler()
        {
            if (kickButton == null) return;
            kickButton.onClick.RemoveAllListeners();
            kickButton.onClick.AddListener(OnKickPressed);
        }

        private void OnKickPressed()
        {
            // Execute any additional action hooked by caller (e.g., confirmation, network call)
            _additionalKickAction?.Invoke();

            // Notify listeners to remove the player from the lobby/session
            KickRequested?.Invoke(_memberName);
            // Do NOT destroy UI immediately; wait for server to confirm removal via events,
            // so the list stays consistent if the action is denied or fails.
        }
        public void SetKickAction(UnityEngine.Events.UnityAction action)
        {
            if (kickButton != null)
            {
                // Allow caller to inject an extra action (e.g., network kick logic or confirmation dialog)
                _additionalKickAction = action;
                // Always ensure our internal flow handles lobby removal request and UI cleanup
                WireKickHandler();
            }
        }
    }
}