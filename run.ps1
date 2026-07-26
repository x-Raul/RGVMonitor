# Compila y lanza la app WinUI RGVMonitor.
# WinUI 3 DEBE compilarse con el MSBuild de Visual Studio (el dotnet de CLI no trae
# las tasks de generacion de recursos PRI). Este script usa el MSBuild de VS 18.

$ErrorActionPreference = "Stop"
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$proj = Join-Path $PSScriptRoot "src-cs\RGVMonitor.App\RGVMonitor.App.csproj"
$exe  = Join-Path $PSScriptRoot "src-cs\RGVMonitor.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\RGVMonitor.exe"

# Cierra una instancia previa que bloquearía el .exe al recompilar.
Stop-Process -Name "RGVMonitor" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

Write-Host "Compilando RGVMonitor.App (MSBuild de VS 18)..." -ForegroundColor Cyan
& $msbuild $proj -t:Restore,Build -p:Configuration=Debug -p:Platform=x64 -v:m -nologo
if ($LASTEXITCODE -ne 0) { Write-Host "Build fallido." -ForegroundColor Red; exit 1 }

Write-Host "Lanzando app... (VALORANT debe estar abierto para ver datos)" -ForegroundColor Green
& $exe
