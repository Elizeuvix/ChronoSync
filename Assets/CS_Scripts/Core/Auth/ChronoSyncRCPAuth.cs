using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking; // fallback HTTP
using CS.Core.Config;

namespace CS.Core.Auth
{
    public class ChronoSyncRCPAuth : MonoBehaviour
    {
    private static HttpClient client;
    public string apiUrl = "http://192.168.3.196:8100";
        [Tooltip("Se marcado, substitui apiUrl pelo ChronoSyncConfig.API_BASE em tempo de execução.")]
        public bool forceConfigBase = true;
        [Header("Security")]
        [Tooltip("Override para a API key do desenvolvedor (deixe vazio para usar ChronoSyncConfig.API_KEY)")]
        [SerializeField] private string apiKeyOverride = "";
        // Caso seu backend use prefixo (ex.: /api), o cliente tentará ambos os caminhos automaticamente
        private static readonly string[] RegisterPaths = new[] { "/register", "/api/register" };
        private static readonly string[] LoginPaths = new[] { "/login", "/api/login" };
        // API key is defined in code (ChronoSyncConfig) so the player doesn't need to know or store it

        // Último ID de player recebido do backend (preenchido no Login)
        public string lastPlayerId { get; private set; } = string.Empty;
        public string lastError { get; private set; } = string.Empty;
    public string lastTriedUrl { get; private set; } = string.Empty;

        // Para HTTP auth, use SOMENTE header X-API-Key; não anexar ?key na URL
        private string WithApiKeyQuery(string url) => url;

        private void AddApiKeyHeader(HttpRequestMessage req)
        {
            var apiKey = string.IsNullOrEmpty(apiKeyOverride) ? ChronoSyncConfig.API_KEY : apiKeyOverride;
            if (!string.IsNullOrEmpty(apiKey)) req.Headers.Add("X-API-Key", apiKey);
        }

        void Awake()
        {
            // Sanitize apiUrl (trim spaces/newlines) to avoid invalid URI formatting
            if (!string.IsNullOrEmpty(apiUrl)) apiUrl = apiUrl.Trim();
            if ((forceConfigBase || string.IsNullOrWhiteSpace(apiUrl) || apiUrl.Contains("localhost")) && !string.IsNullOrWhiteSpace(ChronoSyncConfig.API_BASE))
            {
                apiUrl = ChronoSyncConfig.API_BASE.Trim();
                Debug.Log($"[Auth] apiUrl sobrescrito para ChronoSyncConfig.API_BASE => {apiUrl}");
            }
            EnsureHttpClient();
        }

        private static void EnsureHttpClient()
        {
            if (client != null) return;
            try
            {
                var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = true,
                    AutomaticDecompression = System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.GZip
                };
                client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(10);
                // Default headers can be extended here if needed
            }
            catch
            {
                client = new HttpClient();
                try { client.Timeout = TimeSpan.FromSeconds(10); } catch { }
            }
        }

        public async Task<bool> Register(string username, string password)
        {
            lastError = string.Empty;
            try
            {
                // Include display_name for newer API versions; older backends ignore unknown fields
                    // Auth HTTP: apenas username/password. display_name é rótulo e será enviado via WebSocket após login
                    var json = $"{{\"username\":\"{username}\",\"password\":\"{password}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                // Tenta múltiplos caminhos (/register e /api/register)
                var result = await PostJsonWithFallback(content, RegisterPaths);
                if (!result.success)
                {
                    if (!string.IsNullOrEmpty(result.body) && result.body.Contains("Invalid API key"))
                    {
                        lastError += "\nDica: configure a API key do DEV no inspector (apiKeyOverride) ou em ChronoSyncConfig.API_KEY.";
                    }
                }
                return result.success;
            }
            catch (Exception ex)
            {
                lastError = ExpandException(ex);
                if (!string.IsNullOrEmpty(lastTriedUrl)) lastError += $"\nURL: {lastTriedUrl}";
                Debug.LogError($"[Auth] Register failed: {lastError}");
                return false;
            }
        }

        public async Task<bool> Login(string username, string password)
        {
            lastError = string.Empty;
            try
            {
                var json = $"{{\"username\":\"{username}\",\"password\":\"{password}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                // Tenta múltiplos caminhos (/login e /api/login)
                var result = await PostJsonWithFallback(content, LoginPaths);
                if (!result.success)
                {
                    lastPlayerId = string.Empty;
                    if (!string.IsNullOrEmpty(result.body) && result.body.Contains("Invalid API key"))
                    {
                        lastError += "\nDica: configure a API key do DEV no inspector (apiKeyOverride) ou em ChronoSyncConfig.API_KEY.";
                    }
                    return false;
                }
                var body = result.body ?? string.Empty;
                lastPlayerId = TryExtractFirst(body, "player_id")
                                ?? TryExtractFirst(body, "id")
                                ?? TryExtractFirst(body, "playerId")
                                ?? string.Empty;
                // Alinhado ao backend: o player_id é atribuído no WebSocket; HTTP sucesso é suficiente
                return true;
            }
            catch (Exception ex)
            {
                lastPlayerId = string.Empty;
                lastError = ExpandException(ex);
                if (!string.IsNullOrEmpty(lastTriedUrl)) lastError += $"\nURL: {lastTriedUrl}";
                Debug.LogError($"[Auth] Login failed: {lastError}");
                return false;
            }
        }

