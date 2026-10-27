# ScreenPivot

Avisa de lo que pasa en las apps del equipo (host) mientras trabajas en un Cloud PC / Escritorio remoto a pantalla completa, y te resume lo ocurrido al volver. Plan y análisis de opciones: [docs/plan-comparativo.md](docs/plan-comparativo.md). Resultados de la Fase 0: [spikes/README.md](spikes/README.md).

## Cómo funciona

1. **Detección**:
   - Hooks de WinEvent sobre la ventana de sesión de Windows App (`msrdc.exe`, clase `TscShellContainerClass`), comparada con los monitores.
   - Modo remoto = algún monitor cubierto a pantalla completa (opcionalmente también maximizado).
2. **Fuentes**:
   - **Notificaciones** (`ToastSource`): `UserNotificationListener` lee los toasts del Centro de notificaciones, incluso los que silencia "No molestar". Con identidad de paquete usa el evento `NotificationChanged`; sin ella, sondea cada 2 s.
   - **Apps sin notificaciones** (`WindowActivitySource`, Fase 2), solo durante el modo remoto y nunca de la ventana en primer plano:
     - **Parpadeo** en la barra de tareas (shell hook `HSHELL_FLASH`), con 30 s de enfriamiento por ventana.
     - **Cambios de título** (`EVENT_OBJECT_NAMECHANGE`) que coinciden con las reglas de título (expresiones regulares):
       - si el primer grupo es un número (`^\((\d+)\)`), avisa cuando sube;
       - si no, avisa cuando el texto aparece o cambia.
     - El primer título que se ve de cada ventana solo sirve de referencia, así que arrancar no genera avisos.
     - Reglas por defecto:
       - "Compilación o despliegue terminado" (Alta);
       - "Mensajes sin leer en el título" (desactivada para no duplicar apps que ya notifican).
3. **Prioridad** (`PriorityEngine`): app silenciada → remitente VIP → palabra clave crítica → la mayor entre las reglas de app y la regla de título → Normal. Duplicados en 60 s se descartan.
4. **Overlay** (solo en modo remoto): ventana Win32 topmost que nunca toma el foco.
   - Crítico: banner fijo.
   - Alto: banner unos segundos.
   - Cualquier pendiente: contador.
   - **Sonidos por prioridad** (`PrioritySounds`, `SoundPlayer`):
     - Cada prioridad elige un sonido del esquema de Windows (Notificación, Mensaje instantáneo, Correo nuevo, Recordatorio, SMS, Asterisco, Exclamación, Error crítico), un archivo WAV propio o ninguno.
     - Ajustes tiene un botón *Probar* por fila.
     - Por defecto solo suenan Alta (Asterisco) y Crítica (Error crítico).
     - Un sonido que no existe queda en silencio en lugar de pitar y se anota en el log.
5. **Cambio de pantalla** (`WindowSwitcher`):
   - Lo que muestra el overlay:
     - **Banner:** *Abrir* lleva a la app del aviso y *✕* lo cierra.
     - **En el Cloud PC sin avisos:** acceso rápido *Ir al equipo*, que se puede desactivar.
     - **En el equipo con una sesión abierta:** *↩ Volver al Cloud PC*.
   - Cómo se busca la app:
     - parpadeos y títulos van a la ventana exacta que los produjo;
     - los toasts se buscan por AUMID (ventana o proceso empaquetado), luego por nombre del exe o título;
     - si no hay ventana, se lanza con `shell:AppsFolder\<AUMID>`.
   - Modo configurable: traer la app encima del Cloud PC (por defecto) o minimizar el Cloud PC. Si Windows niega el foco, se minimiza la sesión y se reintenta.
   - El Resumen y la bandeja también tienen *Abrir* por app, *Volver al Cloud PC* y *Minimizar Cloud PC*.
