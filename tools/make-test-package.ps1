# Signs a sideloadable copy of the packages built by toolsuild-packages.ps1, for testing the installed (MSIX) app.
#
#   powershell -ExecutionPolicy Bypass -File toolsuild-packages.ps1 -Channel Store   (or -Channel Dev)
#   powershell -ExecutionPolicy Bypass -File tools\make-test-package.ps1 [-Packages artifacts\packages\dev]
#
# - Creates a throwaway self-signed certificate whose subject matches the manifest Publisher (required by MSIX),
#   signs COPIES of the packages, exports only the public .cer and deletes the certificate and its private key.
#   Nothing is trusted on this machine; the input packages stay unsigned.
# - Output: artifacts	est-package\ (git-ignored) with the signed packages, the Windows App Runtime dependency,
#   the .cer and install/uninstall scripts. Copy that folder to the test machine. The dev-build workflow does the same.
param(
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [string]$Packages = '',
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $Packages) { $Packages = Join-Path $Root 'artifacts\packages\store' }
if (-not $Out) { $Out = Join-Path $Root 'artifacts	est-package' }
$out = $Out

# Identity (name, publisher, version) comes from the built package itself, so Store and Dev builds both work.
$first = Get-ChildItem $Packages -Recurse -Filter 'ScreenPivot.App_*.msix' | Select-Object -First 1
if (-not $first) { throw "No hay paquetes en $Packages; ejecuta toolsuild-packages.ps1 primero." }
$zip = [System.IO.Compression.ZipFile]::OpenRead($first.FullName)
try {
    $reader = New-Object System.IO.StreamReader ($zip.GetEntry('AppxManifest.xml').Open())
    $identity = ([xml]$reader.ReadToEnd()).Package.Identity
    $reader.Dispose()
}
finally { $zip.Dispose() }
$publisher = $identity.Publisher
$name = $identity.Name

$signTool = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool) {
    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kitsin" -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
}
if (-not $signTool) { throw 'No se encontró signtool.exe.' }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

$cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
    -FriendlyName 'ScreenPivot test signing (sideload only)' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddMonths(3)
try {
    Export-Certificate -Cert $cert -FilePath (Join-Path $out 'ScreenPivotTest.cer') | Out-Null

    foreach ($arch in 'x64', 'arm64') {
        $src = Get-ChildItem (Join-Path $Packages $arch) -Filter 'ScreenPivot.App_*.msix' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $src) { Write-Warning "Falta el paquete de $arch en $Packages."; continue }
        $dir = New-Item -ItemType Directory -Force (Join-Path $out $arch)
        Copy-Item $src.FullName $dir
        Copy-Item (Join-Path $src.DirectoryName 'Microsoft.WindowsAppRuntime.1.8.msix') $dir
        & $signTool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint /s My (Join-Path $dir $src.Name) | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "signtool falló para $arch" }
        Write-Host "Firmado: $arch"
    }
}
finally {
    # The private key is not needed after signing; leave nothing behind in the user's certificate store.
    Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey
}

@'
# Run in an ADMINISTRATOR PowerShell on the test machine, from this folder:
#   powershell -ExecutionPolicy Bypass -File .\Install-ScreenPivotTest.ps1
$ErrorActionPreference = 'Stop'
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
Import-Certificate -FilePath "$PSScriptRoot\ScreenPivotTest.cer" -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
$app = Get-ChildItem "$PSScriptRoot\$arch" -Filter 'ScreenPivot.App_*.msix' | Select-Object -First 1
Add-AppxPackage -Path $app.FullName -DependencyPath "$PSScriptRoot\$arch\Microsoft.WindowsAppRuntime.1.8.msix" -ForceApplicationShutdown
Get-AppxPackage __NAME__ | Select-Object Name, Version, Architecture, SignatureKind
'@.Replace('__NAME__', $name) | Set-Content -Encoding UTF8 (Join-Path $out 'Install-ScreenPivotTest.ps1')

@'
# Removes the test install and its certificate. Run in an ADMINISTRATOR PowerShell. Do this before installing from the Store.
Get-AppxPackage __NAME__ | Remove-AppxPackage
$cer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 "$PSScriptRoot\ScreenPivotTest.cer"
Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' | Where-Object Thumbprint -eq $cer.Thumbprint | Remove-Item
Write-Host 'ScreenPivot de prueba desinstalado y certificado eliminado.'
'@.Replace('__NAME__', $name) | Set-Content -Encoding UTF8 (Join-Path $out 'Uninstall-ScreenPivotTest.ps1')

Write-Host "Paquete de prueba ($name $($identity.Version)) listo en $out"
