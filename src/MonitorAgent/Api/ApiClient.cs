using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using MonitorAgent.Infrastructure;

namespace MonitorAgent.Api
{
    static class ApiClient
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // Windows 8/8.1 + .NET 4.8: forzar TLS 1.2 (y 1.3 si el SO lo soporta).
        public static void EnableModernTls()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
        }

        public static bool Send(AgentConfig cfg, string machineId, string json, out string error)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, cfg.ApiUrl))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);
                    req.Headers.Add("X-Machine-Id", machineId);
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        if (resp.IsSuccessStatusCode) { error = null; return true; }
                        error = (int)resp.StatusCode + " " + resp.ReasonPhrase;
                        return false;
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }
}