param(
  [string]$Configuration = 'Release',
  [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repo 'service\AgentLightBridgeService.cs'
if (-not $Output) {
  $Output = Join-Path $repo 'service\AgentLightBridgeService.exe'
}
$defaultOutput = Join-Path $repo 'service\AgentLightBridgeService.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$serviceName = 'AgentLightBridge'
$restartService = $false

if (-not (Test-Path $source)) {
  throw "Service source not found: $source"
}

if (-not (Test-Path $csc)) {
  throw "C# compiler not found: $csc"
}

try {
  $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
  if ($service -and $service.Status -ne 'Stopped' -and $Output -eq $defaultOutput) {
    Write-Host "Stopping running service before build: $serviceName"
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(15))
    $restartService = $true
  }

  $outputDir = Split-Path -Parent $Output
  if ($outputDir) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
  }

  & $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$Output" /reference:System.dll /reference:System.ServiceProcess.dll $source
  if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
  }
} finally {
  if ($restartService) {
    Write-Host "Restarting service after build: $serviceName"
    Start-Service -Name $serviceName -ErrorAction SilentlyContinue
  }
}

Write-Host "Built $Output"
