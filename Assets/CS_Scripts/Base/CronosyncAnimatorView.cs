using UnityEngine;
using CS.Core.Networking;

namespace CS.Base
{
    [AddComponentMenu("ChronoSync/Compat/CronosyncAnimatorView")]
    public class CronosyncAnimatorView : MonoBehaviour
    {
        public Animator animator;
        public string entityId;
        public bool isMine;
        [Header("Parameters")] public string speedParam = "Speed";
        public string forwardParam = "Forward";
        public string onGroundParam = "OnGround";
        public float sendHz = 10f;
        private float nextSend;

        private ChronoSyncRCPWebSocket ws;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            ws = FindObjectOfType<ChronoSyncRCPWebSocket>();
        }

        public void ApplyIdentity(string id, bool mine)
        {
            entityId = id; isMine = mine;
        }

        private void Update()
        {
            if (!isMine || ws == null || !ws.IsConnected) return;
            if (Time.time < nextSend) return;
            nextSend = Time.time + (1f / Mathf.Max(1f, sendHz));
            var content = new System.Collections.Generic.Dictionary<string, object>();
            if (!string.IsNullOrEmpty(speedParam) && animator != null) content["speed"] = animator.GetFloat(speedParam);
            if (!string.IsNullOrEmpty(forwardParam) && animator != null) content["forward"] = animator.GetFloat(forwardParam);
            if (!string.IsNullOrEmpty(onGroundParam) && animator != null) content["onGround"] = animator.GetBool(onGroundParam);
            var payload = "{\"event\":\"custom_event\",\"code\":1001,\"entity_id\":\"" + Escape(entityId) + "\",\"content\":" + MiniJson.Serialize(content) + "}";
            ws.Send(payload);
        }

        // Call from WebSocket listener when receiving custom_event code 1001
        public void ApplyRemote(object content)
        {
            if (animator == null || content == null) return;
            var dict = content as System.Collections.Generic.Dictionary<string, object>;
            if (dict == null) return;
            if (dict.TryGetValue("speed", out var sv) && sv is float sf && !string.IsNullOrEmpty(speedParam)) animator.SetFloat(speedParam, sf);
            if (dict.TryGetValue("forward", out var fv) && fv is float ff && !string.IsNullOrEmpty(forwardParam)) animator.SetFloat(forwardParam, ff);
            if (dict.TryGetValue("onGround", out var gv) && gv is bool gb && !string.IsNullOrEmpty(onGroundParam)) animator.SetBool(onGroundParam, gb);
        }

        private string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
