using UnityEngine;
using CS.Core.Networking;

namespace CS.Base
{
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("ChronoSync/Compat/CronosyncRigidbodyView")]
    public class CronosyncRigidbodyView : MonoBehaviour
    {
        public string entityId;
        public bool isMine;
        public float sendHz = 10f;
        private float nextSend;
        private Rigidbody rb;
        private ChronoSyncRCPWebSocket ws;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            ws = FindObjectOfType<ChronoSyncRCPWebSocket>();
        }

        public void ApplyIdentity(string id, bool mine)
        {
            entityId = id; isMine = mine;
        }

        private void FixedUpdate()
        {
            if (!isMine || ws == null || !ws.IsConnected) return;
            if (Time.time < nextSend) return;
            nextSend = Time.time + (1f / Mathf.Max(1f, sendHz));
            var content = new System.Collections.Generic.Dictionary<string, object>
            {
                {"vel", new float[]{ rb.linearVelocity.x, rb.linearVelocity.y, rb.linearVelocity.z }},
                {"ang", new float[]{ rb.angularVelocity.x, rb.angularVelocity.y, rb.angularVelocity.z }},
                {"kin", rb.isKinematic}
            };
            var payload = "{\"event\":\"custom_event\",\"code\":1002,\"entity_id\":\"" + Escape(entityId) + "\",\"content\":" + MiniJson.Serialize(content) + "}";
            ws.Send(payload);
        }

        public void ApplyRemote(object content)
        {
            if (rb == null || content == null) return;
            var dict = content as System.Collections.Generic.Dictionary<string, object>;
            if (dict == null) return;
            if (dict.TryGetValue("vel", out var vv) && vv is System.Collections.IList vel && vel.Count == 3)
            {
                rb.linearVelocity = new Vector3(ConvertToFloat(vel[0]), ConvertToFloat(vel[1]), ConvertToFloat(vel[2]));
            }
            if (dict.TryGetValue("ang", out var av) && av is System.Collections.IList ang && ang.Count == 3)
            {
                rb.angularVelocity = new Vector3(ConvertToFloat(ang[0]), ConvertToFloat(ang[1]), ConvertToFloat(ang[2]));
            }
            if (dict.TryGetValue("kin", out var kv) && kv is bool kin)
            {
                rb.isKinematic = kin;
            }
        }

        private float ConvertToFloat(object o)
        {
            if (o is float f) return f;
            if (o is double d) return (float)d;
            if (o is int i) return i;
            if (float.TryParse(o?.ToString(), out var p)) return p;
            return 0f;
        }

        private string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
