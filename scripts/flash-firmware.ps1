param(
  [string]$Port = "",
  [switch]$CompileOnly,
  [string]$Esptool = "",
  [string]$FirmwareDir = "",
  [string]$ServiceName = "AgentLightBridge"
)

$ErrorActionPreference = "Stop"

function Resolve-FirstExistingPath {
  param([string[]]$Candidates)
  foreach ($candidate in $Candidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }
  return ""
}

function Invoke-Esptool {
  param([string[]]$Arguments)
  Write-Host "> $Esptool $($Arguments -join ' ')"
  & $Esptool @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "esptool failed with exit code $LASTEXITCODE"
  }
}

$repo = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath "..")).Path

if ($CompileOnly) {
  & (Join-Path -Path $PSScriptRoot -ChildPath "build-firmware-release.ps1")
  if ($LASTEXITCODE -ne 0) {
    throw "firmware release build failed with exit code $LASTEXITCODE"
  }
  return
}

if (-not $Port) {
  throw "Firmware upload requires -Port, for example -Port COM4."
}

if (-not $FirmwareDir) {
  $FirmwareDir = Join-Path -Path $repo -ChildPath "firmware-release"
}
$FirmwareDir = (Resolve-Path -LiteralPath $FirmwareDir).Path

$bootloader = Join-Path -Path $FirmwareDir -ChildPath "agent-light.bootloader.bin"
$partitions = Join-Path -Path $FirmwareDir -ChildPath "agent-light.partitions.bin"
$bootApp0 = Join-Path -Path $FirmwareDir -ChildPath "boot_app0.bin"
$app = Join-Path -Path $FirmwareDir -ChildPath "agent-light.bin"

foreach ($required in @($bootloader, $partitions, $bootApp0, $app)) {
  if (-not (Test-Path -LiteralPath $required)) {
    throw "Missing firmware binary: $required"
  }
}

if (-not $Esptool) {
  $Esptool = Resolve-FirstExistingPath -Candidates @(
    "$repo\tools\esptool\esptool.exe",
    "$repo\..\tools\esptool\esptool.exe",
    "$repo\tools\esptool_py\esptool.exe",
    "$repo\..\tools\esptool_py\esptool.exe",
    "$repo\..\.arduino\data\packages\esp32\tools\esptool_py\5.3.0\esptool.exe"
  )

  if (-not $Esptool) {
    $command = Get-Command -Name "esptool.exe" -ErrorAction SilentlyContinue
    if ($command) {
      $Esptool = $command.Source
    }
  }
}

if (-not $Esptool -or -not (Test-Path -LiteralPath $Esptool)) {
  throw "esptool.exe not found. Put it under tools\esptool\esptool.exe or pass -Esptool."
}

Write-Host "Esptool: $Esptool"
Write-Host "Firmware: $FirmwareDir"
Write-Host "Port: $Port"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$restartService = $false
if ($service -and $service.Status -eq "Running") {
  Write-Host "Stopping service to release serial port: $ServiceName"
  Stop-Service -Name $ServiceName -Force -ErrorAction Stop
  $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(15))
  $restartService = $true
}

try {
  Invoke-Esptool @(
    "--chip", "esp32",
    "--port", $Port,
    "--baud", "921600",
    "--before", "default-reset",
    "--after", "hard-reset",
    "write-flash",
    "-z",
    "--flash-mode", "dio",
    "--flash-freq", "80m",
    "--flash-size", "4MB",
    "0x1000", $bootloader,
    "0x8000", $partitions,
    "0xe000", $bootApp0,
    "0x10000", $app
  )
} finally {
  if ($restartService) {
    Write-Host "Restarting service: $ServiceName"
    Start-Service -Name $ServiceName -ErrorAction SilentlyContinue
  }
}

Write-Host "Firmware flash completed."
