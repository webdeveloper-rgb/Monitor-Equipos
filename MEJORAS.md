# Propuestas de mejora para MonitorAgent

Este documento reúne un backlog amplio de mejoras basado en el código actual y en lo hablado sobre actualizaciones automáticas y supervisión de copias de seguridad. No todas las mejoras tienen el mismo coste ni se pueden aplicar igual en todos los equipos; las prioridades indican por dónde conviene empezar.

## Estado actual

- **Restricción de diseño:** el agente debe seguir siendo un servicio de fondo sin interfaz gráfica. Las mejoras deben mantener bajo el uso de CPU, memoria, disco y red; la visualización y las alertas pertenecen al servidor central.
- Agente Windows como servicio .NET Framework 4.8.
- Recopila hardware, inventario acotado de software, estado SMART y eventos de Windows; publica un informe por HTTPS.
- Mantiene una cola local de informes cuando falla el envío.
- La configuración y la cola protegen sus ACL; el archivo de estado todavía no tiene una protección explícita.
- Se ha añadido una primera comprobación de actualizaciones mediante `UpdateManifestUrl`: descarga un instalador Inno Setup, compara versión, comprueba SHA-256 y lo lanza en silencio. Falta compilarlo y probar el flujo completo con el instalador real y un feed.
- El sondeo de copias de seguridad de Acronis, Iperius y Data Protector **no está implementado**. El inventario actual solo detecta algunos nombres de productos instalados.

## Prioridad 1: saber si las copias de seguridad funcionan

### Modelo común

Definir un registro independiente del fabricante, por ejemplo:

- Producto y versión.
- Nombre o identificador del trabajo.
- Inicio, fin y hora del último resultado.
- Estado normalizado: `running`, `success`, `warning`, `failed`, `unknown`.
- Mensaje resumido y código de error, si existen.
- Origen del dato y nivel de confianza.

Enviar solo el estado y los metadatos necesarios. No subir el contenido completo de los registros, credenciales, rutas con datos personales ni nombres de archivos salvo que exista una necesidad concreta.

### Acronis Cyber Protect / Cyber Backup

- Añadir un adaptador que lea los eventos de Acronis del registro *Application* de Windows.
- Acronis permite registrar eventos de operaciones de backup en ese registro, pero su opción puede estar desactivada por defecto. Documentar y comprobar ese requisito durante el despliegue.
- Identificar proveedor, ID, nivel y texto del evento; normalizar inicio, éxito, advertencia y fallo.
- Probar las distintas ediciones y versiones instaladas. No asumir que todas emiten los mismos eventos.
- Permitir configurar el proveedor y los IDs de eventos si el entorno difiere.

