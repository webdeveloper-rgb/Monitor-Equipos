using System;
using System.Collections.Generic;

namespace MonitorAgent.Cli
{
    static class CommandLine
    {
        public const string Help = @"MonitorAgent
  --install --url <https://.../monitor-ingest.php> [--enroll-code <CODIGO>]
          [--interval 15]                                             Da de alta el equipo e instala/arranca el servicio
                                                                      (el código solo es necesario en el primer alta)
  --uninstall                                                         Detiene y elimina el servicio
  --dump                                                              Imprime el informe JSON sin enviarlo
  --once                                                              Genera y envia un informe ahora
  --console                                                           Ejecuta en primer plano (Enter para salir)";

        public static Dictionary<string, string> Parse(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                string key = args[i].Substring(2);
                string val = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "";
                d[key] = val;
            }
            return d;
        }
    }
}