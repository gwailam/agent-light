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

& (Join-Path -Path $PSScriptRoot -ChildPath "build-manager.ps1")
& (Join-Path -Path $PSScriptRoot -ChildPath "build-wizard.ps1")

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

& (Join-Path -Path $PSScriptRoot -ChildPath "build-windows-service.ps1") -Output (Join-Path -Path $packageRoot -ChildPath "service\AgentLightBridgeService.exe")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "service\agent-light-service.ini.example") -Destination (Join-Path -Path $packageRoot -ChildPath "service")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "manager\AgentLightManager.exe") -Destination (Join-Path -Path $packageRoot -ChildPath "manager")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "manager\AgentLightManager.exe.manifest") -Destination (Join-Path -Path $packageRoot -ChildPath "manager")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "wizard\AgentLightSetupWizard.exe") -Destination (Join-Path -Path $packageRoot -ChildPath "wizard")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "wizard\AgentLightSetupWizard.exe.manifest") -Destination (Join-Path -Path $packageRoot -ChildPath "wizard")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\install-windows-service.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\uninstall-windows-service.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\status-windows-service.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath (Join-Path -Path $repo -ChildPath "scripts\install-cp210x-driver.ps1") -Destination (Join-Path -Path $packageRoot -ChildPath "scripts")
Copy-Item -LiteralPath $driverDir -Destination (Join-Path -Path $packageRoot -ChildPath "CP210x_Universal_Windows_Driver") -Recurse

$readme = @"
Agent Light Full CP210x Package

This package includes:
- AgentLightBridgeService.exe
- AgentLightManager.exe
- AgentLightSetupWizard.exe
- CP210x Universal Windows Driver
- PowerShell helper scripts for driver and service installation

Suggested first-run order:
1. Start .\wizard\AgentLightSetupWizard.exe and follow the guided setup.

Manual fallback:
1. Right-click PowerShell and choose "Run as administrator".
2. If Windows does not show a COM port for the ESP32, run:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-cp210x-driver.ps1
3. Install or repair the bridge service:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-windows-service.ps1 -Serial COM3
4. Start .\manager\AgentLightManager.exe to switch the COM port, Bluetooth COM port, or Wi-Fi TCP target.

The bridge service is a compiled executable and does not require Node.js.
"@

[System.IO.File]::WriteAllText((Join-Path -Path $packageRoot -ChildPath "README-FIRST.txt"), $readme, [System.Text.Encoding]::UTF8)

if (-not $NoZip) {
  $zipPath = Join-Path -Path $OutputRoot -ChildPath ($packageName + ".zip")
  Compress-Archive -Path (Join-Path -Path $packageRoot -ChildPath "*") -DestinationPath $zipPath -Force
  Write-Host "Built full package: $zipPath"
} else {
  Write-Host "Built full package directory: $packageRoot"
}
