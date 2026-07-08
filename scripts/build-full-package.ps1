param(
  [string]$OutputRoot = "",
  [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath "..")).Path
$driverDir = Join-Path -Path $repo -ChildPath "CP210x_Universal_Windows_Driver"
$driverInf = Join-Path -Path $driverDir -ChildPath "silabser.inf"
if (-not (Test-Path -LiteralPath $driverInf)) {
  throw "CP210x driver is required for the full package: $driverInf"
}
$esptoolDir = ""
foreach ($candidate in @(
  (Join-Path -Path $repo -ChildPath "tools\esptool"),
  (Join-Path -Path $repo -ChildPath "..\tools\esptool"),
  (Join-Path -Path $repo -ChildPath "..\.arduino\data\packages\esp32\tools\esptool_py\5.3.0")
)) {
  if (Test-Path -LiteralPath (Join-Path -Path $candidate -ChildPath "esptool.exe")) {
    $esptoolDir = (Resolve-Path -LiteralPath $candidate).Path
    break
  }
}

& (Join-Path -Path $PSScriptRoot -ChildPath "build-manager.ps1")
& (Join-Path -Path $PSScriptRoot -ChildPath "build-wizard.ps1")
& (Join-Path -Path $PSScriptRoot -ChildPath "build-firmware-release.ps1")

if (-not $esptoolDir) {
  throw "esptool.exe is required for the full package."
}

if (-not $OutputRoot) {
  $OutputRoot = Join-Path -Path $repo -ChildPath "dist"
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$packageName = "AgentLight-Full-CP210x-$stamp"
$packageRoot = Join-Path -Path $OutputRoot -ChildPath $packageName

if (Test-Path -LiteralPath $packageRoot) {
  throw "Package directory already exists: $packageRoot"
}

New-Item -ItemType Directory -Path $packageRoot | Out-Null
New-Item -ItemType Directory -Path (Join-Path -Path $packageRoot -ChildPath "service") | Out-Null
New-Item -ItemType Directory -Path (Join-Path -Path $packageRoot -ChildPath "manager") | Out-Null
New-Item -ItemType Directory -Path (Join-Path -Path $packageRoot -ChildPath "wizard") | Out-Null
New-Item -ItemType Directory -Path (Join-Path -Path $packageRoot -ChildPath "scripts") | Out-Null
New-Item -ItemType Directory -Path (Join-Path -Path $packageRoot -ChildPath "tools") | Out-Null

& (Join-Path -Path $PSScriptRoot -ChildPath "build-windows-service.ps1") -Output (Join-Path -Path $packageRoot -ChildPath "service\AgentLightBridgeService.exe")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "service\agent-light-service.ini.example") -Destination (Join-Path -Path $packageRoot -ChildPath "service")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "manager\AgentLightManager.exe") -Destination (Join-Path -Path $packageRoot -ChildPath "manager")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "manager\AgentLightManager.exe.manifest") -Destination (Join-Path -Path $packageRoot -ChildPath "manager")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "wizard\AgentLightSetupWizard.exe") -Destination (Join-Path -Path $packageRoot -ChildPath "wizard")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "wizard\AgentLightSetupWizard.exe.manifest") -Destination (Join-Path -Path $packageRoot -ChildPath "wizard")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\install-windows-service.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\uninstall-windows-service.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\status-windows-service.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\install-codex-hooks.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\diagnose-codex-hooks.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\install-cp210x-driver.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\flash-firmware.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\build-firmware-release.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "hook-client.mjs") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "codex-hooks-snippet.json") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "lib") -Destination (Join-Path -Path $packageRoot -ChildPath "lib") -Recurse
Copy-Item -LiteralPath $driverDir -Destination (Join-Path -Path $packageRoot -ChildPath "CP210x_Universal_Windows_Driver") -Recurse
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "firmware-release") -Destination (Join-Path -Path $packageRoot -ChildPath "firmware-release") -Recurse
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "firmware") -Destination (Join-Path -Path $packageRoot -ChildPath "firmware") -Recurse
$packagedSecrets = Join-Path -Path $packageRoot -ChildPath "firmware\agent-light\secrets.h"
if (Test-Path -LiteralPath $packagedSecrets) {
  Remove-Item -LiteralPath $packagedSecrets -Force
}
New-Item -ItemType Directory -Path (Join-Path -Path $packageRoot -ChildPath "tools\esptool") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path -Path $esptoolDir -ChildPath "esptool.exe") -Destination (Join-Path -Path $packageRoot -ChildPath "tools\esptool")
if (Test-Path -LiteralPath (Join-Path -Path $esptoolDir -ChildPath "LICENSE")) {
  Copy-Item -LiteralPath (Join-Path -Path $esptoolDir -ChildPath "LICENSE") -Destination (Join-Path -Path $packageRoot -ChildPath "tools\esptool")
}

$readme = @"
Agent Light Full CP210x Package

This package includes:
- AgentLightBridgeService.exe
- AgentLightManager.exe
- AgentLightSetupWizard.exe
- CP210x Universal Windows Driver
- Precompiled firmware binaries
- Firmware source and firmware flashing helper
- esptool executable
- PowerShell helper scripts for driver and service installation
- Codex hook client and hook installation helper

Suggested first-run order:
1. Start .\wizard\AgentLightSetupWizard.exe and follow the guided setup.

Manual fallback:
1. Right-click PowerShell and choose "Run as administrator".
2. If Windows does not show a COM port for the ESP32, run:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-cp210x-driver.ps1
3. Install or repair the bridge service:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-windows-service.ps1 -Serial COM3
4. Start .\manager\AgentLightManager.exe to switch the COM port, Bluetooth COM port, or Wi-Fi TCP target.
5. Use the Manager firmware tab to flash firmware. Flashing uses precompiled binaries and does not require Arduino ESP32 core.
6. In Manager, use "安装/修复 Codex Hooks" to write the current package path to ~/.codex/hooks.json, then trust the hooks in Codex with /hooks.

The bridge service is a compiled executable and does not require Node.js. Codex hooks use hook-client.mjs and require Node.js.
"@

[System.IO.File]::WriteAllText((Join-Path -Path $packageRoot -ChildPath "README-FIRST.txt"), $readme, [System.Text.Encoding]::UTF8)

if (-not $NoZip) {
  $zipPath = Join-Path -Path $OutputRoot -ChildPath ($packageName + ".zip")
  Compress-Archive -Path (Join-Path -Path $packageRoot -ChildPath "*") -DestinationPath $zipPath -Force
  Write-Host "Built full package: $zipPath"
} else {
  Write-Host "Built full package directory: $packageRoot"
}
