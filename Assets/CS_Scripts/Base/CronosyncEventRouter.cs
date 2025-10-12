using UnityEngine;
using CS.Core.Networking;

namespace CS.Base
{
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("ChronoSync/Compat/CronosyncEventRouter")]
    public class CronosyncEventRouter : MonoBehaviour
    {
        private ChronoSyncRCPWebSocket ws;

        private void Awake()
        {
            ws = FindObjectOfType<ChronoSyncRCPWebSocket>();
            if (ws != null)
            {
                ws.OnMessageReceived += OnWsMessage;
            }
        }

        private void OnDestroy()
        {
            if (ws != null) ws.OnMessageReceived -= OnWsMessage;
        }

        private void OnWsMessage(string msg)
        {
            if (!msg.Contains("\"event\":\"custom_event\"")) return;
            string entityId = ExtractJsonValue(msg, "entity_id");
            string codeStr = ExtractNumberValue(msg, "code");
            if (!CronosyncView.TryGet(entityId, out var view)) return;

            // Extract content block (very simple JSON picker)
            var contentJson = ExtractObjectBlock(msg, "content");
            object content = SimpleJsonParser.Parse(contentJson);

            if (int.TryParse(codeStr, out int code))
            {
                switch (code)
                {
                    case 1001:
                        view.animatorView?.ApplyRemote(content);
                        break;
                    case 1002:
                        view.rigidbodyView?.ApplyRemote(content);
                        break;
                    default:
                        break;
                }
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            int idx = json.IndexOf("\"" + key + "\":");
            if (idx == -1) return "";
            int start = json.IndexOf('"', idx + key.Length + 3);
            int end = json.IndexOf('"', start + 1);
            if (start == -1 || end == -1) return "";
            return json.Substring(start + 1, end - start - 1);
        }

        private string ExtractNumberValue(string json, string key)
        {
            int idx = json.IndexOf("\"" + key + "\":");
            if (idx == -1) return "";
            int start = json.IndexOf(':', idx) + 1;
            int endComma = json.IndexOf(',', start);
            int endBrace = json.IndexOf('}', start);
            int end = (endComma == -1) ? endBrace : ((endBrace == -1) ? endComma : Mathf.Min(endComma, endBrace));
            if (end == -1) end = json.Length;
            return json.Substring(start, end - start).Trim();
        }

        private string ExtractObjectBlock(string json, string key)
        {
            int idx = json.IndexOf("\"" + key + "\":");
            if (idx == -1) return "{}";
            int start = json.IndexOf('{', idx);
            if (start == -1) return "{}";
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--; if (depth == 0) { return json.Substring(start, i - start + 1); }
                }
            }
            return "{}";
        }
    }
}
