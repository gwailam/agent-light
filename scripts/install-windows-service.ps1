param(
  [string]$ServiceName = "AgentLightBridge",
  [string]$DisplayName = "Agent Light Bridge",
  [ValidateSet("serial", "tcp", "wifi")]
  [string]$Transport = "serial",
  [string]$Serial = "COM3",
  [int]$Baud = 9600,
  [int]$Listen = 8765,
  [string]$DeviceHost = "agent-light.local",
  [int]$DevicePort = 8766,
  [string]$Initial = "claude:idle",
  [string]$NodePath = ""
)

$ErrorActionPreference = "Stop"

function Invoke-Sc {
  param([string[]]$Arguments)
  & sc.exe @Arguments | Write-Host
  if ($LASTEXITCODE -ne 0) {
    throw "sc.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
  }
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw "请用以管理员身份运行的 PowerShell 执行安装脚本。"
}

$repo = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath "..")).Path
$serviceDir = Join-Path -Path $repo -ChildPath "service"
$serviceExe = Join-Path -Path $serviceDir -ChildPath "AgentLightBridgeService.exe"
$configPath = Join-Path -Path $serviceDir -ChildPath "agent-light-service.ini"

if (-not $NodePath) {
  $nodeCommand = Get-Command -Name "node.exe" -ErrorAction Stop
  $NodePath = $nodeCommand.Source
}

if (-not (Test-Path -LiteralPath $serviceExe)) {
  & (Join-Path -Path $PSScriptRoot -ChildPath "build-windows-service.ps1")
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$config = "ServiceName=$ServiceName
NodePath=$NodePath
RepoRoot=$repo
Transport=$Transport
Serial=$Serial
Baud=$Baud
Listen=$Listen
DeviceHost=$DeviceHost
DevicePort=$DevicePort
Initial=$Initial
"
[System.IO.File]::WriteAllText($configPath, $config, $utf8NoBom)

Get-CimInstance -ClassName Win32_Process -Filter "name = 'node.exe'" |
  Where-Object { $_.CommandLine -match "serial-bridge\.mjs" } |
  ForEach-Object {
    Write-Host "Stopping existing bridge process pid=$($_.ProcessId)"
    Stop-Process -Id $_.ProcessId -Force
  }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  if ($existing.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    Start-Sleep -Seconds 2
  }
  Invoke-Sc -Arguments @("config", $ServiceName, "binPath=", $serviceExe, "start=", "auto", "DisplayName=", $DisplayName)
} else {
  New-Service -Name $ServiceName -BinaryPathName $serviceExe -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

Invoke-Sc -Arguments @("description", $ServiceName, "Agent Light ESP32 serial bridge service")
Invoke-Sc -Arguments @("failure", $ServiceName, "reset=", "60", "actions=", "restart/5000/restart/5000/none/5000")

Start-Service -Name $ServiceName -ErrorAction Stop
Start-Sleep -Seconds 2
$service = Get-Service -Name $ServiceName -ErrorAction Stop
if ($service.Status -ne "Running") {
  throw "Service $ServiceName did not reach Running state. Current state: $($service.Status)"
}

Write-Host "Installed and started service: $ServiceName"
Write-Host "Config: $configPath"
