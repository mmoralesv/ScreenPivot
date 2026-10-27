# CI/CD: ramas, builds y publicación en la Store

```
feature/x ──PR (squash)──► develop ──► Dev build (pre-release "ScreenPivot Dev")
                              │
                              │  Promote to main: cherry-pick de los PR aprobados
                              ▼
                    promote/N ──PR (merge commit)──► main ──► Release (GitHub release + Partner Center)
```

| Rama | Para qué | Se actualiza con |
|---|---|---|
| `feature/*`, `fix/*` | Una funcionalidad o corrección | Commits normales; se borra al fusionar |
| `develop` | Integración y pruebas | PR desde `feature/*` con **Squash and merge** (un commit por funcionalidad) |
| `main` | Lo que está (o va a estar) en la Store | Solo PR de promoción `promote/N` con **Create a merge commit** |

## Workflows (`.github/workflows`)

| Workflow | Cuándo corre | Qué hace |
|---|---|---|
| **CI** (`ci.yml`) | PR a `develop` o `main`; push a `feature/**` o `fix/**` | Compila la solución en Release y ejecuta los tests. Lo reutilizan los otros dos. |
| **Dev build** (`dev-build.yml`) | Push a `develop` (cada PR fusionado), o a mano | CI → paquetes **ScreenPivot Dev** x64 + ARM64 firmados con un certificado desechable → artefacto y pre-release `dev-1.0.N.0` con el zip. Guarda las 10 últimas. |
| **Promote to main** (`promote.yml`) | A mano | Cherry-pick (`-x`) de los PR o commits indicados sobre `main` → compila y prueba → abre el PR `promote/N` → `main`. |
| **Release** (`release.yml`) | Push a `main` (al fusionar la promoción), o a mano | CI → paquetes de Store x64 + ARM64 y `ScreenPivot_1.0.N.0.msixupload` → release `v1.0.N.0` en GitHub → sube el `.msixupload` a Partner Center. |

### Versiones

`<mayor>.<menor>` sale de `Identity Version` en `src/ScreenPivot.App/Package.appxmanifest`; el tercer número es el número de ejecución del workflow y el cuarto siempre `0` (la Store lo exige). Para pasar a 1.1, cambia el manifiesto a `1.1.0.0`.

Dev y Store tienen contadores independientes y no chocan: **ScreenPivot Dev** es otra identidad (`JEMA.Tech.ScreenPivot.Dev`, alias `screenpivot-dev`, datos propios) y se instala junto a la versión de la Store sin reemplazarla.

## Día a día

1. **Nueva funcionalidad**
   ```bash
   git switch develop
   git pull
   git switch -c feature/mi-cambio
   ```
   Al terminar: `git push -u origin feature/mi-cambio` y abrir el PR contra `develop`. CI tiene que pasar.
2. **Fusionar en develop** con *Squash and merge*. Dev build publica en *Releases* un pre-release `dev-1.0.N.0`.
3. **Probar** en otra máquina: descargar el zip, descomprimir y, en PowerShell como administrador:
   ```bash
   powershell -ExecutionPolicy Bypass -File .\Install-ScreenPivotTest.ps1
   ```
   `Uninstall-ScreenPivotTest.ps1` la quita junto con su certificado.
4. **Promover lo aprobado**: *Actions → Promote to main → Run workflow*, con los PR en orden, p. ej. `#12 #15`. Si hay conflicto, el workflow falla y dice cuál; normalmente falta promover antes otro PR del que depende.
5. **Fusionar el PR `promote/N`** con *Create a merge commit*. Release genera la versión de producción y la sube a Partner Center como **borrador**.
6. **Enviar a certificación**: revisar el borrador en Partner Center y pulsar *Submit to the Store*, o ejecutar *Release* a mano con **submit** marcado (vuelve a compilar con un número nuevo y lo envía).

### Promoción a mano (conflictos o cambios en `.github/workflows`)

El token de Actions no puede subir cambios de workflows, así que esos PR se promueven a mano:

```bash
git fetch origin
git switch -c promote/manual origin/main
git cherry-pick -x <sha-del-squash-en-develop>
git push -u origin promote/manual
gh pr create --base main --title "Promover a main"
```

Un commit de merge (no squash) se aplica con `git cherry-pick -x -m 1 <sha>`.

## Configuración única

### En GitHub

- [x] *Settings → Actions → General → Workflow permissions*: **Allow GitHub Actions to create and approve pull requests** (lo necesita *Promote to main*).
- [x] *Settings → General*: rama por defecto **`develop`**, para que los PR nuevos apunten ahí.
- [x] *Settings → Rules → Rulesets*:
  - **develop**: sin push directo ni borrado; PR obligatorio con el check **build-test** en verde; solo *Squash merge*.
  - **main**: igual, pero solo *Merge commit*, para conservar cada cherry-pick.
  - En los dos, los administradores pueden saltarse las reglas **solo dentro de un PR** (por ejemplo, para fusionar si CI está caído).
  - El PR que abre *Promote to main* no lanza CI por sí solo (los PR creados con el token de Actions no disparan workflows); por eso el workflow ejecuta CI en la rama `promote/N` después de abrirlo.
- [ ] *Settings → Environments → microsoft-store* (se crea en la primera ejecución de Release): añadir **Required reviewers** si quieres aprobar cada subida a Partner Center.

### En Partner Center (para la subida automática)

Release se salta la subida (con un aviso) mientras falten estos secretos; el resto del workflow funciona igual.

1. Partner Center → ⚙ *Account settings → User management → Microsoft Entra applications* → **Add Microsoft Entra application**. Si la cuenta no tiene tenant de Entra ID, asociar uno o crearlo desde *Tenants*.
2. Crear la aplicación con rol **Manager** (o *Developer* con permiso para enviar).
3. En la aplicación: **Add new key** para obtener el *client secret*. Se muestra solo una vez y caduca; anota la fecha.
4. Datos que hacen falta:
   - *Tenant ID* y *Client ID*: en la misma página de la aplicación.
   - *Seller ID*: *Account settings → Legal info → Developer*.
5. Guardarlos como secretos del repositorio (*Settings → Secrets and variables → Actions*), o con:
   ```bash
   gh secret set PARTNER_CENTER_TENANT_ID
   ```
   Los cuatro: `PARTNER_CENTER_TENANT_ID`, `PARTNER_CENTER_SELLER_ID`, `PARTNER_CENTER_CLIENT_ID`, `PARTNER_CENTER_CLIENT_SECRET`.

### Límites de la subida automática

- **Solo paquetes.** Ficha, capturas y notas para certificación se copian del último envío publicado; si cambian, se editan en Partner Center antes de enviar.
- **Un envío a la vez.** Si hay uno en certificación, la subida falla hasta que termine. `msstore publish` borra el borrador pendiente y crea uno nuevo desde el último publicado, así que no prepares cambios de ficha en un borrador antes de que corra Release.
- **Versión creciente.** Cada subida necesita una versión mayor que la publicada; el número de ejecución lo garantiza mientras no se baje `<mayor>.<menor>`.

## Local

```bash
powershell -ExecutionPolicy Bypass -File tools\build-packages.ps1 -Channel Store
```

Deja en `artifacts\packages\store` los `.msix` x64 y ARM64 y el `.msixupload` (con la versión del manifiesto si no se pasa `-Build`). Con `-Channel Dev` genera ScreenPivot Dev. `tools\make-test-package.ps1 -Packages artifacts\packages\<canal>` firma copias para instalarlas en otra máquina.
