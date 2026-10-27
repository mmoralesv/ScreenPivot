# Fase 0 — Spikes técnicos

App de consola (.NET 10, `net10.0-windows10.0.26100.0`) con tres pruebas aisladas de los riesgos del [plan](../docs/plan-comparativo.md). Interop Win32 con P/Invoke directo, sin dependencias externas.

```
dotnet build ScreenPivot.sln
dotnet test ScreenPivot.sln
spikes\ScreenPivot.Spikes\bin\Debug\net10.0-windows10.0.26100.0\ScreenPivot.Spikes.exe <comando>
```

| Comando | Qué prueba |
|---|---|
| `detect` | Monitores, ventanas de Windows App/RDP y cobertura de cada monitor (`None` / `Maximized` / `FullScreen`). |
| `detect --watch --seconds 60` | Detección por eventos (WinEvent hooks) y transiciones host ↔ remoto. |
| `overlay --delay 5 --seconds 30 [--monitor I] [--no-reassert]` | Banner topmost click-through; mide si queda encima del Cloud PC y si roba el foco. |
| `notifications --seconds 60 [--request] [--show-text]` | `UserNotificationListener`: acceso, toasts existentes, nuevos (evento y polling). El texto se oculta por defecto. |

## Resultados (2026-10-22, Windows 11 26200, 3 monitores 1920x1080, escala 125 %)

### Detección de Windows App — ✅ validado (parcial)
- La sesión del Cloud PC es una ventana del proceso **`msrdc.exe`** con clase **`TscShellContainerClass`** y título = nombre del Cloud PC. El proceso `Windows365.exe` es solo el lanzador ("Windows App", clase `MainWindow`).
- Minimizada se detecta correctamente (`MINIMIZADA`, cobertura `None`).
- **Confirmado después, con la sesión a pantalla completa en 3 monitores:**
  - Windows App usa **una sola ventana `msrdc` de 5760×1080** que abarca todos; los 3 monitores se detectan `FullScreen`.
  - La ventana **no es topmost**.
  - `SHQueryUserNotificationState` devuelve `BUSY`.
  - El PID de `msrdc` cambia al reconectar (no hay que cachearlo).

### UserNotificationListener — ✅ validado, con un hallazgo importante
- **Funciona sin empaquetar**: `GetAccessStatus = Allowed`, `GetNotificationsAsync` devuelve los toasts del Centro de notificaciones con nombre de la app y textos.
- **El evento `NotificationChanged` NO funciona sin identidad de paquete** (`COMException 0x80070490`, elemento no encontrado).
- **El polling cada 2 s funciona**: un toast de prueba se detectó a los ~2 s.
- Implicación de diseño: `ToastSource` usará el evento cuando la app corra empaquetada (MSIX de Store) y polling como fallback. Hay que validar el evento en la build MSIX de la Fase 1 (aquí no se puede: sin modo desarrollador ni admin para confiar un certificado).
- **Pendiente (manual):** confirmar que con "No molestar" automático (Cloud PC a pantalla completa) los toasts de Outlook/Teams siguen llegando al listener.

### Overlay topmost — ⚠️ mecánica validada, z-order sobre el Cloud PC pendiente
- Ventana `WS_EX_TOPMOST | TOOLWINDOW | NOACTIVATE | LAYERED | TRANSPARENT` creada y escalada por DPI (125 %); no tomó el foreground (0 veces).
- **No concluyente** en el spike: durante la prueba el Cloud PC estaba minimizado.
- **Confirmado después con la app MVP** sobre el Cloud PC real a pantalla completa:
  - el contador del overlay se ve en pantalla;
  - en el orden de ventanas (z-order) queda por delante de la sesión;
  - el foreground sigue siendo la sesión.

  Falta el banner con clic (ver el README raíz).

## Pruebas manuales pendientes

1. **Overlay sobre el Cloud PC** (riesgo principal). Ejecuta, y en los 5 s de espera pasa al Cloud PC a pantalla completa y trabaja normalmente (escribe, cambia de ventanas dentro de la sesión):
   ```
   ScreenPivot.Spikes.exe overlay --delay 5 --seconds 60
   ScreenPivot.Spikes.exe overlay --delay 5 --seconds 60 --no-reassert
   ```
   Éxito: el banner se ve durante toda la prueba, el teclado sigue funcionando en el Cloud PC y el resumen dice `OK`. La segunda corrida indica si la re-afirmación del topmost es necesaria. Repetir con el Cloud PC en varios monitores.
2. **Detección en vivo**: `detect --watch --seconds 90` mientras maximizas, minimizas y cambias a pantalla completa (1 y varios monitores).
3. **Notificaciones con No molestar**: `notifications --seconds 120` en una consola del host, luego Cloud PC a pantalla completa y pide que te escriban por Teams / envíate un correo. Revisa la salida al volver.
