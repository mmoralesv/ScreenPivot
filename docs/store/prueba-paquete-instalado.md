# Prueba del paquete instalado (antes de certificar)

Objetivo: comprobar lo que solo funciona con la app **empaquetada (MSIX)**, que no se pudo verificar en el equipo de desarrollo por falta de permisos de administrador.

## 1. Preparar el paquete (equipo de desarrollo)

```
powershell -ExecutionPolicy Bypass -File toolsuild-packages.ps1 -Channel Store
powershell -ExecutionPolicy Bypass -File tools\make-test-package.ps1
```

O, sin compilar nada: el zip del último pre-release `dev-…` en GitHub (*Releases*), que instala **ScreenPivot Dev** junto a la versión de la Store ([docs/ci-cd.md](../ci-cd.md)).

Copia la carpeta `artifacts\test-package` a la máquina de prueba (USB, OneDrive…). Contiene:

| Archivo | Para qué |
|---|---|
| `x64\`, `arm64\` | ScreenPivot firmado para prueba y el Windows App Runtime 1.8 que necesita |
| `ScreenPivotTest.cer` | Certificado público de prueba (caduca en 3 meses; no sirve para firmar nada) |
| `Install-ScreenPivotTest.ps1` | Confía en el certificado e instala la app según la arquitectura |
| `Uninstall-ScreenPivotTest.ps1` | Desinstala la app y quita el certificado |

## 2. Instalar (máquina de prueba con Windows 11 y permisos de administrador)

En **PowerShell como administrador**, dentro de la carpeta copiada:

```
powershell -ExecutionPolicy Bypass -File .\Install-ScreenPivotTest.ps1
```

Debe terminar mostrando `JEMA.Tech.ScreenPivot  1.0.0.0`.

## 3. Pruebas

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 1 | Abrir **ScreenPivot** desde el menú Inicio | Se abre el panel con *Ajustes* (primera vez) y **no** minimizado | |
| 2 | *Ajustes → Permitir acceso* | Aparece el **diálogo de Windows** de acceso a notificaciones; al aceptar: «Recibe notificaciones al instante» | |
| 3 | Con el Cloud PC a pantalla completa, recibir un correo o un mensaje de Teams | El aviso llega **en menos de 1 s** (evento `NotificationChanged`, no sondeo cada 2 s) | |
| 4 | *Ajustes → Iniciar con Windows* activado → cerrar sesión y volver a entrar | ScreenPivot arranca solo, en la bandeja, sin abrir el panel | |
| 5 | En una terminal: `screenpivot --demo` | Se abre la demo con la sesión de ejemplo, junto a la instancia normal | |
| 6 | *Ajustes → Privacidad → Política de privacidad* | Abre la política publicada en GitHub Pages | |
| 7 | Banner, «Abrir», «Volver al Cloud PC», panel lateral, sonidos | Igual que en la versión de desarrollo | |
| 8 | `Settings → Apps → ScreenPivot → Desinstalar` | Se desinstala; `%LOCALAPPDATA%\Packages\JEMA.Tech.ScreenPivot_*` desaparece | |

Si algo falla, copia `%LOCALAPPDATA%\Packages\JEMA.Tech.ScreenPivot_8msdqy49xre9y\LocalCache\Local\ScreenPivot\diagnostics.log` (no contiene el texto de las notificaciones).

## 4. Opcional: Windows App Certification Kit

En la misma máquina, como administrador:

```
"C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" reset
"C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath <ruta>\x64\ScreenPivot.App_1.0.0.0_x64.msix -reportoutputpath %TEMP%\wack-x64.xml
```

## 5. Limpiar

**Antes de instalar la versión de la Store** en esa máquina, quita la de prueba (misma identidad, distinta firma):

```
powershell -ExecutionPolicy Bypass -File .\Uninstall-ScreenPivotTest.ps1
```