        private async Task<string> SafeReadBody(HttpResponseMessage resp)
        {
            try { return await resp.Content.ReadAsStringAsync(); }
            catch { return string.Empty; }
        }

        private string TryExtractFirst(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            var token = $"\"{key}\":";
            int idx = json.IndexOf(token, StringComparison.Ordinal);
            if (idx == -1) return null;
            int start = json.IndexOf('"', idx + token.Length);
            int end = json.IndexOf('"', start + 1);
            if (start == -1 || end == -1 || end <= start) return null;
            return json.Substring(start + 1, end - start - 1);
        }

        private string Combine(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(baseUrl)) return path;
            if (string.IsNullOrEmpty(path)) return baseUrl;
            if (baseUrl.EndsWith("/")) baseUrl = baseUrl.TrimEnd('/');
            return baseUrl + path;
        }

        private string ExpandException(Exception ex)
        {
            if (ex == null) return string.Empty;
            var sb = new StringBuilder();
            int depth = 0;
            var cur = ex;
            while (cur != null && depth < 5)
            {
                if (depth > 0) sb.Append(" -> ");
                sb.Append(cur.GetType().Name).Append(": ").Append(cur.Message);
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        // Fallback strategy: try HttpClient; on socket errors, retry with UnityWebRequest
        private async Task<(bool success, string body)> PostJsonWithFallback(StringContent content, string[] paths)
        {
            foreach (var path in paths)
            {
                var url = WithApiKeyQuery(Combine(apiUrl, path));
                lastTriedUrl = url;
                Debug.Log($"[Auth] POST => {url}");
                // Attempt HttpClient first
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                    AddApiKeyHeader(req);
                    using (var resp = await client.SendAsync(req))
                    {
                        var body = await SafeReadBody(resp);
                        if (resp.IsSuccessStatusCode) return (true, body);
                        lastError = $"{(int)resp.StatusCode} {resp.ReasonPhrase} on {url}: {body}";
                        // Only continue to next path if 404/405 (alternate base path handling)
                        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound || resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                            continue;
                        return (false, body);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Auth] HttpClient falhou em {url}: {ex.Message} - tentando UnityWebRequest");
                    // UnityWebRequest fallback
                    var body = await PostWithUnityWebRequest(url, content);
                    if (body.success) return (true, body.body);
                    lastError = body.errorMessage;
                    // If fallback also fails and it's not a 404 path case, we stop
                    if (!body.retryNextPath) return (false, body.body);
                }
            }
            return (false, lastError);
        }

        private async Task<(bool success, string body, string errorMessage, bool retryNextPath)> PostWithUnityWebRequest(string url, StringContent content)
        {
            try
            {
                var json = await content.ReadAsStringAsync();
                var data = Encoding.UTF8.GetBytes(json);
                var uwr = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                uwr.uploadHandler = new UploadHandlerRaw(data);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");
                var apiKey = string.IsNullOrEmpty(apiKeyOverride) ? ChronoSyncConfig.API_KEY : apiKeyOverride;
                if (!string.IsNullOrEmpty(apiKey)) uwr.SetRequestHeader("X-API-Key", apiKey);
                var op = uwr.SendWebRequest();
                while (!op.isDone) await Task.Yield();
#if UNITY_2020_2_OR_NEWER
                bool netErr = uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.DataProcessingError;
                bool httpErr = uwr.result == UnityWebRequest.Result.ProtocolError;
#else
                bool netErr = uwr.isNetworkError;
                bool httpErr = uwr.isHttpError;
#endif
                if (!netErr && !httpErr && uwr.responseCode >= 200 && uwr.responseCode < 300)
                {
                    return (true, uwr.downloadHandler.text, null, false);
                }
                var errBody = uwr.downloadHandler != null ? uwr.downloadHandler.text : string.Empty;
                var msg = $"UnityWebRequest {(int)uwr.responseCode} {(uwr.error ?? string.Empty)} on {url}: {errBody}";
                // Retry next path only if 404/405
                bool retry = uwr.responseCode == 404 || uwr.responseCode == 405;
                return (false, errBody, msg, retry);
            }
            catch (Exception ex)
            {
                return (false, null, $"UnityWebRequest exception on {url}: {ex.Message}", false);
            }
        }
    }
}
