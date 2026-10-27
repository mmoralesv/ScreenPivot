# Publicación en Microsoft Store — checklist

| Archivo | Para qué |
|---|---|
| [privacy-policy.md](privacy-policy.md) | Política de privacidad (ES + EN), para alojar en una URL pública |
| [listing-es.md](listing-es.md) | Textos de la ficha en español (idioma del paquete) |
| [listing-en.md](listing-en.md) | Textos de la ficha en inglés (opcional; la interfaz está en español) |
| `art/BoxArt_*.png` | Logotipo 1:1 de la ficha (2160, 1080 y 300 px), generado por `tools/generate-assets.ps1` |

Marca cada casilla al completarla.

**Datos del editor:** JEMA.Tech (Marcel Morales) · Store ID `9P09TKC1PX1Z` · identidad `JEMA.Tech.ScreenPivot` · soporte en [GitHub Issues](https://github.com/mmoralesv/ScreenPivot/issues) · sitio web [github.com/mmoralesv/ScreenPivot](https://github.com/mmoralesv/ScreenPivot).

**Política de privacidad:** <https://mmoralesv.github.io/ScreenPivot/store/privacy-policy.html> (GitHub Pages desde `/docs`).

---

## 1. Cuenta y reserva del nombre

- [ ] Cuenta de desarrollador en [Partner Center](https://partner.microsoft.com/dashboard) (individual o empresa; la de empresa requiere verificación de la organización y tarda más).
- [ ] *Apps y juegos → Nueva aplicación* → reservar **ScreenPivot** (alternativas en [listing-es.md](listing-es.md)).
- [ ] En *Administración del producto → Identidad del producto*, copiar:
  - `Package/Identity/Name`
  - `Package/Identity/Publisher` (`CN=…`)
  - `Package/Properties/PublisherDisplayName`

## 2. Preparar el paquete

- [ ] Pegar esos valores en [`src/ScreenPivot.App/Package.appxmanifest`](../../src/ScreenPivot.App/Package.appxmanifest). Hoy son marcadores: `Name="ScreenPivot"`, `Publisher="CN=ScreenPivotDev"`, `PublisherDisplayName` = ScreenPivot.
- [ ] Subir la versión a `1.0.0.0` (`Identity Version`). La Store exige que el cuarto número sea `0`, y cada envío posterior necesita una versión mayor.
- [x] Versión mínima: **Windows 11** (`MinVersion 10.0.22000.0`), lo único probado.
- [x] Las fichas solo prometen **Windows App (Windows 365 / Cloud PC)**. Si se prueba `mstsc` a pantalla completa, se puede añadir «Conexión a Escritorio remoto».
- [ ] Alojar [privacy-policy.md](privacy-policy.md) en una URL pública y ponerla en `App.PrivacyPolicyUrl` ([App.xaml.cs](../../src/ScreenPivot.App/App.xaml.cs)). Así aparece el enlace en *Ajustes → Privacidad*; mientras esté vacía, el enlace queda oculto.
- [ ] Compilar los paquetes de Store, uno por arquitectura:
  ```
  dotnet build src\ScreenPivot.App -c Release -p:Packaged=true -p:Platform=x64 -r win-x64
  dotnet build src\ScreenPivot.App -c Release -p:Packaged=true -p:Platform=ARM64 -r win-arm64
  ```
  Resultado: `src\ScreenPivot.App\AppPackages\ScreenPivot.App_<versión>_{x64,arm64}_Test\*.msix`, sin firmar (la Store firma al publicar). La dependencia `Microsoft.WindowsAppRuntime.1.8` la instala la Store sola.
- [ ] **Windows App Certification Kit** (consola de administrador; ya está instalado con el SDK):
  ```
  "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" reset
  "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath <ruta\ScreenPivot.App_1.0.0.0_x64.msix> -reportoutputpath %TEMP%\wack-x64.xml
  ```
  Revisar que el informe diga `OVERALL_RESULT="PASS"`.
- [ ] **Instalar el MSIX en una máquina limpia** (con modo desarrollador y un certificado de prueba, o pidiéndolo a TI) y comprobar lo que no se pudo verificar sin empaquetar:
  - el diálogo de acceso a notificaciones;
  - que llegan al instante (evento `NotificationChanged`);
  - *Iniciar con Windows* (`StartupTask`);
  - el alias `screenpivot --demo` desde una terminal;
  - que la ventana no se abre minimizada desde el menú Inicio.

## 3. Propiedades (Partner Center → *Propiedades*)

- [ ] Categoría: **Productividad**.
- [ ] **URL de la política de privacidad** (paso 2): obligatoria, porque la app accede a las notificaciones.
- [ ] Sitio web y **contacto de soporte** (correo o URL).
- [ ] Requisitos del sistema: sin hardware especial. En «Declaraciones del producto»:
  - no marcar «probada con las directrices de accesibilidad» si no se ha hecho esa prueba;
  - no depende de controladores ni servicios de terceros.

## 4. Clasificación por edades (cuestionario IARC)

- [ ] Tipo: **aplicación** (no juego). Respuestas que corresponden a ScreenPivot:
  - sin violencia, sexo, drogas, apuestas ni lenguaje ofensivo;
  - sin interacción entre usuarios ni contenido generado por usuarios que se comparta;
  - no comparte la ubicación ni información personal con terceros;
  - sin compras digitales.
- [ ] Resultado esperado: apta para todas las edades (3+ / Everyone).

## 5. Precio y disponibilidad

- [ ] Precio (por ejemplo, gratis) y mercados.
- [ ] Visibilidad: conviene un primer envío **oculto** (solo con enlace directo o a usuarios concretos) para probar la instalación desde la Store antes de hacerla pública.

## 6. Ficha de Store

- [ ] **Español**: copiar [listing-es.md](listing-es.md) campo por campo.
- [ ] **Inglés** (opcional): [listing-en.md](listing-en.md). Mantiene el aviso de que la interfaz está en español.
- [ ] Logotipo 1:1: `art/BoxArt_2160x2160.png` (o la de 1080).
- [ ] **Capturas** (ver abajo): mínimo 1 y hasta 10, PNG de al menos 1366×768.

### Capturas

Ya generadas en [`screenshots/`](screenshots/), a 1920×1080: el panel y el banner reales en modo demo, sobre un fondo neutro. Se suben en *Store listings → Screenshots → Desktop* de cada idioma, en este orden:

| Archivo | Leyenda (ES) |
|---|---|
| `01-banner.png` | Un banner crítico aparece encima del Cloud PC sin quitarle el foco. «Abrir» te lleva a la app. |
| `02-resumen.png` | Al volver, el resumen agrupa lo ocurrido por app y por importancia. |
| `03-prioridades.png` | Elige qué es crítico: remitentes VIP, palabras clave y reglas por app. |
| `04-apps-sin-notificaciones.png` | Avisos también de apps sin notificaciones: parpadeos y cambios de título. |
| `05-sonidos.png` | Un sonido para cada prioridad. |

Para regenerarlas a mano:

Usa siempre el **modo demo**, que tiene datos ficticios, va a una carpeta aparte, no lee notificaciones reales y no toca tu historial:

```
src\ScreenPivot.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ScreenPivot.exe --demo
```

O `screenpivot --demo` con la versión empaquetada. Puede ejecutarse a la vez que la instancia normal.

1. **Banner sobre el escritorio remoto.** Abre una sesión remota a pantalla completa con un escritorio neutro (sin datos de la empresa). Luego, en el engranaje → *Avisos sobre el Cloud PC → Probar banner en 5 s*, cambia a la sesión y captura la pantalla completa.
2. **Panel lateral sobre la sesión.** El panel abierto en el borde derecho, encima del escritorio remoto, con la sesión de ejemplo (llamada de Teams, correo urgente, compilación…). Captura la pantalla completa para que se vea anclado al borde.
3. **Prioridades.** Engranaje → *Prioridades* con un par de remitentes VIP y palabras clave ficticios.
4. **Apps sin notificaciones.** Engranaje → *Apps sin notificaciones*, con las reglas de título.
5. **Sonidos.** Engranaje → *Sonidos*.

Las leyendas de cada captura están en las fichas. Revisa que no se vea ningún nombre, correo ni contenido real.

## 7. Opciones del envío y notas para certificación

- [ ] **Capacidad restringida `runFullTrust`.** Pegar en el cuadro de justificación:

  > ScreenPivot is a Win32 desktop app (WinUI 3 / Windows App SDK) packaged as MSIX; runFullTrust is the standard capability for packaged desktop apps. It uses Win32 APIs not available in an AppContainer: WinEvent and shell hooks to detect when a Remote Desktop / Windows App window covers the screen and when other apps flash their taskbar button or change their window title; a topmost, non-activating overlay window to show alerts over the full-screen remote session; and window enumeration plus SetForegroundWindow to switch to the app that raised an alert when the user clicks "Open". It installs no services or drivers, never elevates, and makes no network connections.

- [ ] **Notas para certificación.** Pegar:

  > ScreenPivot shows alerts from local apps while a Remote Desktop (Windows App / Windows 365, or mstsc) session covers the screen in full screen. The interface is in Spanish.
  >
  > Quick test without a remote session: run `screenpivot --demo` from a terminal. It opens the side panel docked to the right edge with fictional sample data, uses a separate data folder and reads no real notifications. The gear icon opens the settings ("Ajustes"); there, "Probar banner en 5 s" shows the alert banner even without a remote session. The panel hides when another app gets focus or with Esc.
  >
  > Full test: 1) Launch ScreenPivot; in the panel's settings (gear icon) click "Permitir acceso" and accept the Windows notification-access prompt. 2) Open any Remote Desktop connection in full screen. A small "ScreenPivot · Ir al equipo" pill appears in the bottom-right corner. 3) Trigger a notification on the local machine (e.g. an email, or a message whose text contains "urgente"). A banner appears over the remote session without taking keyboard focus. "Abrir" brings the source app to the front and "↩ Volver al Cloud PC" returns to the session. Clicking the pill opens the side panel over the session. 4) Leave full screen: the panel opens with the summary of what happened.
  >
  > The app runs from the notification area (tray icon). Hiding the panel does not exit; use the tray menu "Salir". All data stays on the device; see the privacy policy.

- [ ] Enviar y revisar el informe de certificación. Si se rechaza por la política de notificaciones o de privacidad, comprobar que la URL de la política funciona y que la descripción explica claramente que la app lee notificaciones.

## Riesgos conocidos ante la certificación

- **Leer notificaciones de otras apps** (`userNotificationListener`): es una capacidad estándar con consentimiento del usuario, pero exige política de privacidad y que la descripción lo explique. La app pide el acceso en *Ajustes* con un texto que explica para qué.
- **Overlay siempre encima**: la ficha y las notas explican que es la función principal. El overlay es pequeño, se puede pausar, el acceso rápido se puede desactivar y nunca toma el foco.
- **Inicio con Windows**: desactivado por defecto; solo lo activa el usuario desde *Ajustes*.
- **Marcas de terceros**: la ficha menciona Windows App, Windows 365, Teams y Outlook solo como compatibilidad, con la línea de marcas y «no afiliada a Microsoft». No usar logotipos de Microsoft en las capturas ni en el arte.
