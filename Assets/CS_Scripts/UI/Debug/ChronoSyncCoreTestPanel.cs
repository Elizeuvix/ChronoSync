using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CS.Core.Systems;

namespace CS.UI
{
    [DisallowMultipleComponent]
    public class ChronoSyncCoreTestPanel : MonoBehaviour
    {
        [Header("Buttons (auto-find by name if null)")]
        public Button buttonGetName;
        public Button buttonGetMax;
        public Button buttonGetCount;
    public Button[] extraGetCountButtons; // optional duplicates (e.g., ButtonGetCount (1), (2))
    public Button buttonListMembers; // match: ButtonListMembers
    public Button buttonListRooms;   // match: ButtonListRooms

        [Header("Output Text (TMP or UGUI)")]
        public TMP_Text resultTMP;
        public Text resultUGUI;

        private void Awake()
        {
            // Auto-find by child names if not assigned
            if (buttonGetName == null) buttonGetName = FindButton("ButtonGetName");
            if (buttonGetMax == null) buttonGetMax = FindButton("ButtonGetMax");
            if (buttonGetCount == null) buttonGetCount = FindButton("ButtonGetCount");
            if (buttonListMembers == null) buttonListMembers = FindButton("ButtonListMembers");
            if (buttonListRooms == null) buttonListRooms = FindButton("ButtonListRooms");

            // Try to collect extra count buttons (ButtonGetCount (1), ButtonGetCount (2), ...)
            if (extraGetCountButtons == null || extraGetCountButtons.Length == 0)
            {
                var list = new System.Collections.Generic.List<Button>();
                for (int i = 1; i <= 3; i++) // search a few suffixed variants
                {
                    var b = FindButton($"ButtonGetCount ({i})");
                    if (b != null) list.Add(b);
                }
                extraGetCountButtons = list.ToArray();
            }

            if (resultTMP == null || resultUGUI == null)
            {
                var tr = transform.Find("TextResult");
                if (tr != null)
                {
                    if (resultTMP == null) resultTMP = tr.GetComponent<TMP_Text>();
                    if (resultUGUI == null) resultUGUI = tr.GetComponent<Text>();
                }
            }
        }

        private void OnEnable()
        {
            // Wire listeners
            if (buttonGetName != null) { buttonGetName.onClick.RemoveListener(OnGetName); buttonGetName.onClick.AddListener(OnGetName); }
            if (buttonGetMax != null) { buttonGetMax.onClick.RemoveListener(OnGetMax); buttonGetMax.onClick.AddListener(OnGetMax); }
            if (buttonGetCount != null) { buttonGetCount.onClick.RemoveListener(OnGetCount); buttonGetCount.onClick.AddListener(OnGetCount); }
            if (extraGetCountButtons != null)
            {
                foreach (var b in extraGetCountButtons)
                {
                    if (b == null) continue;
                    b.onClick.RemoveListener(OnGetCount);
                    b.onClick.AddListener(OnGetCount);
                }
            }
            if (buttonListMembers != null) { buttonListMembers.onClick.RemoveListener(OnListMembers); buttonListMembers.onClick.AddListener(OnListMembers); }
            if (buttonListRooms != null) { buttonListRooms.onClick.RemoveListener(OnListRooms); buttonListRooms.onClick.AddListener(OnListRooms); }

            // Auto-refresh rooms when updated (optional)
            if (ChronoSyncCore.Instance != null)
            {
                ChronoSyncCore.Instance.OnRoomsListUpdated -= HandleRoomsUpdated;
                ChronoSyncCore.Instance.OnRoomsListUpdated += HandleRoomsUpdated;
                ChronoSyncCore.Instance.OnMemberJoined -= HandleMembersChanged;
                ChronoSyncCore.Instance.OnMemberJoined += HandleMembersChanged;
                ChronoSyncCore.Instance.OnMemberLeft -= HandleMembersChangedIdOnly;
                ChronoSyncCore.Instance.OnMemberLeft += HandleMembersChangedIdOnly;
            }
        }

