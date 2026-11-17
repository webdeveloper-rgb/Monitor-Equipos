using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MonitorAgent.Infrastructure;
using MonitorAgent.Models;

namespace MonitorAgent.Collectors
{
    sealed class RustDeskCollector : ICollector
    {
        const string RustDeskRelativePath = @"RustDesk\rustdesk.exe";
        const string RustDeskPath = @"C:\Program Files\RustDesk\rustdesk.exe";
        static readonly Regex IdPattern = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9 ._-]{0,127}\z");

        public string Name => "rustdesk";

        public void Collect(Report report, CollectorContext context)
        {
            report.RustDeskId = context.Config.RustDeskId;
            string executable = FindRustDeskExecutable();
            if (executable == null)
                return;

            string rustDeskId = ReadRustDeskId(executable);
            if (!IdPattern.IsMatch(rustDeskId))
                throw new InvalidOperationException("RustDesk devolvió un ID vacío o con formato no válido.");

            report.RustDeskId = rustDeskId;
        }

        static string FindRustDeskExecutable()
        {
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates =
            {
                RustDeskPath,
                string.IsNullOrEmpty(programFilesX86) ? null : Path.Combine(programFilesX86, RustDeskRelativePath)
            };
            return candidates.FirstOrDefault(path => path != null && File.Exists(path));
        }

        static string ReadRustDeskId(string executable)
        {
            string powerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powerShell))
                throw new FileNotFoundException("No se encontró Windows PowerShell.", powerShell);

            string script = "$ErrorActionPreference = 'Stop'; "
                + "$id = (& '" + executable + "' --get-id | Out-String).Trim(); "
                + "if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($id)) { exit 1 }; "
                + "Write-Output $id";
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = powerShell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                    throw new InvalidOperationException("No se pudo iniciar PowerShell para consultar RustDesk.");
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    process.WaitForExit();
                    throw new TimeoutException("La consulta del ID de RustDesk superó los 15 segundos.");
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("PowerShell no pudo obtener el ID de RustDesk.");
                return output;
            }
        }
    }
}
