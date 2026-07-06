param(
  [string]$ServiceName = 'AgentLightBridge',
  [int]$Tail = 40
)

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$logDir = Join-Path $repo 'service\logs'

function Should-ShowLogLine {
  param([string]$Line)

  if ($Line -match ' -> (?<Command>.+?) <- ok\s*$') {
    $command = $Matches.Command.Trim()
    return $command -match '^(sys:|wifi:)' -or $command -eq 'reboot'
  }

  return $true
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
  $service | Format-List Name,DisplayName,Status,StartType
} else {
  Write-Host "Service not installed: $ServiceName"
}

foreach ($log in @('agent-light-service.log', 'bridge-service.out.log', 'bridge-service.err.log')) {
  $path = Join-Path $logDir $log
  if (Test-Path $path) {
    Write-Host "`n== $log =="
    Get-Content -Encoding UTF8 -Path $path -Tail $Tail | Where-Object { Should-ShowLogLine $_ }
  }
}
