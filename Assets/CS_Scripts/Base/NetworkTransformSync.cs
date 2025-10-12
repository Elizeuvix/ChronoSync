using System;
using System.Globalization;
using UnityEngine;
using CS.Core.Networking;

/// <summary>
/// Real-time network transform sync for MMO-like characters, weapons, projectiles.
/// Attach to any GameObject you want to sync. Choose local authority to send updates; others will smoothly follow.
/// Requires a ChronoSyncRCPWebSocket in the scene.
/// </summary>
namespace CS.Base
{
    public class NetworkTransformSync : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique id for this networked entity. For the local player, it defaults to the assignedPlayerId.")]
        public string entityId = "";
        [Tooltip("If true, this instance sends its transform to the server; otherwise, it receives and applies remote updates.")]
        public bool isLocalAuthority = false;

        [Header("Targets")]
        public Transform target; // optional; defaults to this.transform

        [Header("Send Settings")]
        [Tooltip("Updates per second for sending transform when authority.")]
        public float sendHz = 15f;
        [Tooltip("Minimum position delta to trigger a send (meters)")]
        public float minPosDelta = 0.005f;
        [Tooltip("Minimum rotation delta to trigger a send (degrees)")]
        public float minRotDelta = 0.5f;
        [Tooltip("Minimum scale delta to trigger a send")]
        public float minScaleDelta = 0.005f;

        [Header("Receive Smoothing")]
        public float positionLerp = 12f;
        public float rotationLerp = 12f;
        public float scaleLerp = 12f;

        private ChronoSyncRCPWebSocket ws;
        private string localPlayerId = "";
        private float nextSendTime = 0f;
        private Vector3 lastSentPos, lastSentScale;
        private Quaternion lastSentRot;

        // Received targets
        private Vector3 targetPos;
        private Quaternion targetRot;
        private Vector3 targetScale;
        private bool hasRemote;

        void Awake()
        {
            if (target == null) target = transform;
            lastSentPos = target.position;
            lastSentRot = target.rotation;
            lastSentScale = target.localScale;
            targetPos = lastSentPos;
            targetRot = lastSentRot;
            targetScale = lastSentScale;
        }

        void OnEnable()
        {
            ws = FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (ws != null)
            {
                ws.OnMessageReceived += OnWsMessage;
                ws.OnConnected += OnWsConnected;
                CacheLocalId();
            }
            else
            {
                Debug.LogWarning("[NetworkTransformSync] ChronoSyncRCPWebSocket não encontrado na cena.");
            }
        }

        void OnDisable()
        {
            if (ws != null)
            {
                ws.OnMessageReceived -= OnWsMessage;
                ws.OnConnected -= OnWsConnected;
            }
        }

        void OnWsConnected()
        {
            CacheLocalId();
        }

        private void CacheLocalId()
        {
            if (ws == null) return;
            localPlayerId = string.IsNullOrEmpty(ws.assignedPlayerId) ? ws.playerId : ws.assignedPlayerId;
            if (isLocalAuthority && string.IsNullOrEmpty(entityId))
                entityId = localPlayerId;
        }

        void Update()
        {
            if (isLocalAuthority)
            {
                TrySend();
            }
            else if (hasRemote)
            {
                // Smoothly approach the target values
                if (positionLerp > 0f)
                    target.position = Vector3.Lerp(target.position, targetPos, 1f - Mathf.Exp(-positionLerp * Time.deltaTime));
                else
                    target.position = targetPos;

                if (rotationLerp > 0f)
                    target.rotation = Quaternion.Slerp(target.rotation, targetRot, 1f - Mathf.Exp(-rotationLerp * Time.deltaTime));
                else
                    target.rotation = targetRot;

                if (scaleLerp > 0f)
                    target.localScale = Vector3.Lerp(target.localScale, targetScale, 1f - Mathf.Exp(-scaleLerp * Time.deltaTime));
                else
                    target.localScale = targetScale;
            }
        }

