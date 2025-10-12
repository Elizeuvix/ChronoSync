using System;
using UnityEngine;

namespace CS.Core.Identity
{
    public enum Team
    {
        Neutral = 0,
        TeamA = 1,
        TeamB = 2,
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("ChronoSync/Player Identity")]
    public class PlayerIdentity : MonoBehaviour
    {
        [Header("Identity")] 
        [SerializeField] private string playerId;
        [SerializeField] private string nickname = "Player";
        [SerializeField] private Team team = Team.Neutral;
        [Tooltip("Mark this as the local player on this client.")]
        [SerializeField] private bool isLocal;
        public PlayerStats playerStats;

        public static PlayerIdentity Local { get; private set; }

        public string PlayerId => playerId;
        public string Nickname => nickname;
        public Team Team => team;
        public bool IsLocal => isLocal;

        public event Action<PlayerIdentity> IdentityChanged;

        private void Awake()
        {
            if (isLocal) SetLocal();
            playerStats = GetComponentInChildren<PlayerStats>();            
        }

        private void OnValidate()
        {
            // Keep static reference in editor too for previewing nameplates
            if (isLocal)
            {
                Local = this;
            }
        }

        public void SetAll(string newPlayerId, string newNickname, Team newTeam, bool local)
        {
            playerId = newPlayerId;
            nickname = string.IsNullOrWhiteSpace(newNickname) ? "Player" : newNickname;
            team = newTeam;
            isLocal = local;
            playerStats.SetUsername(nickname);
            if (isLocal) SetLocal();
            RaiseChanged();
        }

        public void SetPlayerId(string newPlayerId)
        {
            playerId = newPlayerId;
            RaiseChanged();
        }

        public void SetNickname(string newNickname)
        {
            nickname = string.IsNullOrWhiteSpace(newNickname) ? "Player" : newNickname;
            RaiseChanged();
        }

        public void SetTeam(Team newTeam)
        {
            team = newTeam;
            RaiseChanged();
        }

        public void SetIsLocal(bool local)
        {
            isLocal = local;
            if (isLocal) SetLocal();
            RaiseChanged();
        }

        public bool IsFriendlyTo(PlayerIdentity other)
        {
            if (other == null) return false;
            if (team == Team.Neutral || other.team == Team.Neutral) return false;
            return team == other.team;
        }

        public bool IsEnemyTo(PlayerIdentity other)
        {
            if (other == null) return false;
            if (team == Team.Neutral || other.team == Team.Neutral) return false;
            return team != other.team;
        }

        private void SetLocal()
        {
            Local = this;
        }

        private void RaiseChanged()
        {
            IdentityChanged?.Invoke(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw a small label icon above the player for quick visual debugging in Scene view
            var pos = transform.position + Vector3.up * 2f;
            var c = isLocal ? new Color(0f, 1f, 1f, 0.6f) : (team == Team.Neutral ? new Color(1f, 1f, 1f, 0.6f) : (team == Team.TeamA ? new Color(0.3f, 1f, 0.3f, 0.6f) : new Color(1f, 0.3f, 0.3f, 0.6f)));
            UnityEditor.Handles.color = c;
            UnityEditor.Handles.SphereHandleCap(0, pos, Quaternion.identity, 0.15f, EventType.Repaint);
            UnityEditor.Handles.Label(pos + Vector3.up * 0.1f, $"{nickname} [{team}]" + (isLocal ? " (Local)" : string.Empty));
        }
#endif
    }
}