        private void OnDisable()
        {
            if (buttonGetName != null) buttonGetName.onClick.RemoveListener(OnGetName);
            if (buttonGetMax != null) buttonGetMax.onClick.RemoveListener(OnGetMax);
            if (buttonGetCount != null) buttonGetCount.onClick.RemoveListener(OnGetCount);
            if (extraGetCountButtons != null)
            {
                foreach (var b in extraGetCountButtons)
                {
                    if (b == null) continue;
                    b.onClick.RemoveListener(OnGetCount);
                }
            }
            if (buttonListMembers != null) buttonListMembers.onClick.RemoveListener(OnListMembers);
            if (buttonListRooms != null) buttonListRooms.onClick.RemoveListener(OnListRooms);

            if (ChronoSyncCore.Instance != null)
            {
                ChronoSyncCore.Instance.OnRoomsListUpdated -= HandleRoomsUpdated;
                ChronoSyncCore.Instance.OnMemberJoined -= HandleMembersChanged;
                ChronoSyncCore.Instance.OnMemberLeft -= HandleMembersChangedIdOnly;
            }
        }

        private Button FindButton(string childName)
        {
            var t = transform.Find(childName);
            if (t == null) return null;
            return t.GetComponent<Button>();
        }

        private void OnGetName()
        {
            if (ChronoSyncCore.Instance == null) { SetResult("Core não encontrado"); return; }
            var name = ChronoSyncCore.Instance.GetRoomName();
            SetResult(string.IsNullOrEmpty(name) ? "(sem sala)" : name);
        }

        private void OnGetMax()
        {
            if (ChronoSyncCore.Instance == null) { SetResult("Core não encontrado"); return; }
            SetResult("MaxPlayers: " + ChronoSyncCore.Instance.GetMaxPlayers());
        }

        private void OnGetCount()
        {
            if (ChronoSyncCore.Instance == null) { SetResult("Core não encontrado"); return; }
            SetResult("PlayerCount: " + ChronoSyncCore.Instance.GetPlayerCount());
        }

        private void OnListRooms()
        {
            if (ChronoSyncCore.Instance == null) { SetResult("Core não encontrado"); return; }
            ChronoSyncCore.Instance.RequestRoomsList();
            var rooms = ChronoSyncCore.Instance.GetRoomsList();
            SetResult(rooms != null && rooms.Count > 0 ? ("Rooms: " + string.Join(", ", rooms)) : "Rooms: (nenhuma)");
        }

        private void HandleRoomsUpdated(System.Collections.Generic.List<string> rooms)
        {
            // Optional: auto-print when updated
            if (buttonListRooms == null) return; // only auto-print if the panel has a rooms button
            SetResult(rooms != null && rooms.Count > 0 ? ("Rooms: " + string.Join(", ", rooms)) : "Rooms: (nenhuma)");
        }

        private void OnListMembers()
        {
            if (ChronoSyncCore.Instance == null) { SetResult("Core não encontrado"); return; }
            var ids = ChronoSyncCore.Instance.GetPlayerListById();
            var names = ChronoSyncCore.Instance.GetPlayerListByName();
            if (ids == null || ids.Count == 0)
            {
                SetResult("Members: (nenhum)");
                return;
            }
            var lines = new System.Collections.Generic.List<string>();
            lines.Add($"Members ({ids.Count}):");
            for (int i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var name = (names != null && i < names.Count) ? names[i] : id;
                lines.Add($"- {name} ({id})");
            }
            SetResult(string.Join("\n", lines));
        }

        private void HandleMembersChanged(string id, string displayName)
        {
            if (buttonListMembers == null) return;
            OnListMembers();
        }

        private void HandleMembersChangedIdOnly(string id)
        {
            if (buttonListMembers == null) return;
            OnListMembers();
        }

        private void SetResult(string text)
        {
            if (resultTMP != null) resultTMP.text = text;
            if (resultUGUI != null) resultUGUI.text = text;
            if (resultTMP == null && resultUGUI == null) Debug.Log(text);
        }
    }
}