        private void TrySend()
        {
            if (ws == null || !ws.IsConnected) return;
            if (Time.time < nextSendTime) return;

            var pos = target.position;
            var rot = target.rotation;
            var scl = target.localScale;
            // approximate velocity
            var vel = (pos - lastSentPos) / Mathf.Max(0.0001f, (1f / Mathf.Max(1f, sendHz)));

            bool changed = (Vector3.Distance(pos, lastSentPos) >= minPosDelta) ||
                           (Quaternion.Angle(rot, lastSentRot) >= minRotDelta) ||
                           (Vector3.Distance(scl, lastSentScale) >= minScaleDelta);
            if (!changed) return;

            lastSentPos = pos; lastSentRot = rot; lastSentScale = scl;
            nextSendTime = Time.time + (1f / Mathf.Max(1f, sendHz));
            var inv = CultureInfo.InvariantCulture;
            string statePart = "\"position\":{" +
                                    "\"x\":" + pos.x.ToString("0.###", inv) + "," +
                                    "\"y\":" + pos.y.ToString("0.###", inv) + "," +
                                    "\"z\":" + pos.z.ToString("0.###", inv) +
                                "}," +
                                "\"rotation\":{" +
                                    "\"x\":" + rot.x.ToString("0.###", inv) + "," +
                                    "\"y\":" + rot.y.ToString("0.###", inv) + "," +
                                    "\"z\":" + rot.z.ToString("0.###", inv) + "," +
                                    "\"w\":" + rot.w.ToString("0.###", inv) +
                                "}," +
                                "\"scale\":{" +
                                    "\"x\":" + scl.x.ToString("0.###", inv) + "," +
                                    "\"y\":" + scl.y.ToString("0.###", inv) + "," +
                                    "\"z\":" + scl.z.ToString("0.###", inv) +
                                "}," +
                                "\"velocity\":{" +
                                    "\"x\":" + vel.x.ToString("0.###", inv) + "," +
                                    "\"y\":" + vel.y.ToString("0.###", inv) + "," +
                                    "\"z\":" + vel.z.ToString("0.###", inv) +
                                "}," +
                                "\"grounded\":true";

            string payload = "{" +
                "\"event\":\"state_update\"," +
                (string.IsNullOrEmpty(localPlayerId) ? "" : "\"player_id\":\"" + Escape(localPlayerId) + "\",") +
                "\"entity_id\":\"" + Escape(entityId) + "\"," +
                // Compat: servidor pode esperar 'state' como mapping
                "\"state\":{" + statePart + "}," +
                // Cliente legacy usa 'transform'; mantido por compatibilidade
                "\"transform\":{" + statePart + "}" +
            "}";
            ws.Send(payload);
        }

        private void OnWsMessage(string msg)
        {
            if (!msg.Contains("\"event\":\"state_update\"")) return;
            // Extract optional player_id and entity_id
            string msgPlayer = ExtractJsonValue(msg, "player_id");
            string msgEntity = ExtractJsonValue(msg, "entity_id");
            // Ignore self messages if player_id is present
            if (!string.IsNullOrEmpty(localPlayerId) && !string.IsNullOrEmpty(msgPlayer) && msgPlayer == localPlayerId) return;
            // If this script has a specific entityId, require match when provided
            if (!string.IsNullOrEmpty(entityId) && !string.IsNullOrEmpty(msgEntity) && msgEntity != entityId) return;

            // Parse transform
            var pos = ExtractVector3(msg, "position");
            var rot = ExtractQuaternion(msg, "rotation");
            var scl = ExtractVector3(msg, "scale");
            var vel = ExtractVector3(msg, "velocity");
            if (pos.HasValue) targetPos = pos.Value;
            if (rot.HasValue) targetRot = rot.Value;
            if (scl.HasValue) targetScale = scl.Value;
            hasRemote = true;
        }

        private string Escape(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private string ExtractJsonValue(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\":");
            if (idx == -1) return "";
            int start = json.IndexOf("\"", idx + key.Length + 3);
            int end = json.IndexOf("\"", start + 1);
            if (start == -1 || end == -1) return "";
            return json.Substring(start + 1, end - start - 1);
        }

        private Vector3? ExtractVector3(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\":");
            if (idx == -1) return null;
            int start = json.IndexOf('{', idx);
            int end = json.IndexOf('}', start + 1);
            if (start == -1 || end == -1) return null;
            string block = json.Substring(start, end - start + 1);
            float x = ExtractFloat(block, "x");
            float y = ExtractFloat(block, "y");
            float z = ExtractFloat(block, "z");
            return new Vector3(x, y, z);
        }

        private Quaternion? ExtractQuaternion(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\":");
            if (idx == -1) return null;
            int start = json.IndexOf('{', idx);
            int end = json.IndexOf('}', start + 1);
            if (start == -1 || end == -1) return null;
            string block = json.Substring(start, end - start + 1);
            float x = ExtractFloat(block, "x");
            float y = ExtractFloat(block, "y");
            float z = ExtractFloat(block, "z");
            float w = ExtractFloat(block, "w");
            return new Quaternion(x, y, z, w);
        }

        private float ExtractFloat(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\":");
            if (idx == -1) return 0f;
            int start = json.IndexOf(':', idx) + 1;
            int endComma = json.IndexOf(',', start);
            int endBrace = json.IndexOf('}', start);
            int end = (endComma == -1) ? endBrace : ((endBrace == -1) ? endComma : Mathf.Min(endComma, endBrace));
            if (end == -1) end = json.Length;
            string num = json.Substring(start, end - start).Trim();
            if (float.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                return val;
            // fallback: try replacing comma
            if (float.TryParse(num.Replace(',', '.'), out val)) return val;
            return 0f;
        }

        // Allows external code to assign the id later (e.g., when spawning a remote avatar for a player).
        public void SetEntityId(string id)
        {
            entityId = id;
        }
    }
}