using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using CS.Core.Config;

namespace CS.Core.Auth
{
    public class ChronoSyncRCPAuth : MonoBehaviour
    {
        private static readonly HttpClient client = new HttpClient();
        public string apiUrl = "http://localhost:8000";
        // API key is defined in code (ChronoSyncConfig) so the player doesn't need to know or store it

        // Último ID de player recebido do backend (preenchido no Login)
        public string lastPlayerId { get; private set; } = string.Empty;

        public async Task<bool> Register(string username, string password)
        {
            var json = $"{{\"username\":\"{username}\",\"password\":\"{password}\"}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/register");
            request.Content = content;
            var apiKey = ChronoSyncConfig.API_KEY;
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("X-API-Key", apiKey);
            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> Login(string username, string password)
        {
            var json = $"{{\"username\":\"{username}\",\"password\":\"{password}\"}}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}/login");
            request.Content = content;
            var apiKey = ChronoSyncConfig.API_KEY;
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("X-API-Key", apiKey);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                lastPlayerId = string.Empty;
                return false;
            }
            // Extrair player_id do corpo da resposta
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                // Busca simples por "player_id":"..."
                var key = "\"player_id\":";
                int idx = body.IndexOf(key, StringComparison.Ordinal);
                if (idx != -1)
                {
                    int start = body.IndexOf('"', idx + key.Length);
                    int end = body.IndexOf('"', start + 1);
                    if (start != -1 && end != -1 && end > start)
                    {
                        lastPlayerId = body.Substring(start + 1, end - start - 1);
                    }
                }
            }
            catch
            {
                lastPlayerId = string.Empty;
            }
            return true;
        }
    }
}