Referencia: [Windows event log en Acronis Cyber Protect 15](https://www.acronis.com/en/support/documentation/AcronisCyberProtect_15/windows-event-log.html).

### Iperius Backup

- Añadir un adaptador de los registros en `%ProgramData%\IperiusBackup\Logs`.
- Averiguar mediante muestras reales el formato, rotación, codificación y convención de nombres de cada versión soportada.
- Leer incrementalmente y recordar el último archivo/posición procesados para no reenviar resultados ni perder eventos tras una rotación.
- Publicar solo el resumen final del trabajo; los registros detallados pueden incluir rutas y datos operativos.
- Si el formato no es estable, preferir una notificación o script posterior al trabajo que escriba una línea JSON estable en una ruta acordada.

Referencias: [ubicación de los registros de Iperius](https://www.iperiusbackup.es/33361/como-puedo-exportar-los-registros-de-iperius-backup/) y [ejecución desde línea de comandos](https://www.iperiusbackup.nl/cms/single-post.aspx?rpid=33494&section=T).

### Micro Focus / OpenText Data Protector

- Tratarlo como integración del Cell Manager, no asumir que cada cliente Windows contiene el resultado definitivo de la sesión.
- Data Protector dispone de notificaciones para inicio y fin de sesión, errores y otros eventos. Configurar una notificación en el Cell Manager hacia un script o fichero de salida con formato estable.
- Alternativamente, integrar con la interfaz/CLI de reporting soportada por la versión instalada; evitar analizar archivos internos sin contrato estable.
- Enviar el resultado al inventario con el host o grupo al que corresponde la sesión, ya que una sesión puede proteger varios equipos.
- Definir cómo autenticar al agente que consulta el Cell Manager y limitar los permisos de lectura.

Referencias: [notificaciones de Data Protector](https://docs.microfocus.com/doc/200/24.1/notifications) y [archivos de registro](https://docs.microfocus.com/doc/data_protector/2019.12/logfiles).

### Mejoras comunes para este módulo

- Crear una interfaz `IBackupStatusProvider`, aislar cada proveedor y permitir activar/desactivar cada integración.
- Añadir una opción de configuración para la frecuencia y profundidad del sondeo.
- Mantener cursores por fuente, con detección de truncado/rotación y una retención acotada.
- Distinguir “no se detectó un resultado” de “la copia falló”. No inferir éxito por ausencia de errores.
- Añadir tests con registros anonimizados de cada versión soportada.
- Mostrar en servidor la hora del último éxito, la antigüedad y el último fallo; alertar si el equipo no tiene una copia reciente.

## Prioridad 1: cerrar y endurecer las actualizaciones automáticas

La base cliente ya está añadida, pero necesita completar el ciclo de publicación y validación:

- Publicar un manifiesto HTTPS con versión, URL del instalador y SHA-256, según [UPDATES.md](UPDATES.md).
- Configurar `UpdateManifestUrl` durante la instalación de Inno Setup y conservar esa configuración en futuras actualizaciones.
- Asegurar que el instalador use el mismo `AppId`, directorio y nombre de servicio; revisar que los parámetros silenciosos funcionen y que el servicio se detenga y vuelva a arrancar correctamente.
- Alinear la versión de `MonitorAgent.csproj`, el nombre/versión del instalador y el manifiesto.
- Añadir validación Authenticode del editor esperado. HTTPS más un hash servido desde el mismo origen detecta errores de descarga, pero no protege si se compromete el origen o el manifiesto.
- Limitar el tamaño del manifiesto y validar esquema, versión y URL; añadir descarga temporal, limpieza de parciales y reintentos con espera creciente.
- Registrar resultado, versión anterior/nueva, código de salida y fallo de arranque. Evitar reintentar en cada ciclo cuando hay un fallo permanente.
- Diseñar rollback a la versión anterior si el instalador falla o el servicio no vuelve a iniciar.
- Añadir canales (estable/pruebas), despliegue gradual por grupos, ventana de mantenimiento y opción de aplazar/desactivar actualizaciones.
- Mover el sondeo de actualización a una rutina independiente de la generación/envío del informe, para que un colector lento o una API de ingesta caída no retrase la actualización.
- Probar upgrade, downgrade bloqueado, hash incorrecto, instalador interrumpido, falta de red, servicio detenido, reinicio pendiente y equipos con rutas de instalación distintas.

## Prioridad 1: fiabilidad del agente y del envío

- Evitar que la cola descarte silenciosamente informes antiguos al superar el límite. Registrar el descarte y exponer contador/antigüedad de cola.
- Limitar la cola por cantidad **y tamaño total**, limpiar informes caducados con política documentada y avisar si se aproxima al límite.
- Definir orden e idempotencia de los reintentos. Actualmente un fallo al vaciar la cola no impide intentar enviar el informe nuevo; acordar con el servidor cómo evitar datos fuera de orden o duplicados.
- Guardar una clave de idempotencia por informe y hacer que el servidor deduplique reintentos.
- Añadir backoff exponencial con jitter para API, manifiesto y descargas; distinguir errores transitorios de HTTP 4xx permanentes.
- Añadir límites de tiempo por colector y por ciclo, y cancelación al detener el servicio.
- Asegurar que todas las excepciones relevantes quedan en logs sin filtrar tokens, contraseñas ni mensajes sensibles.
- Proteger explícitamente `state.json` con ACL, igual que la configuración y la cola.
- Rotar logs con política de tamaño/retención clara y proteger sus ACL.
- Medir duración por colector, tamaño del informe, número de fallos y estado de conectividad.

## Prioridad 2: seguridad y privacidad

- Reducir los datos personales/técnicos recopilados al mínimo requerido. Revisar necesidad de UUID, números de serie, dominio, licenciamiento de Windows y mensajes completos de eventos.
- Tratar `RustDeskPassword` como secreto: evitar incluirlo en informes generales o en la cola de informes; usar un canal de secretos separado con controles de acceso y retención.
- Definir retención, acceso, auditoría y borrado de inventarios e informes en el servidor.
- Aplicar límites al texto de eventos y sanear caracteres de control; mantener el límite actual y revisar datos que puedan contener rutas o nombres de usuario.
- Validar configuración al cargarla, registrar errores de formato y no continuar con valores peligrosos o incoherentes.
- Endurecer el protocolo del agente: rotación/revocación de credenciales, expiración, identidad por dispositivo y telemetría de uso de credenciales.
- Revisar permisos del servicio LocalSystem. Si las consultas necesarias lo permiten, evaluar una cuenta de servicio con privilegios mínimos.
- Verificar Authenticode de instaladores y binarios publicados, además del hash de transporte.

## Prioridad 2: calidad y cobertura de datos

- Corregir el caso documentado de `AdapterRAM`: WMI entrega 32 bits y puede truncar GPUs con más de 4 GB; valorar una fuente alternativa.
- Revisar la correspondencia por índice de baterías con `BatteryStaticData`/`BatteryFullChargedCapacity`; asociar por identificador cuando esté disponible.
- Ampliar la tabla de tipos de memoria RAM (por ejemplo LPDDR) y validar conversiones/unidades.
- Documentar las limitaciones de SMART y contadores de fiabilidad según NVMe, SATA, RAID y controladores; `SmartPredictFailure` nulo significa “sin dato”, no “saludable”.
- Validar y normalizar nombres/unidades y manejar valores sentinela que algunos proveedores WMI devuelven.
- Revisar el cálculo de estado de batería y salud para evitar desbordamientos en `FullChargeCapacity * 100`.
- Añadir la recogida de IP, conectividad o fabricante de software solo si hay un caso de uso y una política de privacidad claros.
- Hacer que los colectores WMI liberen explícitamente los `ManagementObject` que reciben.
- Mejorar los errores de colectores con códigos/tipos, sin abortar el informe completo.

## Prioridad 2: calidad del código y mantenibilidad

- Separar servicios de Windows, lógica de ciclo, transporte, persistencia y colectores mediante interfaces para poder probarlos sin hardware Windows.
- Añadir pruebas unitarias para serialización, cursor de eventos, cola, manifiesto, comparación de versiones y normalización de estados.
- Añadir pruebas de integración en Windows para WMI, Event Log, ACL, instalación/actualización de servicio e Inno Setup.
- Añadir compilación automatizada en CI para .NET Framework 4.8, análisis estático y generación de artefactos versionados.
- Mantener dependencias actualizadas y revisar vulnerabilidades conocidas de paquetes.
- Añadir `README.md` con arquitectura, requisitos, comandos de instalación, configuración, desinstalación, actualización y diagnóstico.
- Documentar contrato JSON del informe y las respuestas del servidor (incluido `event_extraction_mode`) con versionado y compatibilidad.
- Mantener el diagnóstico por logs o CLI; no añadir ventanas, iconos de bandeja ni procesos interactivos al agente.
- Estandarizar codificación UTF-8 para evitar mensajes con caracteres dañados.

## Prioridad 3: operación en el servidor central

- Mostrar en el servidor la versión del agente, última comunicación, duración de la cola y última actualización.
- Alertar por agente obsoleto, equipo sin comunicación, informe incompleto y ausencia de copia reciente.
- Añadir grupos, etiquetas y políticas diferentes por cliente/sede/equipo.
- Permitir configurar intervalos independientes para inventario, eventos, backup y actualizaciones.
- Añadir límites configurables de volumen de eventos y tipos de log, con una política por grupo.
- Incorporar métricas de salud del propio agente (último ciclo, errores consecutivos, disco libre, servicio en ejecución).
- Mantener paneles, informes y alertas en el servidor central; el servicio local solo recopila y transmite datos.
- Hacer que la desinstalación gestione errores y permita una opción documentada para conservar o eliminar configuración/estado.
- Revisar los nombres visibles: el servicio/ensamblado se llama `MonitorAgent`, mientras los instaladores de `dist` usan `SauberDesk`; unificar o documentar esta distinción.

## Orden sugerido de implementación

1. Probar y cerrar el flujo Inno Setup + manifiesto, incluyendo firma Authenticode y rollback.
2. Añadir estado de copia común y soporte Acronis mediante eventos de Windows.
3. Validar formatos reales de Iperius e implementar lectura incremental con pruebas de muestras.
4. Diseñar la integración de Data Protector desde el Cell Manager mediante notificación/script estable.
5. Mejorar cola, protección de estado, idempotencia y diagnósticos.
6. Añadir pruebas, CI, documentación operativa y alertas de agente/copia obsoletos.
