# Builds a sideloadable copy of the Store packages for testing the installed (MSIX) app on another machine.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-test-package.ps1
#
# - Creates a throwaway self-signed certificate whose subject matches the manifest Publisher (required by MSIX),
#   signs COPIES of the packages, exports only the public .cer and deletes the certificate and its private key.
#   Nothing is trusted on this machine; the Store packages in AppPackages stay unsigned.
# - Output: artifacts\test-package\ (git-ignored) with the signed packages, the Windows App Runtime dependency,
#   the .cer and install/uninstall scripts. Copy that folder to the test machine.
param(
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [string]$SignTool = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
)
$ErrorActionPreference = 'Stop'

$manifest = [xml](Get-Content (Join-Path $Root 'src\ScreenPivot.App\Package.appxmanifest'))
$publisher = $manifest.Package.Identity.Publisher
$version = $manifest.Package.Identity.Version
$packages = Join-Path $Root 'src\ScreenPivot.App\AppPackages'
$out = Join-Path $Root 'artifacts\test-package'

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

$cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
    -FriendlyName 'ScreenPivot test signing (sideload only)' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddMonths(3)
try {
    Export-Certificate -Cert $cert -FilePath (Join-Path $out 'ScreenPivotTest.cer') | Out-Null

    foreach ($arch in 'x64', 'arm64') {
        $src = Join-Path $packages "ScreenPivot.App_${version}_${arch}_Test\ScreenPivot.App_${version}_${arch}.msix"
        if (-not (Test-Path $src)) { Write-Warning "Falta $src; compila el paquete de $arch primero."; continue }
        $dir = New-Item -ItemType Directory -Force (Join-Path $out $arch)
        Copy-Item $src $dir
        Copy-Item (Join-Path (Split-Path $src) "Dependencies\$arch\Microsoft.WindowsAppRuntime.1.8.msix") $dir
        & $SignTool sign /fd SHA256 /sha1 $cert.Thumbprint /s My (Join-Path $dir (Split-Path $src -Leaf)) | Out-Null
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
Get-AppxPackage JEMA.Tech.ScreenPivot | Select-Object Name, Version, Architecture, SignatureKind
'@ | Set-Content -Encoding UTF8 (Join-Path $out 'Install-ScreenPivotTest.ps1')

@'
# Removes the test install and its certificate. Run in an ADMINISTRATOR PowerShell. Do this before installing from the Store.
Get-AppxPackage JEMA.Tech.ScreenPivot | Remove-AppxPackage
$cer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 "$PSScriptRoot\ScreenPivotTest.cer"
Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' | Where-Object Thumbprint -eq $cer.Thumbprint | Remove-Item
Write-Host 'ScreenPivot de prueba desinstalado y certificado eliminado.'
'@ | Set-Content -Encoding UTF8 (Join-Path $out 'Uninstall-ScreenPivotTest.ps1')

Write-Host "Paquete de prueba listo en $out"
