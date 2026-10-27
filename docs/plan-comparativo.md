# ScreenPivot — Plan comparativo y recomendación

## Contexto

Cuando el usuario trabaja en un Cloud PC (Windows App / Windows 365) maximizado en una o varias pantallas, el escritorio del host queda totalmente cubierto. Además, Windows 11 activa por defecto **No molestar automático "al usar una app en pantalla completa"**, así que los toasts del host (Outlook, Teams, Slack, navegador…) se van en silencio al Centro de notificaciones. El resultado: se pierden avisos urgentes del host o hay que salir de la sesión remota para revisarlos.

**ScreenPivot** será una app pequeña en el host que: (1) detecta cuándo la sesión remota cubre las pantallas, (2) captura eventos de apps del host, (3) los prioriza, (4) los muestra de forma no intrusiva por encima de la sesión remota y (5) entrega un resumen al volver al host. Debe poder publicarse en Microsoft Store.

Decisiones tomadas con el usuario: fuentes = Outlook/Teams + cualquier app con toasts + apps sin toasts; entrega = overlay en el host; resumen = reglas por defecto con IA opcional (local primero); stack = C# / WinUI 3. Directorio `C:\Repos\ScreenPivot` vacío (greenfield).

---

## 1. Análisis comparativo de opciones

### 1.1 Captura de cambios en el host (fuentes)

| Opción | Qué cubre | Pros | Contras | Store | Veredicto |
|---|---|---|---|---|---|
| **A. `UserNotificationListener`** (WinRT) | Todos los toasts del host, incluso los silenciados por No molestar | API oficial, contenido estructurado (app, título, texto), consentimiento del usuario, bajo costo | Solo apps que emiten toasts; requiere identidad de paquete (MSIX) | ✅ Capability `userNotificationListener` | **Núcleo — MVP** |
| **B. Hooks de shell/ventanas** (`RegisterShellHookWindow` HSHELL_FLASH, `SetWinEventHook` EVENT_OBJECT_NAMECHANGE) | Parpadeo en barra de tareas, cambios de título (p.ej. "(3) Inbox", "Build succeeded") | Cubre apps sin toasts; ligero; sin privilegios | Señal pobre (no hay contenido), heurístico por app | ✅ con `runFullTrust` | **Complemento — Fase 2** |
| **C. UI Automation** (lectura de árbol UIA de ventanas concretas) | Texto de controles específicos (estado de build, badge) | Contenido real de apps sin toasts | Frágil ante cambios de UI, costo de CPU, configuración por app | ✅ | Opcional, reglas avanzadas por app |
| **D. Captura de pantalla + diff/OCR** (`Windows.Graphics.Capture` + `Windows.Media.Ocr`) | Cualquier cambio visual en una región/ventana | Universal | Costoso, ventanas ocultas detrás del Cloud PC pueden no repintarse, sensible en privacidad, revisión Store más estricta | ⚠️ `graphicsCapture` + justificación | Descartar en v1; experimento futuro |
| **E. Integraciones API** (Microsoft Graph para correo/Teams/calendario) | Correo, chats, menciones, reuniones con metadatos ricos (remitente VIP, importancia) | Datos fiables, priorización precisa, funciona aunque la app no esté abierta | OAuth/Entra ID, consentimiento de admin en tenants corporativos, solo M365 | ✅ | Fase 3, opcional (A ya cubre Outlook/Teams vía toasts) |

### 1.2 Entrega del aviso sobre la sesión remota maximizada

