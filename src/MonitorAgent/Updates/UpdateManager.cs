using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MonitorAgent.Infrastructure;
using Newtonsoft.Json;

namespace MonitorAgent.Updates
{
    // Feed JSON: { "version": "1.3.6", "installerUrl": "https://.../SauberDesk-1.3.6-setup.exe", "sha256": "<64 hex chars>" }
    static class UpdateManager
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        sealed class UpdateManifest
        {
            public string Version { get; set; }
            public string InstallerUrl { get; set; }
            public string Sha256 { get; set; }
        }

        public static void CheckAndInstall(AgentConfig cfg)
        {
            var manifestUrl = string.IsNullOrWhiteSpace(cfg.UpdateManifestUrl)
                ? AppInfo.DefaultUpdateManifestUrl : cfg.UpdateManifestUrl;
            var manifestUri = RequireHttps(manifestUrl);
            var manifestJson = DownloadManifest(manifestUri);
            var manifest = JsonConvert.DeserializeObject<UpdateManifest>(manifestJson);
            if (manifest == null || !Version.TryParse(manifest.Version, out var latest)
                || !Version.TryParse(AppInfo.Version, out var current))
                throw new InvalidDataException("El manifiesto o la versión del agente no tienen un formato válido.");
            if (latest <= current) return;
            if (!Regex.IsMatch(manifest.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("El manifiesto no contiene un SHA-256 válido.");

            var installerUri = new Uri(manifestUri, manifest.InstallerUrl ?? "");
            if (installerUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("La URL del instalador debe usar HTTPS.");

            Directory.CreateDirectory(AppInfo.DataDir);
            AgentConfig.ProtectDirectory(AppInfo.DataDir);
            var installerPath = Path.Combine(AppInfo.DataDir, "SauberDesk-update-" + latest + ".exe");
            var downloadPath = installerPath + ".partial";
            try
            {
                if (File.Exists(downloadPath)) File.Delete(downloadPath);
                Download(installerUri, downloadPath);
                AgentConfig.ProtectFile(downloadPath);
                if (!string.Equals(HashFile(downloadPath), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El SHA-256 del instalador no coincide con el manifiesto.");

                if (File.Exists(installerPath))
                    File.Replace(downloadPath, installerPath, null);
                else
                    File.Move(downloadPath, installerPath);
            }
            catch
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); } catch { }
                throw;
            }
            AgentConfig.ProtectFile(installerPath);

            LaunchInstaller(installerPath, latest.ToString());
        }

        static Uri RequireHttps(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("La URL del manifiesto debe ser HTTPS.");
            return uri;
        }

        static string DownloadManifest(Uri uri)
        {
            const int maxBytes = 64 * 1024;
            using (var response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes)
                    throw new InvalidDataException("El manifiesto supera el tamaño máximo permitido.");

                using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > maxBytes)
                            throw new InvalidDataException("El manifiesto supera el tamaño máximo permitido.");
                        output.Write(buffer, 0, read);
                    }
                    if (output.Length == 0)
                        throw new InvalidDataException("El manifiesto está vacío.");
                    return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
                }
            }
        }

        static void Download(Uri uri, string destination)
        {
            const long maxBytes = 500L * 1024 * 1024;
            var response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            using (response)
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes)
                    throw new InvalidDataException("El instalador supera el tamaño máximo permitido.");
                using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > maxBytes) throw new InvalidDataException("El instalador supera el tamaño máximo permitido.");
                        output.Write(buffer, 0, read);
                    }
                    if (total == 0) throw new InvalidDataException("El instalador descargado está vacío.");
                }
            }
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        static void LaunchInstaller(string installerPath, string version)
        {
            var logPath = Path.Combine(AppInfo.DataDir, "update-" + version + ".log");
            var script = @"
$ErrorActionPreference = 'Stop'
$serviceName = '" + PsQuote(AppInfo.ServiceName) + @"'
$installer = '" + PsQuote(installerPath) + @"'
$logPath = '" + PsQuote(logPath) + @"'
function Write-UpdateLog($message) { Add-Content -LiteralPath $logPath -Value ((Get-Date).ToString('s') + ' ' + $message) }
try {
  $service = Get-Service -Name $serviceName -ErrorAction Stop
  if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(90))
  }
  Write-UpdateLog 'Servicio detenido; iniciando instalador.'
  $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=""' + $logPath + '""'
  $process = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru
  if ($process.ExitCode -ne 0) { throw ('El instalador terminó con código ' + $process.ExitCode) }
  Write-UpdateLog 'Instalador finalizado correctamente.'
} catch { Write-UpdateLog ('Error de actualizacion: ' + $_.Exception.Message) }
finally {
  try {
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($service.Status -ne 'Running') {
      Start-Service -Name $serviceName
      $service = Get-Service -Name $serviceName -ErrorAction Stop
      $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(90))
    }
    Write-UpdateLog 'Servicio activo tras la actualizacion.'
  } catch { Write-UpdateLog ('No se pudo iniciar el servicio: ' + $_.Exception.Message) }
}
";
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell)) throw new FileNotFoundException("No se encontró Windows PowerShell.", powershell);

            var start = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "
                    + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (Process.Start(start) == null)
                throw new InvalidOperationException("No se pudo iniciar el actualizador del instalador.");
            Log.Info("Actualización " + version + " descargada y verificada; instalador iniciado.");
        }

        static string PsQuote(string value) => (value ?? "").Replace("'", "''");
    }
}