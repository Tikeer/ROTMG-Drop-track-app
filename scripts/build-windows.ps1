<#
.SYNOPSIS
    Builds a clean, self-contained single-file Windows release (RotmgTracker.exe)
    and creates a one-click "RotMG Tracker" shortcut on your Desktop.

.EXAMPLE
    .\scripts\build-windows.ps1
    .\scripts\build-windows.ps1 -NoShortcut
    .\scripts\build-windows.ps1 -OutDir "C:\Games\RotMG Tracker"
#>
param(
    [string]$OutDir = "",
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot "publish\win-x64"
}

$running = Get-Process -Name "RotmgTracker", "RotmgTracker.Desktop" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "ERROR: Close RotMG Tracker before building so files are not locked." -ForegroundColor Red
    exit 1
}

if (Test-Path $OutDir) {
    Write-Host "Cleaning previous build output in $OutDir ..." -ForegroundColor DarkGray
    Remove-Item -Path $OutDir -Recurse -Force
}

Write-Host "Publishing single-file self-contained Windows release..." -ForegroundColor Cyan
dotnet publish (Join-Path $RepoRoot "RotmgTracker.Desktop\RotmgTracker.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:AssemblyName=RotmgTracker `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

$ExePath = Join-Path $OutDir "RotmgTracker.exe"
if (-not (Test-Path $ExePath)) {
    Write-Host "Expected executable not found at $ExePath" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  Executable: $ExePath"
Write-Host "  Contents:"
Get-ChildItem -Path $OutDir | ForEach-Object { Write-Host "    - $($_.Name)" }

if (-not $NoShortcut) {
    $DesktopDir = [Environment]::GetFolderPath("Desktop")
    $ShortcutPath = Join-Path $DesktopDir "RotMG Tracker.lnk"
    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $ExePath
    $Shortcut.WorkingDirectory = $OutDir
    $Shortcut.Description = "RotMG Drop, Dungeon & DPS Tracker"
    $Shortcut.Save()
    Write-Host ""
    Write-Host "Desktop shortcut created: $ShortcutPath" -ForegroundColor Green
}
