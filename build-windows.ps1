# Builds build\windows\Snapmark.exe and installs it for the current user.
# Usage:  powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 [-NoInstall]
#         powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 -Release [-Version 1.2.0]
#   -Release builds the downloadable build\windows\Snapmark-Windows.exe (self-contained: no .NET needed) and does not install.
param([switch]$NoInstall, [switch]$Release, [string]$Version = '')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$out = Join-Path $PSScriptRoot 'build\windows'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

# App icon
dotnet run --project Windows/IconGen -c Release -- "$out\Snapmark.ico"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }

$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

if ($Release) {
    dotnet publish Windows/Snapmark -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none @versionArgs -o "$out\publish"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Copy-Item "$out\publish\Snapmark.exe" "$out\Snapmark-Windows.exe" -Force
    Write-Host "Built $out\Snapmark-Windows.exe"
    return
}

# Single-file exe (uses the installed .NET 8 Desktop Runtime)
dotnet publish Windows/Snapmark -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none @versionArgs -o "$out\publish"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item "$out\publish\Snapmark.exe" "$out\Snapmark.exe" -Force

if ($NoInstall) { Write-Host "Built $out\Snapmark.exe"; return }

# Install: quit the running app, replace it in %LOCALAPPDATA%\Programs\Snapmark, add a Start menu shortcut.
Get-Process Snapmark -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Snapmark'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\Snapmark.exe" "$dest\Snapmark.exe" -Force

$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Snapmark.lnk'
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($lnk)
$s.TargetPath = "$dest\Snapmark.exe"
$s.WorkingDirectory = $dest
$s.Description = 'Screenshot and annotate'
$s.Save()

# If "Start with Windows" was on, point it at the new copy.
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $run -Name Snapmark -ErrorAction SilentlyContinue) {
    Set-ItemProperty -Path $run -Name Snapmark -Value "`"$dest\Snapmark.exe`" --background"
}

Start-Process "$dest\Snapmark.exe" -ArgumentList '--background'
Write-Host "Installed $dest\Snapmark.exe (Start menu: Snapmark)"