| Opción | Pros | Contras | Veredicto |
|---|---|---|---|
| **1. Overlay topmost propio** (ventana `WS_EX_TOPMOST | TOOLWINDOW | NOACTIVATE | LAYERED`, click‑through opcional) — banner/ticker en un borde | Control total de UX y prioridad, no roba foco ni teclado de la sesión remota, no depende de No molestar | Windows App en pantalla completa también es topmost: hay que re‑afirmar z‑order al cambiar foreground (`EVENT_SYSTEM_FOREGROUND`); validar con multi‑monitor y DPI | **Elegida (por usuario)** — requiere spike técnico |
| **2. Toast propio con `scenario="urgent"` / app prioritaria en No molestar** | Cero UI propia, nativo | Depende de configuración del usuario; el toast se ve dentro del área del host, puede quedar debajo; sin control fino | Respaldo para prioridad Crítica |
| **3. Señal sonora/háptica por prioridad** | Funciona aunque nada sea visible | Molesto si abusa; audio puede estar redirigido | Complemento configurable |
| **4. App compañera dentro del Cloud PC** (relay en nube tipo Azure Web PubSub o plugin de canal virtual RDP/DVC) | Aviso nativo dentro de la sesión | Backend + auth, políticas corporativas, DVC difícil de distribuir por Store | Descartada para v1 (fase futura) |
| **5. Push al móvil** (webhook Teams/ntfy/Pushover) | Alcanza al usuario fuera del PC | Saca datos del equipo; dependencia externa | Opcional, fase futura |

### 1.3 Detección de "modo remoto cubriendo pantallas"

- Enumerar ventanas de los procesos de Windows App / cliente RDP (`msrdc.exe`, `Windows365.exe`, `mstsc.exe`, configurable) y comparar su rect con los rects de `EnumDisplayMonitors`; cobertura ≥ 1 monitor = modo remoto.
- Complementar con `SHQueryUserNotificationState` (QUNS_BUSY / QUNS_PRESENTATION_MODE) y `EVENT_SYSTEM_FOREGROUND` / `EVENT_OBJECT_LOCATIONCHANGE` para reaccionar sin polling.
- Fuera de modo remoto la app queda pasiva (solo registra).

### 1.4 Resumen / priorización

| Opción | Pros | Contras | Veredicto |
|---|---|---|---|
| **Reglas locales** (app, remitente VIP, palabras clave, horario, deduplicado, agrupación por app/hilo) | Privado, determinista, instantáneo | Resúmenes "mecánicos" | **Por defecto** |
| **IA local** (Windows AI APIs / Phi Silica en Copilot+ PC; fallback ONNX pequeño) | Privado, resumen en lenguaje natural | Solo hardware compatible | Opcional, Fase 3 |
| **IA en la nube** (Claude / Azure OpenAI con clave del usuario) | Mejor calidad | Envía contenido de notificaciones fuera; política de privacidad más exigente para Store | Opt‑in explícito, Fase 3 |

### 1.5 Stack (ya decidido: C# / WinUI 3) — por qué encaja con Store

- **WinUI 3 + Windows App SDK + .NET 8, MSIX empaquetado**: identidad de paquete (necesaria para `UserNotificationListener`), `startupTask` para autoarranque, actualizaciones por Store.
- Interop Win32 con **CsWin32** (hooks, z‑order, estilos de ventana). Para el overlay: ventana WinUI 3 con `AppWindow` + `OverlappedPresenter` sin bordes, estilos extendidos vía `SetWindowLongPtr`; si la transparencia/click‑through resulta problemática, ventana overlay en Win32/Composition puro dentro del mismo proceso (decidir en spike).
- Alternativas descartadas: WPF (más fácil para overlays, pero UI menos moderna; plan B si el spike de WinUI falla), Tauri/Electron (identidad de paquete y WinRT más laboriosos, mayor footprint).

---

## 2. Arquitectura recomendada

```
ScreenPivot (MSIX, full trust, un proceso, icono en bandeja)
├─ Core/
│  ├─ RemoteSessionDetector   — ¿Cloud PC cubre monitores? (eventos WinEvent + rects)
│  ├─ Sources/
│  │  ├─ ToastSource          — UserNotificationListener (NotificationChanged + polling de respaldo)
│  │  ├─ ShellFlashSource     — HSHELL_FLASH / cambios de título por reglas
│  │  └─ (Fase 3) GraphSource, UiaSource
│  ├─ EventPipeline           — normaliza a HostEvent {app, título, texto, timestamp, origen}
│  ├─ PriorityEngine          — reglas → Crítica / Alta / Normal / Baja; dedup + agrupación
│  ├─ DigestStore             — SQLite local (cifrado con DPAPI), retención configurable
│  └─ Summarizer              — RulesSummarizer (default) | LocalAi | CloudAi (opt‑in)
├─ Presentation/
│  ├─ OverlayWindow           — ticker/banner por monitor elegido, click‑through, auto‑ocultar
│  ├─ Alerting                — sonido por prioridad, toast urgent de respaldo
│  └─ DigestView              — resumen al salir del modo remoto o con hotkey
└─ Settings/                  — reglas, monitor/borde, colores, privacidad, autoarranque
```

