# Actualizaciones automáticas

El agente consulta el manifiesto configurado al final de cada ciclo de monitorización. Si su versión es menor, descarga el instalador de Inno Setup, verifica su SHA-256 y lo ejecuta en modo silencioso. El actualizador detiene el servicio antes de abrir el instalador y vuelve a iniciarlo al terminar.

## Manifiesto

Publica un JSON HTTPS en una URL estable, por ejemplo:

```json
{
  "version": "1.3.6",
  "installerUrl": "https://descargas.example.com/SauberDesk-1.3.6-setup.exe",
  "sha256": "<sha256 hexadecimal de 64 caracteres>"
}
```

`version` debe ser una versión numérica compatible con `System.Version`; `installerUrl` puede ser absoluta o relativa al manifiesto y siempre debe resolver a HTTPS. El hash se calcula sobre el instalador terminado, después de compilarlo.

El agente limita el manifiesto a 64 KiB y el instalador a 500 MiB. Descarga el instalador en un archivo temporal, valida el SHA-256 y solo entonces lo mueve a su nombre final. Si la descarga o la validación falla, elimina el archivo temporal y no inicia el instalador.

Configura la URL durante la instalación del agente:

```text
MonitorAgent.exe --install --url https://api.example.com/monitor-ingest.php --token <TOKEN> --update-manifest https://descargas.example.com/monitor/latest.json
```

También se puede establecer `UpdateManifestUrl` en `%ProgramData%\MonitorAgent\config.json`. Si se deja vacío, el agente no busca actualizaciones. La consulta usa el mismo intervalo que los informes.

## Inno Setup

El instalador debe actualizar la misma aplicación (mismo `AppId` y directorio de instalación) y aceptar los parámetros silenciosos estándar de Inno Setup: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`. El agente detiene el servicio antes de ejecutarlo; en cualquier salida del instalador intenta dejar `MonitorAgent` en estado activo y espera hasta 90 segundos. Si el arranque falla, registra el error para diagnóstico.

El manifiesto y el instalador deben servirse por HTTPS. El SHA-256 detecta descargas alteradas o incompletas, pero como el hash y el instalador vienen del mismo origen no sustituye la firma Authenticode del instalador. Para proteger también frente a una publicación comprometida, firma el instalador con el certificado de la organización y añade validación del certificado editor antes de desplegar en producción. Esa validación requiere el certificado o su huella aprobada, que aún no está configurada en el workspace.
