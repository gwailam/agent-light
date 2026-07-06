param(
  [string]$ServiceName = 'AgentLightBridge'
)

$ErrorActionPreference = 'Stop'

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw '请用“以管理员身份运行”的 PowerShell 执行卸载脚本。'
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
  Write-Host "Service not found: $ServiceName"
  exit 0
}

if ($existing.Status -ne 'Stopped') {
  & sc.exe stop $ServiceName | Write-Host
  Start-Sleep -Seconds 2
}

& sc.exe delete $ServiceName | Write-Host
Write-Host "Deleted service: $ServiceName"