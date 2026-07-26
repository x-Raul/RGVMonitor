# Genera un EXE portable y autocontenido para Windows x64.
# Uso: powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
#
# WinUI 3 extrae sus DLL y recursos nativos a una carpeta temporal durante el
# primer arranque. IncludeAllContentForSelfExtract es obligatorio para incluirlos.

$ErrorActionPreference = "Stop"
$root       = $PSScriptRoot
$proj       = Join-Path $root "src-cs\RGVMonitor.App\RGVMonitor.App.csproj"
$distDir    = Join-Path $root "dist"
$stagingDir = Join-Path $distDir ".publish-staging"
$exe        = Join-Path $distDir "RGVMonitor.exe"
$msbuild    = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path -LiteralPath $msbuild)) {
    Write-Error "No encuentro MSBuild en: $msbuild`nAjusta la ruta en publish.ps1 si Visual Studio esta en otra edicion o ubicacion."
    exit 1
}

# Evita que una copia publicada mantenga bloqueado el EXE de destino.
Stop-Process -Name "RGVMonitor" -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

if (Test-Path -LiteralPath $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
Write-Host "Publicando RGVMonitor..." -ForegroundColor Cyan

& $msbuild $proj `
    -restore `
    -t:Publish `
    -p:Configuration=Release `
    -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 `
    -p:SelfContained=true `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishDir="$stagingDir\" `
    -v:m -nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "El publish fallo (codigo $LASTEXITCODE). La publicacion anterior no fue modificada."
    exit $LASTEXITCODE
}

$stagedExe = Join-Path $stagingDir "RGVMonitor.exe"
$stagedFiles = @(Get-ChildItem -LiteralPath $stagingDir -File -Recurse)

if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
    Write-Error "La publicacion termino sin crear $stagedExe."
    exit 1
}

if ($stagedFiles.Count -ne 1) {
    $unexpected = $stagedFiles.FullName -join "`n"
    Write-Error "La publicacion no produjo un unico EXE. Archivos encontrados:`n$unexpected"
    exit 1
}

# Reemplaza la publicacion anterior solo despues de generar una nueva completa.
$legacyPaths = @(
    $exe,
    (Join-Path $distDir "RGVMonitor.pdb"),
    (Join-Path $distDir "RGVMonitor.Core.pdb"),
    (Join-Path $distDir "RGVMonitor-win-x64"),
    (Join-Path $distDir "RGVMonitor-win-x64.zip")
)

foreach ($legacyPath in $legacyPaths) {
    if (Test-Path -LiteralPath $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Recurse -Force
    }
}

Move-Item -LiteralPath $stagedExe -Destination $exe
Remove-Item -LiteralPath $stagingDir -Recurse -Force

$mb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Listo: $exe ($mb MB)" -ForegroundColor Green
