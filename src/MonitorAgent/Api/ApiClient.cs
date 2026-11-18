using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using MonitorAgent.Models;
using MonitorAgent.Infrastructure;
using Newtonsoft.Json;

namespace MonitorAgent.Api
{
    static class ApiClient
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        sealed class TokenResponse
        {
            public string Token { get; set; }
        }

        sealed class IngestResponse
        {
            [JsonProperty("event_extraction_mode")]
            public string EventExtractionMode { get; set; }
        }

        public static string CreateDeviceToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return "mra_" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public static bool Enroll(string ingestUrl, string code, string machineId, string deviceToken, out string error)
        {
            try
            {
                var ingestUri = new Uri(ingestUrl, UriKind.Absolute);
                if (ingestUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("La URL de ingesta debe usar HTTPS.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(code ?? "", @"\Amre_[0-9a-f]{48}\z")
                    || !System.Text.RegularExpressions.Regex.IsMatch(deviceToken ?? "", @"\Amra_[0-9a-f]{64}\z"))
                    throw new InvalidOperationException("El código de alta o la credencial del equipo no tiene un formato válido.");

                var enrollUri = new Uri(ingestUri, "monitor-enroll.php");
                using (var req = new HttpRequestMessage(HttpMethod.Post, enrollUri))
                {
                    req.Headers.Add("X-Machine-Id", machineId);
                    req.Content = new StringContent(
                        JsonConvert.SerializeObject(new { code = code, machineId = machineId, deviceToken = deviceToken }),
                        Encoding.UTF8,
                        "application/json");

                    using (var resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            error = "Alta del equipo: " + (int)resp.StatusCode + " " + resp.ReasonPhrase;
                            return false;
                        }
                    }
                }

                error = null;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // Windows 8/8.1 + .NET 4.8: forzar TLS 1.2 (y 1.3 si el SO lo soporta).
        public static void EnableModernTls()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
        }

        public static bool Send(AgentConfig cfg, string machineId, string json, out string error)
        {
            return Send(cfg, machineId, json, out error, out _);
        }

        public static bool Send(AgentConfig cfg, string machineId, string json, out string error, out string eventExtractionMode)
        {
            eventExtractionMode = null;
            try
            {
                var ingestUri = new Uri(cfg.ApiUrl, UriKind.Absolute);
                if (ingestUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("La URL de ingesta debe usar HTTPS.");

                var tokenUri = new Uri(ingestUri, "monitor-token.php");
                using (var tokenReq = new HttpRequestMessage(HttpMethod.Post, tokenUri))
                {
                    tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);
                    tokenReq.Headers.Add("X-Api-Key", cfg.ApiToken);
                    tokenReq.Headers.Add("X-Machine-Id", machineId);
                    tokenReq.Content = new StringContent(
                        JsonConvert.SerializeObject(new { machineId = machineId }),
                        Encoding.UTF8,
                        "application/json");

                    using (var tokenResp = Http.SendAsync(tokenReq).GetAwaiter().GetResult())
                    {
                        if (!tokenResp.IsSuccessStatusCode)
                        {
                            error = "Token temporal: " + (int)tokenResp.StatusCode + " " + tokenResp.ReasonPhrase;
                            return false;
                        }
                        var tokenJson = tokenResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        var tokenData = JsonConvert.DeserializeObject<TokenResponse>(tokenJson);
                        if (tokenData == null || string.IsNullOrEmpty(tokenData.Token)
                            || !System.Text.RegularExpressions.Regex.IsMatch(tokenData.Token, @"\Amtr_[0-9a-f]{64}\z"))
                        {
                            error = "El servidor devolvió un token temporal no válido.";
                            return false;
                        }

                        using (var req = new HttpRequestMessage(HttpMethod.Post, ingestUri))
                        {
                            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.Token);
                            req.Headers.Add("X-Api-Key", tokenData.Token);
                            req.Headers.Add("X-Machine-Id", machineId);
                            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                            using (var resp = Http.SendAsync(req).GetAwaiter().GetResult())
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    string responseJson = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                    var responseData = JsonConvert.DeserializeObject<IngestResponse>(responseJson);
                                    eventExtractionMode = responseData?.EventExtractionMode;
                                    error = null;
                                    return true;
                                }
                                error = (int)resp.StatusCode + " " + resp.ReasonPhrase;
                                if ((int)resp.StatusCode == 422)
                                {
                                    string detail = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                    if (!string.IsNullOrWhiteSpace(detail))
                                    {
                                        detail = new string(detail.Where(c => !char.IsControl(c) || c == '\t').ToArray()).Trim();
                                        if (detail.Length > 500) detail = detail.Substring(0, 500);
                                        error += ": " + detail;
                                    }
                                }
                                return false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool SendBackupActivity(AgentConfig cfg, BackupActivitySnapshot snapshot, out string error)
        {
            try
            {
                var ingestUri = new Uri(cfg.ApiUrl, UriKind.Absolute);
                if (ingestUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("La URL de ingesta debe usar HTTPS.");

                var tokenUri = new Uri(ingestUri, "monitor-token.php");
                string temporaryToken;
                using (var tokenReq = new HttpRequestMessage(HttpMethod.Post, tokenUri))
                {
                    tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);
                    tokenReq.Headers.Add("X-Api-Key", cfg.ApiToken);
                    tokenReq.Headers.Add("X-Machine-Id", snapshot.MachineId);
                    tokenReq.Content = new StringContent(
                        JsonConvert.SerializeObject(new { machineId = snapshot.MachineId }),
                        Encoding.UTF8,
                        "application/json");
                    using (var tokenResp = Http.SendAsync(tokenReq).GetAwaiter().GetResult())
                    {
                        if (!tokenResp.IsSuccessStatusCode)
                        {
                            error = "Token temporal: " + (int)tokenResp.StatusCode + " " + tokenResp.ReasonPhrase;
                            return false;
                        }
                        var tokenData = JsonConvert.DeserializeObject<TokenResponse>(tokenResp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        temporaryToken = tokenData?.Token;
                    }
                }
                if (string.IsNullOrWhiteSpace(temporaryToken)
                    || !System.Text.RegularExpressions.Regex.IsMatch(temporaryToken, @"\Amtr_[0-9a-f]{64}\z"))
                {
                    error = "El servidor devolvió un token temporal no válido.";
                    return false;
                }

                var statusUri = new Uri(ingestUri, "monitor-backup-status.php");
                using (var req = new HttpRequestMessage(HttpMethod.Post, statusUri))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", temporaryToken);
                    req.Headers.Add("X-Api-Key", temporaryToken);
                    req.Headers.Add("X-Machine-Id", snapshot.MachineId);
                    req.Content = new StringContent(JsonConvert.SerializeObject(snapshot, JsonDefaults.Settings), Encoding.UTF8, "application/json");
                    using (var resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        if (resp.IsSuccessStatusCode) { error = null; return true; }
                        error = (int)resp.StatusCode + " " + resp.ReasonPhrase;
                        string detail = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(detail))
                        {
                            detail = new string(detail.Where(c => !char.IsControl(c) || c == '\t').ToArray()).Trim();
                            if (detail.Length > 300) detail = detail.Substring(0, 300);
                            error += ": " + detail;
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }
}