Comportamiento por prioridad (en modo remoto):
- **Crítica** (llamada Teams, remitente VIP, palabra clave): banner visible + sonido; persiste hasta descartar.
- **Alta**: banner breve (≈5 s) y contador en el ticker.
- **Normal/Baja**: solo contador en el ticker; van al resumen.
- Al salir del modo remoto (o con hotkey global): se abre **DigestView** agrupado por app.
- Interacción mínima con el overlay para no capturar input de la sesión remota: por defecto click‑through; clic con modificador o hotkey para expandir/descartar.

---

## 3. Fases de implementación

**Fase 0 — Spikes de riesgo (1–2 semanas)**
1. Overlay topmost sobre Windows App maximizado en 1 y 2 monitores, con DPI mixto; verificar que no roba foco ni teclado.
2. `UserNotificationListener` recibiendo toasts de Outlook/Teams con No molestar automático activo.
3. Detección fiable de la ventana de Windows App/msrdc a pantalla completa.

**Fase 1 — MVP (Store‑ready)**: proyecto WinUI 3 empaquetado (`ScreenPivot.App`, `ScreenPivot.Core`, `ScreenPivot.Tests`), bandeja del sistema, RemoteSessionDetector, ToastSource, PriorityEngine con reglas básicas (app + palabras clave + VIP), OverlayWindow, DigestView, ajustes, autoarranque, onboarding que solicita el permiso de notificaciones.

**Fase 2 — Apps sin toasts**: ShellFlashSource, reglas de cambio de título por app, sonidos por prioridad, modo "solo mi monitor X".

**Fase 3 — Enriquecimiento**: Summarizer con IA local (Windows AI APIs) y nube opt‑in; GraphSource opcional para M365; UiaSource para reglas avanzadas.

**Fase 4 — Publicación**: Partner Center, política de privacidad (procesamiento local, qué se guarda, opt‑in nube), justificación de `runFullTrust` y `userNotificationListener`, capturas, pruebas WACK, localización ES/EN.

### Capacidades del manifiesto
`runFullTrust` (restringida, aceptada para apps de escritorio), `userNotificationListener`, `internetClient` (solo si IA nube/Graph), extensión `windows.startupTask`. Evitar hooks de teclado globales de bajo nivel (usar `RegisterHotKey`).

### Riesgos principales
- Z‑order frente a Windows App pantalla completa (mitigación: re‑afirmar topmost en eventos de foreground; plan B: toast urgent + sonido).
- Políticas corporativas (Intune) que bloqueen apps de Store o el acceso a notificaciones.
- Privacidad: contenido de notificaciones de terceros → almacenamiento local cifrado, retención corta, IA nube solo opt‑in.

---

## 4. Verificación

- **Unit tests** (`ScreenPivot.Tests`, xUnit): PriorityEngine, dedup/agrupación, RulesSummarizer, lógica de cobertura de monitores con rects simulados.
- **Prueba E2E manual**: conectar Cloud PC maximizado (1 y 2 monitores) → enviar correo VIP a Outlook del host, mensaje de Teams y toast de prueba (`New-BurntToastNotification` o app de prueba) → verificar banner/ticker por prioridad, sin pérdida de foco en la sesión remota → salir de pantalla completa y verificar DigestView.
- **App de prueba de parpadeo** (`FlashWindowEx`) y cambio de título para validar Fase 2.
- **Windows App Certification Kit** sobre el MSIX antes de enviar a Store; instalación limpia en máquina sin herramientas de desarrollo.