6. **Panel lateral** (`PanelWindow`):
   - Se abre con la píldora, el icono de la bandeja o al volver del Cloud PC.
   - Es una ventana sin marco, siempre encima, fuera de Alt+Tab y de la barra de tareas.
   - Se acopla al borde elegido (izquierda, derecha, arriba o abajo) del monitor elegido, por defecto el monitor donde está el cursor al hacer clic (`PanelLayout`, `MonitorPicker`):
     - en los laterales: 420 DIP de ancho y toda la altura;
     - arriba o abajo: 440 DIP de alto y hasta 980 DIP de ancho, centrado.
   - Sobre el Cloud PC ocupa el monitor entero; en el equipo respeta su barra de tareas.
   - Al abrirse toma el foco (con `AttachThreadInput` si el cliente RDP lo retiene). Por eso Windows muestra la barra de tareas del equipo encima del Cloud PC mientras está abierta.
   - Se oculta cuando el foco pasa a otra app por un clic o una tecla tuya, con Esc o con ✕. Ignora los rebotes de foco de los primeros 600 ms y las apps que se ponen delante solas. Los diálogos propios, como el selector de WAV, no lo ocultan. Mientras está abierto, el overlay no se dibuja encima.
   - Contenido:
     - el **resumen** siempre visible, con *Volver al Cloud PC / Minimizar Cloud PC* fijos al pie y *Marcar como revisado / Borrar historial* en el menú ⋯;
     - el **engranaje** cambia a los **Ajustes**, en una sola columna, con *Guardar* fijo al pie.
7. **Resumen**: al salir del Cloud PC se abre agrupado por app. El historial se guarda cifrado con DPAPI (usuario actual) y caduca según la retención (24 h por defecto). Si fue ScreenPivot quien minimizó la sesión, no se abre.
8. **Bandeja**: abrir resumen, ajustes, volver al Cloud PC o minimizarlo, pausar, salir. Instancia única.
9. **Diagnóstico**: `%LOCALAPPDATA%\ScreenPivot\diagnostics.log` registra cambios de modo remoto y decisiones (origen, app, prioridad, motivo). Nunca registra el título ni el texto de las notificaciones.

## Estructura

| Ruta | Contenido |
|---|---|
| `src/ScreenPivot.Core` | Modelo, reglas, `EventHub`, almacenamiento, detección, fuentes, cambio de ventanas, decisión del overlay. Sin UI. |
| `src/ScreenPivot.App` | WinUI 3 (Windows App SDK 1.8): bandeja, overlay, páginas Resumen y Ajustes, manifiesto MSIX. |
| `tests/ScreenPivot.Core.Tests` | xUnit: cobertura, prioridades, hub, resumen, almacenamiento, emparejado de ventanas, overlay, reglas de título. |
| `spikes/ScreenPivot.Spikes` | Herramientas de diagnóstico de consola (ver abajo). |
| `tools/generate-assets.ps1` | Regenera el icono y los logos MSIX. |

## Compilar y ejecutar

Requiere .NET SDK 10.

```
dotnet test tests\ScreenPivot.Core.Tests
dotnet build src\ScreenPivot.App
src\ScreenPivot.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ScreenPivot.exe
```

La build de desarrollo es **sin empaquetar y autocontenida** (no necesita certificado ni runtime instalado). Configuración, historial y log: `%LOCALAPPDATA%\ScreenPivot`.

### Diagnóstico (`spikes\ScreenPivot.Spikes\bin\Debug\net10.0-windows10.0.26100.0\ScreenPivot.Spikes.exe`)

| Comando | Para qué |
|---|---|
| `detect [--watch]` | Monitores, ventana de sesión y cobertura. |
| `apps` | Ventanas a las que puede ir *Abrir*, con sus AUMID. |
| `activity [--seconds N]` | Parpadeos y cambios de título que detecta la fuente. |
| `fake-app [--delay N]` | App falsa minimizada: cambia su título a "Build succeeded" y parpadea. |
| `notifications`, `overlay` | Pruebas de la Fase 0. |

### Paquete para Microsoft Store

