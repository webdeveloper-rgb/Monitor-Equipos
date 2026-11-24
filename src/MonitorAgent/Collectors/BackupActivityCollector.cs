using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MonitorAgent.Infrastructure;
using MonitorAgent.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MonitorAgent.Collectors
{
    sealed class BackupActivityPollState
    {
        public string LastHash { get; set; }
        public DateTime? LastSentUtc { get; set; }
        public long? AcronisLastRecordId { get; set; }
        public Dictionary<string, DateTime> AcronisOperations { get; } =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    }

    interface IBackupActivityProvider
    {
        bool IsAvailable(AgentConfig config, IReadOnlyCollection<string> installedProducts);
        void Collect(AgentConfig config, BackupActivityPollState state, BackupActivitySnapshot snapshot);
    }

    sealed class AcronisBackupActivityProvider : IBackupActivityProvider
    {
        public bool IsAvailable(AgentConfig config, IReadOnlyCollection<string> installedProducts) => installedProducts.Any(x => x.IndexOf("acronis", StringComparison.OrdinalIgnoreCase) >= 0);
        public void Collect(AgentConfig config, BackupActivityPollState state, BackupActivitySnapshot snapshot) => BackupActivityCollector.ScanAcronis(state, snapshot);
    }

    sealed class IperiusBackupActivityProvider : IBackupActivityProvider
    {
        // Se activará al validar el formato de los logs durante una copia real.
        public bool IsAvailable(AgentConfig config, IReadOnlyCollection<string> installedProducts) => false;
        public void Collect(AgentConfig config, BackupActivityPollState state, BackupActivitySnapshot snapshot) { }
    }

    sealed class VeeamBackupActivityProvider : IBackupActivityProvider
    {
        public bool IsAvailable(AgentConfig config, IReadOnlyCollection<string> installedProducts) => installedProducts.Any(x => x.IndexOf("veeam", StringComparison.OrdinalIgnoreCase) >= 0);
        public void Collect(AgentConfig config, BackupActivityPollState state, BackupActivitySnapshot snapshot) => BackupActivityCollector.ScanVeeam(snapshot);
    }

    sealed class DataProtectorBackupActivityProvider : IBackupActivityProvider
    {
        public bool IsAvailable(AgentConfig config, IReadOnlyCollection<string> installedProducts) =>
            installedProducts.Any(x => x.IndexOf("data protector", StringComparison.OrdinalIgnoreCase) >= 0
                || x.IndexOf("micro focus", StringComparison.OrdinalIgnoreCase) >= 0
                || x.IndexOf("opentext", StringComparison.OrdinalIgnoreCase) >= 0)
            && BackupActivityCollector.HasDataProtectorCli();
        public void Collect(AgentConfig config, BackupActivityPollState state, BackupActivitySnapshot snapshot) => BackupActivityCollector.ScanDataProtector(snapshot);
    }
    static class BackupActivityCollector
    {
        static bool veeamModuleMissingLogged;
        static readonly IReadOnlyList<IBackupActivityProvider> Providers = new IBackupActivityProvider[]
        {
            new AcronisBackupActivityProvider(),
            new IperiusBackupActivityProvider(),
            new VeeamBackupActivityProvider(),
            new DataProtectorBackupActivityProvider()
        };
        static readonly Regex DataProtectorSessionId = new Regex(@"^[A-Za-z0-9_./-]{1,80}$", RegexOptions.Compiled);
        static readonly Regex AcronisStart = new Regex(
            @"(?:operaci[oó]n\s+(?:pendiente|pending)\s+(?<id>\d+).*?(?:se\s+inici[oó]|inici[oó]|started)|(?:pending\s+operation|operation\s+pending)\s+(?<id>\d+).*?started)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex AcronisFinish = new Regex(
            @"(?:operaci[oó]n\s+(?:pendiente|pending)\s+(?<id>\d+).*?(?:complet[oó]|finaliz[oó]|termin[oó]|fall[oó]|error|cancel[oó]|finished|completed|failed|cancelled)|(?:pending\s+operation|operation\s+pending)\s+(?<id>\d+).*?(?:finished|completed|failed|cancelled))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static BackupActivitySnapshot Collect(AgentConfig config, BackupActivityPollState state)
        {
            var snapshot = new BackupActivitySnapshot { ObservedAtUtc = DateTime.UtcNow };
            List<string> products = SoftwareInventoryCollector.ReadBackupProducts();
            foreach (var provider in Providers)
            {
                if (!provider.IsAvailable(config, products)) continue;
                try { provider.Collect(config, state, snapshot); }
                catch (Exception ex) { Log.Error("Proveedor de copias " + provider.GetType().Name + ": " + ex.Message); }
            }

            snapshot.Activities = snapshot.Activities
                .Where(x => !string.IsNullOrWhiteSpace(x.TargetHostname))
                .GroupBy(x => x.Product + "|" + x.SessionKey + "|" + x.TargetHostname, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).Take(200).ToList();
            snapshot.ScannedProducts = snapshot.ScannedProducts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return snapshot;
        }

        public static string Hash(BackupActivitySnapshot snapshot)
        {
            string canonical = string.Join("\n", snapshot.Activities
                .OrderBy(x => x.Product, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SessionKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.TargetHostname, StringComparer.OrdinalIgnoreCase)
                .Select(x => string.Join("|", x.Product, x.SessionKey, x.TargetHostname, x.StartedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))));
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }

        internal static void ScanAcronis(BackupActivityPollState state, BackupActivitySnapshot snapshot)
        {
            const string product = "Acronis";
            try
            {
                string time = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                string cursor = state.AcronisLastRecordId.HasValue
                    ? " and EventRecordID > " + state.AcronisLastRecordId.Value.ToString(CultureInfo.InvariantCulture)
                    : "";
                string xpath = "*[System[TimeCreated[@SystemTime >= '" + time + "']" + cursor + "]]";
                var query = new EventLogQuery("Application", PathType.LogName, xpath);
                using (var reader = new EventLogReader(query))
                {
                    EventRecord record;
                    int read = 0;
                    long? lastRecord = null;
                    while (read++ < 2000 && (record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            if (record.RecordId.HasValue) lastRecord = record.RecordId.Value;
                            if (string.IsNullOrWhiteSpace(record.ProviderName)
                                || record.ProviderName.IndexOf("Acronis", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string message = "";
                            try { message = record.FormatDescription() ?? ""; } catch { }
                            if (message.Length > 4000) message = message.Substring(0, 4000);
                            if (message.IndexOf("I00640000", StringComparison.OrdinalIgnoreCase) >= 0
                                || message.IndexOf("la operación se ha efectuado correctamente", StringComparison.OrdinalIgnoreCase) >= 0
                                || message.IndexOf("the operation completed successfully", StringComparison.OrdinalIgnoreCase) >= 0
                                || Regex.IsMatch(message, @"\bE[0-9A-F]{8}:\s*Error", RegexOptions.IgnoreCase))
                            {
                                state.AcronisOperations.Clear();
                                continue;
                            }
                            var finish = AcronisFinish.Match(message);
                            if (finish.Success)
                            {
                                state.AcronisOperations.Remove(finish.Groups["id"].Value);
                                continue;
                            }
                            var start = AcronisStart.Match(message);
                            if (start.Success)
                            {
                                DateTime started = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow;
                                state.AcronisOperations[start.Groups["id"].Value] = started;
                            }
                        }
                    }
                    if (lastRecord.HasValue) state.AcronisLastRecordId = lastRecord;
                }
                DateTime expiry = DateTime.UtcNow.AddHours(-3);
                foreach (string key in state.AcronisOperations.Where(x => x.Value < expiry).Select(x => x.Key).ToList())
                    state.AcronisOperations.Remove(key);
                snapshot.ScannedProducts.Add(product);
                foreach (var operation in state.AcronisOperations)
                    snapshot.Activities.Add(new BackupActivity
                    {
                        Product = product,
                        TargetHostname = Environment.MachineName,
                        SessionKey = operation.Key,
                        StartedAtUtc = operation.Value,
                        ActiveOperations = 1,
                        Source = "application_event_log"
                    });
            }
            catch (Exception ex) { Log.Error("Lectura Acronis: " + ex.Message); }
        }

        internal static void ScanVeeam(BackupActivitySnapshot snapshot)
        {
            const string product = "Veeam";
            try
            {
                string script = @"
$ErrorActionPreference = 'Stop'
$WarningPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'
$module = Get-Module -ListAvailable -Name Veeam.Backup.PowerShell | Select-Object -First 1
$modulePath = if ($module) { $module.Path } else { $null }
if ([string]::IsNullOrWhiteSpace($modulePath)) {
  $manifest = Join-Path $env:ProgramFiles 'Veeam\Backup and Replication\Console\Veeam.Backup.PowerShell\Veeam.Backup.PowerShell.psd1'
  if (Test-Path -LiteralPath $manifest) { $modulePath = $manifest }
}
if ([string]::IsNullOrWhiteSpace($modulePath)) {
  ConvertTo-Json -InputObject ([pscustomobject]@{ moduleAvailable=$false; items=@() }) -Compress
  exit 0
}
$null = Import-Module $modulePath -ErrorAction Stop
$items = @()
foreach ($session in @(Get-VBRBackupSession -State Working)) {
  $started = $session.CreationTime
  if (-not $started) { $started = Get-Date }
  foreach ($task in @(Get-VBRTaskSession -Session $session)) {
    $target = [string]$task.Name
    if ([string]::IsNullOrWhiteSpace($target)) { continue }
    $items += [pscustomobject]@{ target=$target; session=[string]$session.Id; startedUtc=([datetime]$started).ToUniversalTime().ToString('o') }
  }
}
ConvertTo-Json -InputObject ([pscustomobject]@{ moduleAvailable=$true; items=@($items) }) -Depth 4 -Compress";
                string output = RunPowerShell(script);
                JToken token = JToken.Parse(output.Trim());
                if (!((bool?)token["moduleAvailable"] ?? false))
                {
                    if (!veeamModuleMissingLogged)
                    {
                        Log.Info("Veeam Backup & Replication PowerShell no está instalado; se omite la lectura de sesiones activas de Veeam.");
                        veeamModuleMissingLogged = true;
                    }
                    return;
                }
                IEnumerable<JToken> rows = token["items"] as JArray ?? new JArray();
                foreach (JToken row in rows)
                {
                    string target = (string)row["target"];
                    string session = (string)row["session"];
                    DateTime started;
                    if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(session)) continue;
                    if (!DateTime.TryParse((string)row["startedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out started))
                        started = DateTime.UtcNow;
                    snapshot.Activities.Add(new BackupActivity
                    {
                        Product = product,
                        TargetHostname = target,
                        SessionKey = session,
                        StartedAtUtc = started,
                        ActiveOperations = 1,
                        Source = "veeam_powershell_working_session"
                    });
                }
                snapshot.ScannedProducts.Add(product);
            }
            catch (Exception ex) { Log.Error("Lectura Veeam: " + ex.Message); }
        }

        static string RunPowerShell(string script)
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(executable)) executable = "powershell.exe";
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "`\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("No se pudo iniciar PowerShell.");
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("La consulta local de Veeam superó 30 segundos.");
                }
                Task.WaitAll(stdoutTask, stderrTask);
                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "PowerShell finalizó con error." : stderr.Trim().Substring(0, Math.Min(300, stderr.Trim().Length)));
                if (string.IsNullOrWhiteSpace(stdout)) throw new InvalidOperationException("Veeam no devolvió una lista de sesiones válida.");
                return stdout;
            }
        }

        internal static bool HasDataProtectorCli() => FindDataProtectorExecutable("omnistat.exe") != null;

        internal static void ScanDataProtector(BackupActivitySnapshot snapshot)
        {
            const string product = "Data Protector";
            try
            {
                string omnistat = FindDataProtectorExecutable("omnistat.exe");
                string omnirpt = FindDataProtectorExecutable("omnirpt.exe");
                if (omnistat == null || omnirpt == null)
                    throw new FileNotFoundException("No se encontraron omnistat.exe y omnirpt.exe de Data Protector.");

                string output = RunDataProtector(omnistat, "-detail", TimeSpan.FromSeconds(20));
                var activeSessions = ParseDataProtectorSessions(output)
                    .Where(x => string.Equals(x.Type, "Backup", StringComparison.OrdinalIgnoreCase)
                        && x.Status.StartsWith("In Progress", StringComparison.OrdinalIgnoreCase)
                        && DataProtectorSessionId.IsMatch(x.Id))
                    .Take(50)
                    .ToList();
                var activities = new List<BackupActivity>();
                foreach (var session in activeSessions)
                {
                    string objectsReport = RunDataProtector(omnirpt,
                        "-report session_objects -session \"" + session.Id + "\" -tab", TimeSpan.FromSeconds(15));
                    foreach (string target in ParseDataProtectorActiveObjects(objectsReport))
                    {
                        activities.Add(new BackupActivity
                        {
                            Product = product,
                            TargetHostname = target,
                            SessionKey = session.Id,
                            StartedAtUtc = session.StartedAtUtc,
                            ActiveOperations = 1,
                            Source = "dataprotector_omnistat_session_objects"
                        });
                    }
                }
                snapshot.Activities.AddRange(activities);
                snapshot.ScannedProducts.Add(product);
            }
            catch (Exception ex) { Log.Error("Lectura Data Protector: " + ex.Message); }
        }

        sealed class DataProtectorSession
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string Status { get; set; }
            public DateTime StartedAtUtc { get; set; }
        }

        static IEnumerable<DataProtectorSession> ParseDataProtectorSessions(string output)
        {
            var result = new List<DataProtectorSession>();
            DataProtectorSession current = null;
            foreach (string line in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match id = Regex.Match(line, @"^\s*SessionID\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase);
                if (id.Success)
                {
                    if (current != null) result.Add(current);
                    current = new DataProtectorSession { Id = id.Groups[1].Value.Trim(), StartedAtUtc = DateTime.UtcNow };
                    continue;
                }
                if (current == null) continue;
                Match field = Regex.Match(line, @"^\s*(Session type|Session status|Session started)\s*:\s*(.*?)\s*$", RegexOptions.IgnoreCase);
                if (!field.Success) continue;
                string name = field.Groups[1].Value.ToLowerInvariant();
                string value = field.Groups[2].Value.Trim();
                if (name == "session type") current.Type = value;
                else if (name == "session status") current.Status = value;
                else if (name == "session started") current.StartedAtUtc = ParseDataProtectorDate(value, current.Id);
            }
            if (current != null) result.Add(current);
            return result;
        }

        static DateTime ParseDataProtectorDate(string value, string sessionId)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
                return parsed.ToUniversalTime();
            Match date = Regex.Match(sessionId ?? "", @"(?:R-)?(?<date>\d{4}/\d{2}/\d{2})-");
            if (date.Success && DateTime.TryParseExact(date.Groups["date"].Value, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out parsed))
                return parsed.ToUniversalTime();
            return DateTime.UtcNow;
        }

        static IEnumerable<string> ParseDataProtectorActiveObjects(string output)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int clientIndex = 1;
            int statusIndex = 5;
            bool headerSeen = false;
            foreach (string rawLine in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    string header = trimmed.TrimStart('#').Trim();
                    string[] columns = header.Split('\t').Select(x => x.Trim()).ToArray();
                    int candidateClient = Array.FindIndex(columns, x => string.Equals(x, "Client", StringComparison.OrdinalIgnoreCase));
                    int candidateStatus = Array.FindIndex(columns, x => string.Equals(x, "Status", StringComparison.OrdinalIgnoreCase));
                    if (candidateClient >= 0 && candidateStatus >= 0)
                    {
                        clientIndex = candidateClient;
                        statusIndex = candidateStatus;
                        headerSeen = true;
                    }
                    continue;
                }
                if (!headerSeen && !trimmed.Contains("\t")) continue;
                string[] fields = line.Split('\t').Select(x => x.Trim()).ToArray();
                int requiredIndex = Math.Max(clientIndex, statusIndex);
                if (fields.Length <= requiredIndex) continue;
                string client = fields[clientIndex].Trim(' ', '"');
                string status = fields[statusIndex].Trim(' ', '"');
                if (client.Length > 0 && status.StartsWith("In Progress", StringComparison.OrdinalIgnoreCase))
                    targets.Add(client);
            }
            return targets;
        }

        static string FindDataProtectorExecutable(string name)
        {
            var directories = new List<string>();
            foreach (string variable in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
            {
                string root = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(root)) continue;
                foreach (string relative in new[] { @"OmniBack\bin", @"Micro Focus\Data Protector\bin", @"OpenText\Data Protector\bin" })
                    directories.Add(Path.Combine(root, relative));
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            directories.AddRange(path.Split(Path.PathSeparator).Where(x => !string.IsNullOrWhiteSpace(x)));
            foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim().Trim('"'), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        static string RunDataProtector(string executable, string arguments, TimeSpan timeout)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("No se pudo iniciar la herramienta de Data Protector.");
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("La consulta de Data Protector superó el tiempo límite.");
                }
                Task.WaitAll(stdoutTask, stderrTask);
                string stdout = stdoutTask.Result;
                if (process.ExitCode != 0)
                {
                    string stderr = stderrTask.Result.Trim();
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                        ? "La herramienta de Data Protector terminó con error."
                        : stderr.Substring(0, Math.Min(300, stderr.Length)));
                }
                return stdout;
            }
        }
    }
}
