# Builds the MSIX packages (x64 + ARM64) for one release channel. Used by the GitHub Actions workflows and locally.
#
#   powershell -ExecutionPolicy Bypass -File tools\build-packages.ps1 -Channel Store -Build 7
#   powershell -ExecutionPolicy Bypass -File tools\build-packages.ps1 -Channel Dev   -Build 42
#
# Channels:
# - Store: the real identity (JEMA.Tech.ScreenPivot). Output: two unsigned .msix plus ScreenPivot_<version>.msixupload
#   (an x64+ARM64 bundle) that Partner Center and "msstore publish" accept. The Store signs it.
# - Dev:   a separate identity (JEMA.Tech.ScreenPivot.Dev, "ScreenPivot Dev", alias screenpivot-dev) so test builds
#   install next to the Store app without replacing it. Sign them with tools\make-test-package.ps1.
#
# Version: <major>.<minor> from Package.appxmanifest, <Build> as the third number (the workflow run number), and 0 as
# the fourth, which the Store requires. Each channel has its own run counter; the Store needs only that it goes up.
# The manifest is patched for the build and restored afterwards, so the working tree stays clean.
param(
    [ValidateSet('Store', 'Dev')][string]$Channel = 'Store',
    [int]$Build = -1,
    [string]$Configuration = 'Release',
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path $Root).Path
$project = Join-Path $Root 'src\ScreenPivot.App'
$manifestPath = Join-Path $project 'Package.appxmanifest'
if (-not $Out) { $Out = Join-Path $Root "artifacts\packages\$($Channel.ToLowerInvariant())" }

$backup = Join-Path ([System.IO.Path]::GetTempPath()) "Package.appxmanifest.$PID.bak"
Copy-Item $manifestPath $backup -Force
$original = [System.IO.File]::ReadAllText($manifestPath)
$manifest = [xml]$original
$baseVersion = [version]$manifest.Package.Identity.Version
$version = if ($Build -ge 0) { "$($baseVersion.Major).$($baseVersion.Minor).$Build.0" } else { $baseVersion.ToString() }

function Find-SdkTool([string]$name) {
    # Microsoft.Windows.SDK.BuildTools (a package reference of the app) ships makeappx and signtool; after a restore
    # they are in the NuGet cache on any machine, CI runners included.
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $tool = Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) {
        $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter $name -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    }
    if (-not $tool) { throw "No se encontró $name (restaura la solución o instala el Windows SDK)." }
    $tool.FullName
}

try {
    $patched = [xml]$original
    $patched.Package.Identity.Version = $version
    if ($Channel -eq 'Dev') {
        $ns = New-Object System.Xml.XmlNamespaceManager $patched.NameTable
        $ns.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
        $ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
        $ns.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
        $ns.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
        $patched.Package.Identity.Name = "$($manifest.Package.Identity.Name).Dev"
        $patched.Package.Properties.DisplayName = 'ScreenPivot Dev'
        $patched.SelectSingleNode('//uap:VisualElements', $ns).DisplayName = 'ScreenPivot Dev'
        $patched.SelectSingleNode('//uap5:StartupTask', $ns).DisplayName = 'ScreenPivot Dev'
        $patched.SelectSingleNode('//desktop:ExecutionAlias', $ns).Alias = 'screenpivot-dev.exe'
    }
    $patched.Save($manifestPath)

    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    New-Item -ItemType Directory -Force $Out | Out-Null
    $appPackages = Join-Path $project 'AppPackages'

    foreach ($arch in 'x64', 'arm64') {
        $platform = if ($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
        Write-Host "== $Channel $version $arch"
        dotnet build $project -c $Configuration -p:Packaged=true -p:Platform=$platform -r "win-$arch" -nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "dotnet build falló para $arch" }

        $msix = Get-ChildItem $appPackages -Recurse -Filter "ScreenPivot.App_${version}_${arch}.msix" |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $msix) { throw "No se generó el paquete de $arch ($version)." }
        $dir = New-Item -ItemType Directory -Force (Join-Path $Out $arch)
        Copy-Item $msix.FullName $dir
        Copy-Item (Join-Path $msix.DirectoryName "Dependencies\$arch\Microsoft.WindowsAppRuntime.1.8.msix") $dir
    }

    if ($Channel -eq 'Store') {
        # One upload for both architectures: bundle the two .msix, then wrap the bundle in a .msixupload (a zip).
        $makeAppx = Find-SdkTool 'makeappx.exe'
        $stage = New-Item -ItemType Directory -Force (Join-Path $Out 'bundle-input')
        Get-ChildItem (Join-Path $Out 'x64'), (Join-Path $Out 'arm64') -Filter 'ScreenPivot.App_*.msix' | Copy-Item -Destination $stage
        $bundle = Join-Path $Out "ScreenPivot_$version.msixbundle"
        & $makeAppx bundle /d $stage /p $bundle /bv $version /o | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'makeappx bundle falló' }
        Remove-Item -Recurse -Force $stage
        $upload = Join-Path $Out "ScreenPivot_$version.msixupload"
        Compress-Archive -Path $bundle -DestinationPath "$upload.zip" -Force
        Move-Item "$upload.zip" $upload -Force
        Remove-Item $bundle
    }
}
finally {
    Move-Item $backup $manifestPath -Force
}

# No BOM: Windows PowerShell's Out-File -Encoding utf8 would add one and break the output name.
if ($env:GITHUB_OUTPUT) { [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "version=$version`n") }
Write-Host "Paquetes $Channel $version en $Out"