```
dotnet build src\ScreenPivot.App -c Release -p:Packaged=true -p:Platform=x64
```

Genera `src\ScreenPivot.App\AppPackages\ScreenPivot.App_<versión>_x64_Test\*.msix` sin firmar (la Store firma al publicar). Antes de enviar:
- Sustituir `Identity Name/Publisher` y `PublisherDisplayName` de `Package.appxmanifest` por los de la reserva en Partner Center.
- Justificar `runFullTrust` y `userNotificationListener`, y publicar política de privacidad.
- Pasar Windows App Certification Kit.

Para instalarlo localmente hace falta modo desarrollador o un certificado de confianza (requiere administrador).

Todo lo necesario para publicar está en [docs/store/](docs/store/README.md): checklist de Partner Center, política de privacidad, textos de la ficha (ES/EN), arte y notas para certificación.

### Modo demo

`ScreenPivot.exe --demo` (o `screenpivot --demo` con la versión empaquetada, gracias al alias de ejecución):
- abre el Resumen con una sesión remota ficticia;
- usa la carpeta `%LOCALAPPDATA%\ScreenPivot\demo` y no lee notificaciones ni ventanas reales;
- puede convivir con la instancia normal.

Sirve para las capturas de la Store y para los certificadores sin Cloud PC.

## Estado verificado (2026-10-22)

- ✅ 79 tests de Core.
- ✅ Panel lateral validado por el usuario sobre el Cloud PC: se abre con el primer clic en *Ir al equipo* (en el monitor del cursor).
- ✅ Panel lateral en modo demo:
  - se acopla al borde derecho del monitor principal (525 px a escala 125 %);
  - el engranaje abre Ajustes y *Ocultar* lo esconde (comprobado con automatización de interfaz).
- ✅ Paquetes de Store x64 y ARM64 generados, con idioma `ES-ES` y alias `screenpivot.exe` en el manifiesto validado.
- ✅ Sonidos:
  - los 8 sonidos del catálogo tienen archivo en el esquema de Windows de este equipo;
  - `PlaySound` reproduce los existentes y falla en silencio con uno inexistente.
- ✅ La app arranca; una segunda ejecución se redirige a la primera.
- ✅ Un toast real llega al historial cifrado con la prioridad correcta.
- ✅ Con el Cloud PC real a pantalla completa (una ventana de 5760×1080 en 3 monitores, no topmost):
  - el contador, "Ir al equipo" y el banner con *Abrir / ✕* se ven encima de la sesión;
  - el foco se queda en el Cloud PC.
- ✅ Banner de prueba validado por el usuario: se ve, suena, se cierra con clic y el teclado sigue en la sesión.
- ✅ `apps` encuentra los AUMID reales: Teams (proceso), Outlook PWA de Edge (ventana), WhatsApp, Chrome…
- ✅ Fase 2 de punta a punta con el Cloud PC a pantalla completa (`fake-app` como proceso aparte):
  - el cambio de título entró como **Alto** por la regla de compilación;
  - el parpadeo entró como **Normal**.
- ✅ El MSIX se genera con el manifiesto válido.
- ✅ Cambio de pantalla validado por el usuario con clics reales: *Abrir* trae la app correcta y *↩ Volver al Cloud PC* reactiva la sesión.
- ✅ Detección real de una app sin toast: un parpadeo de Microsoft Teams entró como **Alto** por la regla "Mensajes de Teams".

## Pendiente

- **Build empaquetada instalada**: confirmar el evento `NotificationChanged`, el consentimiento de acceso y la tarea de inicio (`StartupTask`).
- La ventana abrió minimizada al lanzarla desde una consola sin ventana; verificar el arranque normal (menú Inicio / doble clic).
- Revisar en la app la sección *Ajustes → Sonidos*: *Probar*, elegir un WAV propio y oírlo con un aviso real sobre el Cloud PC.
- Desviación del plan: el historial usa JSON cifrado con DPAPI en lugar de SQLite (suficiente para el volumen actual).
